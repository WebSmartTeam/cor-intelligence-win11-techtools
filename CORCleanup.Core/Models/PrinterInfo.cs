namespace CORCleanup.Core.Models;

public sealed class PrinterInfo
{
    public required string Name { get; init; }
    public string? DriverName { get; init; }
    public string? PortName { get; init; }
    public bool IsDefault { get; init; }
    public bool IsNetwork { get; init; }
    public string Status { get; init; } = "Unknown";
    public int JobCount { get; init; }

    public string TypeDisplay => IsNetwork ? "Network" : "Local";
    public string DefaultDisplay => IsDefault ? "Yes" : "";
    public string JobCountDisplay => JobCount > 0 ? JobCount.ToString() : "";
}
