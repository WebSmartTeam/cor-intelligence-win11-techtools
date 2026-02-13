namespace CORCleanup.Core.Models;

public sealed class SystemInfo
{
    // OS
    public required string OsEdition { get; init; }
    public required string OsVersion { get; init; }
    public required string OsBuild { get; init; }
    public required DateTime InstallDate { get; init; }
    public required string ComputerName { get; init; }
    public required WindowsEdition Edition { get; init; }

    // CPU
    public required string CpuName { get; init; }
    public required int CpuCores { get; init; }
    public required int CpuThreads { get; init; }
    public required uint CpuMaxClockMhz { get; init; }

    // GPU
    public required string GpuName { get; init; }
    public required string GpuDriverVersion { get; init; }
    public required long GpuVramBytes { get; init; }

    // Motherboard
    public required string MotherboardManufacturer { get; init; }
    public required string MotherboardProduct { get; init; }
    public required string BiosVersion { get; init; }
    public required string BiosDate { get; init; }

    // Memory summary
    public required long TotalPhysicalMemoryBytes { get; init; }

    // Helpers
    public string TotalRamFormatted =>
        $"{TotalPhysicalMemoryBytes / (1024.0 * 1024 * 1024):F1} GB";

    public string GpuVramFormatted =>
        GpuVramBytes > 0 ? $"{GpuVramBytes / (1024.0 * 1024 * 1024):F1} GB" : "N/A";
}
