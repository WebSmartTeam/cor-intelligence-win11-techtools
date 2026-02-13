using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Cleanup;

/// <summary>
/// Scans and cleans Windows system temp files, caches, and junk.
/// Uses whitelist approach — only deletes from known-safe locations.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class SystemCleanupService : ICleanupService
{
    private static readonly Dictionary<CleanupCategory, CleanupTarget> Targets = new()
    {
        [CleanupCategory.WindowsTemp] = new(
            "Windows Temp Files",
            "Temporary files from %TEMP% and C:\\Windows\\Temp",
            true,
            () => new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Temp",
                @"C:\Windows\Temp"
            }),
        [CleanupCategory.WindowsErrorReporting] = new(
            "Windows Error Reports",
            "Crash reports sent to Microsoft",
            true,
            () => new[] { @"C:\ProgramData\Microsoft\Windows\WER" }),
        [CleanupCategory.RecycleBin] = new(
            "Recycle Bin",
            "Deleted files in all Recycle Bins",
            true,
            () => Array.Empty<string>()), // Special handling via SHEmptyRecycleBin
        [CleanupCategory.Prefetch] = new(
            "Prefetch Files",
            "Application launch optimisation cache",
            true,
            () => new[] { @"C:\Windows\Prefetch" }),
        [CleanupCategory.ThumbnailCache] = new(
            "Thumbnail Cache",
            "Cached folder/file thumbnail images",
            true,
            () => new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\Explorer")
            }),
        [CleanupCategory.InstallerTemp] = new(
            "Windows Installer Temp",
            "Leftover installer temporary files",
            true,
            () => new[] { @"C:\Windows\Installer\$PatchCache$" }),
        [CleanupCategory.WindowsLogs] = new(
            "Windows Log Files",
            "Old CBS, DISM, and setup log files",
            true,
            () => new[]
            {
                @"C:\Windows\Logs\CBS",
                @"C:\Windows\Logs\DISM"
            }),
        [CleanupCategory.MemoryDumps] = new(
            "Memory Dump Files",
            "Crash memory dumps (MEMORY.DMP and minidumps)",
            true,
            () => new[]
            {
                @"C:\Windows\MEMORY.DMP",
                @"C:\Windows\Minidump"
            }),
        [CleanupCategory.BrowserCache] = new(
            "Browser Cache",
            "Temporary internet files for Chrome, Edge, Firefox, Brave",
            true,
            () => GetBrowserCachePaths()),
        [CleanupCategory.DnsCache] = new(
            "DNS Cache",
            "Cached DNS lookups — flushing forces fresh name resolution",
            false,
            () => Array.Empty<string>()), // Special handling via DnsFlushResolverCache
    };

    public Task<List<CleanupItem>> ScanAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var items = new List<CleanupItem>();

            foreach (var (category, target) in Targets)
            {
                ct.ThrowIfCancellationRequested();

                long size = 0;
                if (category == CleanupCategory.RecycleBin)
                {
                    // Recycle Bin size is estimated from $Recycle.Bin on all drives
                    size = EstimateRecycleBinSize();
                }
                else
                {
                    foreach (var path in target.GetPaths())
                    {
                        if (File.Exists(path))
                            size += new FileInfo(path).Length;
                        else if (Directory.Exists(path))
                            size += GetDirectorySize(path);
                    }
                }

                items.Add(new CleanupItem
                {
                    Category = category,
                    DisplayName = target.DisplayName,
                    Description = target.Description,
                    EstimatedSizeBytes = size,
                    IsSelectedByDefault = target.SelectedByDefault,
                    IsSelected = target.SelectedByDefault
                });
            }

            return items;
        }, ct);

    public Task<CleanupResult> CleanAsync(
        IEnumerable<CleanupCategory> selectedCategories,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long totalFreed = 0;
            var details = new List<CleanupItemResult>();
            var selected = selectedCategories.ToHashSet();

            foreach (var (category, target) in Targets)
            {
                ct.ThrowIfCancellationRequested();
                if (!selected.Contains(category)) continue;

                progress?.Report($"Cleaning {target.DisplayName}...");
                long freed = 0;
                var success = true;
                string? error = null;

                try
                {
                    if (category == CleanupCategory.RecycleBin)
                    {
                        freed = CleanRecycleBin();
                    }
                    else if (category == CleanupCategory.DnsCache)
                    {
                        FlushDnsCache();
                    }
                    else
                    {
                        foreach (var path in target.GetPaths())
                        {
                            ct.ThrowIfCancellationRequested();
                            freed += CleanPath(path);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    success = false;
                    error = ex.Message;
                }

                totalFreed += freed;
                details.Add(new CleanupItemResult
                {
                    Category = category,
                    DisplayName = target.DisplayName,
                    BytesFreed = freed,
                    Success = success,
                    ErrorMessage = error
                });
            }

            sw.Stop();
            return new CleanupResult
            {
                TotalBytesFreed = totalFreed,
                ItemsCleaned = details.Count(d => d.Success),
                ItemsFailed = details.Count(d => !d.Success),
                Duration = sw.Elapsed,
                Details = details
            };
        }, ct);

    private static long CleanPath(string path)
    {
        long freed = 0;

        if (File.Exists(path))
        {
            var size = new FileInfo(path).Length;
            try { File.Delete(path); freed += size; }
            catch { /* In use — skip */ }
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
                // File in use or protected — skip silently, continue cleaning
            }
        }

        // Remove empty subdirectories
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Count(c => c == Path.DirectorySeparatorChar))) // Deepest first
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

    private static long CleanRecycleBin()
    {
        var sizeBefore = EstimateRecycleBinSize();
        try
        {
            // SHEmptyRecycleBin via P/Invoke
            NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                NativeMethods.SHERB_NOCONFIRMATION |
                NativeMethods.SHERB_NOPROGRESSUI |
                NativeMethods.SHERB_NOSOUND);
        }
        catch { /* May fail if empty */ }
        return sizeBefore;
    }

    private static void FlushDnsCache()
    {
        try
        {
            NativeMethods.DnsFlushResolverCache();
        }
        catch { /* Non-critical */ }
    }

    private static long EstimateRecycleBinSize()
    {
        long total = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var recyclePath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            if (Directory.Exists(recyclePath))
                total += GetDirectorySize(recyclePath);
        }
        return total;
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

    private static string[] GetBrowserCachePaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var paths = new List<string>();

        // Chrome
        var chromePath = Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache");
        if (Directory.Exists(chromePath)) paths.Add(chromePath);
        var chromeCache2 = Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache");
        if (Directory.Exists(chromeCache2)) paths.Add(chromeCache2);

        // Edge
        var edgePath = Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache");
        if (Directory.Exists(edgePath)) paths.Add(edgePath);

        // Brave
        var bravePath = Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache");
        if (Directory.Exists(bravePath)) paths.Add(bravePath);

        // Firefox — profiles directory structure
        var firefoxPath = Path.Combine(localAppData, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxPath))
        {
            foreach (var profile in Directory.EnumerateDirectories(firefoxPath))
            {
                var cache2 = Path.Combine(profile, "cache2");
                if (Directory.Exists(cache2)) paths.Add(cache2);
            }
        }

        return paths.ToArray();
    }

    private sealed record CleanupTarget(
        string DisplayName,
        string Description,
        bool SelectedByDefault,
        Func<string[]> GetPaths);

    private static partial class NativeMethods
    {
        public const int SHERB_NOCONFIRMATION = 0x00000001;
        public const int SHERB_NOPROGRESSUI = 0x00000002;
        public const int SHERB_NOSOUND = 0x00000004;

        [System.Runtime.InteropServices.LibraryImport("shell32.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
        public static partial int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, int dwFlags);

        [System.Runtime.InteropServices.LibraryImport("dnsapi.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool DnsFlushResolverCache();
    }
}
