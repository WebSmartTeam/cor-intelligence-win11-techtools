using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services.Admin;

[SupportedOSPlatform("windows")]
public sealed partial class HostsFileService : IHostsFileService
{
    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    // Matches: optional '#' + whitespace + IP + whitespace + hostname + optional inline comment
    [GeneratedRegex(@"^(?<disabled>\#)?\s*(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}|[:\da-fA-F]+)\s+(?<host>\S+)(?:\s+\#\s*(?<comment>.*))?$")]
    private static partial Regex HostEntryPattern();

    public Task<List<HostsEntry>> ReadHostsFileAsync()
    {
        return Task.Run(() =>
        {
            var entries = new List<HostsEntry>();

            if (!File.Exists(HostsPath))
                return entries;

            var lines = File.ReadAllLines(HostsPath, Encoding.UTF8);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Skip pure comment lines (lines that start with # but aren't disabled entries)
                // and blank lines
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var match = HostEntryPattern().Match(line);
                if (!match.Success)
                    continue;

                entries.Add(new HostsEntry
                {
                    IpAddress = match.Groups["ip"].Value,
                    Hostname = match.Groups["host"].Value,
                    Comment = match.Groups["comment"].Success ? match.Groups["comment"].Value.Trim() : null,
                    IsEnabled = !match.Groups["disabled"].Success,
                    LineNumber = i + 1
                });
            }

            return entries;
        });
    }

    public Task SaveHostsFileAsync(IEnumerable<HostsEntry> entries)
    {
        return Task.Run(() =>
        {
            // Read existing file to preserve pure comments and blank lines
            var existingLines = File.Exists(HostsPath)
                ? File.ReadAllLines(HostsPath, Encoding.UTF8).ToList()
                : new List<string>();

            // Build a lookup of entries by hostname for quick matching (last entry wins for duplicates)
            var entryLookup = new Dictionary<string, HostsEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                entryLookup[e.Hostname] = e;
            var usedHostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var output = new List<string>();

            // Update existing lines in-place
            for (int i = 0; i < existingLines.Count; i++)
            {
                var line = existingLines[i];
                var match = HostEntryPattern().Match(line.Trim());

                if (match.Success)
                {
                    var hostname = match.Groups["host"].Value;

                    if (entryLookup.TryGetValue(hostname, out var entry))
                    {
                        output.Add(FormatEntry(entry));
                        usedHostnames.Add(hostname);
                    }
                    // Entry was removed — skip it
                }
                else
                {
                    // Preserve comment lines, blank lines, and other non-entry lines
                    output.Add(line);
                }
            }

            // Append any new entries not found in the original file
            foreach (var entry in entries)
            {
                if (!usedHostnames.Contains(entry.Hostname))
                    output.Add(FormatEntry(entry));
            }

            File.WriteAllLines(HostsPath, output, new UTF8Encoding(false));
        });
    }

    public async Task AddEntryAsync(string ipAddress, string hostname, string? comment = null)
    {
        // Validate inputs to prevent malformed hosts file entries
        if (!InputSanitiser.IsValidHostsIp(ipAddress))
            throw new ArgumentException("Invalid IP address.", nameof(ipAddress));

        if (!InputSanitiser.IsValidHostsHostname(hostname))
            throw new ArgumentException("Invalid hostname. Use alphanumeric characters, hyphens, and dots only.", nameof(hostname));

        var entries = await ReadHostsFileAsync();

        // Check for duplicate hostname
        if (entries.Any(e => e.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Hostname '{hostname}' already exists in the hosts file.");

        entries.Add(new HostsEntry
        {
            IpAddress = ipAddress,
            Hostname = hostname,
            Comment = comment,
            IsEnabled = true,
            LineNumber = 0
        });

        await SaveHostsFileAsync(entries);
    }

    public async Task RemoveEntryAsync(string hostname)
    {
        var entries = await ReadHostsFileAsync();
        var updated = entries.Where(e => !e.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase)).ToList();

        if (updated.Count == entries.Count)
            throw new InvalidOperationException($"Hostname '{hostname}' not found in the hosts file.");

        await SaveHostsFileAsync(updated);
    }

    public async Task ToggleEntryAsync(string hostname)
    {
        var entries = await ReadHostsFileAsync();
        var entry = entries.FirstOrDefault(e => e.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new InvalidOperationException($"Hostname '{hostname}' not found in the hosts file.");

        entry.IsEnabled = !entry.IsEnabled;
        await SaveHostsFileAsync(entries);
    }

    private static string FormatEntry(HostsEntry entry)
    {
        var prefix = entry.IsEnabled ? "" : "# ";
        var line = $"{prefix}{entry.IpAddress,-20}{entry.Hostname}";

        if (!string.IsNullOrWhiteSpace(entry.Comment))
            line += $"  # {entry.Comment}";

        return line;
    }
}
