using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

/// <summary>
/// Advanced IP Scanner-style network discovery service.
/// Scans a subnet via ICMP ping sweep, resolves MAC addresses from the
/// ARP cache, performs reverse-DNS hostname lookups, and maps MAC OUI
/// prefixes to hardware manufacturers.
/// </summary>
public interface INetworkScannerService
{
    /// <summary>
    /// Returns all active local network adapters with their IP, subnet mask,
    /// gateway and calculated CIDR notation, suitable for populating a
    /// subnet-selection dropdown.
    /// </summary>
    Task<List<NetworkAdapterInfo>> GetAdaptersWithSubnetsAsync();

    /// <summary>
    /// Performs a full subnet scan, yielding each discovered device as soon
    /// as it responds. Results stream in real-time via <see cref="IAsyncEnumerable{T}"/>
    /// so the UI can update live.
    /// </summary>
    /// <param name="baseIp">
    /// Any IP within the target subnet (e.g. "192.168.1.0" or "192.168.1.100").
    /// </param>
    /// <param name="cidr">CIDR prefix length (e.g. 24 for a /24 subnet).</param>
    /// <param name="ct">Cancellation token to abort the scan.</param>
    /// <returns>An async stream of discovered <see cref="NetworkDevice"/> instances.</returns>
    IAsyncEnumerable<NetworkDevice> ScanSubnetAsync(
        string baseIp,
        int cidr,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the MAC address for a single IP by querying the local ARP cache.
    /// Returns <c>null</c> if the IP is not present in the cache.
    /// </summary>
    Task<string?> GetMacAddressAsync(string ip);

    /// <summary>
    /// Maps a MAC address to its hardware manufacturer using the OUI
    /// (first 3 octets) lookup table.
    /// </summary>
    /// <param name="macAddress">
    /// Colon-separated MAC (e.g. "AA:BB:CC:DD:EE:FF"). Only the first
    /// 3 octets are used for the lookup.
    /// </param>
    /// <returns>Manufacturer name, or "Unknown" if the OUI is not recognised.</returns>
    string LookupVendor(string macAddress);
}
