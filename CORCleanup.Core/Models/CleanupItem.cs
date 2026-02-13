namespace CORCleanup.Core.Models;

public enum CleanupCategory
{
    WindowsTemp,
    WindowsUpdate,
    WindowsErrorReporting,
    RecycleBin,
    Prefetch,
    ThumbnailCache,
    DnsCache,
    FontCache,
    InstallerTemp,
    UpdateLogs,
    MemoryDumps,
    WindowsLogs,
    BrowserCache,
    BrowserDownloadHistory,
    BrowserSessionData,
    ApplicationTemp
}

public sealed class CleanupItem
{
    public required CleanupCategory Category { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required long EstimatedSizeBytes { get; init; }
    public required bool IsSelectedByDefault { get; init; }
    public bool IsSelected { get; set; }

    public string SizeFormatted => ByteFormatter.Format(EstimatedSizeBytes);
}

public sealed class CleanupResult
{
    public required long TotalBytesFreed { get; init; }
    public required int ItemsCleaned { get; init; }
    public required int ItemsFailed { get; init; }
    public required TimeSpan Duration { get; init; }
    public List<CleanupItemResult> Details { get; init; } = new();

    public string TotalFreedFormatted => ByteFormatter.Format(TotalBytesFreed);
}

public sealed class CleanupItemResult
{
    public required CleanupCategory Category { get; init; }
    public required string DisplayName { get; init; }
    public required long BytesFreed { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
