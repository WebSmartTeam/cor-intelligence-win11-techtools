namespace CORCleanup.Core.Models;

/// <summary>
/// Final results of a bandwidth speed test including download, upload, latency, and jitter.
/// </summary>
public sealed class SpeedTestResult
{
    public required double DownloadMbps { get; init; }
    public required double UploadMbps { get; init; }
    public required double LatencyMs { get; init; }
    public required double JitterMs { get; init; }
    public required string ServerName { get; init; }
    public required DateTime TestedAt { get; init; }

    /// <summary>Human-readable download speed (e.g. "85.4 Mbps").</summary>
    public string DownloadFormatted => DownloadMbps >= 1000
        ? $"{DownloadMbps / 1000:F2} Gbps"
        : $"{DownloadMbps:F1} Mbps";

    /// <summary>Human-readable upload speed (e.g. "22.1 Mbps").</summary>
    public string UploadFormatted => UploadMbps >= 1000
        ? $"{UploadMbps / 1000:F2} Gbps"
        : $"{UploadMbps:F1} Mbps";
}

/// <summary>
/// Progress reporting during a speed test — phase description and percentage complete.
/// </summary>
public sealed class SpeedTestProgress
{
    public required string Phase { get; init; }
    public required int PercentComplete { get; init; }
}
