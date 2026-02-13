using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Tools;

[SupportedOSPlatform("windows")]
public sealed class SoftwareInventoryService : ISoftwareInventoryService
{
    // Registry paths for installed software — NOT Win32_Product (slow + triggers MSI repair)
    private static readonly string[] RegistryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public Task<List<SoftwareEntry>> GetInstalledSoftwareAsync(bool includeSystemComponents = false)
    {
        var entries = new Dictionary<string, SoftwareEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootKey in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in RegistryPaths)
            {
                try
                {
                    using var key = rootKey.OpenSubKey(path);
                    if (key is null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey is null) continue;

                            var displayName = subKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            bool isSystemComponent = (int)(subKey.GetValue("SystemComponent", 0) ?? 0) == 1;
                            if (!includeSystemComponents && isSystemComponent) continue;

                            // Skip Windows updates (KB entries)
                            if (displayName.StartsWith("KB") || displayName.Contains("Security Update"))
                                continue;

                            // Deduplicate by display name
                            if (entries.ContainsKey(displayName)) continue;

                            long estimatedSize = 0;
                            var sizeValue = subKey.GetValue("EstimatedSize");
                            if (sizeValue is int sizeInt)
                                estimatedSize = sizeInt;

                            entries[displayName] = new SoftwareEntry
                            {
                                DisplayName = displayName,
                                Publisher = subKey.GetValue("Publisher") as string,
                                DisplayVersion = subKey.GetValue("DisplayVersion") as string,
                                InstallDate = subKey.GetValue("InstallDate") as string,
                                EstimatedSizeKb = estimatedSize,
                                InstallLocation = subKey.GetValue("InstallLocation") as string,
                                RegistryKey = $@"{rootKey.Name}\{path}\{subKeyName}",
                                IsSystemComponent = isSystemComponent
                            };
                        }
                        catch
                        {
                            // Skip individual entries that fail to read
                        }
                    }
                }
                catch
                {
                    // Skip registry hives that fail to open
                }
            }
        }

        var sorted = entries.Values
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(sorted);
    }

    public async Task ExportToCsvAsync(IEnumerable<SoftwareEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"Name\",\"Publisher\",\"Version\",\"Install Date\",\"Size\",\"Install Location\"");

        foreach (var entry in entries)
        {
            sb.AppendLine(string.Join(",",
                Escape(entry.DisplayName),
                Escape(entry.Publisher ?? ""),
                Escape(entry.DisplayVersion ?? ""),
                Escape(entry.InstallDateFormatted),
                Escape(entry.SizeFormatted),
                Escape(entry.InstallLocation ?? "")));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static string Escape(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
