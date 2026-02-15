using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.ViewModels;

public partial class HardwareViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IWifiService _wifiService;
    private readonly IDriverService _driverService;

    [ObservableProperty] private string _pageTitle = "Hardware Information";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready";

    // System Info
    [ObservableProperty] private string _osInfo = "";
    [ObservableProperty] private string _cpuInfo = "";
    [ObservableProperty] private string _gpuInfo = "";
    [ObservableProperty] private string _motherboardInfo = "";
    [ObservableProperty] private string _totalRam = "";

    // RAM
    [ObservableProperty] private string _ramSlotSummary = "";
    [ObservableProperty] private string _ramUpgradeAdvice = "";
    public ObservableCollection<RamDimm> RamDimms { get; } = new();

    // Disk Health
    public ObservableCollection<DiskHealthInfo> Disks { get; } = new();

    // Battery
    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private string _batteryHealth = "";
    [ObservableProperty] private string _batteryDesign = "";
    [ObservableProperty] private string _batteryFullCharge = "";
    [ObservableProperty] private int _batteryCycles;
    [ObservableProperty] private string _batteryChemistry = "";
    [ObservableProperty] private int _batteryPercent;
    [ObservableProperty] private bool _batteryNeedsReplacement;

    // Product Key
    [ObservableProperty] private string _productKey = "";

    // Wi-Fi Passwords
    public ObservableCollection<WifiProfile> WifiProfiles { get; } = new();

    // Drivers
    [ObservableProperty] private string _driverFilter = "";
    public ObservableCollection<DriverInfo> Drivers { get; } = new();
    public ObservableCollection<DriverInfo> FilteredDrivers { get; } = new();

    // Active sub-tab
    [ObservableProperty] private int _selectedTabIndex;

    public HardwareViewModel(ISystemInfoService systemInfo, IWifiService wifiService, IDriverService driverService)
    {
        _systemInfo = systemInfo;
        _wifiService = wifiService;
        _driverService = driverService;
    }

    [RelayCommand]
    private async Task LoadSystemInfoAsync()
    {
        IsLoading = true;
        StatusText = "Gathering system information...";

        try
        {
            var info = await _systemInfo.GetSystemInfoAsync();
            OsInfo = $"{info.OsEdition} (Build {info.OsBuild})";
            CpuInfo = $"{info.CpuName} — {info.CpuCores}C/{info.CpuThreads}T @ {info.CpuMaxClockMhz} MHz";
            GpuInfo = $"{info.GpuName} — {info.GpuVramFormatted} VRAM (Driver: {info.GpuDriverVersion})";
            MotherboardInfo = $"{info.MotherboardManufacturer} {info.MotherboardProduct} (BIOS: {info.BiosVersion})";
            TotalRam = info.TotalRamFormatted;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatusText = "System information loaded";
        }
    }

    [RelayCommand]
    private async Task LoadRamInfoAsync()
    {
        IsLoading = true;
        StatusText = "Reading RAM configuration...";

        try
        {
            var summary = await _systemInfo.GetRamSummaryAsync();
            RamDimms.Clear();
            foreach (var dimm in summary.Dimms)
                RamDimms.Add(dimm);

            RamSlotSummary = $"{summary.UsedSlots} of {summary.TotalSlots} slots used — {summary.InstalledFormatted} installed ({summary.ChannelConfig})";

            if (summary.EmptySlots > 0)
            {
                var perSlot = summary.Dimms.FirstOrDefault()?.CapacityBytes ?? 0;
                var perSlotGb = perSlot / (1024.0 * 1024 * 1024);
                var dimmType = summary.Dimms.FirstOrDefault()?.MemoryType ?? "DDR";
                var speed = summary.Dimms.FirstOrDefault()?.SpeedMhz ?? 0;
                RamUpgradeAdvice = $"{summary.EmptySlots} empty slot(s) available. Max supported: {summary.MaxCapacityFormatted}. You can add {summary.EmptySlots}x {perSlotGb:F0} GB {dimmType}-{speed} sticks.";
            }
            else
            {
                RamUpgradeAdvice = "All slots occupied. To upgrade, replace existing DIMMs with higher capacity.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatusText = "RAM information loaded";
        }
    }

    [RelayCommand]
    private async Task LoadDiskHealthAsync()
    {
        IsLoading = true;
        StatusText = "Reading disk health...";

        try
        {
            var disks = await _systemInfo.GetDiskHealthAsync();
            Disks.Clear();
            foreach (var disk in disks)
                Disks.Add(disk);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatusText = $"{Disks.Count} disk(s) detected";
        }
    }

    [RelayCommand]
    private async Task LoadBatteryInfoAsync()
    {
        IsLoading = true;
        StatusText = "Reading battery health...";

        try
        {
            var battery = await _systemInfo.GetBatteryInfoAsync();
            HasBattery = battery.HasBattery;

            if (battery.HasBattery)
            {
                BatteryHealth = $"{battery.HealthPercent:F1}%";
                BatteryDesign = battery.DesignCapacityFormatted;
                BatteryFullCharge = battery.FullChargeFormatted;
                BatteryCycles = battery.CycleCount;
                BatteryChemistry = battery.Chemistry;
                BatteryPercent = battery.ChargePercent;
                BatteryNeedsReplacement = battery.NeedsReplacement;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatusText = HasBattery ? "Battery information loaded" : "No battery detected (desktop)";
        }
    }

    [RelayCommand]
    private async Task LoadProductKeyAsync()
    {
        IsLoading = true;
        StatusText = "Recovering product key...";

        try
        {
            var key = await _systemInfo.GetProductKeyAsync();
            ProductKey = key ?? "Not found — may be digitally licensed";
        }
        catch (Exception ex)
        {
            ProductKey = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatusText = "Product key recovery complete";
        }
    }

    [RelayCommand]
    private async Task LoadWifiPasswordsAsync()
    {
        IsLoading = true;
        StatusText = "Recovering Wi-Fi passwords...";

        try
        {
            var profiles = await _wifiService.GetSavedProfilesAsync();
            WifiProfiles.Clear();
            foreach (var profile in profiles)
                WifiProfiles.Add(profile);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatusText = $"{WifiProfiles.Count} Wi-Fi profile(s) found";
        }
    }

    [RelayCommand]
    private async Task LoadDriversAsync()
    {
        IsLoading = true;
        StatusText = "Enumerating device drivers...";

        try
        {
            var drivers = await _driverService.GetAllDriversAsync();
            Drivers.Clear();
            FilteredDrivers.Clear();
            foreach (var driver in drivers)
            {
                Drivers.Add(driver);
                FilteredDrivers.Add(driver);
            }
            ApplyDriverFilter();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatusText = $"{Drivers.Count} driver(s) found";
        }
    }

    partial void OnDriverFilterChanged(string value) => ApplyDriverFilter();

    private void ApplyDriverFilter()
    {
        FilteredDrivers.Clear();
        var filter = DriverFilter.Trim();

        foreach (var driver in Drivers)
        {
            if (string.IsNullOrEmpty(filter)
                || driver.DeviceName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || driver.Manufacturer.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || driver.DeviceClass.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredDrivers.Add(driver);
            }
        }
    }

    [RelayCommand]
    private async Task LoadAllAsync()
    {
        // Run all hardware queries in parallel — each is an independent WMI/registry call
        await Task.WhenAll(
            LoadSystemInfoAsync(),
            LoadRamInfoAsync(),
            LoadDiskHealthAsync(),
            LoadBatteryInfoAsync(),
            LoadProductKeyAsync());
    }
}
