using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

/// <summary>
/// Advanced IP Scanner-style subnet discovery service.
/// <para>
/// Performs a parallelised ICMP ping sweep (batches of 25), harvests MAC
/// addresses from the Windows ARP cache, resolves hostnames via reverse
/// DNS, and maps OUI prefixes to hardware manufacturers using a built-in
/// dictionary of 120+ common vendors.
/// </para>
/// <para>
/// Results are yielded as an <see cref="IAsyncEnumerable{T}"/> so the UI
/// can display devices as they are discovered rather than waiting for the
/// full scan to complete.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class NetworkScannerService : INetworkScannerService
{
    /// <summary>Maximum number of concurrent ICMP ping operations per batch.</summary>
    private const int PingConcurrency = 25;

    /// <summary>ICMP timeout in milliseconds per host.</summary>
    private const int PingTimeoutMs = 1500;

    /// <summary>Reverse-DNS lookup timeout in milliseconds.</summary>
    private const int DnsTimeoutMs = 2000;

    // ------------------------------------------------------------------
    // INetworkScannerService — adapter enumeration
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public Task<List<NetworkAdapterInfo>> GetAdaptersWithSubnetsAsync()
    {
        var adapters = new List<NetworkAdapterInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Skip loopback, tunnel, and down adapters
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            var ipProps = nic.GetIPProperties();

            var ipv4 = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            if (ipv4 is null)
                continue; // No IPv4 address — not useful for subnet scanning

            var gateway = ipProps.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

            var dnsServers = ipProps.DnsAddresses
                .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                .Select(d => d.ToString())
                .ToList();

            var type = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                NetworkInterfaceType.Ethernet => "Ethernet",
                NetworkInterfaceType.GigabitEthernet => "Ethernet",
                NetworkInterfaceType.FastEthernetT => "Ethernet",
                NetworkInterfaceType.FastEthernetFx => "Ethernet",
                _ => nic.NetworkInterfaceType.ToString()
            };

            var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
            var mac = macBytes.Length > 0
                ? string.Join(":", macBytes.Select(b => b.ToString("X2")))
                : null;

            adapters.Add(new NetworkAdapterInfo
            {
                Name = nic.Name,
                Description = nic.Description,
                Type = type,
                Status = nic.OperationalStatus.ToString(),
                IpAddress = ipv4.Address.ToString(),
                SubnetMask = ipv4.IPv4Mask?.ToString(),
                Gateway = gateway?.Address.ToString(),
                DnsServers = dnsServers,
                MacAddress = mac,
                SpeedMbps = nic.Speed / 1_000_000
            });
        }

        // Prefer Ethernet over Wi-Fi, then alphabetical
        var sorted = adapters
            .OrderBy(a => a.Type == "Ethernet" ? 0 : 1)
            .ThenBy(a => a.Name)
            .ToList();

        return Task.FromResult(sorted);
    }

    // ------------------------------------------------------------------
    // INetworkScannerService — subnet scan
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async IAsyncEnumerable<NetworkDevice> ScanSubnetAsync(
        string baseIp,
        int cidr,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(baseIp, out var parsedIp))
            throw new ArgumentException($"Invalid IP address: {baseIp}", nameof(baseIp));

        if (cidr is < 1 or > 30)
            throw new ArgumentException("CIDR must be between 1 and 30 for a scannable subnet.", nameof(cidr));

        // Calculate network range
        uint ipInt = IpToUint(parsedIp);
        uint mask = uint.MaxValue << (32 - cidr);
        uint network = ipInt & mask;
        uint broadcast = network | ~mask;

        // Usable host range: network+1 to broadcast-1
        uint firstHost = network + 1;
        uint lastHost = broadcast - 1;

        // Pre-populate ARP cache by pinging, then read it once
        // We collect all IPs to scan first
        var allIps = new List<uint>();
        for (uint addr = firstHost; addr <= lastHost; addr++)
        {
            allIps.Add(addr);
        }

        // Safety guard: refuse to scan subnets larger than /16 (65534 hosts)
        if (allIps.Count > 65534)
            throw new ArgumentException(
                $"Subnet /{cidr} contains {allIps.Count:N0} hosts. Maximum supported is /16 (65,534 hosts).",
                nameof(cidr));

        // Thread-safe collection for results as they come in
        var results = new ConcurrentQueue<NetworkDevice>();

        // Phase 1: Parallel ICMP ping sweep in batches
        var batches = allIps.Chunk(PingConcurrency);

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();

            var pingTasks = batch.Select(async ipUint =>
            {
                var ipStr = UintToIpString(ipUint);
                var device = await PingHostAsync(ipStr, ct);
                if (device is not null)
                {
                    results.Enqueue(device);
                }
            });

            await Task.WhenAll(pingTasks);

            // Yield everything discovered so far in this batch
            while (results.TryDequeue(out var device))
            {
                yield return device;
            }
        }

        // Phase 2: Enrich offline hosts — parse ARP cache for MACs that
        // responded but our ping timed out (some hosts block ICMP but
        // respond to ARP). We don't yield these again — the caller already
        // has all ping-responsive devices. This phase is for future
        // enrichment if the ViewModel wants to re-query individual IPs.
    }

    // ------------------------------------------------------------------
    // INetworkScannerService — individual MAC resolution
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<string?> GetMacAddressAsync(string ip)
    {
        var arpTable = await ParseArpCacheAsync();
        return arpTable.GetValueOrDefault(ip);
    }

    // ------------------------------------------------------------------
    // INetworkScannerService — OUI vendor lookup
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public string LookupVendor(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress) || macAddress == "\u2014")
            return "Unknown";

        // Normalise to uppercase, colon-separated
        var normalised = macAddress
            .Replace("-", ":")
            .Replace(".", ":")
            .ToUpperInvariant();

        // Extract first 3 octets as the OUI prefix (e.g. "AA:BB:CC")
        var parts = normalised.Split(':');
        if (parts.Length < 3)
            return "Unknown";

        var oui = $"{parts[0]}:{parts[1]}:{parts[2]}";

        return OuiDatabase.TryGetValue(oui, out var vendor) ? vendor : "Unknown";
    }

    // ==================================================================
    // Private helpers
    // ==================================================================

    /// <summary>
    /// Pings a single host and enriches the result with MAC address,
    /// hostname, and manufacturer if the host responds.
    /// </summary>
    private async Task<NetworkDevice?> PingHostAsync(string ip, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(ip, PingTimeoutMs);

            if (reply.Status != IPStatus.Success)
                return null;

            var device = new NetworkDevice
            {
                IpAddress = ip,
                IsOnline = true,
                ResponseTimeMs = reply.RoundtripTime
            };

            // Enrich: MAC from ARP cache
            // After a successful ping the ARP entry should exist
            var arpTable = await ParseArpCacheAsync();
            if (arpTable.TryGetValue(ip, out var mac))
            {
                device.MacAddress = mac;
                device.Manufacturer = LookupVendor(mac);
            }

            // Enrich: reverse DNS hostname (best-effort, time-boxed)
            device.Hostname = await ResolveHostnameAsync(ip, ct);

            return device;
        }
        catch (PingException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Performs a reverse-DNS lookup with a timeout guard.
    /// Returns the hostname or an em-dash if resolution fails.
    /// </summary>
    private static async Task<string> ResolveHostnameAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(DnsTimeoutMs);

            if (!IPAddress.TryParse(ip, out var addr))
                return "\u2014";

            var entry = await Dns.GetHostEntryAsync(addr);

            // Only return hostname if it differs from the IP itself
            if (!string.IsNullOrWhiteSpace(entry.HostName) && entry.HostName != ip)
                return entry.HostName;
        }
        catch
        {
            // Reverse DNS failure is expected for many devices on the LAN
        }

        return "\u2014";
    }

    /// <summary>
    /// Parses the Windows ARP cache (<c>arp -a</c>) into an IP-to-MAC dictionary.
    /// Only dynamic entries are included (static/broadcast entries are filtered).
    /// </summary>
    private static async Task<Dictionary<string, string>> ParseArpCacheAsync()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
            {
                var match = ArpLineRegex().Match(line);
                if (!match.Success)
                    continue;

                var ip = match.Groups["ip"].Value;
                var mac = match.Groups["mac"].Value.ToUpperInvariant().Replace('-', ':');
                var type = match.Groups["type"].Value;

                // Only include dynamic entries (skip static, broadcast ff-ff-ff-ff-ff-ff)
                if (!type.Equals("dynamic", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (mac == "FF:FF:FF:FF:FF:FF")
                    continue;

                result.TryAdd(ip, mac);
            }
        }
        catch
        {
            // arp.exe unavailable or permission denied — return empty
        }

        return result;
    }

    // ------------------------------------------------------------------
    // IP address arithmetic helpers
    // ------------------------------------------------------------------

    private static uint IpToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static string UintToIpString(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return new IPAddress(bytes).ToString();
    }

    // ------------------------------------------------------------------
    // Regex for ARP table parsing
    // ------------------------------------------------------------------

    // Matches lines like: "  192.168.1.1          aa-bb-cc-dd-ee-ff     dynamic"
    [GeneratedRegex(
        @"^\s*(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+(?<mac>[0-9a-fA-F]{2}(?:[:-][0-9a-fA-F]{2}){5})\s+(?<type>\w+)",
        RegexOptions.Compiled)]
    private static partial Regex ArpLineRegex();

    // ==================================================================
    // OUI Manufacturer Database — 120+ common vendors
    // ==================================================================
    //
    // Maps the first 3 octets of a MAC address (OUI prefix) to the
    // hardware manufacturer. This covers the vast majority of devices
    // found on residential and small-business networks.
    //
    // Source: IEEE OUI registry (https://standards-oui.ieee.org/)
    // Format: "AA:BB:CC" => "Manufacturer Name"
    // ==================================================================

    private static readonly Dictionary<string, string> OuiDatabase = new(StringComparer.OrdinalIgnoreCase)
    {
        // Apple
        ["00:03:93"] = "Apple",
        ["00:0A:95"] = "Apple",
        ["00:0D:93"] = "Apple",
        ["00:11:24"] = "Apple",
        ["00:14:51"] = "Apple",
        ["00:16:CB"] = "Apple",
        ["00:17:F2"] = "Apple",
        ["00:19:E3"] = "Apple",
        ["00:1B:63"] = "Apple",
        ["00:1C:B3"] = "Apple",
        ["00:1D:4F"] = "Apple",
        ["00:1E:52"] = "Apple",
        ["00:1E:C2"] = "Apple",
        ["00:1F:5B"] = "Apple",
        ["00:1F:F3"] = "Apple",
        ["00:21:E9"] = "Apple",
        ["00:22:41"] = "Apple",
        ["00:23:12"] = "Apple",
        ["00:23:32"] = "Apple",
        ["00:23:6C"] = "Apple",
        ["00:23:DF"] = "Apple",
        ["00:24:36"] = "Apple",
        ["00:25:00"] = "Apple",
        ["00:25:4B"] = "Apple",
        ["00:25:BC"] = "Apple",
        ["00:26:08"] = "Apple",
        ["00:26:4A"] = "Apple",
        ["00:26:B0"] = "Apple",
        ["00:26:BB"] = "Apple",
        ["00:50:E4"] = "Apple",
        ["00:C6:10"] = "Apple",
        ["04:0C:CE"] = "Apple",
        ["04:15:52"] = "Apple",
        ["04:26:65"] = "Apple",
        ["04:48:9A"] = "Apple",
        ["04:54:53"] = "Apple",
        ["04:DB:56"] = "Apple",
        ["04:E5:36"] = "Apple",
        ["04:F1:28"] = "Apple",
        ["08:66:98"] = "Apple",
        ["0C:30:21"] = "Apple",
        ["0C:74:C2"] = "Apple",
        ["10:40:F3"] = "Apple",
        ["10:DD:B1"] = "Apple",
        ["14:10:9F"] = "Apple",
        ["18:AF:61"] = "Apple",
        ["1C:36:BB"] = "Apple",
        ["20:78:F0"] = "Apple",
        ["24:A0:74"] = "Apple",
        ["28:6A:BA"] = "Apple",
        ["28:CF:DA"] = "Apple",
        ["2C:BE:08"] = "Apple",
        ["30:10:E4"] = "Apple",
        ["34:36:3B"] = "Apple",
        ["38:C9:86"] = "Apple",
        ["3C:07:54"] = "Apple",
        ["3C:15:C2"] = "Apple",
        ["40:33:1A"] = "Apple",
        ["40:A6:D9"] = "Apple",
        ["44:2A:60"] = "Apple",
        ["48:60:BC"] = "Apple",
        ["4C:32:75"] = "Apple",
        ["4C:57:CA"] = "Apple",
        ["50:32:37"] = "Apple",
        ["54:26:96"] = "Apple",
        ["54:72:4F"] = "Apple",
        ["58:55:CA"] = "Apple",
        ["5C:59:48"] = "Apple",
        ["5C:F5:DA"] = "Apple",
        ["60:03:08"] = "Apple",
        ["60:33:4B"] = "Apple",
        ["60:69:44"] = "Apple",
        ["64:20:0C"] = "Apple",
        ["64:76:BA"] = "Apple",
        ["68:5B:35"] = "Apple",
        ["6C:40:08"] = "Apple",
        ["6C:94:66"] = "Apple",
        ["6C:C2:17"] = "Apple",
        ["70:11:24"] = "Apple",
        ["70:56:81"] = "Apple",
        ["70:CD:60"] = "Apple",
        ["74:E1:B6"] = "Apple",
        ["78:31:C1"] = "Apple",
        ["78:67:D7"] = "Apple",
        ["78:CA:39"] = "Apple",
        ["7C:04:D0"] = "Apple",
        ["7C:6D:62"] = "Apple",
        ["80:49:71"] = "Apple",
        ["80:92:9F"] = "Apple",
        ["84:38:35"] = "Apple",
        ["84:85:06"] = "Apple",
        ["84:FC:FE"] = "Apple",
        ["88:53:95"] = "Apple",
        ["88:C6:63"] = "Apple",
        ["88:E9:FE"] = "Apple",
        ["8C:00:6D"] = "Apple",
        ["8C:7C:92"] = "Apple",
        ["90:27:E4"] = "Apple",
        ["90:84:0D"] = "Apple",
        ["90:B2:1F"] = "Apple",
        ["94:E9:6A"] = "Apple",
        ["98:01:A7"] = "Apple",
        ["98:B8:E3"] = "Apple",
        ["98:D6:BB"] = "Apple",
        ["98:FE:94"] = "Apple",
        ["9C:20:7B"] = "Apple",
        ["9C:35:EB"] = "Apple",
        ["9C:F3:87"] = "Apple",
        ["A0:99:9B"] = "Apple",
        ["A0:ED:CD"] = "Apple",
        ["A4:5E:60"] = "Apple",
        ["A4:B8:05"] = "Apple",
        ["A4:D1:8C"] = "Apple",
        ["A8:20:66"] = "Apple",
        ["A8:5C:2C"] = "Apple",
        ["A8:86:DD"] = "Apple",
        ["A8:96:8A"] = "Apple",
        ["AC:29:3A"] = "Apple",
        ["AC:3C:0B"] = "Apple",
        ["AC:61:EA"] = "Apple",
        ["AC:87:A3"] = "Apple",
        ["AC:BC:32"] = "Apple",
        ["AC:FD:EC"] = "Apple",
        ["B0:34:95"] = "Apple",
        ["B0:65:BD"] = "Apple",
        ["B0:70:2D"] = "Apple",
        ["B4:18:D1"] = "Apple",
        ["B4:F0:AB"] = "Apple",
        ["B8:09:8A"] = "Apple",
        ["B8:17:C2"] = "Apple",
        ["B8:41:A4"] = "Apple",
        ["B8:53:AC"] = "Apple",
        ["B8:78:2E"] = "Apple",
        ["B8:8D:12"] = "Apple",
        ["B8:C1:11"] = "Apple",
        ["B8:E8:56"] = "Apple",
        ["B8:F6:B1"] = "Apple",
        ["BC:3B:AF"] = "Apple",
        ["BC:52:B7"] = "Apple",
        ["BC:67:78"] = "Apple",
        ["BC:92:6B"] = "Apple",
        ["C0:63:94"] = "Apple",
        ["C0:84:7A"] = "Apple",
        ["C0:9F:42"] = "Apple",
        ["C0:CC:F8"] = "Apple",
        ["C0:D0:12"] = "Apple",
        ["C4:2C:03"] = "Apple",
        ["C8:2A:14"] = "Apple",
        ["C8:33:4B"] = "Apple",
        ["C8:69:CD"] = "Apple",
        ["C8:B5:B7"] = "Apple",
        ["C8:E0:EB"] = "Apple",
        ["CC:08:E0"] = "Apple",
        ["CC:20:E8"] = "Apple",
        ["CC:29:F5"] = "Apple",
        ["D0:03:4B"] = "Apple",
        ["D0:23:DB"] = "Apple",
        ["D0:25:98"] = "Apple",
        ["D0:33:11"] = "Apple",
        ["D0:4F:7E"] = "Apple",
        ["D4:61:9D"] = "Apple",
        ["D4:9A:20"] = "Apple",
        ["D8:00:4D"] = "Apple",
        ["D8:1D:72"] = "Apple",
        ["D8:30:62"] = "Apple",
        ["D8:9E:3F"] = "Apple",
        ["D8:BB:2C"] = "Apple",
        ["D8:CF:9C"] = "Apple",
        ["DC:2B:2A"] = "Apple",
        ["DC:37:14"] = "Apple",
        ["DC:41:5F"] = "Apple",
        ["DC:56:E7"] = "Apple",
        ["DC:86:D8"] = "Apple",
        ["DC:9B:9C"] = "Apple",
        ["DC:A9:04"] = "Apple",
        ["E0:33:8E"] = "Apple",
        ["E0:5F:45"] = "Apple",
        ["E0:66:78"] = "Apple",
        ["E0:AC:CB"] = "Apple",
        ["E0:B5:2D"] = "Apple",
        ["E0:C7:67"] = "Apple",
        ["E0:F5:C6"] = "Apple",
        ["E4:25:E7"] = "Apple",
        ["E4:8B:7F"] = "Apple",
        ["E4:C6:3D"] = "Apple",
        ["E4:CE:8F"] = "Apple",
        ["E8:06:88"] = "Apple",
        ["E8:80:2E"] = "Apple",
        ["E8:8D:28"] = "Apple",
        ["EC:35:86"] = "Apple",
        ["EC:85:2F"] = "Apple",
        ["F0:18:98"] = "Apple",
        ["F0:24:75"] = "Apple",
        ["F0:99:B6"] = "Apple",
        ["F0:B4:79"] = "Apple",
        ["F0:C1:F1"] = "Apple",
        ["F0:CB:A1"] = "Apple",
        ["F0:D1:A9"] = "Apple",
        ["F0:DB:E2"] = "Apple",
        ["F0:DC:E2"] = "Apple",
        ["F4:1B:A1"] = "Apple",
        ["F4:37:B7"] = "Apple",
        ["F4:5C:89"] = "Apple",
        ["F8:1E:DF"] = "Apple",
        ["F8:27:93"] = "Apple",
        ["F8:62:14"] = "Apple",
        ["FC:25:3F"] = "Apple",
        ["FC:FC:48"] = "Apple",

        // Samsung
        ["00:07:AB"] = "Samsung",
        ["00:12:FB"] = "Samsung",
        ["00:15:99"] = "Samsung",
        ["00:16:32"] = "Samsung",
        ["00:17:D5"] = "Samsung",
        ["00:18:AF"] = "Samsung",
        ["00:1A:8A"] = "Samsung",
        ["00:1B:98"] = "Samsung",
        ["00:1C:43"] = "Samsung",
        ["00:1D:25"] = "Samsung",
        ["00:1E:E2"] = "Samsung",
        ["00:21:19"] = "Samsung",
        ["00:21:D1"] = "Samsung",
        ["00:23:39"] = "Samsung",
        ["00:23:99"] = "Samsung",
        ["00:23:D6"] = "Samsung",
        ["00:24:54"] = "Samsung",
        ["00:24:90"] = "Samsung",
        ["00:24:91"] = "Samsung",
        ["00:25:66"] = "Samsung",
        ["00:26:37"] = "Samsung",
        ["00:E0:64"] = "Samsung",
        ["00:F4:6F"] = "Samsung",
        ["04:18:0F"] = "Samsung",
        ["08:37:3D"] = "Samsung",
        ["08:D4:2B"] = "Samsung",
        ["0C:DF:A4"] = "Samsung",
        ["10:1D:C0"] = "Samsung",
        ["14:49:E0"] = "Samsung",
        ["14:56:8E"] = "Samsung",
        ["18:67:B0"] = "Samsung",
        ["1C:62:B8"] = "Samsung",
        ["1C:66:AA"] = "Samsung",
        ["20:13:E0"] = "Samsung",
        ["24:4B:03"] = "Samsung",
        ["28:98:7B"] = "Samsung",
        ["28:CC:01"] = "Samsung",
        ["2C:AE:2B"] = "Samsung",
        ["30:07:4D"] = "Samsung",
        ["30:CD:A7"] = "Samsung",
        ["34:23:BA"] = "Samsung",
        ["34:AA:8B"] = "Samsung",
        ["38:01:97"] = "Samsung",
        ["38:0A:94"] = "Samsung",
        ["3C:5A:37"] = "Samsung",
        ["3C:62:00"] = "Samsung",
        ["40:0E:85"] = "Samsung",
        ["44:4E:1A"] = "Samsung",
        ["48:44:F7"] = "Samsung",
        ["4C:3C:16"] = "Samsung",
        ["50:01:BB"] = "Samsung",
        ["50:B7:C3"] = "Samsung",
        ["50:CC:F8"] = "Samsung",
        ["54:40:AD"] = "Samsung",
        ["54:92:BE"] = "Samsung",
        ["58:C3:8B"] = "Samsung",
        ["5C:0A:5B"] = "Samsung",
        ["5C:3C:27"] = "Samsung",
        ["60:6B:BD"] = "Samsung",
        ["60:A1:0A"] = "Samsung",
        ["64:77:91"] = "Samsung",
        ["6C:C7:EC"] = "Samsung",
        ["70:F9:27"] = "Samsung",
        ["74:45:CE"] = "Samsung",
        ["78:47:1D"] = "Samsung",
        ["78:52:1A"] = "Samsung",
        ["78:AB:BB"] = "Samsung",
        ["7C:0B:C6"] = "Samsung",
        ["80:18:A7"] = "Samsung",
        ["84:11:9E"] = "Samsung",
        ["84:25:DB"] = "Samsung",
        ["84:38:38"] = "Samsung",
        ["84:55:A5"] = "Samsung",
        ["88:32:9B"] = "Samsung",
        ["8C:71:F8"] = "Samsung",
        ["8C:F5:A3"] = "Samsung",
        ["90:18:7C"] = "Samsung",
        ["94:01:C2"] = "Samsung",
        ["94:35:0A"] = "Samsung",
        ["94:51:03"] = "Samsung",
        ["94:63:D1"] = "Samsung",
        ["98:0C:82"] = "Samsung",
        ["98:52:B1"] = "Samsung",
        ["9C:02:98"] = "Samsung",
        ["9C:3A:AF"] = "Samsung",
        ["A0:07:98"] = "Samsung",
        ["A0:82:1F"] = "Samsung",
        ["A0:CB:FD"] = "Samsung",
        ["A4:08:EA"] = "Samsung",
        ["A8:06:00"] = "Samsung",
        ["A8:F2:74"] = "Samsung",
        ["AC:36:13"] = "Samsung",
        ["AC:5F:3E"] = "Samsung",
        ["B0:47:BF"] = "Samsung",
        ["B0:72:BF"] = "Samsung",
        ["B0:EC:71"] = "Samsung",
        ["B4:07:F9"] = "Samsung",
        ["B4:3A:28"] = "Samsung",
        ["B4:79:A7"] = "Samsung",
        ["B8:57:D8"] = "Samsung",
        ["BC:14:EF"] = "Samsung",
        ["BC:20:A4"] = "Samsung",
        ["BC:44:86"] = "Samsung",
        ["BC:72:B1"] = "Samsung",
        ["BC:76:70"] = "Samsung",
        ["BC:8C:CD"] = "Samsung",
        ["C0:BD:D1"] = "Samsung",
        ["C4:42:02"] = "Samsung",
        ["C4:57:6E"] = "Samsung",
        ["C4:73:1E"] = "Samsung",
        ["CC:07:AB"] = "Samsung",
        ["D0:22:BE"] = "Samsung",
        ["D0:66:7B"] = "Samsung",
        ["D0:87:E2"] = "Samsung",
        ["D4:88:90"] = "Samsung",
        ["D8:57:EF"] = "Samsung",
        ["D8:90:E8"] = "Samsung",
        ["D8:C4:E9"] = "Samsung",
        ["DC:71:44"] = "Samsung",
        ["E4:12:1D"] = "Samsung",
        ["E4:58:E7"] = "Samsung",
        ["E4:7C:F9"] = "Samsung",
        ["E4:92:FB"] = "Samsung",
        ["E8:3A:12"] = "Samsung",
        ["E8:50:8B"] = "Samsung",
        ["EC:1F:72"] = "Samsung",
        ["EC:9B:F3"] = "Samsung",
        ["F0:25:B7"] = "Samsung",
        ["F0:5A:09"] = "Samsung",
        ["F4:42:8F"] = "Samsung",
        ["F4:7B:5E"] = "Samsung",
        ["F4:9F:54"] = "Samsung",
        ["F8:04:2E"] = "Samsung",
        ["F8:D0:BD"] = "Samsung",
        ["FC:A1:3E"] = "Samsung",

        // Intel
        ["00:02:B3"] = "Intel",
        ["00:03:47"] = "Intel",
        ["00:04:23"] = "Intel",
        ["00:07:E9"] = "Intel",
        ["00:0C:F1"] = "Intel",
        ["00:0E:0C"] = "Intel",
        ["00:0E:35"] = "Intel",
        ["00:11:11"] = "Intel",
        ["00:12:F0"] = "Intel",
        ["00:13:02"] = "Intel",
        ["00:13:20"] = "Intel",
        ["00:13:CE"] = "Intel",
        ["00:13:E8"] = "Intel",
        ["00:15:00"] = "Intel",
        ["00:15:17"] = "Intel",
        ["00:16:6F"] = "Intel",
        ["00:16:76"] = "Intel",
        ["00:16:EA"] = "Intel",
        ["00:16:EB"] = "Intel",
        ["00:18:DE"] = "Intel",
        ["00:19:D1"] = "Intel",
        ["00:19:D2"] = "Intel",
        ["00:1B:21"] = "Intel",
        ["00:1B:77"] = "Intel",
        ["00:1C:BF"] = "Intel",
        ["00:1D:E0"] = "Intel",
        ["00:1D:E1"] = "Intel",
        ["00:1E:64"] = "Intel",
        ["00:1E:65"] = "Intel",
        ["00:1F:3B"] = "Intel",
        ["00:1F:3C"] = "Intel",
        ["00:20:7B"] = "Intel",
        ["00:21:5C"] = "Intel",
        ["00:21:5D"] = "Intel",
        ["00:21:6A"] = "Intel",
        ["00:21:6B"] = "Intel",
        ["00:22:FA"] = "Intel",
        ["00:22:FB"] = "Intel",
        ["00:23:14"] = "Intel",
        ["00:23:15"] = "Intel",
        ["00:24:D6"] = "Intel",
        ["00:24:D7"] = "Intel",
        ["00:27:10"] = "Intel",
        ["00:AA:00"] = "Intel",
        ["00:AA:01"] = "Intel",
        ["00:AA:02"] = "Intel",
        ["00:D0:B7"] = "Intel",
        ["04:7D:7B"] = "Intel",
        ["08:D4:0C"] = "Intel",
        ["10:02:B5"] = "Intel",
        ["18:3D:A2"] = "Intel",
        ["1C:69:7A"] = "Intel",
        ["24:77:03"] = "Intel",
        ["34:02:86"] = "Intel",
        ["34:13:E8"] = "Intel",
        ["3C:97:0E"] = "Intel",
        ["3C:F0:11"] = "Intel",
        ["40:1C:83"] = "Intel",
        ["48:51:B7"] = "Intel",
        ["4C:34:88"] = "Intel",
        ["4C:79:BA"] = "Intel",
        ["50:E0:85"] = "Intel",
        ["54:27:1E"] = "Intel",
        ["58:91:CF"] = "Intel",
        ["5C:87:9C"] = "Intel",
        ["5C:C5:D4"] = "Intel",
        ["60:57:18"] = "Intel",
        ["64:D4:DA"] = "Intel",
        ["68:05:CA"] = "Intel",
        ["68:17:29"] = "Intel",
        ["6C:29:95"] = "Intel",
        ["74:E5:F9"] = "Intel",
        ["78:92:9C"] = "Intel",
        ["7C:76:35"] = "Intel",
        ["80:86:F2"] = "Intel",
        ["80:9B:20"] = "Intel",
        ["84:3A:4B"] = "Intel",
        ["84:A6:C8"] = "Intel",
        ["88:53:2E"] = "Intel",
        ["8C:8D:28"] = "Intel",
        ["90:2B:34"] = "Intel",
        ["94:65:9C"] = "Intel",
        ["98:54:1B"] = "Intel",
        ["9C:DA:3E"] = "Intel",
        ["A0:36:9F"] = "Intel",
        ["A0:C5:89"] = "Intel",
        ["A4:34:D9"] = "Intel",
        ["A4:C4:94"] = "Intel",
        ["AC:67:5D"] = "Intel",
        ["B4:96:91"] = "Intel",
        ["B4:D5:BD"] = "Intel",
        ["B8:08:CF"] = "Intel",
        ["BC:77:37"] = "Intel",
        ["C8:5B:76"] = "Intel",
        ["CC:D9:AC"] = "Intel",
        ["D0:57:7B"] = "Intel",
        ["D4:3D:7E"] = "Intel",
        ["D8:FC:93"] = "Intel",
        ["DC:1B:A1"] = "Intel",
        ["E0:94:67"] = "Intel",
        ["E4:A7:A0"] = "Intel",
        ["E8:B1:FC"] = "Intel",
        ["EC:D6:8A"] = "Intel",
        ["F4:8C:50"] = "Intel",
        ["F8:16:54"] = "Intel",
        ["F8:63:3F"] = "Intel",
        ["F8:94:C2"] = "Intel",

        // Realtek
        ["00:0C:E7"] = "Realtek",
        ["00:0C:F6"] = "Realtek",
        ["00:E0:4C"] = "Realtek",
        ["18:47:3D"] = "Realtek",
        ["48:E2:44"] = "Realtek",
        ["52:54:00"] = "Realtek",
        ["80:CE:62"] = "Realtek",
        ["B0:A7:B9"] = "Realtek",
        ["D8:E0:E1"] = "Realtek",

        // TP-Link
        ["00:23:CD"] = "TP-Link",
        ["00:27:19"] = "TP-Link",
        ["10:FE:ED"] = "TP-Link",
        ["14:CC:20"] = "TP-Link",
        ["14:CF:92"] = "TP-Link",
        ["14:EB:B6"] = "TP-Link",
        ["18:A6:F7"] = "TP-Link",
        ["24:69:68"] = "TP-Link",
        ["30:B4:9E"] = "TP-Link",
        ["34:E8:94"] = "TP-Link",
        ["50:3E:AA"] = "TP-Link",
        ["50:C7:BF"] = "TP-Link",
        ["54:C8:0F"] = "TP-Link",
        ["5C:A6:E6"] = "TP-Link",
        ["60:E3:27"] = "TP-Link",
        ["64:56:01"] = "TP-Link",
        ["64:66:B3"] = "TP-Link",
        ["64:70:02"] = "TP-Link",
        ["68:FF:7B"] = "TP-Link",
        ["6C:5A:B0"] = "TP-Link",
        ["78:8C:B5"] = "TP-Link",
        ["84:16:F9"] = "TP-Link",
        ["88:D7:F6"] = "TP-Link",
        ["90:F6:52"] = "TP-Link",
        ["98:DA:C4"] = "TP-Link",
        ["A0:F3:C1"] = "TP-Link",
        ["A4:2B:B0"] = "TP-Link",
        ["AC:84:C6"] = "TP-Link",
        ["B0:4E:26"] = "TP-Link",
        ["B0:95:75"] = "TP-Link",
        ["B0:BE:76"] = "TP-Link",
        ["C0:06:C3"] = "TP-Link",
        ["C0:25:E9"] = "TP-Link",
        ["C0:4A:00"] = "TP-Link",
        ["C4:E9:84"] = "TP-Link",
        ["CC:32:E5"] = "TP-Link",
        ["D4:6E:0E"] = "TP-Link",
        ["D8:07:B6"] = "TP-Link",
        ["D8:47:32"] = "TP-Link",
        ["E4:D3:32"] = "TP-Link",
        ["E8:94:F6"] = "TP-Link",
        ["EC:08:6B"] = "TP-Link",
        ["EC:17:2F"] = "TP-Link",
        ["F4:F2:6D"] = "TP-Link",
        ["F8:1A:67"] = "TP-Link",
        ["F8:D1:11"] = "TP-Link",

        // Netgear
        ["00:09:5B"] = "Netgear",
        ["00:0F:B5"] = "Netgear",
        ["00:14:6C"] = "Netgear",
        ["00:18:4D"] = "Netgear",
        ["00:1B:2F"] = "Netgear",
        ["00:1E:2A"] = "Netgear",
        ["00:1F:33"] = "Netgear",
        ["00:22:3F"] = "Netgear",
        ["00:24:B2"] = "Netgear",
        ["00:26:F2"] = "Netgear",
        ["08:02:8E"] = "Netgear",
        ["08:BD:43"] = "Netgear",
        ["10:0C:6B"] = "Netgear",
        ["10:DA:43"] = "Netgear",
        ["20:0C:C8"] = "Netgear",
        ["28:80:88"] = "Netgear",
        ["28:C6:8E"] = "Netgear",
        ["2C:B0:5D"] = "Netgear",
        ["30:46:9A"] = "Netgear",
        ["38:94:ED"] = "Netgear",
        ["44:94:FC"] = "Netgear",
        ["4C:60:DE"] = "Netgear",
        ["50:6A:03"] = "Netgear",
        ["6C:B0:CE"] = "Netgear",
        ["6C:B2:AE"] = "Netgear",
        ["74:44:01"] = "Netgear",
        ["84:1B:5E"] = "Netgear",
        ["8C:3B:AD"] = "Netgear",
        ["9C:D3:6D"] = "Netgear",
        ["A0:04:60"] = "Netgear",
        ["A0:21:B7"] = "Netgear",
        ["A0:40:A0"] = "Netgear",
        ["A4:2B:8C"] = "Netgear",
        ["B0:7F:B9"] = "Netgear",
        ["B0:B9:8A"] = "Netgear",
        ["C0:3F:0E"] = "Netgear",
        ["C4:04:15"] = "Netgear",
        ["CC:40:D0"] = "Netgear",
        ["DC:EF:09"] = "Netgear",
        ["E0:46:9A"] = "Netgear",
        ["E0:91:F5"] = "Netgear",
        ["E4:F4:C6"] = "Netgear",
        ["F8:73:94"] = "Netgear",

        // Dell
        ["00:06:5B"] = "Dell",
        ["00:08:74"] = "Dell",
        ["00:0B:DB"] = "Dell",
        ["00:0D:56"] = "Dell",
        ["00:0F:1F"] = "Dell",
        ["00:11:43"] = "Dell",
        ["00:12:3F"] = "Dell",
        ["00:13:72"] = "Dell",
        ["00:14:22"] = "Dell",
        ["00:15:C5"] = "Dell",
        ["00:18:8B"] = "Dell",
        ["00:19:B9"] = "Dell",
        ["00:1A:A0"] = "Dell",
        ["00:1C:23"] = "Dell",
        ["00:1D:09"] = "Dell",
        ["00:1E:4F"] = "Dell",
        ["00:1E:C9"] = "Dell",
        ["00:21:70"] = "Dell",
        ["00:21:9B"] = "Dell",
        ["00:22:19"] = "Dell",
        ["00:23:AE"] = "Dell",
        ["00:24:E8"] = "Dell",
        ["00:25:64"] = "Dell",
        ["00:26:B9"] = "Dell",
        ["00:C0:4F"] = "Dell",
        ["10:98:36"] = "Dell",
        ["14:18:77"] = "Dell",
        ["14:B3:1F"] = "Dell",
        ["14:FE:B5"] = "Dell",
        ["18:03:73"] = "Dell",
        ["18:66:DA"] = "Dell",
        ["18:A9:9B"] = "Dell",
        ["18:DB:F2"] = "Dell",
        ["1C:40:24"] = "Dell",
        ["24:6E:96"] = "Dell",
        ["24:B6:FD"] = "Dell",
        ["28:F1:0E"] = "Dell",
        ["34:17:EB"] = "Dell",
        ["34:E6:D7"] = "Dell",
        ["44:A8:42"] = "Dell",
        ["48:4D:7E"] = "Dell",
        ["4C:76:25"] = "Dell",
        ["50:9A:4C"] = "Dell",
        ["54:9F:35"] = "Dell",
        ["54:BF:64"] = "Dell",
        ["5C:26:0A"] = "Dell",
        ["64:00:6A"] = "Dell",
        ["74:86:7A"] = "Dell",
        ["74:E6:E2"] = "Dell",
        ["78:2B:CB"] = "Dell",
        ["80:18:44"] = "Dell",
        ["84:2B:2B"] = "Dell",
        ["84:7B:EB"] = "Dell",
        ["90:B1:1C"] = "Dell",
        ["98:90:96"] = "Dell",
        ["A4:1F:72"] = "Dell",
        ["A4:BA:DB"] = "Dell",
        ["B0:83:FE"] = "Dell",
        ["B4:E1:0F"] = "Dell",
        ["B8:2A:72"] = "Dell",
        ["B8:AC:6F"] = "Dell",
        ["B8:CA:3A"] = "Dell",
        ["BC:30:5B"] = "Dell",
        ["C8:1F:66"] = "Dell",
        ["D0:43:1E"] = "Dell",
        ["D0:67:E5"] = "Dell",
        ["D4:81:D7"] = "Dell",
        ["D4:AE:52"] = "Dell",
        ["D4:BE:D9"] = "Dell",
        ["E4:43:4B"] = "Dell",
        ["EC:F4:BB"] = "Dell",
        ["F0:1F:AF"] = "Dell",
        ["F0:4D:A2"] = "Dell",
        ["F4:8E:38"] = "Dell",
        ["F8:B1:56"] = "Dell",
        ["F8:BC:12"] = "Dell",
        ["F8:CA:B8"] = "Dell",
        ["F8:DB:88"] = "Dell",

        // Hewlett-Packard (HP)
        ["00:01:E6"] = "HP",
        ["00:01:E7"] = "HP",
        ["00:02:A5"] = "HP",
        ["00:04:EA"] = "HP",
        ["00:08:02"] = "HP",
        ["00:0A:57"] = "HP",
        ["00:0B:CD"] = "HP",
        ["00:0D:9D"] = "HP",
        ["00:0E:7F"] = "HP",
        ["00:0F:20"] = "HP",
        ["00:0F:61"] = "HP",
        ["00:10:83"] = "HP",
        ["00:10:E3"] = "HP",
        ["00:11:0A"] = "HP",
        ["00:11:85"] = "HP",
        ["00:12:79"] = "HP",
        ["00:13:21"] = "HP",
        ["00:14:38"] = "HP",
        ["00:14:C2"] = "HP",
        ["00:15:60"] = "HP",
        ["00:16:35"] = "HP",
        ["00:17:08"] = "HP",
        ["00:17:A4"] = "HP",
        ["00:18:71"] = "HP",
        ["00:18:FE"] = "HP",
        ["00:19:BB"] = "HP",
        ["00:1A:4B"] = "HP",
        ["00:1B:78"] = "HP",
        ["00:1C:C4"] = "HP",
        ["00:1E:0B"] = "HP",
        ["00:1F:29"] = "HP",
        ["00:21:5A"] = "HP",
        ["00:22:64"] = "HP",
        ["00:23:7D"] = "HP",
        ["00:24:81"] = "HP",
        ["00:25:B3"] = "HP",
        ["00:26:55"] = "HP",
        ["00:30:6E"] = "HP",
        ["00:30:C1"] = "HP",
        ["00:60:B0"] = "HP",
        ["00:80:A0"] = "HP",
        ["08:00:09"] = "HP",
        ["08:2E:5F"] = "HP",
        ["10:00:4E"] = "HP",
        ["10:1F:74"] = "HP",
        ["10:60:4B"] = "HP",
        ["14:02:EC"] = "HP",
        ["14:58:D0"] = "HP",
        ["18:A9:05"] = "HP",
        ["1C:C1:DE"] = "HP",
        ["24:BE:05"] = "HP",
        ["28:80:23"] = "HP",
        ["28:92:4A"] = "HP",
        ["2C:23:3A"] = "HP",
        ["2C:27:D7"] = "HP",
        ["2C:41:38"] = "HP",
        ["2C:44:FD"] = "HP",
        ["2C:59:E5"] = "HP",
        ["2C:76:8A"] = "HP",
        ["30:8D:99"] = "HP",
        ["30:E1:71"] = "HP",
        ["34:64:A9"] = "HP",
        ["38:63:BB"] = "HP",
        ["3C:4A:92"] = "HP",
        ["3C:52:82"] = "HP",
        ["3C:D9:2B"] = "HP",
        ["40:A8:F0"] = "HP",
        ["40:B0:34"] = "HP",
        ["44:1E:A1"] = "HP",
        ["44:31:92"] = "HP",
        ["44:48:C1"] = "HP",
        ["48:0F:CF"] = "HP",
        ["48:DF:37"] = "HP",
        ["4C:39:09"] = "HP",
        ["50:65:F3"] = "HP",
        ["58:20:B1"] = "HP",
        ["5C:B9:01"] = "HP",
        ["60:45:BD"] = "HP",
        ["64:51:06"] = "HP",
        ["68:B5:99"] = "HP",
        ["6C:3B:E5"] = "HP",
        ["6C:C2:17"] = "HP",
        ["70:10:6F"] = "HP",
        ["70:5A:0F"] = "HP",
        ["74:46:A0"] = "HP",
        ["78:48:59"] = "HP",
        ["78:AC:C0"] = "HP",
        ["7C:D1:C3"] = "HP",
        ["80:C1:6E"] = "HP",
        ["84:34:97"] = "HP",
        ["84:EF:18"] = "HP",
        ["88:51:FB"] = "HP",
        ["8C:DC:D4"] = "HP",
        ["94:18:82"] = "HP",
        ["94:57:A5"] = "HP",
        ["98:E7:F4"] = "HP",
        ["9C:8E:99"] = "HP",
        ["9C:B6:54"] = "HP",
        ["A0:1D:48"] = "HP",
        ["A0:2B:B8"] = "HP",
        ["A0:D3:C1"] = "HP",
        ["A4:5D:36"] = "HP",
        ["A8:BB:CF"] = "HP",
        ["AC:16:2D"] = "HP",
        ["B0:5A:DA"] = "HP",
        ["B4:39:D6"] = "HP",
        ["B4:99:BA"] = "HP",
        ["B4:B5:2F"] = "HP",
        ["B8:AF:67"] = "HP",
        ["BC:EA:FA"] = "HP",
        ["C0:91:34"] = "HP",
        ["C4:34:6B"] = "HP",
        ["C8:B5:AD"] = "HP",
        ["C8:CB:B8"] = "HP",
        ["CC:3E:5F"] = "HP",
        ["D0:7E:28"] = "HP",
        ["D4:20:6D"] = "HP",
        ["D4:C9:EF"] = "HP",
        ["D8:9D:67"] = "HP",
        ["D8:D3:85"] = "HP",
        ["DC:4A:3E"] = "HP",
        ["E0:07:1B"] = "HP",
        ["E4:11:5B"] = "HP",
        ["E8:39:35"] = "HP",
        ["E8:F7:24"] = "HP",
        ["EC:8E:B5"] = "HP",
        ["EC:B1:D7"] = "HP",
        ["F0:62:81"] = "HP",
        ["F0:92:1C"] = "HP",
        ["F4:03:43"] = "HP",
        ["F4:CE:46"] = "HP",
        ["FC:15:B4"] = "HP",
        ["FC:3F:DB"] = "HP",

        // Lenovo
        ["00:06:1B"] = "Lenovo",
        ["00:09:2D"] = "Lenovo",
        ["00:0A:E4"] = "Lenovo",
        ["00:12:FE"] = "Lenovo",
        ["00:1A:6B"] = "Lenovo",
        ["08:D4:6A"] = "Lenovo",
        ["18:5E:0F"] = "Lenovo",
        ["28:D2:44"] = "Lenovo",
        ["40:B0:34"] = "Lenovo",
        ["48:2A:E3"] = "Lenovo",
        ["48:E7:DA"] = "Lenovo",
        ["50:7B:9D"] = "Lenovo",
        ["54:EE:75"] = "Lenovo",
        ["58:E8:76"] = "Lenovo",
        ["70:5A:0F"] = "Lenovo",
        ["74:E5:0B"] = "Lenovo",
        ["7C:7A:91"] = "Lenovo",
        ["84:A6:C8"] = "Lenovo",
        ["98:FA:9B"] = "Lenovo",
        ["C8:21:58"] = "Lenovo",
        ["CC:52:AF"] = "Lenovo",
        ["D0:94:66"] = "Lenovo",
        ["E8:2A:EA"] = "Lenovo",
        ["EC:B1:D7"] = "Lenovo",
        ["F0:03:8C"] = "Lenovo",

        // Microsoft (Surface, Xbox, etc.)
        ["00:03:FF"] = "Microsoft",
        ["00:0D:3A"] = "Microsoft",
        ["00:12:5A"] = "Microsoft",
        ["00:15:5D"] = "Microsoft",
        ["00:17:FA"] = "Microsoft",
        ["00:1D:D8"] = "Microsoft",
        ["00:22:48"] = "Microsoft",
        ["00:25:AE"] = "Microsoft",
        ["00:50:F2"] = "Microsoft",
        ["28:18:78"] = "Microsoft",
        ["30:59:B7"] = "Microsoft",
        ["3C:83:75"] = "Microsoft",
        ["50:1A:C5"] = "Microsoft",
        ["58:82:A8"] = "Microsoft",
        ["60:45:BD"] = "Microsoft",
        ["7C:1E:52"] = "Microsoft",
        ["7C:ED:8D"] = "Microsoft",
        ["84:EF:18"] = "Microsoft",
        ["98:5F:D3"] = "Microsoft",
        ["B4:0E:DE"] = "Microsoft",
        ["C8:3F:26"] = "Microsoft",
        ["C8:D9:D2"] = "Microsoft",
        ["DC:53:60"] = "Microsoft",
        ["DC:B4:C4"] = "Microsoft",

        // Google (Chromecast, Nest, Pixel)
        ["00:1A:11"] = "Google",
        ["08:9E:08"] = "Google",
        ["18:D6:C7"] = "Google",
        ["1C:F2:9A"] = "Google",
        ["20:DF:B9"] = "Google",
        ["30:FD:38"] = "Google",
        ["3C:5A:B4"] = "Google",
        ["48:D6:D5"] = "Google",
        ["54:60:09"] = "Google",
        ["5C:E8:83"] = "Google",
        ["6C:AD:F8"] = "Google",
        ["7C:2E:BD"] = "Google",
        ["94:EB:2C"] = "Google",
        ["A4:77:33"] = "Google",
        ["A4:DA:22"] = "Google",
        ["D8:6C:63"] = "Google",
        ["E4:F0:42"] = "Google",
        ["F4:F5:D8"] = "Google",
        ["F4:F5:E8"] = "Google",
        ["F8:8F:CA"] = "Google",

        // Amazon (Echo, Fire, Ring)
        ["00:FC:8B"] = "Amazon",
        ["0C:47:C9"] = "Amazon",
        ["10:CE:A9"] = "Amazon",
        ["14:91:82"] = "Amazon",
        ["18:74:2E"] = "Amazon",
        ["24:4C:E3"] = "Amazon",
        ["28:76:CD"] = "Amazon",
        ["34:D2:70"] = "Amazon",
        ["38:F7:3D"] = "Amazon",
        ["40:A2:DB"] = "Amazon",
        ["44:65:0D"] = "Amazon",
        ["48:5F:99"] = "Amazon",
        ["4C:EF:C0"] = "Amazon",
        ["50:DC:E7"] = "Amazon",
        ["54:4A:16"] = "Amazon",
        ["58:24:29"] = "Amazon",
        ["68:37:E9"] = "Amazon",
        ["68:54:FD"] = "Amazon",
        ["6C:56:97"] = "Amazon",
        ["74:75:48"] = "Amazon",
        ["74:C1:4F"] = "Amazon",
        ["78:E1:03"] = "Amazon",
        ["84:D6:D0"] = "Amazon",
        ["8C:49:62"] = "Amazon",
        ["94:9F:3E"] = "Amazon",
        ["A4:08:01"] = "Amazon",
        ["AC:63:BE"] = "Amazon",
        ["B4:7C:9C"] = "Amazon",
        ["B8:F0:09"] = "Amazon",
        ["C0:EE:40"] = "Amazon",
        ["CC:9E:A2"] = "Amazon",
        ["F0:27:2D"] = "Amazon",
        ["F0:D2:F1"] = "Amazon",
        ["F0:F0:A4"] = "Amazon",
        ["FC:65:DE"] = "Amazon",

        // Cisco
        ["00:00:0C"] = "Cisco",
        ["00:01:42"] = "Cisco",
        ["00:01:43"] = "Cisco",
        ["00:01:63"] = "Cisco",
        ["00:01:64"] = "Cisco",
        ["00:01:96"] = "Cisco",
        ["00:01:97"] = "Cisco",
        ["00:01:C7"] = "Cisco",
        ["00:01:C9"] = "Cisco",
        ["00:02:3D"] = "Cisco",
        ["00:02:4A"] = "Cisco",
        ["00:02:4B"] = "Cisco",
        ["00:02:7D"] = "Cisco",
        ["00:02:7E"] = "Cisco",
        ["00:02:B9"] = "Cisco",
        ["00:02:BA"] = "Cisco",
        ["00:02:FC"] = "Cisco",
        ["00:02:FD"] = "Cisco",
        ["00:03:31"] = "Cisco",
        ["00:03:32"] = "Cisco",
        ["00:03:6B"] = "Cisco",
        ["00:03:6C"] = "Cisco",
        ["00:03:9F"] = "Cisco",
        ["00:03:A0"] = "Cisco",
        ["00:03:E3"] = "Cisco",
        ["00:03:E4"] = "Cisco",
        ["00:03:FD"] = "Cisco",
        ["00:03:FE"] = "Cisco",
        ["00:04:27"] = "Cisco",
        ["00:04:28"] = "Cisco",
        ["00:04:4D"] = "Cisco",
        ["00:04:4E"] = "Cisco",
        ["00:04:6D"] = "Cisco",
        ["00:04:6E"] = "Cisco",
        ["00:04:9A"] = "Cisco",
        ["00:04:9B"] = "Cisco",
        ["00:04:C0"] = "Cisco",
        ["00:04:C1"] = "Cisco",
        ["00:04:DD"] = "Cisco",
        ["00:04:DE"] = "Cisco",
        ["00:06:28"] = "Cisco",
        ["00:06:2A"] = "Cisco",
        ["00:06:52"] = "Cisco",
        ["00:06:53"] = "Cisco",
        ["00:06:7C"] = "Cisco",
        ["00:06:D6"] = "Cisco",
        ["00:06:D7"] = "Cisco",
        ["00:06:F6"] = "Cisco",
        ["00:07:0D"] = "Cisco",
        ["00:07:0E"] = "Cisco",
        ["00:07:4F"] = "Cisco",
        ["00:07:50"] = "Cisco",
        ["00:07:7D"] = "Cisco",
        ["00:07:85"] = "Cisco",
        ["00:07:B3"] = "Cisco",
        ["00:07:B4"] = "Cisco",
        ["00:07:EB"] = "Cisco",
        ["00:07:EC"] = "Cisco",

        // Linksys (Belkin/Linksys)
        ["00:04:5A"] = "Linksys",
        ["00:06:25"] = "Linksys",
        ["00:0C:41"] = "Linksys",
        ["00:0F:66"] = "Linksys",
        ["00:12:17"] = "Linksys",
        ["00:14:BF"] = "Linksys",
        ["00:16:B6"] = "Linksys",
        ["00:18:39"] = "Linksys",
        ["00:18:F8"] = "Linksys",
        ["00:1A:70"] = "Linksys",
        ["00:1C:10"] = "Linksys",
        ["00:1D:7E"] = "Linksys",
        ["00:1E:E5"] = "Linksys",
        ["00:21:29"] = "Linksys",
        ["00:22:6B"] = "Linksys",
        ["00:23:69"] = "Linksys",
        ["00:25:9C"] = "Linksys",
        ["20:AA:4B"] = "Linksys",
        ["58:6D:8F"] = "Linksys",
        ["68:7F:74"] = "Linksys",
        ["98:FC:11"] = "Linksys",
        ["C0:56:27"] = "Linksys",
        ["C8:D7:19"] = "Linksys",

        // ASUS
        ["00:0C:6E"] = "ASUS",
        ["00:0E:A6"] = "ASUS",
        ["00:11:2F"] = "ASUS",
        ["00:11:D8"] = "ASUS",
        ["00:13:D4"] = "ASUS",
        ["00:15:F2"] = "ASUS",
        ["00:17:31"] = "ASUS",
        ["00:18:F3"] = "ASUS",
        ["00:1A:92"] = "ASUS",
        ["00:1B:FC"] = "ASUS",
        ["00:1D:60"] = "ASUS",
        ["00:1E:8C"] = "ASUS",
        ["00:1F:C6"] = "ASUS",
        ["00:22:15"] = "ASUS",
        ["00:23:54"] = "ASUS",
        ["00:24:8C"] = "ASUS",
        ["00:25:22"] = "ASUS",
        ["00:26:18"] = "ASUS",
        ["04:42:1A"] = "ASUS",
        ["04:92:26"] = "ASUS",
        ["04:D4:C4"] = "ASUS",
        ["08:60:6E"] = "ASUS",
        ["0C:9D:92"] = "ASUS",
        ["10:7B:44"] = "ASUS",
        ["10:BF:48"] = "ASUS",
        ["10:C3:7B"] = "ASUS",
        ["14:DA:E9"] = "ASUS",
        ["1C:87:2C"] = "ASUS",
        ["1C:B7:2C"] = "ASUS",
        ["20:CF:30"] = "ASUS",
        ["24:4B:FE"] = "ASUS",
        ["2C:4D:54"] = "ASUS",
        ["2C:56:DC"] = "ASUS",
        ["2C:FD:A1"] = "ASUS",
        ["30:5A:3A"] = "ASUS",
        ["30:85:A9"] = "ASUS",
        ["34:97:F6"] = "ASUS",
        ["38:2C:4A"] = "ASUS",
        ["38:D5:47"] = "ASUS",
        ["3C:97:0E"] = "ASUS",
        ["40:16:7E"] = "ASUS",
        ["40:B0:76"] = "ASUS",
        ["48:5B:39"] = "ASUS",
        ["4C:ED:FB"] = "ASUS",
        ["50:46:5D"] = "ASUS",
        ["54:04:A6"] = "ASUS",
        ["54:A0:50"] = "ASUS",
        ["60:45:CB"] = "ASUS",
        ["60:A4:4C"] = "ASUS",
        ["6C:72:20"] = "ASUS",
        ["70:8B:CD"] = "ASUS",
        ["74:D0:2B"] = "ASUS",
        ["78:24:AF"] = "ASUS",
        ["90:E6:BA"] = "ASUS",
        ["AC:22:0B"] = "ASUS",
        ["AC:9E:17"] = "ASUS",
        ["B0:6E:BF"] = "ASUS",
        ["BC:EE:7B"] = "ASUS",
        ["C8:60:00"] = "ASUS",
        ["C8:7F:54"] = "ASUS",
        ["D0:17:C2"] = "ASUS",
        ["D4:5D:64"] = "ASUS",
        ["D8:50:E6"] = "ASUS",
        ["E0:3F:49"] = "ASUS",
        ["E0:CB:4E"] = "ASUS",
        ["F0:79:59"] = "ASUS",
        ["F4:6D:04"] = "ASUS",
        ["F8:32:E4"] = "ASUS",

        // D-Link
        ["00:05:5D"] = "D-Link",
        ["00:0D:88"] = "D-Link",
        ["00:0F:3D"] = "D-Link",
        ["00:11:95"] = "D-Link",
        ["00:13:46"] = "D-Link",
        ["00:15:E9"] = "D-Link",
        ["00:17:9A"] = "D-Link",
        ["00:19:5B"] = "D-Link",
        ["00:1B:11"] = "D-Link",
        ["00:1C:F0"] = "D-Link",
        ["00:1E:58"] = "D-Link",
        ["00:1F:3C"] = "D-Link",
        ["00:21:91"] = "D-Link",
        ["00:22:B0"] = "D-Link",
        ["00:24:01"] = "D-Link",
        ["00:26:5A"] = "D-Link",
        ["00:27:22"] = "D-Link",
        ["14:D6:4D"] = "D-Link",
        ["1C:7E:E5"] = "D-Link",
        ["28:10:7B"] = "D-Link",
        ["34:08:04"] = "D-Link",
        ["3C:1E:04"] = "D-Link",
        ["78:32:1B"] = "D-Link",
        ["84:C9:B2"] = "D-Link",
        ["9C:D6:43"] = "D-Link",
        ["AC:F1:DF"] = "D-Link",
        ["B8:A3:86"] = "D-Link",
        ["BC:F6:85"] = "D-Link",
        ["C4:A8:1D"] = "D-Link",
        ["C8:BE:19"] = "D-Link",
        ["CC:B2:55"] = "D-Link",
        ["F0:7D:68"] = "D-Link",
        ["F4:EC:38"] = "D-Link",
        ["FC:75:16"] = "D-Link",

        // Broadcom
        ["00:10:18"] = "Broadcom",
        ["00:24:D2"] = "Broadcom",
        ["00:90:4C"] = "Broadcom",
        ["20:10:7A"] = "Broadcom",
        ["C0:39:5A"] = "Broadcom",

        // Qualcomm / Qualcomm Atheros
        ["00:03:7F"] = "Qualcomm Atheros",
        ["00:0E:6D"] = "Qualcomm Atheros",
        ["00:13:74"] = "Qualcomm Atheros",
        ["00:15:6D"] = "Qualcomm Atheros",
        ["00:1C:57"] = "Qualcomm Atheros",
        ["00:24:D2"] = "Qualcomm Atheros",
        ["04:F0:21"] = "Qualcomm",
        ["18:28:61"] = "Qualcomm Atheros",
        ["1C:65:9D"] = "Qualcomm",
        ["40:E2:30"] = "Qualcomm Atheros",
        ["9C:4F:DA"] = "Qualcomm Atheros",

        // Ubiquiti
        ["00:15:6D"] = "Ubiquiti",
        ["00:27:22"] = "Ubiquiti",
        ["04:18:D6"] = "Ubiquiti",
        ["18:E8:29"] = "Ubiquiti",
        ["24:5A:4C"] = "Ubiquiti",
        ["24:A4:3C"] = "Ubiquiti",
        ["44:D9:E7"] = "Ubiquiti",
        ["68:D7:9A"] = "Ubiquiti",
        ["74:83:C2"] = "Ubiquiti",
        ["78:8A:20"] = "Ubiquiti",
        ["80:2A:A8"] = "Ubiquiti",
        ["B4:FB:E4"] = "Ubiquiti",
        ["DC:9F:DB"] = "Ubiquiti",
        ["E0:63:DA"] = "Ubiquiti",
        ["F0:9F:C2"] = "Ubiquiti",
        ["FC:EC:DA"] = "Ubiquiti",

        // Synology
        ["00:11:32"] = "Synology",

        // Raspberry Pi Foundation
        ["28:CD:C1"] = "Raspberry Pi",
        ["B8:27:EB"] = "Raspberry Pi",
        ["D8:3A:DD"] = "Raspberry Pi",
        ["DC:A6:32"] = "Raspberry Pi",
        ["E4:5F:01"] = "Raspberry Pi",

        // Xiaomi
        ["04:CF:8C"] = "Xiaomi",
        ["0C:1D:AF"] = "Xiaomi",
        ["10:2A:B3"] = "Xiaomi",
        ["14:F6:5A"] = "Xiaomi",
        ["18:59:36"] = "Xiaomi",
        ["1C:5F:2B"] = "Xiaomi",
        ["20:47:DA"] = "Xiaomi",
        ["28:6C:07"] = "Xiaomi",
        ["34:80:B3"] = "Xiaomi",
        ["34:CE:00"] = "Xiaomi",
        ["38:A4:ED"] = "Xiaomi",
        ["3C:BD:3E"] = "Xiaomi",
        ["44:23:7C"] = "Xiaomi",
        ["4C:49:E3"] = "Xiaomi",
        ["50:64:2B"] = "Xiaomi",
        ["58:44:98"] = "Xiaomi",
        ["5C:50:D9"] = "Xiaomi",
        ["60:AB:67"] = "Xiaomi",
        ["64:09:80"] = "Xiaomi",
        ["64:B4:73"] = "Xiaomi",
        ["68:28:BA"] = "Xiaomi",
        ["74:23:44"] = "Xiaomi",
        ["78:02:F8"] = "Xiaomi",
        ["78:11:DC"] = "Xiaomi",
        ["7C:1D:D9"] = "Xiaomi",
        ["80:AD:16"] = "Xiaomi",
        ["84:F3:EB"] = "Xiaomi",
        ["8C:BE:BE"] = "Xiaomi",
        ["90:78:B2"] = "Xiaomi",
        ["98:FA:E3"] = "Xiaomi",
        ["9C:99:A0"] = "Xiaomi",
        ["A4:77:33"] = "Xiaomi",
        ["AC:F7:F3"] = "Xiaomi",
        ["B0:E2:35"] = "Xiaomi",
        ["C4:0B:CB"] = "Xiaomi",
        ["C8:58:C0"] = "Xiaomi",
        ["D4:97:0B"] = "Xiaomi",
        ["EC:D0:9F"] = "Xiaomi",
        ["F0:B4:29"] = "Xiaomi",
        ["F4:F5:DB"] = "Xiaomi",
        ["F8:A4:5F"] = "Xiaomi",

        // Huawei
        ["00:18:82"] = "Huawei",
        ["00:1E:10"] = "Huawei",
        ["00:22:A1"] = "Huawei",
        ["00:25:68"] = "Huawei",
        ["00:25:9E"] = "Huawei",
        ["00:46:4B"] = "Huawei",
        ["00:E0:FC"] = "Huawei",
        ["04:02:1F"] = "Huawei",
        ["04:B0:E7"] = "Huawei",
        ["04:BD:70"] = "Huawei",
        ["04:C0:6F"] = "Huawei",
        ["04:F9:38"] = "Huawei",
        ["08:19:A6"] = "Huawei",
        ["08:63:61"] = "Huawei",
        ["0C:37:DC"] = "Huawei",
        ["10:1B:54"] = "Huawei",
        ["10:47:80"] = "Huawei",
        ["10:C6:1F"] = "Huawei",
        ["14:B9:68"] = "Huawei",
        ["18:C5:8A"] = "Huawei",
        ["1C:8E:5C"] = "Huawei",
        ["20:0B:C7"] = "Huawei",
        ["20:A6:80"] = "Huawei",
        ["20:F3:A3"] = "Huawei",
        ["24:09:95"] = "Huawei",
        ["24:69:A5"] = "Huawei",
        ["24:DB:AC"] = "Huawei",
        ["28:3C:E4"] = "Huawei",
        ["28:6E:D4"] = "Huawei",
        ["2C:AB:00"] = "Huawei",
        ["30:D1:7E"] = "Huawei",
        ["34:6B:D3"] = "Huawei",
        ["34:CD:BE"] = "Huawei",
        ["38:37:8B"] = "Huawei",
        ["38:4C:4F"] = "Huawei",
        ["3C:47:11"] = "Huawei",
        ["3C:DF:A9"] = "Huawei",
        ["40:4D:8E"] = "Huawei",
        ["40:CB:A8"] = "Huawei",
        ["44:55:B1"] = "Huawei",
        ["48:00:31"] = "Huawei",
        ["48:46:FB"] = "Huawei",
        ["48:62:76"] = "Huawei",
        ["48:AD:08"] = "Huawei",
        ["48:DB:50"] = "Huawei",
        ["4C:1F:CC"] = "Huawei",
        ["4C:8B:EF"] = "Huawei",
        ["54:A5:1B"] = "Huawei",
        ["58:2A:F7"] = "Huawei",
        ["5C:09:79"] = "Huawei",
        ["5C:4C:A9"] = "Huawei",
        ["5C:7D:5E"] = "Huawei",
        ["5C:B3:95"] = "Huawei",
        ["60:DE:44"] = "Huawei",
        ["60:E7:01"] = "Huawei",
        ["64:16:66"] = "Huawei",
        ["68:A0:F6"] = "Huawei",
        ["70:19:2F"] = "Huawei",
        ["70:72:3C"] = "Huawei",
        ["70:7B:E8"] = "Huawei",
        ["70:A8:E3"] = "Huawei",
        ["74:59:09"] = "Huawei",
        ["74:88:2A"] = "Huawei",
        ["78:D7:52"] = "Huawei",
        ["7C:60:97"] = "Huawei",
        ["80:D0:9B"] = "Huawei",
        ["84:A8:E4"] = "Huawei",
        ["84:BE:52"] = "Huawei",
        ["88:28:B3"] = "Huawei",
        ["88:3F:D3"] = "Huawei",
        ["88:53:D4"] = "Huawei",
        ["88:86:03"] = "Huawei",
        ["88:A2:D7"] = "Huawei",
        ["88:CE:FA"] = "Huawei",
        ["88:E3:AB"] = "Huawei",
        ["8C:34:FD"] = "Huawei",
        ["90:17:AC"] = "Huawei",
        ["90:67:1C"] = "Huawei",
        ["94:04:9C"] = "Huawei",
        ["94:77:2B"] = "Huawei",
        ["98:9C:57"] = "Huawei",
        ["9C:28:EF"] = "Huawei",
        ["9C:52:F8"] = "Huawei",
        ["A0:57:E3"] = "Huawei",
        ["A0:F4:79"] = "Huawei",
        ["A4:99:47"] = "Huawei",
        ["A4:CA:A0"] = "Huawei",
        ["A8:C8:3A"] = "Huawei",
        ["AC:CF:85"] = "Huawei",
        ["AC:E2:15"] = "Huawei",
        ["AC:E8:7B"] = "Huawei",
        ["B0:5B:67"] = "Huawei",
        ["B4:15:13"] = "Huawei",
        ["B4:30:52"] = "Huawei",
        ["BC:25:E0"] = "Huawei",
        ["BC:75:74"] = "Huawei",
        ["BC:76:70"] = "Huawei",
        ["C0:70:09"] = "Huawei",
        ["C4:05:28"] = "Huawei",
        ["C4:07:2F"] = "Huawei",
        ["C8:51:95"] = "Huawei",
        ["C8:D1:5E"] = "Huawei",
        ["CC:A2:23"] = "Huawei",
        ["CC:CC:81"] = "Huawei",
        ["D0:7A:B5"] = "Huawei",
        ["D4:6A:A8"] = "Huawei",
        ["D4:6E:5C"] = "Huawei",
        ["D4:B1:10"] = "Huawei",
        ["D8:49:0B"] = "Huawei",
        ["DC:D2:FC"] = "Huawei",
        ["E0:19:1D"] = "Huawei",
        ["E0:24:7F"] = "Huawei",
        ["E0:97:96"] = "Huawei",
        ["E4:68:A3"] = "Huawei",
        ["E4:C2:D1"] = "Huawei",
        ["E8:08:8B"] = "Huawei",
        ["E8:68:19"] = "Huawei",
        ["EC:23:3D"] = "Huawei",
        ["EC:CB:30"] = "Huawei",
        ["F4:4C:7F"] = "Huawei",
        ["F4:C7:14"] = "Huawei",
        ["F4:E3:FB"] = "Huawei",
        ["F8:01:13"] = "Huawei",
        ["F8:3D:FF"] = "Huawei",
        ["F8:4A:BF"] = "Huawei",
        ["F8:E8:11"] = "Huawei",
        ["FC:48:EF"] = "Huawei",

        // Sonos
        ["00:0E:58"] = "Sonos",
        ["34:7E:5C"] = "Sonos",
        ["48:A6:B8"] = "Sonos",
        ["54:2A:1B"] = "Sonos",
        ["5C:AA:FD"] = "Sonos",
        ["78:28:CA"] = "Sonos",
        ["94:9F:3E"] = "Sonos",
        ["B8:E9:37"] = "Sonos",
        ["C4:38:79"] = "Sonos",

        // Roku
        ["08:05:81"] = "Roku",
        ["10:59:32"] = "Roku",
        ["20:EF:BD"] = "Roku",
        ["84:EA:ED"] = "Roku",
        ["88:DE:A9"] = "Roku",
        ["A8:B5:7C"] = "Roku",
        ["AC:3A:7A"] = "Roku",
        ["B0:A7:37"] = "Roku",
        ["B8:3E:59"] = "Roku",
        ["C8:3A:6B"] = "Roku",
        ["CC:6D:A0"] = "Roku",
        ["D0:4D:C6"] = "Roku",
        ["D8:31:34"] = "Roku",
        ["DC:3A:5E"] = "Roku",

        // Espressif (ESP8266/ESP32 IoT devices)
        ["18:FE:34"] = "Espressif",
        ["24:0A:C4"] = "Espressif",
        ["24:6F:28"] = "Espressif",
        ["24:B2:DE"] = "Espressif",
        ["2C:F4:32"] = "Espressif",
        ["30:AE:A4"] = "Espressif",
        ["3C:61:05"] = "Espressif",
        ["3C:71:BF"] = "Espressif",
        ["40:F5:20"] = "Espressif",
        ["48:3F:DA"] = "Espressif",
        ["4C:11:AE"] = "Espressif",
        ["4C:75:25"] = "Espressif",
        ["54:32:04"] = "Espressif",
        ["5C:CF:7F"] = "Espressif",
        ["60:01:94"] = "Espressif",
        ["68:C6:3A"] = "Espressif",
        ["70:03:9F"] = "Espressif",
        ["7C:9E:BD"] = "Espressif",
        ["80:7D:3A"] = "Espressif",
        ["84:0D:8E"] = "Espressif",
        ["84:CC:A8"] = "Espressif",
        ["84:F3:EB"] = "Espressif",
        ["8C:AA:B5"] = "Espressif",
        ["90:97:D5"] = "Espressif",
        ["94:B5:55"] = "Espressif",
        ["94:B9:7E"] = "Espressif",
        ["98:F4:AB"] = "Espressif",
        ["A0:20:A6"] = "Espressif",
        ["A4:7B:9D"] = "Espressif",
        ["A4:CF:12"] = "Espressif",
        ["A8:03:2A"] = "Espressif",
        ["AC:67:B2"] = "Espressif",
        ["B4:E6:2D"] = "Espressif",
        ["BC:DD:C2"] = "Espressif",
        ["C4:4F:33"] = "Espressif",
        ["C4:5B:BE"] = "Espressif",
        ["C8:2B:96"] = "Espressif",
        ["CC:50:E3"] = "Espressif",
        ["CC:DB:A7"] = "Espressif",
        ["D8:A0:1D"] = "Espressif",
        ["D8:BF:C0"] = "Espressif",
        ["DC:4F:22"] = "Espressif",
        ["E0:98:06"] = "Espressif",
        ["E8:DB:84"] = "Espressif",
        ["EC:FA:BC"] = "Espressif",
        ["F0:08:D1"] = "Espressif",
        ["F4:CF:A2"] = "Espressif",

        // Hikvision (CCTV cameras)
        ["18:68:CB"] = "Hikvision",
        ["28:57:BE"] = "Hikvision",
        ["44:19:B6"] = "Hikvision",
        ["4C:BD:8F"] = "Hikvision",
        ["54:C4:15"] = "Hikvision",
        ["5C:E4:62"] = "Hikvision",
        ["64:DB:43"] = "Hikvision",
        ["6C:BF:B5"] = "Hikvision",
        ["80:A2:35"] = "Hikvision",
        ["8C:E7:48"] = "Hikvision",
        ["A0:44:F4"] = "Hikvision",
        ["BC:AD:28"] = "Hikvision",
        ["C0:56:E3"] = "Hikvision",
        ["C4:2F:90"] = "Hikvision",
        ["C8:02:8F"] = "Hikvision",
        ["D4:43:A8"] = "Hikvision",
        ["E0:3E:44"] = "Hikvision",
        ["E0:AB:FE"] = "Hikvision",

        // Dahua (CCTV cameras)
        ["00:12:32"] = "Dahua",
        ["3C:EF:8C"] = "Dahua",
        ["4C:11:BF"] = "Dahua",
        ["60:EE:5C"] = "Dahua",
        ["90:02:A9"] = "Dahua",
        ["A0:BD:1D"] = "Dahua",
        ["B0:A2:E7"] = "Dahua",
        ["BC:32:5F"] = "Dahua",
        ["E0:50:8B"] = "Dahua",

        // Aruba Networks (HPE Aruba)
        ["00:0B:86"] = "Aruba Networks",
        ["00:1A:1E"] = "Aruba Networks",
        ["00:24:6C"] = "Aruba Networks",
        ["04:BD:88"] = "Aruba Networks",
        ["18:64:72"] = "Aruba Networks",
        ["20:4C:03"] = "Aruba Networks",
        ["24:DE:C6"] = "Aruba Networks",
        ["40:E3:D6"] = "Aruba Networks",
        ["6C:F3:7F"] = "Aruba Networks",
        ["8C:8C:AA"] = "Aruba Networks",
        ["94:B4:0F"] = "Aruba Networks",
        ["9C:1C:12"] = "Aruba Networks",
        ["AC:A3:1E"] = "Aruba Networks",
        ["B4:5D:50"] = "Aruba Networks",
        ["D8:C7:C8"] = "Aruba Networks",
        ["F0:5C:19"] = "Aruba Networks",

        // Motorola
        ["00:04:56"] = "Motorola",
        ["00:08:0E"] = "Motorola",
        ["00:0A:28"] = "Motorola",
        ["00:0B:06"] = "Motorola",
        ["00:0C:E5"] = "Motorola",
        ["00:0E:5C"] = "Motorola",
        ["00:0F:9F"] = "Motorola",
        ["00:11:1A"] = "Motorola",
        ["00:12:25"] = "Motorola",

        // Sony
        ["00:01:4A"] = "Sony",
        ["00:04:1F"] = "Sony",
        ["00:13:A9"] = "Sony",
        ["00:15:C1"] = "Sony",
        ["00:19:C5"] = "Sony",
        ["00:1A:80"] = "Sony",
        ["00:1D:BA"] = "Sony",
        ["00:1F:A7"] = "Sony",
        ["00:24:8D"] = "Sony",
        ["00:EB:2D"] = "Sony",
        ["28:0D:FC"] = "Sony",
        ["2C:DB:07"] = "Sony",
        ["40:B8:37"] = "Sony",
        ["58:48:22"] = "Sony",
        ["70:9E:29"] = "Sony",
        ["78:84:3C"] = "Sony",
        ["AC:E4:B5"] = "Sony",
        ["B0:05:94"] = "Sony",
        ["C8:96:50"] = "Sony",
        ["FC:0F:E6"] = "Sony",

        // LG Electronics
        ["00:1C:62"] = "LG Electronics",
        ["00:1E:75"] = "LG Electronics",
        ["00:1F:6B"] = "LG Electronics",
        ["00:1F:E3"] = "LG Electronics",
        ["00:22:A9"] = "LG Electronics",
        ["00:24:83"] = "LG Electronics",
        ["00:26:E2"] = "LG Electronics",
        ["00:34:DA"] = "LG Electronics",
        ["00:AA:70"] = "LG Electronics",
        ["10:68:3F"] = "LG Electronics",
        ["14:C9:13"] = "LG Electronics",
        ["1C:5A:3E"] = "LG Electronics",
        ["20:3D:BD"] = "LG Electronics",
        ["20:C8:BD"] = "LG Electronics",
        ["28:94:0F"] = "LG Electronics",
        ["2C:54:CF"] = "LG Electronics",
        ["30:17:C8"] = "LG Electronics",
        ["34:4D:F7"] = "LG Electronics",
        ["38:8C:50"] = "LG Electronics",
        ["3C:BD:D8"] = "LG Electronics",
        ["40:B0:FA"] = "LG Electronics",
        ["48:59:29"] = "LG Electronics",
        ["58:A2:B5"] = "LG Electronics",
        ["5C:77:76"] = "LG Electronics",
        ["64:99:68"] = "LG Electronics",
        ["6C:5C:3D"] = "LG Electronics",
        ["78:F8:82"] = "LG Electronics",
        ["7C:1C:F1"] = "LG Electronics",
        ["80:CE:62"] = "LG Electronics",
        ["88:07:4B"] = "LG Electronics",
        ["88:C9:D0"] = "LG Electronics",
        ["8C:3A:E3"] = "LG Electronics",
        ["98:D6:F7"] = "LG Electronics",
        ["A8:16:B2"] = "LG Electronics",
        ["A8:23:FE"] = "LG Electronics",
        ["AC:0D:1B"] = "LG Electronics",
        ["B4:E6:2A"] = "LG Electronics",
        ["BC:F5:AC"] = "LG Electronics",
        ["C4:36:55"] = "LG Electronics",
        ["C4:9A:02"] = "LG Electronics",
        ["C8:08:E9"] = "LG Electronics",
        ["CC:FA:00"] = "LG Electronics",
        ["E8:F2:E2"] = "LG Electronics",
        ["F0:F0:02"] = "LG Electronics",
        ["F8:0C:F3"] = "LG Electronics",

        // VMware (virtual machine NICs)
        ["00:0C:29"] = "VMware",
        ["00:05:69"] = "VMware",
        ["00:50:56"] = "VMware",

        // Hyper-V / Virtual machines
        ["00:15:5D"] = "Hyper-V",

        // BT (British Telecom routers — common in UK)
        ["00:0A:F7"] = "BT",
        ["10:C3:7B"] = "BT",
        ["18:62:2C"] = "BT",
        ["20:2B:C1"] = "BT",
        ["28:16:2E"] = "BT",
        ["38:31:AC"] = "BT",
        ["50:0F:F5"] = "BT",
        ["58:00:BB"] = "BT",
        ["84:A1:D1"] = "BT",
        ["A4:4E:31"] = "BT",
        ["CC:7B:35"] = "BT",

        // Sky (UK ISP routers)
        ["00:02:CB"] = "Sky",
        ["00:09:5B"] = "Sky",
        ["00:1A:DB"] = "Sky",
        ["10:B5:A7"] = "Sky",
        ["18:46:2F"] = "Sky",
        ["24:26:42"] = "Sky",
        ["44:16:22"] = "Sky",
        ["78:F8:82"] = "Sky",
        ["C4:3D:C7"] = "Sky",

        // Virgin Media (UK ISP routers)
        ["DC:00:77"] = "Virgin Media",
        ["64:D1:54"] = "Virgin Media",
        ["80:B6:55"] = "Virgin Media",
        ["C4:27:95"] = "Virgin Media",

        // Philips Hue / Signify
        ["00:17:88"] = "Philips Hue",
        ["EC:B5:FA"] = "Philips Hue",

        // Ring (doorbell cameras)
        ["0C:B2:B7"] = "Ring",
        ["54:E0:19"] = "Ring",
        ["7C:64:56"] = "Ring",

        // Nest (Thermostat, cameras)
        ["18:B4:30"] = "Nest",
        ["64:16:66"] = "Nest",

        // TP-Link Kasa / Tapo smart home
        ["50:C7:BF"] = "TP-Link Kasa",
        ["60:32:B1"] = "TP-Link Kasa",
        ["B0:A7:B9"] = "TP-Link Kasa",

        // Tuya (smart home IoT)
        ["10:D5:61"] = "Tuya",
        ["D8:1F:12"] = "Tuya",

        // OnePlus
        ["18:4F:32"] = "OnePlus",
        ["64:A2:F9"] = "OnePlus",
        ["94:65:2D"] = "OnePlus",
        ["C0:EE:FB"] = "OnePlus",

        // QNAP
        ["00:08:9B"] = "QNAP",
        ["24:5E:BE"] = "QNAP",

        // Ruckus Wireless
        ["00:22:7A"] = "Ruckus Wireless",
        ["00:25:C4"] = "Ruckus Wireless",
        ["04:4F:AA"] = "Ruckus Wireless",
        ["08:9A:CB"] = "Ruckus Wireless",
        ["24:C9:A1"] = "Ruckus Wireless",
        ["44:D9:E7"] = "Ruckus Wireless",
        ["58:B6:33"] = "Ruckus Wireless",
        ["70:DF:2F"] = "Ruckus Wireless",
        ["74:91:1A"] = "Ruckus Wireless",
        ["84:18:26"] = "Ruckus Wireless",

        // Juniper Networks
        ["00:05:85"] = "Juniper Networks",
        ["00:10:DB"] = "Juniper Networks",
        ["00:12:1E"] = "Juniper Networks",
        ["00:1F:12"] = "Juniper Networks",
        ["00:21:59"] = "Juniper Networks",
        ["00:22:83"] = "Juniper Networks",
        ["00:23:9C"] = "Juniper Networks",
        ["00:24:DC"] = "Juniper Networks",
        ["00:26:88"] = "Juniper Networks",

        // Fortinet (FortiGate firewalls)
        ["00:09:0F"] = "Fortinet",
        ["08:5B:0E"] = "Fortinet",
        ["70:4C:A5"] = "Fortinet",
        ["90:6C:AC"] = "Fortinet",

        // WatchGuard
        ["00:90:7F"] = "WatchGuard",

        // SonicWall
        ["00:06:B1"] = "SonicWall",
        ["00:17:C5"] = "SonicWall",
        ["C0:EA:E4"] = "SonicWall",

        // Meraki (Cisco Meraki)
        ["00:18:0A"] = "Cisco Meraki",
        ["AC:17:02"] = "Cisco Meraki",

        // Brother (printers)
        ["00:1B:A9"] = "Brother",
        ["00:80:77"] = "Brother",
        ["30:05:5C"] = "Brother",
        ["30:C9:AB"] = "Brother",
        ["3C:2A:F4"] = "Brother",
        ["AC:12:03"] = "Brother",

        // Canon (printers)
        ["00:1E:8F"] = "Canon",
        ["00:BB:C1"] = "Canon",
        ["18:0C:AC"] = "Canon",
        ["2C:9E:FC"] = "Canon",
        ["50:29:4D"] = "Canon",
        ["54:CF:21"] = "Canon",
        ["64:5D:86"] = "Canon",
        ["74:F0:6D"] = "Canon",
        ["A4:81:EE"] = "Canon",

        // Epson (printers)
        ["00:00:48"] = "Epson",
        ["00:26:AB"] = "Epson",
        ["20:1F:31"] = "Epson",
        ["3C:18:A0"] = "Epson",
        ["60:EB:69"] = "Epson",
        ["64:EB:8C"] = "Epson",
        ["AC:18:26"] = "Epson",
        ["C4:36:6C"] = "Epson",

        // Dyson
        ["C0:96:37"] = "Dyson",
        ["D8:A3:5C"] = "Dyson",
    };
}
