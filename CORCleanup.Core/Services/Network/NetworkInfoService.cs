using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

[SupportedOSPlatform("windows")]
public sealed class NetworkInfoService : INetworkInfoService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public Task<List<NetworkAdapterInfo>> GetAdaptersAsync()
    {
        var adapters = new List<NetworkAdapterInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Skip loopback and tunnel adapters
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            var ipProps = nic.GetIPProperties();

            var ipv4 = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

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
                IpAddress = ipv4?.Address.ToString(),
                SubnetMask = ipv4?.IPv4Mask?.ToString(),
                Gateway = gateway?.Address.ToString(),
                DnsServers = dnsServers,
                MacAddress = mac,
                SpeedMbps = nic.Speed / 1_000_000
            });
        }

        // Sort: Up first, then by type (Ethernet before Wi-Fi)
        var sorted = adapters
            .OrderByDescending(a => a.Status == "Up")
            .ThenBy(a => a.Type)
            .ToList();

        return Task.FromResult(sorted);
    }

    public async Task<string?> GetPublicIpAsync(CancellationToken ct = default)
    {
        // Try multiple services for reliability
        string[] apis =
        [
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://checkip.amazonaws.com"
        ];

        foreach (var api in apis)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(api, ct);
                var ip = response.Trim();

                // Basic validation — should look like an IP address
                if (System.Net.IPAddress.TryParse(ip, out _))
                    return ip;
            }
            catch
            {
                // Try next API
            }
        }

        return null;
    }
}
