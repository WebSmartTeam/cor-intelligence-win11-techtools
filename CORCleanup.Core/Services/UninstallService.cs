using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using WinRegistry = Microsoft.Win32.Registry;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services;

/// <summary>
/// Enumerates installed programs from the registry with source detection,
/// supports batch uninstall, post-uninstall leftover scanning, and
/// leftover removal (files/folders to Recycle Bin, registry keys deleted).
/// Inspired by BCUninstaller patterns, built fresh for .NET 8 + Win11.
/// DOES NOT use Win32_Product (slow, triggers MSI reconfiguration).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallService : IUninstallService
{
    private static readonly string[] UninstallKeyPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    // ── Programme enumeration ──────────────────────────────────────

    public Task<List<InstalledProgram>> GetInstalledProgramsAsync() => Task.Run(() =>
    {
        var programs = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // HKLM — system-wide installs
        foreach (var keyPath in UninstallKeyPaths)
            EnumerateUninstallKey(WinRegistry.LocalMachine, keyPath, programs, seen);

        // HKCU — per-user installs (often missed by other tools)
        foreach (var keyPath in UninstallKeyPaths)
            EnumerateUninstallKey(WinRegistry.CurrentUser, keyPath, programs, seen);

        return programs
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName))
            .OrderBy(p => p.DisplayName)
            .ToList();
    });

    // ── Standard uninstall ─────────────────────────────────────────

    public Task<bool> UninstallAsync(InstalledProgram program) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(program.UninstallString))
            return false;

        return ExecuteUninstallCommand(program.UninstallString);
    });

    // ── Quiet (silent) uninstall ───────────────────────────────────

    public Task<bool> QuietUninstallAsync(InstalledProgram program) => Task.Run(() =>
    {
        // Prefer explicit QuietUninstallString from registry
        if (!string.IsNullOrWhiteSpace(program.QuietUninstallString))
            return ExecuteUninstallCommand(program.QuietUninstallString);

        // For MSI, derive quiet flags automatically
        if (program.Source == InstallSource.Msi &&
            !string.IsNullOrWhiteSpace(program.UninstallString))
        {
            var quietCmd = program.UninstallString;
            if (!quietCmd.Contains("/qn", StringComparison.OrdinalIgnoreCase))
                quietCmd += " /qn";
            return ExecuteUninstallCommand(quietCmd);
        }

        return false;
    });

    // ── Leftover scanning ──────────────────────────────────────────

    public Task<List<UninstallLeftover>> ScanLeftoversAsync(InstalledProgram program) => Task.Run(() =>
    {
        var leftovers = new List<UninstallLeftover>();

        // Build search terms from programme name and publisher
        var searchTerms = new List<string>();
        if (!string.IsNullOrWhiteSpace(program.DisplayName))
            searchTerms.Add(program.DisplayName);
        if (!string.IsNullOrWhiteSpace(program.Publisher) &&
            !program.Publisher.Equals(program.DisplayName, StringComparison.OrdinalIgnoreCase))
            searchTerms.Add(program.Publisher);

        // 1. Install directory (most reliable leftover indicator)
        ScanInstallLocation(program, leftovers);

        // 2. Programme Files directories
        ScanProgramFiles(searchTerms, leftovers);

        // 3. AppData / LocalAppData / ProgramData
        ScanDataFolders(program, searchTerms, leftovers);

        // 4. Registry: check if uninstall key still exists
        ScanRegistryLeftover(program, leftovers);

        return leftovers;
    });

    // ── Leftover removal ───────────────────────────────────────────

    public Task<int> RemoveLeftoversAsync(IEnumerable<UninstallLeftover> leftovers) => Task.Run(() =>
    {
        int removed = 0;

        foreach (var leftover in leftovers)
        {
            try
            {
                switch (leftover.Type)
                {
                    case LeftoverType.Folder:
                        if (Directory.Exists(leftover.Path) && SendToRecycleBin(leftover.Path))
                            removed++;
                        break;

                    case LeftoverType.File:
                        if (File.Exists(leftover.Path) && SendToRecycleBin(leftover.Path))
                            removed++;
                        break;

                    case LeftoverType.RegistryKey:
                        if (TryDeleteRegistryKey(leftover.Path))
                            removed++;
                        break;
                }
            }
            catch
            {
                // Individual leftover removal failure — continue with others
            }
        }

        return removed;
    });

    // ── Private: registry enumeration ──────────────────────────────

    private static void EnumerateUninstallKey(
        RegistryKey root,
        string keyPath,
        List<InstalledProgram> programs,
        HashSet<string> seen)
    {
        try
        {
            using var key = root.OpenSubKey(keyPath);
            if (key is null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var displayName = subKey.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    // Skip system components and updates
                    var systemComponent = subKey.GetValue("SystemComponent");
                    if (systemComponent is int sc && sc == 1) continue;

                    // De-duplicate by display name
                    if (!seen.Add(displayName)) continue;

                    var sizeKb = subKey.GetValue("EstimatedSize");
                    long? sizeBytes = sizeKb is int kb ? kb * 1024L : null;

                    var installDateStr = subKey.GetValue("InstallDate")?.ToString();
                    DateTime? installDate = null;
                    if (installDateStr is { Length: 8 } &&
                        DateTime.TryParseExact(installDateStr, "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var parsed))
                    {
                        installDate = parsed;
                    }

                    programs.Add(new InstalledProgram
                    {
                        DisplayName = displayName,
                        Publisher = subKey.GetValue("Publisher")?.ToString(),
                        DisplayVersion = subKey.GetValue("DisplayVersion")?.ToString(),
                        InstallDate = installDate,
                        EstimatedSizeBytes = sizeBytes,
                        UninstallString = subKey.GetValue("UninstallString")?.ToString(),
                        QuietUninstallString = subKey.GetValue("QuietUninstallString")?.ToString(),
                        InstallLocation = subKey.GetValue("InstallLocation")?.ToString(),
                        RegistryKeyPath = subKey.Name,
                        IsWindowsApp = false,
                        Source = DetectInstallSource(subKey, subKeyName),
                    });
                }
                catch
                {
                    // Individual subkey read failure — skip
                }
            }
        }
        catch
        {
            // Key not accessible — skip
        }
    }

    // ── Private: source detection ──────────────────────────────────

    private static InstallSource DetectInstallSource(RegistryKey subKey, string subKeyName)
    {
        // WindowsInstaller flag is the most reliable indicator
        if (subKey.GetValue("WindowsInstaller") is int wi && wi == 1)
            return InstallSource.Msi;

        // MSI products have GUID subkey names like {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
        if (Guid.TryParse(subKeyName, out _))
            return InstallSource.Msi;

        // Check if uninstall string references MsiExec
        var uninstallStr = subKey.GetValue("UninstallString")?.ToString() ?? "";
        if (uninstallStr.Contains("MsiExec", StringComparison.OrdinalIgnoreCase))
            return InstallSource.Msi;

        // Has an uninstall string — EXE-based installer (NSIS, InnoSetup, InstallShield, etc.)
        if (!string.IsNullOrWhiteSpace(uninstallStr))
            return InstallSource.Exe;

        return InstallSource.Unknown;
    }

    // ── Private: uninstall command execution ───────────────────────

    private static bool ExecuteUninstallCommand(string uninstallCmd)
    {
        try
        {
            string fileName;
            string arguments;

            // Handle MsiExec uninstall strings
            if (uninstallCmd.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "msiexec.exe";
                arguments = uninstallCmd["MsiExec.exe".Length..].Trim();
            }
            // Handle quoted paths
            else if (uninstallCmd.StartsWith('"'))
            {
                var endQuote = uninstallCmd.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    fileName = uninstallCmd[1..endQuote];
                    arguments = uninstallCmd[(endQuote + 1)..].Trim();
                }
                else
                {
                    fileName = uninstallCmd.Trim('"');
                    arguments = "";
                }
            }
            else
            {
                var exeIdx = uninstallCmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx >= 0)
                {
                    fileName = uninstallCmd[..(exeIdx + 4)];
                    arguments = uninstallCmd[(exeIdx + 4)..].Trim();
                }
                else
                {
                    fileName = uninstallCmd;
                    arguments = "";
                }
            }

            // Validate the executable before running — prevents arbitrary command execution
            if (!InputSanitiser.IsValidUninstallExecutable(fileName))
                return false;

            // UseShellExecute = false prevents shell interpretation of arguments
            // For msiexec, we hardcode the full system path for safety
            var safeFileName = fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Environment.SystemDirectory, "msiexec.exe")
                : fileName;

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = safeFileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(TimeSpan.FromMinutes(5));
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ── Private: leftover scanning helpers ─────────────────────────

    private static void ScanInstallLocation(InstalledProgram program, List<UninstallLeftover> leftovers)
    {
        if (string.IsNullOrWhiteSpace(program.InstallLocation)) return;

        var installDir = program.InstallLocation.Trim('"');
        if (!Directory.Exists(installDir)) return;

        try
        {
            leftovers.Add(new UninstallLeftover
            {
                Path = installDir,
                Type = LeftoverType.Folder,
                Confidence = LeftoverConfidence.Safe,
                SizeBytes = GetDirectorySize(installDir),
                Description = "Install directory",
            });
        }
        catch { /* access denied */ }
    }

    private static void ScanProgramFiles(List<string> searchTerms, List<UninstallLeftover> leftovers)
    {
        var programFilePaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var pf in programFilePaths)
        {
            if (string.IsNullOrWhiteSpace(pf)) continue;

            foreach (var term in searchTerms)
            {
                var path = Path.Combine(pf, term);
                if (!Directory.Exists(path)) continue;
                if (leftovers.Any(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;

                try
                {
                    leftovers.Add(new UninstallLeftover
                    {
                        Path = path,
                        Type = LeftoverType.Folder,
                        Confidence = LeftoverConfidence.Safe,
                        SizeBytes = GetDirectorySize(path),
                        Description = $"Folder in {System.IO.Path.GetFileName(pf)}",
                    });
                }
                catch { /* access denied */ }
            }
        }
    }

    private static void ScanDataFolders(
        InstalledProgram program,
        List<string> searchTerms,
        List<UninstallLeftover> leftovers)
    {
        var dataLocations = new[]
        {
            (BasePath: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
             DefaultConfidence: LeftoverConfidence.Safe),
            (BasePath: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
             DefaultConfidence: LeftoverConfidence.Safe),
            (BasePath: Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
             DefaultConfidence: LeftoverConfidence.Review),
        };

        foreach (var (basePath, defaultConfidence) in dataLocations)
        {
            if (string.IsNullOrWhiteSpace(basePath)) continue;

            foreach (var term in searchTerms)
            {
                var path = System.IO.Path.Combine(basePath, term);
                if (!Directory.Exists(path)) continue;
                if (leftovers.Any(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;

                try
                {
                    // Publisher-named folders get Caution rating (may contain other apps)
                    var confidence = term.Equals(program.Publisher, StringComparison.OrdinalIgnoreCase)
                        ? LeftoverConfidence.Caution
                        : defaultConfidence;

                    leftovers.Add(new UninstallLeftover
                    {
                        Path = path,
                        Type = LeftoverType.Folder,
                        Confidence = confidence,
                        SizeBytes = GetDirectorySize(path),
                        Description = $"Data in {System.IO.Path.GetFileName(basePath)}",
                        IsSelected = confidence != LeftoverConfidence.Caution,
                    });
                }
                catch { /* access denied */ }
            }
        }
    }

    private static void ScanRegistryLeftover(InstalledProgram program, List<UninstallLeftover> leftovers)
    {
        if (string.IsNullOrWhiteSpace(program.RegistryKeyPath)) return;

        if (TryOpenRegistryKey(program.RegistryKeyPath))
        {
            leftovers.Add(new UninstallLeftover
            {
                Path = program.RegistryKeyPath,
                Type = LeftoverType.RegistryKey,
                Confidence = LeftoverConfidence.Safe,
                Description = "Uninstall registry entry",
            });
        }
    }

    // ── Private: utility methods ───────────────────────────────────

    private static long GetDirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => { try { return f.Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }

    private static bool TryOpenRegistryKey(string fullPath)
    {
        try
        {
            var (root, subPath) = ParseRegistryPath(fullPath);
            if (root is null) return false;

            using var key = root.OpenSubKey(subPath);
            return key is not null;
        }
        catch { return false; }
    }

    private static bool TryDeleteRegistryKey(string fullPath)
    {
        try
        {
            var (root, subPath) = ParseRegistryPath(fullPath);
            if (root is null) return false;

            root.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
            return true;
        }
        catch { return false; }
    }

    private static (RegistryKey? Root, string SubPath) ParseRegistryPath(string fullPath)
    {
        if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            return (WinRegistry.LocalMachine, fullPath["HKEY_LOCAL_MACHINE\\".Length..]);

        if (fullPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            return (WinRegistry.CurrentUser, fullPath["HKEY_CURRENT_USER\\".Length..]);

        return (null, "");
    }

    // ── Shell32 P/Invoke for Recycle Bin ───────────────────────────

    /// <summary>
    /// Sends a file or folder to the Windows Recycle Bin.
    /// Uses SHFileOperation for reliable Recycle Bin integration.
    /// </summary>
    private static bool SendToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0", // double-null terminated (marshaller adds second)
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
        };
        return SHFileOperation(ref op) == 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
}
