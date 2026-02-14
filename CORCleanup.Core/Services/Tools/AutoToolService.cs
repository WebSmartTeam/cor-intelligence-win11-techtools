using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Tools;

[SupportedOSPlatform("windows")]
public sealed class AutoToolService : IAutoToolService
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IMemoryExplorerService _memoryExplorer;
    private readonly IProcessExplorerService _processExplorer;
    private readonly INetworkInfoService _networkInfo;
    private readonly IEventLogService _eventLog;
    private readonly ICleanupService _cleanup;
    private readonly IRegistryCleanerService _registryCleaner;
    private readonly ISoftwareInventoryService _softwareInventory;
    private readonly IAntivirusService _antivirus;
    private readonly IStartupService _startup;
    private readonly IServicesManagerService _servicesManager;
    private readonly IBsodViewerService _bsodViewer;
    private readonly IDebloatService _debloat;
    private readonly ISystemRepairService _systemRepair;
    private readonly IPrinterService _printerService;

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private const string WebhookUrl = "https://n8n.corsolutions.co.uk/webhook/cor-cleanup-autotool";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public AutoToolService(
        ISystemInfoService systemInfo,
        IMemoryExplorerService memoryExplorer,
        IProcessExplorerService processExplorer,
        INetworkInfoService networkInfo,
        IEventLogService eventLog,
        ICleanupService cleanup,
        IRegistryCleanerService registryCleaner,
        ISoftwareInventoryService softwareInventory,
        IAntivirusService antivirus,
        IStartupService startup,
        IServicesManagerService servicesManager,
        IBsodViewerService bsodViewer,
        IDebloatService debloat,
        ISystemRepairService systemRepair,
        IPrinterService printerService)
    {
        _systemInfo = systemInfo;
        _memoryExplorer = memoryExplorer;
        _processExplorer = processExplorer;
        _networkInfo = networkInfo;
        _eventLog = eventLog;
        _cleanup = cleanup;
        _registryCleaner = registryCleaner;
        _softwareInventory = softwareInventory;
        _antivirus = antivirus;
        _startup = startup;
        _servicesManager = servicesManager;
        _bsodViewer = bsodViewer;
        _debloat = debloat;
        _systemRepair = systemRepair;
        _printerService = printerService;
    }

    // ================================================================
    // Diagnostics — fire all read-only scans in parallel
    // ================================================================

    public async Task<DiagnosticReport> RunDiagnosticsAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Default values for each section in case of failure
        SystemInfo? sysInfo = null;
        RamSummary? ramSummary = null;
        List<DiskHealthInfo> diskHealth = new();
        List<DriverInfo> outdatedDrivers = new();
        BatteryInfo? battery = null;
        MemoryInfo? memInfo = null;
        List<MemoryConsumer> topMemory = new();
        List<ProcessEntry> topCpu = new();
        List<NetworkAdapterInfo> adapters = new();
        string? publicIp = null;
        List<EventLogEntry> errors = new();
        List<EventLogEntry> warnings = new();
        List<CleanupItem> cleanupItems = new();
        List<RegistryIssue> registryIssues = new();
        List<SoftwareEntry> software = new();
        List<AntivirusProduct> avProducts = new();
        List<StartupEntry> startupItems = new();
        List<ServiceEntry> services = new();
        List<BsodCrashEntry> crashes = new();
        List<AppxPackageInfo> bloatware = new();

        // Fire all diagnostic tasks in parallel — each with its own try/catch
        var tasks = new List<Task>();

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Scanning system information...");
                sysInfo = await _systemInfo.GetSystemInfoAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Scanning RAM configuration...");
                ramSummary = await _systemInfo.GetRamSummaryAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Checking disk health...");
                diskHealth = await _systemInfo.GetDiskHealthAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Checking for outdated drivers...");
                outdatedDrivers = await _systemInfo.GetOutdatedDriversAsync(3);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                battery = await _systemInfo.GetBatteryInfoAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Analysing memory usage...");
                memInfo = await _memoryExplorer.GetMemoryInfoAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                topMemory = await _memoryExplorer.GetTopConsumersAsync(10, ct);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Sampling CPU processes...");
                topCpu = await _processExplorer.GetProcessesAsync(ct);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Detecting network adapters...");
                adapters = await _networkInfo.GetAdaptersAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                publicIp = await _networkInfo.GetPublicIpAsync(ct);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Reading event log errors...");
                errors = await _eventLog.GetRecentEventsAsync(30, EventSeverity.Error);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                warnings = await _eventLog.GetRecentEventsAsync(30, EventSeverity.Warning);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Scanning for cleanable files...");
                cleanupItems = await _cleanup.ScanAsync(ct);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Scanning registry for issues...");
                registryIssues = await _registryCleaner.ScanAsync(cancellationToken: ct);
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Inventorying installed software...");
                software = await _softwareInventory.GetInstalledSoftwareAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Scanning antivirus products...");
                avProducts = await _antivirus.ScanAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                startupItems = await _startup.GetStartupItemsAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                services = await _servicesManager.GetServicesAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                crashes = await _bsodViewer.GetCrashEntriesAsync();
            }
            catch { }
        }, ct));

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                progress?.Report("Checking for bloatware...");
                bloatware = await _debloat.GetBloatwareListAsync();
            }
            catch { }
        }, ct));

        await Task.WhenAll(tasks);
        progress?.Report("Compiling diagnostic report...");

        // Provide fallbacks for required fields that may have failed
        sysInfo ??= new SystemInfo
        {
            ComputerName = Environment.MachineName,
            OsEdition = Environment.OSVersion.ToString(),
            OsVersion = Environment.OSVersion.Version.ToString(),
            OsBuild = "Unknown",
            InstallDate = DateTime.MinValue,
            Edition = WindowsEdition.Unknown,
            CpuName = "Unknown",
            CpuCores = Environment.ProcessorCount,
            CpuThreads = Environment.ProcessorCount,
            CpuMaxClockMhz = 0,
            GpuName = "Unknown",
            GpuDriverVersion = "Unknown",
            GpuVramBytes = 0,
            MotherboardManufacturer = "Unknown",
            MotherboardProduct = "Unknown",
            BiosVersion = "Unknown",
            BiosDate = "Unknown",
            TotalPhysicalMemoryBytes = 0
        };

        ramSummary ??= new RamSummary
        {
            Dimms = new(),
            TotalSlots = 0,
            UsedSlots = 0,
            MaxCapacityBytes = 0,
            InstalledCapacityBytes = 0,
            ChannelConfig = "Unknown"
        };

        memInfo ??= new MemoryInfo
        {
            TotalPhysicalBytes = 0,
            AvailablePhysicalBytes = 0,
            TotalPageFileBytes = 0,
            AvailablePageFileBytes = 0,
            TotalVirtualBytes = 0,
            AvailableVirtualBytes = 0,
            MemoryLoadPercent = 0
        };

        return new DiagnosticReport
        {
            SystemInfo = sysInfo,
            RamSummary = ramSummary,
            DiskHealth = diskHealth,
            OutdatedDrivers = outdatedDrivers,
            Battery = battery,
            MemoryInfo = memInfo,
            TopMemoryConsumers = topMemory,
            TopCpuProcesses = topCpu.Take(10).ToList(),
            NetworkAdapters = adapters,
            PublicIp = publicIp,
            RecentErrors = errors,
            RecentWarnings = warnings,
            CleanupItems = cleanupItems,
            RegistryIssues = registryIssues,
            InstalledSoftware = software,
            AntivirusProducts = avProducts,
            StartupItems = startupItems,
            RunningServices = services,
            RecentCrashes = crashes,
            BloatwareApps = bloatware,
            GeneratedAt = DateTime.UtcNow
        };
    }

    // ================================================================
    // AI Consultation — POST report to N8N webhook
    // ================================================================

    public async Task<AiRecommendation?> SubmitToAiAsync(
        DiagnosticReport report,
        CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(report, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(WebhookUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<AiRecommendation>(body, _jsonOptions);
        }
        catch
        {
            // Graceful degradation — user can still manually select actions
            return null;
        }
    }

    // ================================================================
    // Action Catalogue — static list of available remediation actions
    // ================================================================

    public List<AutoToolAction> GetActionCatalogue() => new()
    {
        new()
        {
            ActionId = "CLEAN_TEMP",
            DisplayName = "Clean Temporary Files",
            Description = "Removes Windows temp files, browser caches, recycle bin, prefetch, thumbnails, and update logs",
            Category = "Cleanup",
            RiskLevel = ActionRiskLevel.Safe
        },
        new()
        {
            ActionId = "FIX_REGISTRY_SAFE",
            DisplayName = "Fix Safe Registry Issues",
            Description = "Fixes registry entries marked as Safe risk level (missing DLLs, unused extensions, dead shortcuts)",
            Category = "Cleanup",
            RiskLevel = ActionRiskLevel.Low
        },
        new()
        {
            ActionId = "FIX_REGISTRY_ALL",
            DisplayName = "Fix All Registry Issues",
            Description = "Fixes all detected registry issues including Review and Caution items (backup created first)",
            Category = "Cleanup",
            RiskLevel = ActionRiskLevel.Medium
        },
        new()
        {
            ActionId = "RUN_SFC",
            DisplayName = "System File Checker (SFC)",
            Description = "Scans and repairs corrupted Windows system files using SFC /scannow",
            Category = "Repair",
            RiskLevel = ActionRiskLevel.Low
        },
        new()
        {
            ActionId = "RUN_DISM",
            DisplayName = "DISM Health Restore",
            Description = "Repairs the Windows component store using DISM /Online /RestoreHealth",
            Category = "Repair",
            RiskLevel = ActionRiskLevel.Low
        },
        new()
        {
            ActionId = "FLUSH_DNS",
            DisplayName = "Flush DNS Cache",
            Description = "Clears the DNS resolver cache to fix stale or incorrect DNS entries",
            Category = "Network",
            RiskLevel = ActionRiskLevel.Safe
        },
        new()
        {
            ActionId = "RESET_NETWORK",
            DisplayName = "Reset Network Stack",
            Description = "Resets TCP/IP stack and Winsock catalogue (requires reboot to take effect)",
            Category = "Network",
            RiskLevel = ActionRiskLevel.Medium
        },
        new()
        {
            ActionId = "RESET_WINDOWS_UPDATE",
            DisplayName = "Reset Windows Update",
            Description = "Stops update services, clears update cache, and re-registers DLLs",
            Category = "Repair",
            RiskLevel = ActionRiskLevel.Medium
        },
        new()
        {
            ActionId = "CLEAR_PRINT_SPOOLER",
            DisplayName = "Clear Print Spooler",
            Description = "Stops the print spooler service, clears stuck print jobs, then restarts it",
            Category = "Repair",
            RiskLevel = ActionRiskLevel.Safe
        },
        new()
        {
            ActionId = "DISABLE_COPILOT",
            DisplayName = "Disable Windows Copilot",
            Description = "Disables Microsoft Copilot via registry policies and removes the AppX package",
            Category = "Privacy",
            RiskLevel = ActionRiskLevel.Low
        },
        new()
        {
            ActionId = "DISABLE_RECALL",
            DisplayName = "Disable Windows Recall",
            Description = "Disables Windows Recall via registry policies (24H2 Copilot+ PCs)",
            Category = "Privacy",
            RiskLevel = ActionRiskLevel.Low
        },
        new()
        {
            ActionId = "APPLY_PRIVACY_TWEAKS",
            DisplayName = "Apply Privacy Settings",
            Description = "Disables telemetry, advertising ID, Start menu suggestions, Bing search, and lock screen ads",
            Category = "Privacy",
            RiskLevel = ActionRiskLevel.Low
        },
        new()
        {
            ActionId = "REMOVE_SAFE_BLOATWARE",
            DisplayName = "Remove Safe Bloatware",
            Description = "Removes AppX packages marked as Safe: Xbox apps, Clipchamp, News, Weather, Solitaire, etc.",
            Category = "Cleanup",
            RiskLevel = ActionRiskLevel.Medium
        }
    };

    // ================================================================
    // Action Execution — dispatches to the appropriate service method
    // ================================================================

    public async Task<string> ExecuteActionAsync(
        AutoToolAction action,
        DiagnosticReport report,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return action.ActionId switch
        {
            "CLEAN_TEMP" => await ExecuteCleanTempAsync(report, progress, ct),
            "FIX_REGISTRY_SAFE" => await ExecuteFixRegistrySafeAsync(report, ct),
            "FIX_REGISTRY_ALL" => await ExecuteFixRegistryAllAsync(report, ct),
            "RUN_SFC" => await ExecuteSfcAsync(progress, ct),
            "RUN_DISM" => await ExecuteDismAsync(progress, ct),
            "FLUSH_DNS" => await ExecuteFlushDnsAsync(ct),
            "RESET_NETWORK" => await ExecuteResetNetworkAsync(progress, ct),
            "RESET_WINDOWS_UPDATE" => await ExecuteResetWindowsUpdateAsync(progress, ct),
            "CLEAR_PRINT_SPOOLER" => await ExecuteClearSpoolerAsync(),
            "DISABLE_COPILOT" => await ExecuteDisableCopilotAsync(progress),
            "DISABLE_RECALL" => await ExecuteDisableRecallAsync(progress),
            "APPLY_PRIVACY_TWEAKS" => await ExecutePrivacyTweaksAsync(progress),
            "REMOVE_SAFE_BLOATWARE" => await ExecuteRemoveSafeBloatwareAsync(progress),
            _ => throw new ArgumentException($"Unknown action: {action.ActionId}")
        };
    }

    // ----------------------------------------------------------------
    // Individual action implementations
    // ----------------------------------------------------------------

    private async Task<string> ExecuteCleanTempAsync(DiagnosticReport report, IProgress<string>? progress, CancellationToken ct)
    {
        // Select all categories from the scan for cleaning
        var categories = report.CleanupItems
            .Select(c => c.Category)
            .Distinct();

        var result = await _cleanup.CleanAsync(categories, progress, ct);
        return $"Freed {result.TotalFreedFormatted} ({result.ItemsCleaned} items cleaned, {result.ItemsFailed} failed)";
    }

    private async Task<string> ExecuteFixRegistrySafeAsync(DiagnosticReport report, CancellationToken ct)
    {
        var safeIssues = report.RegistryIssues
            .Where(i => i.Risk == RegistryRiskLevel.Safe)
            .ToList();

        if (safeIssues.Count == 0)
            return "No safe registry issues found";

        var result = await _registryCleaner.FixSelectedAsync(safeIssues, ct);
        return $"Fixed {result.Fixed}/{result.TotalSelected} safe issues (backup: {Path.GetFileName(result.BackupFilePath)})";
    }

    private async Task<string> ExecuteFixRegistryAllAsync(DiagnosticReport report, CancellationToken ct)
    {
        if (report.RegistryIssues.Count == 0)
            return "No registry issues found";

        var result = await _registryCleaner.FixSelectedAsync(report.RegistryIssues, ct);
        return $"Fixed {result.Fixed}/{result.TotalSelected} issues (backup: {Path.GetFileName(result.BackupFilePath)})";
    }

    private async Task<string> ExecuteSfcAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var result = await _systemRepair.RunSfcScanAsync(progress, ct);
        return result.Success
            ? $"SFC completed successfully — {result.Output}"
            : $"SFC completed with issues — {result.Output}";
    }

    private async Task<string> ExecuteDismAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var result = await _systemRepair.RunDismRestoreHealthAsync(progress, ct);
        return result.Success
            ? $"DISM restore health completed successfully"
            : $"DISM completed with issues — {result.Output}";
    }

    private async Task<string> ExecuteFlushDnsAsync(CancellationToken ct)
    {
        var result = await _systemRepair.FlushDnsAsync(ct);
        return result.Success ? "DNS cache flushed successfully" : $"DNS flush failed — {result.Output}";
    }

    private async Task<string> ExecuteResetNetworkAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var result = await _systemRepair.ResetNetworkStackAsync(progress, ct);
        return result.Success
            ? "Network stack reset — reboot recommended"
            : $"Network reset encountered issues — {result.Output}";
    }

    private async Task<string> ExecuteResetWindowsUpdateAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var result = await _systemRepair.ResetWindowsUpdateAsync(progress, ct);
        return result.Success
            ? "Windows Update components reset successfully"
            : $"Windows Update reset encountered issues — {result.Output}";
    }

    private async Task<string> ExecuteClearSpoolerAsync()
    {
        var success = await _printerService.ClearSpoolerAsync();
        return success ? "Print spooler cleared and restarted" : "Failed to clear print spooler";
    }

    private async Task<string> ExecuteDisableCopilotAsync(IProgress<string>? progress)
    {
        var success = await _debloat.DisableCopilotAsync(progress);
        return success ? "Windows Copilot disabled" : "Failed to disable Copilot";
    }

    private async Task<string> ExecuteDisableRecallAsync(IProgress<string>? progress)
    {
        var success = await _debloat.DisableRecallAsync(progress);
        return success ? "Windows Recall disabled" : "Failed to disable Recall";
    }

    private async Task<string> ExecutePrivacyTweaksAsync(IProgress<string>? progress)
    {
        var success = await _debloat.ApplyPrivacyTweaksAsync(progress);
        return success
            ? "Privacy tweaks applied (telemetry, ads, Bing search disabled)"
            : "Some privacy tweaks may not have applied";
    }

    private async Task<string> ExecuteRemoveSafeBloatwareAsync(IProgress<string>? progress)
    {
        var bloatware = await _debloat.GetBloatwareListAsync();
        var safeApps = bloatware
            .Where(b => b.IsInstalled && b.Safety == DebloatSafety.Safe)
            .ToList();

        if (safeApps.Count == 0)
            return "No safe bloatware found to remove";

        var result = await _debloat.RemovePackagesAsync(safeApps, progress);
        return $"Removed {result.Removed}/{result.TotalSelected} bloatware packages ({result.Failed} failed)";
    }
}
