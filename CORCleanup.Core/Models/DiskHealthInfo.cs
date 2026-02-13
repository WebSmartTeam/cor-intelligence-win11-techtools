namespace CORCleanup.Core.Models;

public enum DiskHealthStatus
{
    Good,
    Caution,
    Bad,
    Unknown
}

public sealed class SmartAttribute
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int CurrentValue { get; init; }
    public required int WorstValue { get; init; }
    public required int Threshold { get; init; }
    public required long RawValue { get; init; }
    public DiskHealthStatus Status { get; init; } = DiskHealthStatus.Unknown;
}

public sealed class DiskHealthInfo
{
    public required string Model { get; init; }
    public required string SerialNumber { get; init; }
    public required string FirmwareRevision { get; init; }
    public required long SizeBytes { get; init; }
    public required string InterfaceType { get; init; }
    public required string MediaType { get; init; }
    public required DiskHealthStatus OverallHealth { get; init; }
    public int? TemperatureCelsius { get; init; }
    public long? PowerOnHours { get; init; }
    public int? ReallocatedSectors { get; init; }
    public int? PendingSectors { get; init; }
    public int? WearLevellingPercent { get; init; }
    public List<SmartAttribute> SmartAttributes { get; init; } = new();

    public string SizeFormatted =>
        SizeBytes > 0 ? $"{SizeBytes / (1024.0 * 1024 * 1024):F0} GB" : "N/A";
}
