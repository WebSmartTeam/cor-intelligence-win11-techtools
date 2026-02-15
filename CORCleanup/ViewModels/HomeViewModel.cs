using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using Wpf.Ui;

namespace CORCleanup.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly ISystemInfoService _systemInfoService;
    private readonly IProcessExplorerService _processExplorerService;
    private readonly IMemoryExplorerService _memoryExplorerService;
    private readonly INetworkInfoService _networkInfoService;
    private readonly IEventLogService _eventLogService;

    private readonly int _ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;
    private CancellationTokenSource? _autoRefreshCts;

    public HomeViewModel(
        INavigationService navigationService,
        ISystemInfoService systemInfoService,
        IProcessExplorerService processExplorerService,
        IMemoryExplorerService memoryExplorerService,
        INetworkInfoService networkInfoService,
        IEventLogService eventLogService)
    {
        _navigationService = navigationService;
        _systemInfoService = systemInfoService;
        _processExplorerService = processExplorerService;
        _memoryExplorerService = memoryExplorerService;
        _networkInfoService = networkInfoService;
        _eventLogService = eventLogService;

        _ = InitializeDashboardAsync();
        _ = StartAutoRefreshAsync();
    }

    // ================================================================
    // System Identity
    // ================================================================

    [ObservableProperty] private string _computerName = "Loading...";
    [ObservableProperty] private string _osCaption = string.Empty;
    [ObservableProperty] private string _cpuShortName = "CPU";
    [ObservableProperty] private string _cpuSummary = string.Empty;
    [ObservableProperty] private string _ramDisplay = "RAM";
    [ObservableProperty] private string _ramSlots = string.Empty;
    [ObservableProperty] private string _gpuShortName = "GPU";
    [ObservableProperty] private string _gpuVram = string.Empty;

    // ================================================================
    // Network
    // ================================================================

    [ObservableProperty] private string _localIpAddress = "Detecting...";
    [ObservableProperty] private string _wanIpAddress = "Detecting...";
    [ObservableProperty] private string _networkAdapter = string.Empty;

    // ================================================================
    // Memory Gauge
    // ================================================================

    [ObservableProperty] private string _memoryUsed = "—";
    [ObservableProperty] private string _memoryTotal = "—";
    [ObservableProperty] private int _memoryPercent;
    [ObservableProperty] private string _memoryHealth = "Good";

    // ================================================================
    // Loading State
    // ================================================================

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusText = "Loading dashboard...";

    // ================================================================
    // Collections
    // ================================================================

    public ObservableCollection<DiskDisplayItem> LogicalDrives { get; } = new();
    public ObservableCollection<DiskHealthInfo> PhysicalDisks { get; } = new();
    public ObservableCollection<ProcessEntry> TopCpuProcesses { get; } = new();
    public ObservableCollection<MemoryConsumer> TopMemoryProcesses { get; } = new();
    public ObservableCollection<EventLogEntry> RecentErrors { get; } = new();
    public ObservableCollection<DriverInfo> OutdatedDrivers { get; } = new();

    // ================================================================
    // Dashboard Initialization
    // ================================================================

    private async Task InitializeDashboardAsync()
    {
        IsLoading = true;

        // Fire all independent tasks — each handles its own errors
        var sysTask = LoadSystemInfoAsync();
        var netTask = LoadNetworkAsync();
        var memTask = LoadMemoryAsync();
        var processTask = LoadTopProcessesAsync();
        var errorTask = LoadRecentErrorsAsync();
        var driverTask = LoadOutdatedDriversAsync();

        await Task.WhenAll(sysTask, netTask, memTask, processTask, errorTask, driverTask);

        IsLoading = false;
        StatusText = $"Updated {DateTime.Now:HH:mm:ss} — auto-refreshes every 30s";
    }

    private async Task LoadSystemInfoAsync()
    {
        try
        {
            var sysTask = _systemInfoService.GetSystemInfoAsync();
            var ramTask = _systemInfoService.GetRamSummaryAsync();
            var diskTask = _systemInfoService.GetDiskHealthAsync();

            await Task.WhenAll(sysTask, ramTask, diskTask);

            var sys = sysTask.Result;
            var ram = ramTask.Result;
            var disks = diskTask.Result;

            // OS
            ComputerName = sys.ComputerName;
            OsCaption = $"{sys.OsEdition} ({sys.OsBuild})";

            // CPU
            CpuShortName = ShortenCpuName(sys.CpuName);
            CpuSummary = $"{sys.CpuCores}C / {sys.CpuThreads}T \u2022 {sys.CpuMaxClockMhz} MHz";

            // RAM
            RamDisplay = sys.TotalRamFormatted;
            RamSlots = $"{ram.UsedSlots}/{ram.TotalSlots} slots \u2022 {ram.ChannelConfig}";

            // GPU
            GpuShortName = ShortenGpuName(sys.GpuName);
            GpuVram = sys.GpuVramFormatted != "N/A"
                ? $"{sys.GpuVramFormatted} VRAM"
                : "Integrated";

            // Physical disks
            foreach (var d in disks)
                PhysicalDisks.Add(d);

            // Logical drives
            LoadLogicalDrives();
        }
        catch
        {
            ComputerName = Environment.MachineName;
            OsCaption = Environment.OSVersion.ToString();
            LoadLogicalDrives();
        }
    }

    private void LoadLogicalDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;

            var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
            var usedGb = (drive.TotalSize - drive.TotalFreeSpace) / (1024.0 * 1024 * 1024);
            var freeGb = drive.TotalFreeSpace / (1024.0 * 1024 * 1024);
            var pct = drive.TotalSize > 0
                ? (double)(drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize * 100
                : 0;

            LogicalDrives.Add(new DiskDisplayItem
            {
                Label = $"{drive.Name.TrimEnd('\\')} {drive.VolumeLabel}".Trim(),
                TotalDisplay = $"{totalGb:F0} GB",
                UsedDisplay = $"{usedGb:F0} GB",
                FreeDisplay = $"{freeGb:F0} GB free",
                UsedPercent = pct,
                Health = pct > 90 ? "Caution" : "Good"
            });
        }
    }

    private async Task LoadNetworkAsync()
    {
        try
        {
            var adaptersTask = _networkInfoService.GetAdaptersAsync();
            var wanTask = _networkInfoService.GetPublicIpAsync();

            await Task.WhenAll(adaptersTask, wanTask);

            var adapters = adaptersTask.Result;
            var wan = wanTask.Result;

            // Find the active adapter with an IP address
            var active = adapters.FirstOrDefault(a =>
                a.Status == "Up" && !string.IsNullOrEmpty(a.IpAddress));

            LocalIpAddress = active?.IpAddress ?? "No network";
            NetworkAdapter = active != null
                ? $"{active.Type} \u2022 {active.SpeedDisplay}"
                : "Disconnected";

            WanIpAddress = wan ?? "Unavailable";
        }
        catch
        {
            LocalIpAddress = "Error";
            WanIpAddress = "Error";
        }
    }

    private async Task LoadMemoryAsync()
    {
        try
        {
            var info = await _memoryExplorerService.GetMemoryInfoAsync();
            MemoryUsed = info.UsedFormatted;
            MemoryTotal = info.TotalFormatted;
            MemoryPercent = info.MemoryLoadPercent;
            MemoryHealth = info.HealthLevel;

            var consumers = await _memoryExplorerService.GetTopConsumersAsync(5);
            foreach (var c in consumers)
                TopMemoryProcesses.Add(c);
        }
        catch
        {
            MemoryHealth = "Error";
        }
    }

    private async Task LoadTopProcessesAsync()
    {
        try
        {
            var entries = await _processExplorerService.GetProcessesAsync();
            var top5 = entries
                .Where(e => !e.IsSystem && e.Pid != _ownPid)
                .OrderByDescending(e => e.CpuPercent)
                .Take(5);

            foreach (var p in top5)
                TopCpuProcesses.Add(p);
        }
        catch { }
    }

    private async Task LoadRecentErrorsAsync()
    {
        try
        {
            var events = await _eventLogService.GetRecentEventsAsync(days: 7, EventSeverity.Error);
            foreach (var e in events.Take(5))
                RecentErrors.Add(e);
        }
        catch { }
    }

    private async Task LoadOutdatedDriversAsync()
    {
        try
        {
            var drivers = await _systemInfoService.GetOutdatedDriversAsync(3);
            foreach (var d in drivers)
                OutdatedDrivers.Add(d);
        }
        catch { }
    }

    // ================================================================
    // Refresh Command
    // ================================================================

    [RelayCommand]
    private async Task RefreshDashboardAsync()
    {
        LogicalDrives.Clear();
        PhysicalDisks.Clear();
        TopCpuProcesses.Clear();
        TopMemoryProcesses.Clear();
        RecentErrors.Clear();
        OutdatedDrivers.Clear();

        await InitializeDashboardAsync();
    }

    private async Task StartAutoRefreshAsync()
    {
        _autoRefreshCts = new CancellationTokenSource();
        try
        {
            while (!_autoRefreshCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _autoRefreshCts.Token);
                await RefreshDashboardAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    // ================================================================
    // Navigation
    // ================================================================

    [RelayCommand]
    private void Navigate(string page)
    {
        var pageType = page switch
        {
            "Network" => typeof(Views.NetworkPage),
            "Cleanup" => typeof(Views.CleanupPage),
            "Registry" => typeof(Views.RegistryPage),
            "Uninstaller" => typeof(Views.UninstallerPage),
            "Hardware" => typeof(Views.HardwarePage),
            "Tools" => typeof(Views.ToolsPage),
            "Admin" => typeof(Views.AdminPage),
            "Settings" => typeof(Views.SettingsPage),
            "AutoTool" => typeof(Views.AutoToolPage),
            _ => null
        };

        if (pageType is not null)
            _navigationService.Navigate(pageType);
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static string ShortenCpuName(string name)
    {
        var s = name
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("CPU @", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Processor", "", StringComparison.OrdinalIgnoreCase)
            .Replace("16-Core", "", StringComparison.OrdinalIgnoreCase)
            .Replace("8-Core", "", StringComparison.OrdinalIgnoreCase)
            .Replace("6-Core", "", StringComparison.OrdinalIgnoreCase)
            .Replace("4-Core", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (s.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase))
            s = s[6..].Trim();

        var atIdx = s.IndexOf('@');
        if (atIdx > 0)
            s = s[..atIdx].Trim();

        return s.Length > 28 ? s[..28] : s;
    }

    private static string ShortenGpuName(string name)
    {
        var s = name
            .Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("AMD ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Intel ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Graphics", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return s.Length > 28 ? s[..28] : s;
    }
}

// ================================================================
// Display model for logical drive bar
// ================================================================

public sealed class DiskDisplayItem
{
    public required string Label { get; init; }
    public required string TotalDisplay { get; init; }
    public required string UsedDisplay { get; init; }
    public string FreeDisplay { get; init; } = string.Empty;
    public required double UsedPercent { get; init; }
    public required string Health { get; init; }
}
