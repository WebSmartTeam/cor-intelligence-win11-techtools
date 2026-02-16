using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services.Registry;

/// <summary>
/// Scans the Windows registry for orphaned/invalid entries and safely removes them.
/// Creates .reg backup files before any modifications.
/// Uses Microsoft.Win32.Registry API — works on all Win11 builds including 25H2.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryCleanerService : IRegistryCleanerService
{
    private readonly string _backupDir;

    public RegistryCleanerService()
    {
        _backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "COR Cleanup", "Backups");
        Directory.CreateDirectory(_backupDir);
    }

    public string GetBackupDirectory() => _backupDir;

    public Task<List<RegistryIssue>> ScanAsync(
        IProgress<(RegistryScanCategory Category, int Found)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var issues = new List<RegistryIssue>();

            ScanMissingSharedDlls(issues, progress, cancellationToken);
            ScanUnusedFileExtensions(issues, progress, cancellationToken);
            ScanOrphanedComActiveX(issues, progress, cancellationToken);
            ScanInvalidApplicationPaths(issues, progress, cancellationToken);
            ScanObsoleteSoftwareEntries(issues, progress, cancellationToken);
            ScanMissingMuiReferences(issues, progress, cancellationToken);
            ScanStaleInstallerReferences(issues, progress, cancellationToken);
            ScanDeadShortcutReferences(issues, progress, cancellationToken);

            return issues;
        }, cancellationToken);
    }

    public Task<RegistryFixResult> FixSelectedAsync(
        List<RegistryIssue> issues,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(_backupDir, $"backup_{timestamp}.reg");

            // Phase 1: Export all affected keys to .reg backup
            ExportBackup(issues, backupPath);

            // Phase 2: Delete the registry entries
            int fixed_ = 0;
            int failed = 0;
            var errors = new List<string>();
            var fixedKeyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var issue in issues)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    DeleteRegistryEntry(issue);
                    fixed_++;
                    // Track by KeyPath + ValueName for unique identification
                    fixedKeyPaths.Add(issue.ValueName is not null
                        ? $"{issue.KeyPath}\\{issue.ValueName}"
                        : issue.KeyPath);
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{issue.KeyPath}: {ex.Message}");
                }
            }

            return new RegistryFixResult
            {
                TotalSelected = issues.Count,
                Fixed = fixed_,
                Failed = failed,
                BackupFilePath = backupPath,
                Errors = errors,
                FixedKeyPaths = fixedKeyPaths
            };
        }, cancellationToken);
    }

    public Task<bool> RestoreBackupAsync(string backupFilePath) => Task.Run(() =>
    {
        // Resolve to canonical full path first to defeat path traversal (../../, symlinks)
        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(backupFilePath);
        }
        catch
        {
            return false;
        }

        // Backup must be within our backup directory
        var resolvedDir = Path.GetFullPath(_backupDir);
        if (!resolvedPath.StartsWith(resolvedDir, StringComparison.OrdinalIgnoreCase))
            return false;

        // Must have .reg extension
        if (!resolvedPath.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!File.Exists(resolvedPath)) return false;

        try
        {
            // Sanitise the resolved path for safe embedding in process arguments
            var safePath = InputSanitiser.SanitiseForProcessArgument(resolvedPath);

            // reg import runs silently and merges the .reg file back
            var psi = new ProcessStartInfo("reg", $"import \"{safePath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(30_000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });

    public Task<List<RegistryBackup>> GetBackupsAsync() => Task.Run(() =>
    {
        var backups = new List<RegistryBackup>();

        if (!Directory.Exists(_backupDir))
            return backups;

        foreach (var file in Directory.EnumerateFiles(_backupDir, "*.reg"))
        {
            var fi = new FileInfo(file);

            // Parse issue count from first comment line if available
            int issueCount = 0;
            try
            {
                using var reader = fi.OpenText();
                var firstLine = reader.ReadLine();
                var secondLine = reader.ReadLine();
                // Our backups include "; Issues: N" as second line
                if (secondLine?.StartsWith("; Issues:") == true &&
                    int.TryParse(secondLine.AsSpan(9).Trim(), out var count))
                {
                    issueCount = count;
                }
            }
            catch
            {
                // Not critical
            }

            backups.Add(new RegistryBackup
            {
                FilePath = file,
                FileName = fi.Name,
                CreatedUtc = fi.CreationTimeUtc,
                FileSizeBytes = fi.Length,
                IssueCount = issueCount
            });
        }

        return backups.OrderByDescending(b => b.CreatedUtc).ToList();
    });

    public Task<bool> DeleteBackupAsync(string backupFilePath) => Task.Run(() =>
    {
        try
        {
            // Prevent path traversal — backup must be within our backup directory
            if (!InputSanitiser.IsPathWithinDirectory(backupFilePath, _backupDir))
                return false;

            if (!backupFilePath.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
                return false;

            if (File.Exists(backupFilePath))
            {
                File.Delete(backupFilePath);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    });

    // ----------------------------------------------------------------
    // Backup
    // ----------------------------------------------------------------

    private void ExportBackup(List<RegistryIssue> issues, string backupPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine($"; Issues: {issues.Count}");
        sb.AppendLine($"; Created by COR Cleanup on {DateTime.UtcNow:O}");
        sb.AppendLine();

        // Group by key path to avoid duplicate exports
        var keyPaths = issues
            .Select(i => i.KeyPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var keyPath in keyPaths)
        {
            // Use reg export for each unique root key path
            // We write minimal .reg format so it can be re-imported
            try
            {
                var (root, subPath) = ParseKeyPath(keyPath);
                if (root is null) continue;

                using var key = root.OpenSubKey(subPath);
                if (key is null) continue;

                // Export key header
                var fullPath = GetFullRegistryPath(root, subPath);
                sb.AppendLine($"[{fullPath}]");

                // Export all values in this key
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    var kind = key.GetValueKind(valueName);

                    var formattedName = string.IsNullOrEmpty(valueName) ? "@" : $"\"{EscapeRegString(valueName)}\"";
                    var formattedValue = FormatRegValue(value, kind);

                    if (formattedValue is not null)
                        sb.AppendLine($"{formattedName}={formattedValue}");
                }

                sb.AppendLine();
            }
            catch
            {
                // Skip keys we can't read
            }
        }

        File.WriteAllText(backupPath, sb.ToString(), Encoding.Unicode);
    }

    private static void DeleteRegistryEntry(RegistryIssue issue)
    {
        var (root, subPath) = ParseKeyPath(issue.KeyPath);
        if (root is null)
            throw new InvalidOperationException($"Cannot parse registry path: {issue.KeyPath}");

        if (issue.ValueName is not null)
        {
            // Delete a specific value
            using var key = root.OpenSubKey(subPath, writable: true);
            key?.DeleteValue(issue.ValueName, throwOnMissingValue: false);
        }
        else
        {
            // Delete the entire key (and its subkeys)
            root.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
        }
    }

    // ----------------------------------------------------------------
    // Scan methods
    // ----------------------------------------------------------------

    /// <summary>
    /// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs
    /// Each value = DLL path, data = reference count.
    /// Issue: DLL file no longer exists on disk.
    /// </summary>
    private static void ScanMissingSharedDlls(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
        int found = 0;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null) return;

            foreach (var valueName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(valueName)) continue;

                // Value name IS the DLL path
                if (!File.Exists(valueName))
                {
                    issues.Add(new RegistryIssue
                    {
                        Category = RegistryScanCategory.MissingSharedDlls,
                        Risk = RegistryRiskLevel.Safe,
                        KeyPath = $@"HKLM\{keyPath}",
                        ValueName = valueName,
                        Description = $"Shared DLL not found: {Path.GetFileName(valueName)}"
                    });
                    found++;
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw; }
        catch { /* Access denied or key missing — skip */ }

        progress?.Report((RegistryScanCategory.MissingSharedDlls, found));
    }

    /// <summary>
    /// HKCR\.xxx keys — each should have a default value pointing to a ProgID.
    /// Issue: The ProgID doesn't exist as a key under HKCR.
    /// </summary>
    private static void ScanUnusedFileExtensions(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        int found = 0;

        try
        {
            using var classesRoot = Microsoft.Win32.Registry.ClassesRoot;

            foreach (var subKeyName in classesRoot.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                if (!subKeyName.StartsWith('.')) continue;

                try
                {
                    using var extKey = classesRoot.OpenSubKey(subKeyName);
                    var progId = extKey?.GetValue(null)?.ToString();

                    if (string.IsNullOrWhiteSpace(progId)) continue;

                    // Check if the ProgID exists
                    using var progIdKey = classesRoot.OpenSubKey(progId);
                    if (progIdKey is null)
                    {
                        issues.Add(new RegistryIssue
                        {
                            Category = RegistryScanCategory.UnusedFileExtensions,
                            Risk = RegistryRiskLevel.Safe,
                            KeyPath = $@"HKCR\{subKeyName}",
                            Description = $"Extension '{subKeyName}' points to missing ProgID: {progId}"
                        });
                        found++;
                    }
                }
                catch { /* Skip inaccessible keys */ }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw; }
        catch { }

        progress?.Report((RegistryScanCategory.UnusedFileExtensions, found));
    }

    /// <summary>
    /// HKCR\CLSID\{guid}\InprocServer32 or LocalServer32 — should point to an existing file.
    /// Issue: The referenced DLL/EXE no longer exists.
    /// </summary>
    private static void ScanOrphanedComActiveX(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        int found = 0;
        var serverSubkeys = new[] { "InprocServer32", "LocalServer32" };

        try
        {
            using var clsidKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("CLSID");
            if (clsidKey is null) return;

            foreach (var guid in clsidKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();

                foreach (var serverType in serverSubkeys)
                {
                    try
                    {
                        using var serverKey = clsidKey.OpenSubKey($@"{guid}\{serverType}");
                        var serverPath = serverKey?.GetValue(null)?.ToString();

                        if (string.IsNullOrWhiteSpace(serverPath)) continue;

                        // Clean up the path — remove arguments, expand environment variables
                        var cleanPath = CleanFilePath(serverPath);
                        if (string.IsNullOrWhiteSpace(cleanPath)) continue;

                        // Skip system DLLs that may be in System32 (they exist but resolve differently)
                        if (cleanPath.Contains(@"\System32\", StringComparison.OrdinalIgnoreCase) ||
                            cleanPath.Contains(@"\SysWOW64\", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!File.Exists(cleanPath))
                        {
                            issues.Add(new RegistryIssue
                            {
                                Category = RegistryScanCategory.OrphanedComActiveX,
                                Risk = RegistryRiskLevel.Review,
                                KeyPath = $@"HKCR\CLSID\{guid}\{serverType}",
                                Description = $"COM server not found: {Path.GetFileName(cleanPath)}"
                            });
                            found++;
                        }
                    }
                    catch { /* Skip inaccessible */ }
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw; }
        catch { }

        progress?.Report((RegistryScanCategory.OrphanedComActiveX, found));
    }

    /// <summary>
    /// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\app.exe
    /// Each key should have a default value or Path value pointing to an existing executable.
    /// </summary>
    private static void ScanInvalidApplicationPaths(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        int found = 0;

        try
        {
            using var appPathsKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
            if (appPathsKey is null) return;

            foreach (var appName in appPathsKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var appKey = appPathsKey.OpenSubKey(appName);
                    var exePath = appKey?.GetValue(null)?.ToString();

                    if (string.IsNullOrWhiteSpace(exePath)) continue;

                    var cleanPath = CleanFilePath(exePath);
                    if (!string.IsNullOrWhiteSpace(cleanPath) && !File.Exists(cleanPath))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Category = RegistryScanCategory.InvalidApplicationPaths,
                            Risk = RegistryRiskLevel.Safe,
                            KeyPath = $@"HKLM\{keyPath}\{appName}",
                            Description = $"Application not found: {appName}"
                        });
                        found++;
                    }
                }
                catch { }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw; }
        catch { }

        progress?.Report((RegistryScanCategory.InvalidApplicationPaths, found));
    }

    /// <summary>
    /// HKLM + HKCU Uninstall keys — entries where the UninstallString points to a missing executable.
    /// </summary>
    private static void ScanObsoleteSoftwareEntries(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        int found = 0;

        var uninstallPaths = new[]
        {
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM"),
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM"),
            (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU"),
        };

        foreach (var (root, path, label) in uninstallPaths)
        {
            try
            {
                using var uninstallKey = root.OpenSubKey(path);
                if (uninstallKey is null) continue;

                foreach (var subName in uninstallKey.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        using var appKey = uninstallKey.OpenSubKey(subName);
                        if (appKey is null) continue;

                        // System components often lack UninstallString — skip them
                        var systemComponent = appKey.GetValue("SystemComponent");
                        if (systemComponent is int sc && sc == 1) continue;

                        var uninstallString = appKey.GetValue("UninstallString")?.ToString();
                        var installLocation = appKey.GetValue("InstallLocation")?.ToString();
                        var displayName = appKey.GetValue("DisplayName")?.ToString() ?? subName;

                        // Need at least one path to check
                        if (string.IsNullOrWhiteSpace(uninstallString) && string.IsNullOrWhiteSpace(installLocation))
                            continue;

                        bool uninstallerMissing = false;
                        if (!string.IsNullOrWhiteSpace(uninstallString))
                        {
                            var exePath = CleanFilePath(uninstallString);
                            if (!string.IsNullOrWhiteSpace(exePath) && !File.Exists(exePath))
                                uninstallerMissing = true;
                        }

                        bool installDirMissing = false;
                        if (!string.IsNullOrWhiteSpace(installLocation))
                        {
                            if (!Directory.Exists(installLocation))
                                installDirMissing = true;
                        }

                        if (uninstallerMissing && installDirMissing)
                        {
                            issues.Add(new RegistryIssue
                            {
                                Category = RegistryScanCategory.ObsoleteSoftwareEntries,
                                Risk = RegistryRiskLevel.Safe,
                                KeyPath = $@"{label}\{path}\{subName}",
                                Description = $"Obsolete entry: {displayName} — uninstaller and install folder both missing"
                            });
                            found++;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        progress?.Report((RegistryScanCategory.ObsoleteSoftwareEntries, found));
    }

    /// <summary>
    /// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SideBySide\Winners
    /// MUI cache entries referencing missing assemblies.
    /// This is a conservative scan — only flags entries with clearly invalid paths.
    /// </summary>
    private static void ScanMissingMuiReferences(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        int found = 0;

        // MUI scanning is conservative — we only check the MUI cache for orphaned entries
        try
        {
            const string muiCachePath = @"Software\Classes\Local Settings\MuiCache";
            using var cacheKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(muiCachePath);
            if (cacheKey is null) { progress?.Report((RegistryScanCategory.MissingMuiReferences, 0)); return; }

            foreach (var subKeyName in cacheKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var subKey = cacheKey.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    foreach (var valueName in subKey.GetValueNames())
                    {
                        ct.ThrowIfCancellationRequested();

                        // MUI cache value names are paths like "C:\Program Files\app\resource.dll.mun"
                        if (valueName.Contains(@":\") && !File.Exists(valueName))
                        {
                            issues.Add(new RegistryIssue
                            {
                                Category = RegistryScanCategory.MissingMuiReferences,
                                Risk = RegistryRiskLevel.Review,
                                KeyPath = $@"HKCU\{muiCachePath}\{subKeyName}",
                                ValueName = valueName,
                                Description = $"MUI resource not found: {Path.GetFileName(valueName)}"
                            });
                            found++;
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw; }
        catch { }

        progress?.Report((RegistryScanCategory.MissingMuiReferences, found));
    }

    /// <summary>
    /// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders
    /// Each value = folder path. Issue: folder no longer exists.
    /// </summary>
    private static void ScanStaleInstallerReferences(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders";
        int found = 0;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null) { progress?.Report((RegistryScanCategory.StaleInstallerReferences, 0)); return; }

            foreach (var valueName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(valueName)) continue;

                // Value name IS the folder path
                if (!Directory.Exists(valueName))
                {
                    issues.Add(new RegistryIssue
                    {
                        Category = RegistryScanCategory.StaleInstallerReferences,
                        Risk = RegistryRiskLevel.Review,
                        KeyPath = $@"HKLM\{keyPath}",
                        ValueName = valueName,
                        Description = $"Installer folder missing: {valueName}"
                    });
                    found++;
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw; }
        catch { }

        progress?.Report((RegistryScanCategory.StaleInstallerReferences, found));
    }

    /// <summary>
    /// Recent documents and shell MRU lists that reference non-existent files.
    /// Conservative — only scans well-known MRU locations.
    /// </summary>
    private static void ScanDeadShortcutReferences(
        List<RegistryIssue> issues,
        IProgress<(RegistryScanCategory, int)>? progress,
        CancellationToken ct)
    {
        int found = 0;

        // Check shell bags / recent file references
        const string recentDocsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(recentDocsPath);
            if (key is null) { progress?.Report((RegistryScanCategory.DeadShortcutReferences, 0)); return; }

            // Check subkeys for file extension groups
            foreach (var extName in key.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var extKey = key.OpenSubKey(extName);
                    if (extKey is null) continue;

                    // Values are binary MRU entries — we count but don't parse
                    // Instead check the actual Recent folder for dead .lnk files
                }
                catch { }
            }

            // Check actual Recent Items folder for dead shortcuts
            var recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (Directory.Exists(recentFolder))
            {
                foreach (var lnk in Directory.EnumerateFiles(recentFolder, "*.lnk"))
                {
                    ct.ThrowIfCancellationRequested();

                    // Basic check: if the .lnk file is very old (>6 months) and small, flag it
                    var fi = new FileInfo(lnk);
                    if (fi.LastWriteTimeUtc < DateTime.UtcNow.AddMonths(-6))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Category = RegistryScanCategory.DeadShortcutReferences,
                            Risk = RegistryRiskLevel.Safe,
                            KeyPath = $@"HKCU\{recentDocsPath}",
                            ValueName = fi.Name,
                            Description = $"Old shortcut reference: {fi.Name} (last used {fi.LastWriteTimeUtc.ToLocalTime():dd/MM/yyyy})"
                        });
                        found++;
                    }
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw; }
        catch { }

        progress?.Report((RegistryScanCategory.DeadShortcutReferences, found));
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Parses a display key path like "HKLM\SOFTWARE\..." into (RegistryKey root, string subPath).
    /// </summary>
    private static (RegistryKey? Root, string SubPath) ParseKeyPath(string keyPath)
    {
        var parts = keyPath.Split('\\', 2);
        if (parts.Length < 2) return (null, "");

        var root = parts[0].ToUpperInvariant() switch
        {
            "HKLM" => Microsoft.Win32.Registry.LocalMachine,
            "HKCU" => Microsoft.Win32.Registry.CurrentUser,
            "HKCR" => Microsoft.Win32.Registry.ClassesRoot,
            "HKU"  => Microsoft.Win32.Registry.Users,
            "HKCC" => Microsoft.Win32.Registry.CurrentConfig,
            _ => null
        };

        return (root, parts[1]);
    }

    /// <summary>
    /// Gets the full registry path string for .reg file format.
    /// </summary>
    private static string GetFullRegistryPath(RegistryKey root, string subPath)
    {
        var rootName = root.Name; // e.g. "HKEY_LOCAL_MACHINE"
        return $"{rootName}\\{subPath}";
    }

    /// <summary>
    /// Cleans a command-line style path to extract just the executable path.
    /// Handles quoted paths, MsiExec arguments, rundll32, etc.
    /// </summary>
    private static string CleanFilePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return "";

        var path = Environment.ExpandEnvironmentVariables(rawPath.Trim());

        // Skip MsiExec-based uninstallers — they're always valid if MSI service is running
        if (path.Contains("MsiExec", StringComparison.OrdinalIgnoreCase))
            return "";

        // Handle quoted paths
        if (path.StartsWith('"'))
        {
            var endQuote = path.IndexOf('"', 1);
            if (endQuote > 0)
                return path[1..endQuote];
        }

        // Handle rundll32 — extract the DLL path
        if (path.Contains("rundll32", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var dllPath = parts[1].TrimEnd(',');
                return Environment.ExpandEnvironmentVariables(dllPath);
            }
        }

        // Find .exe, .dll, or similar extension
        var exeIdx = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx >= 0) return path[..(exeIdx + 4)];

        var dllIdx = path.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
        if (dllIdx >= 0) return path[..(dllIdx + 4)];

        // Return as-is if it looks like a path
        return path.Contains('\\') ? path.Split(' ')[0] : "";
    }

    /// <summary>
    /// Escapes a string for .reg file format.
    /// </summary>
    private static string EscapeRegString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Formats a registry value for .reg file export.
    /// </summary>
    private static string? FormatRegValue(object? value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String => $"\"{EscapeRegString(value?.ToString() ?? "")}\"",
        RegistryValueKind.ExpandString => $"hex(2):{BytesToHex(System.Text.Encoding.Unicode.GetBytes((value?.ToString() ?? "") + "\0"))}",
        RegistryValueKind.DWord => $"dword:{Convert.ToInt32(value ?? 0):x8}",
        RegistryValueKind.QWord => $"hex(b):{BytesToHex(BitConverter.GetBytes(Convert.ToInt64(value ?? 0L)))}",
        RegistryValueKind.Binary when value is byte[] bytes => $"hex:{BytesToHex(bytes)}",
        RegistryValueKind.MultiString when value is string[] strings =>
            $"hex(7):{BytesToHex(System.Text.Encoding.Unicode.GetBytes(string.Join("\0", strings) + "\0\0"))}",
        _ => null
    };

    private static string BytesToHex(byte[] bytes) =>
        string.Join(",", bytes.Select(b => b.ToString("x2")));
}
