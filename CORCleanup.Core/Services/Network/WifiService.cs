using System.Diagnostics;
using System.Text.RegularExpressions;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services.Network;

/// <summary>
/// Recovers saved Wi-Fi passwords using netsh wlan commands.
/// Requires admin privileges.
/// </summary>
public sealed partial class WifiService : IWifiService
{
    public async Task<List<WifiProfile>> GetSavedProfilesAsync()
    {
        var profiles = new List<WifiProfile>();

        // Get all profile names
        var profileListOutput = await RunNetshAsync("wlan show profiles");
        var profileNames = ProfileNameRegex().Matches(profileListOutput)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        // Get password for each profile
        foreach (var name in profileNames)
        {
            var safeName = InputSanitiser.SanitiseWifiSsid(name);
            var detailOutput = await RunNetshAsync($"wlan show profile name=\"{safeName}\" key=clear");

            var security = SecurityTypeRegex().Match(detailOutput);
            var keyContent = KeyContentRegex().Match(detailOutput);

            profiles.Add(new WifiProfile
            {
                Ssid = name,
                SecurityType = security.Success ? security.Groups[1].Value.Trim() : "Unknown",
                Password = keyContent.Success ? keyContent.Groups[1].Value.Trim() : null
            });
        }

        return profiles;
    }

    private static async Task<string> RunNetshAsync(string arguments)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

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
        var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        return output;
    }

    // "All User Profile     : MyWiFiNetwork"
    [GeneratedRegex(@"All User Profile\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex ProfileNameRegex();

    // "Authentication         : WPA2-Personal"
    [GeneratedRegex(@"Authentication\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex SecurityTypeRegex();

    // "Key Content            : MyPassword123"
    [GeneratedRegex(@"Key Content\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex KeyContentRegex();
}
