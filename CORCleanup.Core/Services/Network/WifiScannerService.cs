using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

/// <summary>
/// Scans for nearby Wi-Fi networks and monitors connected signal strength
/// using netsh wlan commands (available on all Windows versions).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WifiScannerService : IWifiScannerService
{
    public async Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken ct = default)
    {
        var output = await RunNetshAsync("wlan show networks mode=Bssid", ct);
        return ParseScanOutput(output);
    }

    public async IAsyncEnumerable<WifiSignalReading> MonitorSignalAsync(
        int intervalMs = 1000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var output = await RunNetshAsync("wlan show interfaces", ct);
            var reading = ParseInterfaceOutput(output);

            if (reading is not null)
                yield return reading;

            try { await Task.Delay(intervalMs, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    public List<ChannelUsageInfo> GetChannelUsage(List<WifiNetwork> networks)
    {
        return networks
            .GroupBy(n => n.Channel)
            .Select(g => new ChannelUsageInfo
            {
                Channel = g.Key,
                Band = g.First().Band,
                NetworkCount = g.Count(),
                StrongestSignal = g.Max(n => n.SignalPercent)
            })
            .OrderBy(c => c.Channel)
            .ToList();
    }

    // ----------------------------------------------------------------
    // Parsing
    // ----------------------------------------------------------------

    private static List<WifiNetwork> ParseScanOutput(string output)
    {
        var networks = new List<WifiNetwork>();
        var lines = output.Split('\n');

        string currentSsid = "";
        string currentNetworkType = "";
        string currentAuth = "";
        string currentEncryption = "";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            var ssidMatch = SsidRegex().Match(line);
            if (ssidMatch.Success)
            {
                currentSsid = ssidMatch.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(currentSsid))
                    currentSsid = "[Hidden Network]";
                continue;
            }

            var netTypeMatch = NetworkTypeRegex().Match(line);
            if (netTypeMatch.Success)
            {
                currentNetworkType = netTypeMatch.Groups[1].Value.Trim();
                continue;
            }

            var authMatch = AuthRegex().Match(line);
            if (authMatch.Success)
            {
                currentAuth = authMatch.Groups[1].Value.Trim();
                continue;
            }

            var encMatch = EncryptionRegex().Match(line);
            if (encMatch.Success)
            {
                currentEncryption = encMatch.Groups[1].Value.Trim();
                continue;
            }

            var bssidMatch = BssidRegex().Match(line);
            if (bssidMatch.Success)
            {
                var bssid = bssidMatch.Groups[1].Value.Trim();
                int signal = 0;
                string radioType = "";
                int channel = 0;

                // Read the indented lines following BSSID
                for (int j = i + 1; j < lines.Length && j <= i + 5; j++)
                {
                    var subLine = lines[j].Trim();

                    var sigMatch = SignalRegex().Match(subLine);
                    if (sigMatch.Success)
                    {
                        int.TryParse(sigMatch.Groups[1].Value, out signal);
                        continue;
                    }

                    var radioMatch = RadioTypeRegex().Match(subLine);
                    if (radioMatch.Success)
                    {
                        radioType = radioMatch.Groups[1].Value.Trim();
                        continue;
                    }

                    var chanMatch = ChannelRegex().Match(subLine);
                    if (chanMatch.Success)
                    {
                        int.TryParse(chanMatch.Groups[1].Value, out channel);
                        continue;
                    }

                    // Stop if we hit another BSSID or SSID
                    if (BssidRegex().IsMatch(subLine) || SsidRegex().IsMatch(subLine))
                        break;
                }

                networks.Add(new WifiNetwork
                {
                    Ssid = currentSsid,
                    Bssid = bssid,
                    Channel = channel,
                    SignalPercent = signal,
                    RadioType = radioType,
                    Authentication = currentAuth,
                    Encryption = currentEncryption,
                    NetworkType = currentNetworkType
                });
            }
        }

        return networks.OrderByDescending(n => n.SignalPercent).ToList();
    }

    private static WifiSignalReading? ParseInterfaceOutput(string output)
    {
        var signalMatch = SignalRegex().Match(output);
        if (!signalMatch.Success) return null;

        int.TryParse(signalMatch.Groups[1].Value, out var signal);

        var ssidMatch = InterfaceSsidRegex().Match(output);
        var channelMatch = ChannelRegex().Match(output);
        var rxMatch = ReceiveRateRegex().Match(output);
        var txMatch = TransmitRateRegex().Match(output);

        int? channel = channelMatch.Success && int.TryParse(channelMatch.Groups[1].Value, out var ch) ? ch : null;
        int? linkSpeed = null;
        if (rxMatch.Success && int.TryParse(rxMatch.Groups[1].Value, out var rx))
            linkSpeed = rx;
        else if (txMatch.Success && int.TryParse(txMatch.Groups[1].Value, out var tx))
            linkSpeed = tx;

        return new WifiSignalReading
        {
            Timestamp = DateTime.Now,
            SignalPercent = signal,
            LinkSpeedMbps = linkSpeed,
            Channel = channel,
            Ssid = ssidMatch.Success ? ssidMatch.Groups[1].Value.Trim() : null
        };
    }

    // ----------------------------------------------------------------
    // netsh runner
    // ----------------------------------------------------------------

    private static async Task<string> RunNetshAsync(string arguments, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

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
        var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);
        return output;
    }

    // ----------------------------------------------------------------
    // Regex patterns for netsh output
    // ----------------------------------------------------------------

    // "SSID 1 : MyNetwork" or "SSID 12 : MyNetwork"
    [GeneratedRegex(@"^SSID\s+\d+\s*:\s*(.*)", RegexOptions.Compiled)]
    private static partial Regex SsidRegex();

    // "Network type            : Infrastructure"
    [GeneratedRegex(@"Network type\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex NetworkTypeRegex();

    // "Authentication          : WPA2-Personal"
    [GeneratedRegex(@"Authentication\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex AuthRegex();

    // "Encryption              : CCMP"
    [GeneratedRegex(@"Encryption\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex EncryptionRegex();

    // "BSSID 1                 : aa:bb:cc:dd:ee:ff"
    [GeneratedRegex(@"BSSID\s+\d+\s*:\s*([0-9a-fA-F:]+)", RegexOptions.Compiled)]
    private static partial Regex BssidRegex();

    // "Signal             : 85%"
    [GeneratedRegex(@"Signal\s*:\s*(\d+)%", RegexOptions.Compiled)]
    private static partial Regex SignalRegex();

    // "Radio type         : 802.11ac"
    [GeneratedRegex(@"Radio type\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex RadioTypeRegex();

    // "Channel            : 36"
    [GeneratedRegex(@"Channel\s*:\s*(\d+)", RegexOptions.Compiled)]
    private static partial Regex ChannelRegex();

    // Interface-specific: "    SSID                   : MyNetwork" (leading spaces, no number)
    [GeneratedRegex(@"^\s+SSID\s*:\s*(.+)", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex InterfaceSsidRegex();

    // "Receive rate (Mbps)    : 866"
    [GeneratedRegex(@"Receive rate\s*\(Mbps\)\s*:\s*(\d+)", RegexOptions.Compiled)]
    private static partial Regex ReceiveRateRegex();

    // "Transmit rate (Mbps)   : 866"
    [GeneratedRegex(@"Transmit rate\s*\(Mbps\)\s*:\s*(\d+)", RegexOptions.Compiled)]
    private static partial Regex TransmitRateRegex();
}
