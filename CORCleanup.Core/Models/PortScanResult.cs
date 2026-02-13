namespace CORCleanup.Core.Models;

public sealed class PortScanResult
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Protocol { get; init; }
    public required bool IsOpen { get; init; }
    public string? ServiceName { get; init; }
    public long ResponseTimeMs { get; init; }

    public string Status => IsOpen ? "Open" : "Closed";
    public string PortDisplay => $"{Port}/{Protocol}";
}

public sealed class LocalPortEntry
{
    public required string Protocol { get; init; }
    public required string LocalAddress { get; init; }
    public required int LocalPort { get; init; }
    public string? RemoteAddress { get; init; }
    public int? RemotePort { get; init; }
    public required string State { get; init; }
    public int? ProcessId { get; init; }
    public string? ProcessName { get; init; }

    public string LocalEndpoint => $"{LocalAddress}:{LocalPort}";
    public string RemoteEndpoint => RemoteAddress is not null ? $"{RemoteAddress}:{RemotePort}" : "";
}
