namespace CORCleanup.Core.Models;

public sealed class NetworkAdapterInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public string? IpAddress { get; init; }
    public string? SubnetMask { get; init; }
    public string? Gateway { get; init; }
    public List<string> DnsServers { get; init; } = new();
    public string? MacAddress { get; init; }
    public long SpeedMbps { get; init; }

    public string SpeedDisplay => SpeedMbps >= 1000
        ? $"{SpeedMbps / 1000.0:F1} Gbps"
        : $"{SpeedMbps} Mbps";

    public string DnsDisplay => DnsServers.Count > 0
        ? string.Join(", ", DnsServers)
        : "None";
}
