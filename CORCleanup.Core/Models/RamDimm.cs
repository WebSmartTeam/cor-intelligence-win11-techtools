namespace CORCleanup.Core.Models;

/// <summary>
/// Represents a single physical memory DIMM slot.
/// Populated from WMI Win32_PhysicalMemory.
/// </summary>
public sealed class RamDimm
{
    public required string SlotLabel { get; init; }
    public required long CapacityBytes { get; init; }
    public required uint SpeedMhz { get; init; }
    public required string MemoryType { get; init; }
    public required string Manufacturer { get; init; }
    public required string PartNumber { get; init; }
    public required string SerialNumber { get; init; }
    public required string FormFactor { get; init; }
    public required bool IsEcc { get; init; }

    public string CapacityFormatted =>
        $"{CapacityBytes / (1024.0 * 1024 * 1024):F0} GB";
}

public sealed class RamSummary
{
    public required List<RamDimm> Dimms { get; init; }
    public required int TotalSlots { get; init; }
    public required int UsedSlots { get; init; }
    public required long MaxCapacityBytes { get; init; }
    public required long InstalledCapacityBytes { get; init; }
    public required string ChannelConfig { get; init; }

    public int EmptySlots => TotalSlots - UsedSlots;

    public string MaxCapacityFormatted =>
        $"{MaxCapacityBytes / (1024.0 * 1024 * 1024):F0} GB";

    public string InstalledFormatted =>
        $"{InstalledCapacityBytes / (1024.0 * 1024 * 1024):F0} GB";
}
