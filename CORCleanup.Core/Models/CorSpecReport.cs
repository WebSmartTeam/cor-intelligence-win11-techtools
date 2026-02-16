namespace CORCleanup.Core.Models;

/// <summary>
/// Aggregate model holding all COR Spec sections.
/// Used for display binding and for export/clipboard serialisation.
/// </summary>
public sealed class CorSpecReport
{
    public required string ComputerName { get; init; }
    public required DateTime GeneratedAt { get; init; }

    public SystemInfo? System { get; init; }
    public RamSummary? Ram { get; init; }
    public List<DiskHealthInfo> PhysicalDisks { get; init; } = [];
    public List<LogicalVolumeInfo> LogicalVolumes { get; init; } = [];
    public BatteryInfo? Battery { get; init; }
    public List<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];
    public List<AudioDeviceInfo> AudioDevices { get; init; } = [];

    public bool HasBattery => Battery is { HasBattery: true };
    public bool HasAudio => AudioDevices.Count > 0;
    public bool HasNetwork => NetworkAdapters.Count > 0;
    public bool HasDisks => PhysicalDisks.Count > 0;
    public bool HasVolumes => LogicalVolumes.Count > 0;

    public string GeneratedAtFormatted => GeneratedAt.ToString("dd/MM/yyyy HH:mm");
}
