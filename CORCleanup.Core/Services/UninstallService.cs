using System.Runtime.Versioning;
using Microsoft.Win32;
using WinRegistry = Microsoft.Win32.Registry;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services;

/// <summary>
/// Enumerates installed programs from the registry.
/// DOES NOT use Win32_Product (slow, triggers MSI reconfiguration).
/// Reads from both HKLM and HKCU Uninstall keys (catches per-user installs).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallService : IUninstallService
{
    private static readonly string[] UninstallKeyPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    public Task<List<InstalledProgram>> GetInstalledProgramsAsync() => Task.Run(() =>
    {
        var programs = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // HKLM — system-wide installs
        foreach (var keyPath in UninstallKeyPaths)
        {
            EnumerateUninstallKey(WinRegistry.LocalMachine, keyPath, programs, seen, isWindowsApp: false);
        }

        // HKCU — per-user installs (often missed by other tools)
        foreach (var keyPath in UninstallKeyPaths)
        {
            EnumerateUninstallKey(WinRegistry.CurrentUser, keyPath, programs, seen, isWindowsApp: false);
        }

        return programs
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName))
            .OrderBy(p => p.DisplayName)
            .ToList();
    });

    public Task<bool> UninstallAsync(InstalledProgram program) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(program.UninstallString))
            return false;

        try
        {
            var uninstallCmd = program.UninstallString;
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
    });

    private static void EnumerateUninstallKey(
        RegistryKey root,
        string keyPath,
        List<InstalledProgram> programs,
        HashSet<string> seen,
        bool isWindowsApp)
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
                        InstallLocation = subKey.GetValue("InstallLocation")?.ToString(),
                        RegistryKeyPath = subKey.Name,
                        IsWindowsApp = isWindowsApp
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
}
