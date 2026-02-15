namespace CORCleanup.Core.Models;

/// <summary>
/// Represents a device discovered during a network scan.
/// Each instance captures the IP, hostname, MAC address, vendor,
/// online status and response time for a single host on the subnet.
/// </summary>
public sealed class NetworkDevice
{
    /// <summary>IPv4 address of the discovered device.</summary>
    public required string IpAddress { get; init; }

    /// <summary>
    /// Reverse-DNS hostname. Defaults to an em-dash when resolution fails.
    /// </summary>
    public string Hostname { get; set; } = "\u2014";

    /// <summary>
    /// MAC address in colon-separated hex (AA:BB:CC:DD:EE:FF).
    /// Defaults to an em-dash when ARP resolution is unavailable.
    /// </summary>
    public string MacAddress { get; set; } = "\u2014";

    /// <summary>
    /// Hardware manufacturer derived from the OUI (first 3 octets) of the MAC address.
    /// </summary>
    public string Manufacturer { get; set; } = "\u2014";

    /// <summary>Whether the device responded to ICMP echo.</summary>
    public bool IsOnline { get; set; }

    /// <summary>Round-trip ICMP response time in milliseconds.</summary>
    public long ResponseTimeMs { get; set; }

    /// <summary>UTC timestamp when the device was first discovered in this scan.</summary>
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;

    // ----------------------------------------------------------------
    // Display helpers
    // ----------------------------------------------------------------

    /// <summary>Human-readable online/offline label for data-grid binding.</summary>
    public string StatusDisplay => IsOnline ? "Online" : "Offline";

    /// <summary>Formatted response time, or em-dash if offline.</summary>
    public string ResponseTimeDisplay => IsOnline ? $"{ResponseTimeMs} ms" : "\u2014";
}
