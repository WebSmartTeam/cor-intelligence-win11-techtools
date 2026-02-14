namespace CORCleanup.Core.Models;

/// <summary>
/// System memory overview — physical RAM and page file usage.
/// </summary>
public sealed class MemoryInfo
{
    public required long TotalPhysicalBytes { get; init; }
    public required long AvailablePhysicalBytes { get; init; }
    public required long TotalPageFileBytes { get; init; }
    public required long AvailablePageFileBytes { get; init; }
    public required long TotalVirtualBytes { get; init; }
    public required long AvailableVirtualBytes { get; init; }
    public required int MemoryLoadPercent { get; init; }

    // Computed
    public long UsedPhysicalBytes => TotalPhysicalBytes - AvailablePhysicalBytes;
    public long UsedPageFileBytes => TotalPageFileBytes - AvailablePageFileBytes;
    public double UsedPhysicalPercent => TotalPhysicalBytes > 0
        ? (double)UsedPhysicalBytes / TotalPhysicalBytes * 100 : 0;
    public double UsedPageFilePercent => TotalPageFileBytes > 0
        ? (double)UsedPageFileBytes / TotalPageFileBytes * 100 : 0;

    // Display
    public string TotalFormatted => ByteFormatter.Format(TotalPhysicalBytes);
    public string AvailableFormatted => ByteFormatter.Format(AvailablePhysicalBytes);
    public string UsedFormatted => ByteFormatter.Format(UsedPhysicalBytes);
    public string PageFileTotalFormatted => ByteFormatter.Format(TotalPageFileBytes);
    public string PageFileUsedFormatted => ByteFormatter.Format(UsedPageFileBytes);

    public string HealthLevel => MemoryLoadPercent switch
    {
        > 90 => "Critical",
        > 75 => "Warning",
        > 50 => "Moderate",
        _ => "Good"
    };
}

/// <summary>
/// A process entry specifically for the memory explorer view — sorted by memory usage.
/// </summary>
public sealed class MemoryConsumer
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required long PrivateBytes { get; init; }
    public required double MemoryPercent { get; init; }

    public string WorkingSetFormatted => ByteFormatter.Format(WorkingSetBytes);
    public string PrivateBytesFormatted => ByteFormatter.Format(PrivateBytes);
    public string PercentFormatted => MemoryPercent.ToString("F1") + "%";
}
