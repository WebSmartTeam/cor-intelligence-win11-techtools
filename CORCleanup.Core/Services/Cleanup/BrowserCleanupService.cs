using System.Diagnostics;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Cleanup;

/// <summary>
/// Detects installed browsers, scans their data directories for cleanable items,
/// and performs selective deletion of cache, history, cookies, and session data.
/// Follows the safe-defaults philosophy: cache is ON by default, user data (history,
/// cookies, sessions, passwords) is OFF by default.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BrowserCleanupService : IBrowserCleanupService
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string RoamingAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// Chromium browser definitions: (display name, profile root relative to LocalAppData, process name).
    /// </summary>
    private static readonly (string Name, string ProfileRoot, string ProcessName)[] ChromiumBrowsers =
    {
        ("Chrome", @"Google\Chrome\User Data\Default", "chrome"),
        ("Edge", @"Microsoft\Edge\User Data\Default", "msedge"),
        ("Brave", @"BraveSoftware\Brave-Browser\User Data\Default", "brave"),
    };

    /// <summary>
    /// Chromium cleanup targets: (category, relative path within profile, safe by default).
    /// </summary>
    private static readonly (string Category, string RelativePath, bool IsSafe, bool IsFile)[] ChromiumTargets =
    {
        ("Cache", @"Cache\Cache_Data", true, false),
        ("Cache", "Code Cache", true, false),
        ("Cache", "Service Worker\\CacheStorage", true, false),
        ("History", "History", false, true),
        ("Cookies", "Cookies", false, true),
        ("Sessions", "Sessions", false, false),
        ("Download History", "History", false, true),
    };

    public Task<List<BrowserCleanupItem>> ScanBrowserDataAsync()
    {
        return Task.Run(() =>
        {
            var items = new List<BrowserCleanupItem>();

            // Scan Chromium-based browsers
            foreach (var (name, profileRoot, _) in ChromiumBrowsers)
            {
                var profilePath = Path.Combine(LocalAppData, profileRoot);
                if (!Directory.Exists(profilePath)) continue;

                // De-duplicate: Download History and History both reference the same file.
                // Only emit separate entries for distinct physical paths.
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (category, relativePath, isSafe, isFile) in ChromiumTargets)
                {
                    var fullPath = Path.Combine(profilePath, relativePath);

                    // Skip if this exact path was already added for this browser
                    if (!seenPaths.Add(fullPath)) continue;

                    long size = 0;
                    bool exists = false;

                    if (isFile)
                    {
                        if (File.Exists(fullPath))
                        {
                            exists = true;
                            try { size = new FileInfo(fullPath).Length; }
                            catch { /* Locked file */ }
                        }
                    }
                    else
                    {
                        if (Directory.Exists(fullPath))
                        {
                            exists = true;
                            size = GetDirectorySize(fullPath);
                        }
                    }

                    if (!exists) continue;

                    items.Add(new BrowserCleanupItem
                    {
                        BrowserName = name,
                        Category = category,
                        Path = fullPath,
                        SizeBytes = size,
                        IsSafe = isSafe,
                        IsSelected = isSafe
                    });
                }
            }

            // Scan Firefox
            ScanFirefox(items);

            return items;
        });
    }

    public Task<long> CleanSelectedAsync(List<BrowserCleanupItem> items, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long totalFreed = 0;

            foreach (var item in items.Where(i => i.IsSelected))
            {
                ct.ThrowIfCancellationRequested();
                totalFreed += DeletePath(item.Path);
            }

            return totalFreed;
        }, ct);
    }

    public List<string> GetRunningBrowsers()
    {
        var running = new List<string>();
        var processNames = new[] { "chrome", "msedge", "brave", "firefox" };
        var displayNames = new[] { "Chrome", "Edge", "Brave", "Firefox" };

        for (int i = 0; i < processNames.Length; i++)
        {
            try
            {
                var processes = Process.GetProcessesByName(processNames[i]);
                if (processes.Length > 0)
                    running.Add(displayNames[i]);

                // Dispose all returned Process objects
                foreach (var proc in processes)
                    proc.Dispose();
            }
            catch
            {
                // Access denied or transient error — skip
            }
        }

        return running;
    }

    private static void ScanFirefox(List<BrowserCleanupItem> items)
    {
        var profilesRoot = Path.Combine(RoamingAppData, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(profilesRoot)) return;

        // Firefox uses random-named profile directories; find the default
        string? defaultProfile = null;
        foreach (var dir in Directory.EnumerateDirectories(profilesRoot))
        {
            // Default profile typically ends with ".default-release" or ".default"
            var dirName = Path.GetFileName(dir);
            if (dirName.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase)
                || dirName.EndsWith(".default", StringComparison.OrdinalIgnoreCase))
            {
                defaultProfile = dir;
                break;
            }
        }

        // Fallback: use the first profile directory
        defaultProfile ??= Directory.EnumerateDirectories(profilesRoot).FirstOrDefault();
        if (defaultProfile is null) return;

        // Firefox also stores cache under LocalAppData
        var localProfilesRoot = Path.Combine(LocalAppData, @"Mozilla\Firefox\Profiles");
        string? localCacheProfile = null;
        if (Directory.Exists(localProfilesRoot))
        {
            var profileDirName = Path.GetFileName(defaultProfile);
            var localDir = Path.Combine(localProfilesRoot, profileDirName);
            if (Directory.Exists(localDir))
                localCacheProfile = localDir;
        }

        // Cache (under LocalAppData profile)
        if (localCacheProfile is not null)
        {
            var cachePath = Path.Combine(localCacheProfile, "cache2", "entries");
            if (Directory.Exists(cachePath))
            {
                items.Add(new BrowserCleanupItem
                {
                    BrowserName = "Firefox",
                    Category = "Cache",
                    Path = cachePath,
                    SizeBytes = GetDirectorySize(cachePath),
                    IsSafe = true,
                    IsSelected = true
                });
            }
        }

        // History (places.sqlite — OFF by default)
        var historyPath = Path.Combine(defaultProfile, "places.sqlite");
        if (File.Exists(historyPath))
        {
            items.Add(new BrowserCleanupItem
            {
                BrowserName = "Firefox",
                Category = "History",
                Path = historyPath,
                SizeBytes = SafeFileSize(historyPath),
                IsSafe = false,
                IsSelected = false
            });
        }

        // Cookies (cookies.sqlite — OFF by default)
        var cookiesPath = Path.Combine(defaultProfile, "cookies.sqlite");
        if (File.Exists(cookiesPath))
        {
            items.Add(new BrowserCleanupItem
            {
                BrowserName = "Firefox",
                Category = "Cookies",
                Path = cookiesPath,
                SizeBytes = SafeFileSize(cookiesPath),
                IsSafe = false,
                IsSelected = false
            });
        }
    }

    private static long DeletePath(string path)
    {
        long freed = 0;

        if (File.Exists(path))
        {
            try
            {
                var size = new FileInfo(path).Length;
                File.Delete(path);
                freed += size;
            }
            catch
            {
                // File locked by browser — skip
            }
            return freed;
        }

        if (!Directory.Exists(path)) return 0;

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                var size = info.Length;
                info.Delete();
                freed += size;
            }
            catch
            {
                // File locked or protected — skip, continue cleaning
            }
        }

        // Remove empty subdirectories (deepest first)
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Count(c => c == Path.DirectorySeparatorChar)))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { /* Skip */ }
            }
        }
        catch { /* Skip */ }

        return freed;
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; }
                catch { /* Skip inaccessible files */ }
            }
        }
        catch { /* Skip inaccessible directories */ }
        return size;
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
