using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

/// <summary>
/// Built-in internet speed test using Cloudflare's speed test endpoints.
/// <para>
/// <b>Latency</b>: 10 ICMP pings to cloudflare.com — calculates average and jitter.<br/>
/// <b>Download</b>: Fetches a 25 MB payload from <c>speed.cloudflare.com/__down</c> and measures throughput.<br/>
/// <b>Upload</b>: POSTs a 5 MB random payload to <c>speed.cloudflare.com/__up</c> and measures throughput.
/// </para>
/// <para>
/// No third-party NuGet packages required — uses only built-in <see cref="HttpClient"/>
/// and <see cref="Ping"/>. All network calls are user-initiated (no telemetry).
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SpeedTestService : ISpeedTestService
{
    private const string PingTarget = "cloudflare.com";
    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=25000000"; // 25 MB
    private const string UploadUrl = "https://speed.cloudflare.com/__up";
    private const int UploadSizeBytes = 5_000_000; // 5 MB
    private const int PingSampleCount = 10;
    private const string ServerDisplayName = "Cloudflare (speed.cloudflare.com)";

    private static readonly Lazy<HttpClient> _httpClient = new(() =>
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("COR-Cleanup/1.0");
        return client;
    });

    public async Task<SpeedTestResult> RunTestAsync(
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken ct = default)
    {
        // ============================
        // Phase 1: Latency & Jitter
        // ============================
        progress?.Report(new SpeedTestProgress { Phase = "Testing latency...", PercentComplete = 0 });

        var (latencyMs, jitterMs) = await MeasureLatencyAsync(ct);

        progress?.Report(new SpeedTestProgress { Phase = "Latency complete", PercentComplete = 15 });

        // ============================
        // Phase 2: Download Speed
        // ============================
        progress?.Report(new SpeedTestProgress { Phase = "Testing download...", PercentComplete = 20 });

        var downloadMbps = await MeasureDownloadAsync(progress, ct);

        progress?.Report(new SpeedTestProgress { Phase = "Download complete", PercentComplete = 65 });

        // ============================
        // Phase 3: Upload Speed
        // ============================
        progress?.Report(new SpeedTestProgress { Phase = "Testing upload...", PercentComplete = 70 });

        var uploadMbps = await MeasureUploadAsync(progress, ct);

        progress?.Report(new SpeedTestProgress { Phase = "Test complete", PercentComplete = 100 });

        return new SpeedTestResult
        {
            DownloadMbps = downloadMbps,
            UploadMbps = uploadMbps,
            LatencyMs = latencyMs,
            JitterMs = jitterMs,
            ServerName = ServerDisplayName,
            TestedAt = DateTime.Now
        };
    }

    /// <summary>
    /// Sends <see cref="PingSampleCount"/> ICMP echo requests and computes average
    /// round-trip time and jitter (mean absolute deviation between consecutive samples).
    /// </summary>
    private static async Task<(double AvgMs, double JitterMs)> MeasureLatencyAsync(CancellationToken ct)
    {
        var samples = new List<long>(PingSampleCount);

        for (int i = 0; i < PingSampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(PingTarget, 3000);
                if (reply.Status == IPStatus.Success)
                    samples.Add(reply.RoundtripTime);
            }
            catch (PingException)
            {
                // Skip failed pings — we only need successful samples for the average
            }

            // Brief pause between pings to avoid rate-limiting
            if (i < PingSampleCount - 1)
            {
                try { await Task.Delay(100, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        if (samples.Count == 0)
            return (0, 0);

        double avg = samples.Average();

        // Jitter = mean absolute difference between consecutive samples
        double jitter = 0;
        if (samples.Count > 1)
        {
            double sumDiff = 0;
            for (int i = 1; i < samples.Count; i++)
                sumDiff += Math.Abs(samples[i] - samples[i - 1]);

            jitter = sumDiff / (samples.Count - 1);
        }

        return (Math.Round(avg, 1), Math.Round(jitter, 1));
    }

    /// <summary>
    /// Downloads a 25 MB payload from Cloudflare's speed test CDN and measures
    /// throughput in megabits per second. Reads in 64 KB chunks so we can report
    /// intermediate progress.
    /// </summary>
    private static async Task<double> MeasureDownloadAsync(
        IProgress<SpeedTestProgress>? progress, CancellationToken ct)
    {
        var client = _httpClient.Value;
        var sw = Stopwatch.StartNew();
        long totalBytes = 0;

        using var response = await client.GetAsync(DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? 25_000_000;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[65_536]; // 64 KB read buffer
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            totalBytes += bytesRead;

            // Report download progress (mapped to 20-65% of overall test)
            if (progress is not null && contentLength > 0)
            {
                var downloadPercent = (double)totalBytes / contentLength;
                var overallPercent = 20 + (int)(downloadPercent * 45); // 20% to 65%
                progress.Report(new SpeedTestProgress
                {
                    Phase = $"Downloading... {totalBytes / 1_048_576.0:F1} MB",
                    PercentComplete = Math.Min(overallPercent, 65)
                });
            }
        }

        sw.Stop();

        if (sw.Elapsed.TotalSeconds < 0.001 || totalBytes == 0)
            return 0;

        // Convert bytes/second to megabits/second
        double bytesPerSecond = totalBytes / sw.Elapsed.TotalSeconds;
        return Math.Round(bytesPerSecond * 8 / 1_000_000, 2);
    }

    /// <summary>
    /// Uploads a 5 MB random payload to Cloudflare's speed test endpoint and
    /// measures throughput in megabits per second.
    /// </summary>
    private static async Task<double> MeasureUploadAsync(
        IProgress<SpeedTestProgress>? progress, CancellationToken ct)
    {
        var client = _httpClient.Value;

        // Generate random upload payload
        var payload = new byte[UploadSizeBytes];
        Random.Shared.NextBytes(payload);

        // Use a custom stream to track upload progress
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        progress?.Report(new SpeedTestProgress
        {
            Phase = "Uploading... 0.0 MB",
            PercentComplete = 70
        });

        var sw = Stopwatch.StartNew();

        using var response = await client.PostAsync(UploadUrl, content, ct);

        sw.Stop();

        // Report upload completion
        progress?.Report(new SpeedTestProgress
        {
            Phase = $"Uploaded {UploadSizeBytes / 1_048_576.0:F1} MB",
            PercentComplete = 95
        });

        if (sw.Elapsed.TotalSeconds < 0.001)
            return 0;

        // Convert bytes/second to megabits/second
        double bytesPerSecond = UploadSizeBytes / sw.Elapsed.TotalSeconds;
        return Math.Round(bytesPerSecond * 8 / 1_000_000, 2);
    }
}
