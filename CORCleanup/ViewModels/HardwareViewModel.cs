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
    private readonly ISoftwareInventoryService _softwareInventoryService;
    private readonly IProcessExplorerService _processExplorerService;
    private readonly IMemoryExplorerService _memoryExplorerService;

    [ObservableProperty] private string _pageTitle = "System Info";
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

    // Software Inventory
    [ObservableProperty] private bool _isLoadingSoftware;
    [ObservableProperty] private string _softwareSearchFilter = "";
    [ObservableProperty] private bool _includeSystemComponents;
    public ObservableCollection<SoftwareEntry> SoftwareEntries { get; } = new();
    public ObservableCollection<SoftwareEntry> FilteredSoftware { get; } = new();

    // Process Explorer
    [ObservableProperty] private bool _isLoadingProcesses;
    [ObservableProperty] private ProcessEntry? _selectedProcess;
    [ObservableProperty] private string _processSearchFilter = "";
    public ObservableCollection<ProcessEntry> AllProcesses { get; } = new();
    public ObservableCollection<ProcessEntry> FilteredProcesses { get; } = new();

    // Memory Explorer
    [ObservableProperty] private bool _isLoadingMemory;
    [ObservableProperty] private MemoryInfo? _currentMemoryInfo;
    public ObservableCollection<MemoryConsumer> MemoryConsumers { get; } = new();

    // Active sub-tab
    [ObservableProperty] private int _selectedTabIndex;

    public HardwareViewModel(
        ISystemInfoService systemInfo,
        IWifiService wifiService,
        IDriverService driverService,
        ISoftwareInventoryService softwareInventoryService,
        IProcessExplorerService processExplorerService,
        IMemoryExplorerService memoryExplorerService)
    {
        _systemInfo = systemInfo;
        _wifiService = wifiService;
        _driverService = driverService;
        _softwareInventoryService = softwareInventoryService;
        _processExplorerService = processExplorerService;
        _memoryExplorerService = memoryExplorerService;
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

    // ================================================================
    // Software Inventory Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadSoftwareAsync()
    {
        IsLoadingSoftware = true;
        SoftwareEntries.Clear();
        FilteredSoftware.Clear();
        StatusText = "Loading installed software...";

        try
        {
            var entries = await _softwareInventoryService.GetInstalledSoftwareAsync(IncludeSystemComponents);
            foreach (var entry in entries)
                SoftwareEntries.Add(entry);

            ApplySoftwareFilter();
            StatusText = $"{SoftwareEntries.Count} program(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingSoftware = false;
        }
    }

    partial void OnSoftwareSearchFilterChanged(string value) => ApplySoftwareFilter();

    private void ApplySoftwareFilter()
    {
        FilteredSoftware.Clear();

        var filter = SoftwareSearchFilter?.Trim() ?? "";
        var source = string.IsNullOrEmpty(filter)
            ? SoftwareEntries
            : SoftwareEntries.Where(e =>
                e.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (e.Publisher?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var entry in source)
            FilteredSoftware.Add(entry);
    }

    [RelayCommand]
    private async Task ExportSoftwareCsvAsync()
    {
        if (FilteredSoftware.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Software Inventory",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"Software_Inventory_{DateTime.Now:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _softwareInventoryService.ExportToCsvAsync(FilteredSoftware, dialog.FileName);
                StatusText = $"Exported {FilteredSoftware.Count} entries to {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
            }
        }
    }

    // ================================================================
    // Process Explorer Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadProcessesAsync()
    {
        IsLoadingProcesses = true;
        AllProcesses.Clear();
        FilteredProcesses.Clear();
        SelectedProcess = null;
        StatusText = "Sampling processes (CPU measurement ~500ms)...";

        try
        {
            var entries = await _processExplorerService.GetProcessesAsync();
            foreach (var entry in entries)
                AllProcesses.Add(entry);

            ApplyProcessFilter();

            var totalCpu = entries.Sum(e => e.CpuPercent);
            var totalMemMb = entries.Sum(e => e.WorkingSetBytes) / (1024.0 * 1024);
            StatusText = $"{entries.Count} processes — CPU: {totalCpu:F1}% — Memory: {totalMemMb:F0} MB total";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingProcesses = false;
        }
    }

    partial void OnProcessSearchFilterChanged(string value) => ApplyProcessFilter();

    private void ApplyProcessFilter()
    {
        FilteredProcesses.Clear();

        var filter = ProcessSearchFilter?.Trim() ?? "";
        var source = string.IsNullOrEmpty(filter)
            ? AllProcesses
            : AllProcesses.Where(e =>
                e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (e.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                e.Pid.ToString().Contains(filter));

        foreach (var entry in source)
            FilteredProcesses.Add(entry);
    }

    [RelayCommand]
    private async Task KillSelectedProcessAsync()
    {
        if (SelectedProcess is null) return;

        var pid = SelectedProcess.Pid;
        var name = SelectedProcess.Name;

        if (SelectedProcess.IsSystem)
        {
            StatusText = $"Cannot kill system process: {name} (PID {pid})";
            return;
        }

        StatusText = $"Killing {name} (PID {pid})...";
        var success = await _processExplorerService.KillProcessAsync(pid);

        StatusText = success
            ? $"Process {name} (PID {pid}) terminated"
            : $"Failed to kill {name} (PID {pid}) — access denied or already exited";

        if (success)
            await LoadProcessesAsync();
    }

    [RelayCommand]
    private void OpenProcessLocation()
    {
        if (SelectedProcess?.FilePath is null)
        {
            StatusText = "No file path available for this process";
            return;
        }

        _processExplorerService.OpenFileLocation(SelectedProcess.FilePath);
        StatusText = $"Opened location for {SelectedProcess.Name}";
    }

    // ================================================================
    // Memory Explorer Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadMemoryAsync()
    {
        IsLoadingMemory = true;
        MemoryConsumers.Clear();
        CurrentMemoryInfo = null;
        StatusText = "Querying system memory...";

        try
        {
            var info = await _memoryExplorerService.GetMemoryInfoAsync();
            CurrentMemoryInfo = info;

            var consumers = await _memoryExplorerService.GetTopConsumersAsync();
            foreach (var consumer in consumers)
                MemoryConsumers.Add(consumer);

            StatusText = $"RAM: {info.UsedFormatted} / {info.TotalFormatted} ({info.MemoryLoadPercent}%) — " +
                         $"Page File: {info.PageFileUsedFormatted} / {info.PageFileTotalFormatted} — " +
                         $"Health: {info.HealthLevel}";
        }
        catch (Exception ex)
        {
            StatusText = $"Memory query error: {ex.Message}";
        }
        finally
        {
            IsLoadingMemory = false;
        }
    }

    [RelayCommand]
    private async Task LoadAllAsync()
    {
        await Task.WhenAll(
            LoadSystemInfoAsync(),
            LoadRamInfoAsync(),
            LoadBatteryInfoAsync(),
            LoadProductKeyAsync());
    }
}
