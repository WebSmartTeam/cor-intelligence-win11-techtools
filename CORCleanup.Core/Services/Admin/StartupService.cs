using System.Runtime.Versioning;
using Microsoft.Win32;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Admin;

/// <summary>
/// Enumerates and manages startup items from Registry Run keys and Startup folder.
/// Does NOT scan Scheduled Tasks or Services — those are separate concerns.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupService : IStartupService
{
    private static readonly (RegistryKey Root, string Path, string Label)[] RegistryPaths =
    {
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run"),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU\\RunOnce"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM\\RunOnce"),
    };

    public Task<List<StartupEntry>> GetStartupItemsAsync() => Task.Run(() =>
    {
        var entries = new List<StartupEntry>();

        // Registry Run/RunOnce keys
        foreach (var (root, path, label) in RegistryPaths)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key is null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString();
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    var filePath = ExtractFilePath(value);
                    var isMicrosoft = IsMicrosoftPath(filePath);

                    entries.Add(new StartupEntry
                    {
                        Name = valueName,
                        FilePath = filePath,
                        Source = label.Contains("RunOnce")
                            ? StartupSource.RegistryRunOnce
                            : StartupSource.RegistryRun,
                        IsEnabled = true, // Present in Run = enabled
                        IsMicrosoft = isMicrosoft,
                        Publisher = isMicrosoft ? "Microsoft" : null,
                        Description = $"Registry: {label}",
                        RegistryPath = $"{label}\\{valueName}"
                    });
                }
            }
            catch
            {
                // Registry key may not be accessible
            }
        }

        // Startup folder items
        var startupFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        };

        foreach (var folder in startupFolders)
        {
            if (!Directory.Exists(folder)) continue;

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var ext = Path.GetExtension(file).ToLowerInvariant();

                // Resolve .lnk shortcuts to their target
                var targetPath = ext == ".lnk" ? ResolveShortcut(file) ?? file : file;

                entries.Add(new StartupEntry
                {
                    Name = name,
                    FilePath = targetPath,
                    Source = StartupSource.StartupFolder,
                    IsEnabled = true,
                    IsMicrosoft = IsMicrosoftPath(targetPath),
                    Description = $"Startup folder: {folder}"
                });
            }
        }

        return entries;
    });

    public Task<bool> SetEnabledAsync(StartupEntry entry, bool enabled) => Task.Run(() =>
    {
        if (entry.Source == StartupSource.StartupFolder)
        {
            // For startup folder items, rename with .disabled extension
            var currentPath = entry.FilePath;
            var disabledPath = currentPath + ".disabled";

            if (enabled && File.Exists(disabledPath))
            {
                File.Move(disabledPath, currentPath);
                return true;
            }
            if (!enabled && File.Exists(currentPath))
            {
                File.Move(currentPath, disabledPath);
                return true;
            }
            return false;
        }

        // For registry entries, move between Run and a "disabled" subkey
        if (entry.RegistryPath is null) return false;

        try
        {
            var isHklm = entry.RegistryPath.StartsWith("HKLM");
            var root = isHklm ? Registry.LocalMachine : Registry.CurrentUser;
            var isRunOnce = entry.Source == StartupSource.RegistryRunOnce;
            var runPath = isRunOnce
                ? @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
                : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

            using var runKey = root.OpenSubKey(runPath, writable: true);
            if (runKey is null) return false;

            if (enabled)
            {
                // Re-add to Run key
                runKey.SetValue(entry.Name, entry.FilePath);
            }
            else
            {
                // Remove from Run key
                runKey.DeleteValue(entry.Name, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    });

    private static string ExtractFilePath(string commandLine)
    {
        // Handle quoted paths: "C:\Program Files\App\app.exe" --args
        if (commandLine.StartsWith('"'))
        {
            var endQuote = commandLine.IndexOf('"', 1);
            return endQuote > 0 ? commandLine[1..endQuote] : commandLine.Trim('"');
        }

        // Unquoted: split at first space before .exe
        var exeIdx = commandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIdx >= 0 ? commandLine[..(exeIdx + 4)] : commandLine;
    }

    private static bool IsMicrosoftPath(string path) =>
        path.Contains(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveShortcut(string lnkPath)
    {
        // Basic .lnk target resolution via COM IShellLink
        // Full implementation requires COM interop — return null to use lnk path as-is
        // The WPF app will add full COM-based resolution
        return null;
    }
}
