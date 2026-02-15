using System.Diagnostics;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Admin;

/// <summary>
/// Enumerates and controls Windows Firewall rules via netsh advfirewall.
/// Provides read access to all inbound/outbound rules and the ability
/// to enable or disable individual rules by name.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FirewallService : IFirewallService
{
    public Task<List<FirewallRule>> GetAllRulesAsync()
    {
        return Task.Run(async () =>
        {
            var output = await RunNetshAsync("advfirewall firewall show rule name=all verbose");
            return ParseRules(output);
        });
    }

    public async Task SetRuleEnabledAsync(string ruleName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            throw new ArgumentException("Rule name must not be empty.", nameof(ruleName));

        // Sanitise: netsh uses quoted rule names; reject embedded quotes to prevent injection
        if (ruleName.Contains('"'))
            throw new ArgumentException("Rule name must not contain double-quote characters.", nameof(ruleName));

        var enableValue = enabled ? "yes" : "no";
        var output = await RunNetshAsync($"advfirewall firewall set rule name=\"{ruleName}\" new enable={enableValue}");

        // netsh returns "No rules match the specified criteria." on failure
        if (output.Contains("No rules match", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Firewall rule '{ruleName}' not found.");
    }

    private static List<FirewallRule> ParseRules(string output)
    {
        var rules = new List<FirewallRule>();
        var lines = output.Split('\n');

        // Each rule block is separated by a blank line (or a line of dashes).
        // Fields within a block are "Key:   Value" format.
        string? name = null;
        string? direction = null;
        string? action = null;
        string? profiles = null;
        string? enabled = null;
        string? protocol = null;
        string? localPort = null;
        string? remotePort = null;
        string? program = null;
        string? description = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Separator or blank line = end of a rule block
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---"))
            {
                if (name is not null)
                {
                    rules.Add(new FirewallRule
                    {
                        Name = name,
                        Direction = direction ?? "Unknown",
                        Action = action ?? "Unknown",
                        Profile = profiles ?? "Any",
                        Enabled = string.Equals(enabled, "Yes", StringComparison.OrdinalIgnoreCase),
                        Protocol = protocol ?? "Any",
                        LocalPort = localPort ?? "Any",
                        RemotePort = remotePort ?? "Any",
                        Program = program ?? "Any",
                        Description = description ?? ""
                    });

                    // Reset for next block
                    name = null;
                    direction = null;
                    action = null;
                    profiles = null;
                    enabled = null;
                    protocol = null;
                    localPort = null;
                    remotePort = null;
                    program = null;
                    description = null;
                }
                continue;
            }

            // Parse "Key:   Value" pairs
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            switch (key)
            {
                case "Rule Name":
                    name = value;
                    break;
                case "Enabled":
                    enabled = value;
                    break;
                case "Direction":
                    direction = value;
                    break;
                case "Profiles":
                    profiles = value;
                    break;
                case "Action":
                    action = value;
                    break;
                case "Protocol":
                    protocol = value;
                    break;
                case "LocalPort":
                    localPort = value;
                    break;
                case "RemotePort":
                    remotePort = value;
                    break;
                case "Program":
                    program = value;
                    break;
                case "Description":
                    description = value;
                    break;
            }
        }

        // Capture the final block if the output does not end with a blank line
        if (name is not null)
        {
            rules.Add(new FirewallRule
            {
                Name = name,
                Direction = direction ?? "Unknown",
                Action = action ?? "Unknown",
                Profile = profiles ?? "Any",
                Enabled = string.Equals(enabled, "Yes", StringComparison.OrdinalIgnoreCase),
                Protocol = protocol ?? "Any",
                LocalPort = localPort ?? "Any",
                RemotePort = remotePort ?? "Any",
                Program = program ?? "Any",
                Description = description ?? ""
            });
        }

        return rules;
    }

    private static async Task<string> RunNetshAsync(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output;
    }
}
