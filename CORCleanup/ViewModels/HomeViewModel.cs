using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using Wpf.Ui;

namespace CORCleanup.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly ISystemInfoService _systemInfoService;

    public HomeViewModel(
        INavigationService navigationService,
        ISystemInfoService systemInfoService)
    {
        _navigationService = navigationService;
        _systemInfoService = systemInfoService;

        _ = LoadSystemDataAsync();
    }

    // ================================================================
    // System Overview Properties
    // ================================================================

    [ObservableProperty] private string _computerName = "Loading...";
    [ObservableProperty] private string _osCaption = string.Empty;
    [ObservableProperty] private string _cpuShortName = "CPU";
    [ObservableProperty] private string _cpuSummary = string.Empty;
    [ObservableProperty] private string _ramDisplay = "RAM";
    [ObservableProperty] private string _ramSlots = string.Empty;
    [ObservableProperty] private string _gpuShortName = "GPU";
    [ObservableProperty] private string _gpuVram = string.Empty;

    public ObservableCollection<DiskDisplayItem> Disks { get; } = new();

    // ================================================================
    // Data Loading
    // ================================================================

    private async Task LoadSystemDataAsync()
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

            // CPU — shorten the name
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

            // Disks — use logical drive info from disk health
            foreach (var d in disks)
            {
                Disks.Add(new DiskDisplayItem
                {
                    Label = $"{d.Model} ({d.SizeFormatted})",
                    TotalDisplay = d.SizeFormatted,
                    UsedDisplay = d.SizeFormatted, // full capacity display
                    UsedPercent = d.WearLevellingPercent ?? 0,
                    Health = d.OverallHealth.ToString()
                });
            }

            // If no SMART data, show logical drives instead
            if (Disks.Count == 0)
                await LoadLogicalDrivesAsync();
        }
        catch
        {
            ComputerName = Environment.MachineName;
            OsCaption = Environment.OSVersion.ToString();
            await LoadLogicalDrivesAsync();
        }
    }

    private Task LoadLogicalDrivesAsync()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;

            var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
            var usedGb = (drive.TotalSize - drive.TotalFreeSpace) / (1024.0 * 1024 * 1024);
            var pct = drive.TotalSize > 0
                ? (double)(drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize * 100
                : 0;

            Disks.Add(new DiskDisplayItem
            {
                Label = $"{drive.Name.TrimEnd('\\')} {drive.VolumeLabel}",
                TotalDisplay = $"{totalGb:F0} GB",
                UsedDisplay = $"{usedGb:F0} GB",
                UsedPercent = pct,
                Health = pct > 90 ? "Caution" : "Good"
            });
        }
        return Task.CompletedTask;
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
        // "Intel(R) Core(TM) i7-13700K CPU @ 3.40GHz" → "Core i7-13700K"
        // "AMD Ryzen 9 7950X 16-Core Processor" → "Ryzen 9 7950X"
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

        // Remove "Intel " prefix, keep "Core..."
        if (s.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase))
            s = s[6..].Trim();

        // Trim anything after the clock speed
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
// Display model for disk drive bar
// ================================================================

public sealed class DiskDisplayItem
{
    public required string Label { get; init; }
    public required string TotalDisplay { get; init; }
    public required string UsedDisplay { get; init; }
    public required double UsedPercent { get; init; }
    public required string Health { get; init; }
}
