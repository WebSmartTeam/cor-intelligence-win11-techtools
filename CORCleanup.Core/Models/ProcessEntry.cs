namespace CORCleanup.Core.Models;

/// <summary>
/// A running process with CPU, memory, and I/O metrics.
/// Snapshot data — not live-updating.
/// </summary>
public sealed class ProcessEntry
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? FilePath { get; init; }
    public required double CpuPercent { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required long PrivateBytes { get; init; }
    public required int ThreadCount { get; init; }
    public required int HandleCount { get; init; }
    public required DateTime StartTime { get; init; }
    public string? UserName { get; init; }
    public bool IsSystem { get; init; }

    // Display helpers
    public string WorkingSetFormatted => ByteFormatter.Format(WorkingSetBytes);
    public string PrivateBytesFormatted => ByteFormatter.Format(PrivateBytes);
    public string CpuFormatted => CpuPercent < 0.1 ? "0" : CpuPercent.ToString("F1");
    public string StartTimeFormatted => StartTime == DateTime.MinValue
        ? "—"
        : StartTime.ToString("dd/MM HH:mm:ss");

    public string CpuLevel => CpuPercent switch
    {
        > 50 => "High",
        > 15 => "Medium",
        _ => "Normal"
    };

    public string MemoryLevel => WorkingSetBytes switch
    {
        > 1024L * 1024 * 1024 => "High",      // >1 GB
        > 256L * 1024 * 1024 => "Medium",      // >256 MB
        _ => "Normal"
    };
}
