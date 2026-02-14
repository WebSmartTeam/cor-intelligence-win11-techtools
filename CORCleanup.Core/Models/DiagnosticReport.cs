using System.Text.Json.Serialization;

namespace CORCleanup.Core.Models;

/// <summary>
/// Aggregated system diagnostic report containing data from all diagnostic services.
/// Serialized to JSON for AI consultation via N8N webhook.
/// </summary>
public sealed class DiagnosticReport
{
    // System Identity (ISystemInfoService)
    public required SystemInfo SystemInfo { get; init; }
    public required RamSummary RamSummary { get; init; }
    public required List<DiskHealthInfo> DiskHealth { get; init; }
    public required List<DriverInfo> OutdatedDrivers { get; init; }
    public BatteryInfo? Battery { get; init; }

    // Memory (IMemoryExplorerService)
    public required MemoryInfo MemoryInfo { get; init; }
    public required List<MemoryConsumer> TopMemoryConsumers { get; init; }

    // Processes (IProcessExplorerService)
    public required List<ProcessEntry> TopCpuProcesses { get; init; }

    // Network (INetworkInfoService)
    public required List<NetworkAdapterInfo> NetworkAdapters { get; init; }
    public string? PublicIp { get; init; }

    // Event Log (IEventLogService)
    public required List<EventLogEntry> RecentErrors { get; init; }
    public required List<EventLogEntry> RecentWarnings { get; init; }

    // Cleanup (ICleanupService)
    public required List<CleanupItem> CleanupItems { get; init; }

    // Registry (IRegistryCleanerService)
    public required List<RegistryIssue> RegistryIssues { get; init; }

    // Software (ISoftwareInventoryService)
    public required List<SoftwareEntry> InstalledSoftware { get; init; }

    // AV Health (IAntivirusService)
    public required List<AntivirusProduct> AntivirusProducts { get; init; }

    // Startup (IStartupService)
    public required List<StartupEntry> StartupItems { get; init; }

    // Services (IServicesManagerService)
    public required List<ServiceEntry> RunningServices { get; init; }

    // BSOD History (IBsodViewerService)
    public required List<BsodCrashEntry> RecentCrashes { get; init; }

    // Debloat (IDebloatService)
    public required List<AppxPackageInfo> BloatwareApps { get; init; }

    // Metadata
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public string AppVersion { get; init; } = "1.0.5";

    // Computed summaries for the report card UI
    [JsonIgnore]
    public string MachineName => SystemInfo.ComputerName;

    [JsonIgnore]
    public long TotalCleanableBytes => CleanupItems.Sum(c => c.EstimatedSizeBytes);

    [JsonIgnore]
    public string TotalCleanableFormatted => ByteFormatter.Format(TotalCleanableBytes);

    [JsonIgnore]
    public int TotalIssueCount =>
        RegistryIssues.Count +
        OutdatedDrivers.Count +
        RecentErrors.Count +
        BloatwareApps.Count(b => b.IsInstalled);
}
