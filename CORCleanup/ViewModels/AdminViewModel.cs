using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Helpers;

namespace CORCleanup.ViewModels;

public partial class AdminViewModel : ObservableObject
{
    private readonly IStartupService _startupService;
    private readonly IServicesManagerService _servicesManager;
    private readonly IEventLogService _eventLogService;
    private readonly ISystemRepairService _repairService;
    private readonly IPrinterService _printerService;
    private readonly IHostsFileService _hostsFileService;
    private readonly IDebloatService _debloatService;

    [ObservableProperty] private string _pageTitle = "System Administration";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _selectedTabIndex;

    // ================================================================
    // Startup Items
    // ================================================================

    [ObservableProperty] private bool _hideMicrosoftEntries;
    public ObservableCollection<StartupEntry> StartupEntries { get; } = new();
    public ObservableCollection<StartupEntry> FilteredStartupEntries { get; } = new();

    // ================================================================
    // Services
    // ================================================================

    [ObservableProperty] private string _serviceFilter = "";
    [ObservableProperty] private ServiceEntry? _selectedService;
    public ObservableCollection<ServiceEntry> Services { get; } = new();
    public ObservableCollection<ServiceEntry> FilteredServices { get; } = new();

    // ================================================================
    // Event Log
    // ================================================================

    [ObservableProperty] private double _eventLogDays = 7;
    public ObservableCollection<EventLogEntry> EventLogEntries { get; } = new();

    // ================================================================
    // System Repair
    // ================================================================

    [ObservableProperty] private bool _isRepairing;
    [ObservableProperty] private string _repairOutput = "";
    public ObservableCollection<SystemRepairResult> RepairResults { get; } = new();

    // ================================================================
    // Printer Management
    // ================================================================

    [ObservableProperty] private bool _isLoadingPrinters;
    [ObservableProperty] private PrinterInfo? _selectedPrinter;
    public ObservableCollection<PrinterInfo> Printers { get; } = new();

    // ================================================================
    // Hosts File Editor
    // ================================================================

    [ObservableProperty] private bool _isLoadingHosts;
    [ObservableProperty] private HostsEntry? _selectedHostEntry;
    [ObservableProperty] private string _newHostIp = "127.0.0.1";
    [ObservableProperty] private string _newHostHostname = "";
    [ObservableProperty] private string _newHostComment = "";
    public ObservableCollection<HostsEntry> HostsEntries { get; } = new();

    // ================================================================
    // Debloat / Bloatware Removal
    // ================================================================

    [ObservableProperty] private bool _isLoadingBloatware;
    [ObservableProperty] private string _debloatOutput = "";
    [ObservableProperty] private string _debloatCategoryFilter = "All";
    public ObservableCollection<AppxPackageInfo> BloatwarePackages { get; } = new();
    public ObservableCollection<AppxPackageInfo> FilteredBloatwarePackages { get; } = new();

    public string[] DebloatCategories { get; } = ["All", "AI / Copilot", "Xbox / Gaming", "Entertainment", "Communication", "Productivity", "System Extras"];

    // ================================================================
    // Constructor
    // ================================================================

    public AdminViewModel(
        IStartupService startupService,
        IServicesManagerService servicesManager,
        IEventLogService eventLogService,
        ISystemRepairService repairService,
        IPrinterService printerService,
        IHostsFileService hostsFileService,
        IDebloatService debloatService)
    {
        _startupService = startupService;
        _servicesManager = servicesManager;
        _eventLogService = eventLogService;
        _repairService = repairService;
        _printerService = printerService;
        _hostsFileService = hostsFileService;
        _debloatService = debloatService;
    }

    partial void OnHideMicrosoftEntriesChanged(bool value) => ApplyStartupFilter();
    partial void OnServiceFilterChanged(string value) => ApplyServiceFilter();
    partial void OnDebloatCategoryFilterChanged(string value) => ApplyBloatwareFilter();

    // ================================================================
    // Startup Items Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadStartupItemsAsync()
    {
        IsLoading = true;
        StatusText = "Scanning startup items...";

        try
        {
            var entries = await _startupService.GetStartupItemsAsync();
            StartupEntries.Clear();
            foreach (var entry in entries)
                StartupEntries.Add(entry);

            ApplyStartupFilter();
            StatusText = $"{StartupEntries.Count} startup item(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleStartupItemAsync(StartupEntry? entry)
    {
        if (entry is null) return;
        var newState = !entry.IsEnabled;
        var success = await _startupService.SetEnabledAsync(entry, newState);
        if (success)
        {
            StatusText = $"{entry.Name} — {(newState ? "enabled" : "disabled")}";
            await LoadStartupItemsAsync();
        }
        else
        {
            StatusText = $"Failed to change {entry.Name}";
        }
    }

    private void ApplyStartupFilter()
    {
        FilteredStartupEntries.Clear();
        foreach (var entry in StartupEntries)
        {
            if (HideMicrosoftEntries && entry.IsMicrosoft)
                continue;
            FilteredStartupEntries.Add(entry);
        }
    }

    // ================================================================
    // Services Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadServicesAsync()
    {
        IsLoading = true;
        StatusText = "Enumerating services...";

        try
        {
            var services = await _servicesManager.GetServicesAsync();
            Services.Clear();
            foreach (var svc in services)
                Services.Add(svc);

            ApplyServiceFilter();
            StatusText = $"{Services.Count} service(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        if (SelectedService is null) return;
        try
        {
            StatusText = $"Starting {SelectedService.DisplayName}...";
            await _servicesManager.StartServiceAsync(SelectedService.ServiceName);
            StatusText = $"{SelectedService.DisplayName} started";
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error starting service: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopServiceAsync()
    {
        if (SelectedService is null) return;
        if (!DialogHelper.Confirm($"Stop service '{SelectedService.DisplayName}'?"))
            return;

        try
        {
            StatusText = $"Stopping {SelectedService.DisplayName}...";
            await _servicesManager.StopServiceAsync(SelectedService.ServiceName);
            StatusText = $"{SelectedService.DisplayName} stopped";
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error stopping service: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        if (SelectedService is null) return;
        try
        {
            StatusText = $"Restarting {SelectedService.DisplayName}...";
            await _servicesManager.RestartServiceAsync(SelectedService.ServiceName);
            StatusText = $"{SelectedService.DisplayName} restarted";
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error restarting service: {ex.Message}";
        }
    }

    private void ApplyServiceFilter()
    {
        FilteredServices.Clear();
        var filter = ServiceFilter?.Trim() ?? "";

        foreach (var svc in Services)
        {
            if (string.IsNullOrEmpty(filter) ||
                svc.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                svc.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredServices.Add(svc);
            }
        }
    }

    // ================================================================
    // Event Log Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadEventLogAsync()
    {
        IsLoading = true;
        StatusText = $"Reading event log (last {(int)EventLogDays} days)...";

        try
        {
            var events = await _eventLogService.GetRecentEventsAsync((int)EventLogDays);
            EventLogEntries.Clear();
            foreach (var evt in events)
                EventLogEntries.Add(evt);

            StatusText = $"{EventLogEntries.Count} event(s) found in last {(int)EventLogDays} days";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ================================================================
    // System Repair Commands
    // ================================================================

    [RelayCommand]
    private async Task RunSfcScanAsync()
    {
        await RunRepairOperationAsync("SFC Scan",
            () => _repairService.RunSfcScanAsync(new Progress<string>(AppendRepairOutput)));
    }

    [RelayCommand]
    private async Task RunDismRepairAsync()
    {
        await RunRepairOperationAsync("DISM Restore Health",
            () => _repairService.RunDismRestoreHealthAsync(new Progress<string>(AppendRepairOutput)));
    }

    [RelayCommand]
    private async Task RunNetworkResetAsync()
    {
        if (!DialogHelper.Confirm("Reset the network stack?\nThis will reset Winsock, TCP/IP, DNS cache, and may require a reboot."))
            return;

        await RunRepairOperationAsync("Network Stack Reset",
            () => _repairService.ResetNetworkStackAsync(new Progress<string>(AppendRepairOutput)));
    }

    [RelayCommand]
    private async Task RunDnsFlushAsync()
    {
        await RunRepairOperationAsync("DNS Cache Flush",
            () => _repairService.FlushDnsAsync());
    }

    [RelayCommand]
    private async Task RunWindowsUpdateResetAsync()
    {
        await RunRepairOperationAsync("Windows Update Reset",
            () => _repairService.ResetWindowsUpdateAsync(new Progress<string>(AppendRepairOutput)));
    }

    private async Task RunRepairOperationAsync(string name, Func<Task<SystemRepairResult>> operation)
    {
        IsRepairing = true;
        _repairOutputBuilder.Clear();
        RepairOutput = "";
        StatusText = $"Running {name}...";

        try
        {
            var result = await operation();
            RepairResults.Insert(0, result);
            RepairOutput = result.Output;
            StatusText = result.Success
                ? $"{name} completed successfully ({result.DurationFormatted})"
                : $"{name} failed — exit code {result.ExitCode} ({result.DurationFormatted})";
        }
        catch (Exception ex)
        {
            StatusText = $"Error running {name}: {ex.Message}";
        }
        finally
        {
            IsRepairing = false;
        }
    }

    private readonly System.Text.StringBuilder _repairOutputBuilder = new();

    private void AppendRepairOutput(string line)
    {
        _repairOutputBuilder.AppendLine(line);
        RepairOutput = _repairOutputBuilder.ToString();
    }

    // ================================================================
    // Printer Management Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadPrintersAsync()
    {
        IsLoadingPrinters = true;
        StatusText = "Enumerating printers...";

        try
        {
            var printers = await _printerService.GetPrintersAsync();
            Printers.Clear();
            foreach (var printer in printers)
                Printers.Add(printer);

            StatusText = $"{Printers.Count} printer(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingPrinters = false;
        }
    }

    [RelayCommand]
    private async Task ClearSpoolerAsync()
    {
        StatusText = "Clearing print spooler...";
        try
        {
            var success = await _printerService.ClearSpoolerAsync();
            StatusText = success ? "Print spooler cleared" : "Failed to clear spooler";
            if (success) await LoadPrintersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemovePrinterAsync()
    {
        if (SelectedPrinter is null) return;
        if (!DialogHelper.Confirm($"Remove printer '{SelectedPrinter.Name}'?"))
            return;

        StatusText = $"Removing {SelectedPrinter.Name}...";
        try
        {
            var success = await _printerService.RemovePrinterAsync(SelectedPrinter.Name);
            StatusText = success
                ? $"Printer '{SelectedPrinter.Name}' removed"
                : $"Failed to remove '{SelectedPrinter.Name}'";
            if (success) await LoadPrintersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetDefaultPrinterAsync()
    {
        if (SelectedPrinter is null) return;

        StatusText = $"Setting {SelectedPrinter.Name} as default...";
        try
        {
            var success = await _printerService.SetDefaultPrinterAsync(SelectedPrinter.Name);
            StatusText = success
                ? $"'{SelectedPrinter.Name}' set as default printer"
                : $"Failed to set default printer";
            if (success) await LoadPrintersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PrintTestPageAsync()
    {
        if (SelectedPrinter is null) return;

        StatusText = $"Printing test page on {SelectedPrinter.Name}...";
        try
        {
            var success = await _printerService.PrintTestPageAsync(SelectedPrinter.Name);
            StatusText = success
                ? $"Test page sent to '{SelectedPrinter.Name}'"
                : $"Failed to print test page";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    // ================================================================
    // Hosts File Editor Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadHostsFileAsync()
    {
        IsLoadingHosts = true;
        StatusText = "Reading hosts file...";

        try
        {
            var entries = await _hostsFileService.ReadHostsFileAsync();
            HostsEntries.Clear();
            foreach (var entry in entries)
                HostsEntries.Add(entry);

            StatusText = $"{HostsEntries.Count} host entry/entries found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error reading hosts file: {ex.Message}";
        }
        finally
        {
            IsLoadingHosts = false;
        }
    }

    [RelayCommand]
    private async Task AddHostEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewHostHostname))
        {
            StatusText = "Please enter a hostname";
            return;
        }

        try
        {
            var comment = string.IsNullOrWhiteSpace(NewHostComment) ? null : NewHostComment.Trim();
            await _hostsFileService.AddEntryAsync(NewHostIp.Trim(), NewHostHostname.Trim(), comment);

            NewHostHostname = "";
            NewHostComment = "";
            StatusText = "Host entry added";
            await LoadHostsFileAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveHostEntryAsync()
    {
        if (SelectedHostEntry is null) return;
        if (!DialogHelper.Confirm($"Remove host entry '{SelectedHostEntry.Hostname}'?"))
            return;

        try
        {
            await _hostsFileService.RemoveEntryAsync(SelectedHostEntry.Hostname);
            StatusText = $"Removed '{SelectedHostEntry.Hostname}'";
            await LoadHostsFileAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleHostEntryAsync()
    {
        if (SelectedHostEntry is null) return;

        try
        {
            var action = SelectedHostEntry.IsEnabled ? "Disabled" : "Enabled";
            await _hostsFileService.ToggleEntryAsync(SelectedHostEntry.Hostname);
            StatusText = $"{action} '{SelectedHostEntry.Hostname}'";
            await LoadHostsFileAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    // ================================================================
    // Debloat / Bloatware Removal Commands
    // ================================================================

    [RelayCommand]
    private async Task ScanBloatwareAsync()
    {
        IsLoadingBloatware = true;
        DebloatOutput = "";
        StatusText = "Scanning for bloatware...";

        try
        {
            var packages = await _debloatService.GetBloatwareListAsync();
            BloatwarePackages.Clear();
            foreach (var pkg in packages)
                BloatwarePackages.Add(pkg);

            ApplyBloatwareFilter();

            var installed = packages.Count(p => p.IsInstalled);
            StatusText = $"{installed} bloatware package(s) found installed out of {packages.Count} known";
        }
        catch (Exception ex)
        {
            StatusText = $"Error scanning: {ex.Message}";
        }
        finally
        {
            IsLoadingBloatware = false;
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedBloatwareAsync()
    {
        var selected = BloatwarePackages.Where(p => p.IsSelected && p.IsInstalled).ToList();
        if (selected.Count == 0)
        {
            StatusText = "No packages selected for removal";
            return;
        }

        if (!DialogHelper.Confirm($"Remove {selected.Count} selected package(s)?\nThis will remove them for all users."))
            return;

        IsLoadingBloatware = true;
        _debloatOutputBuilder.Clear();
        DebloatOutput = "";
        StatusText = $"Removing {selected.Count} package(s)...";

        try
        {
            var result = await _debloatService.RemovePackagesAsync(selected,
                new Progress<string>(AppendDebloatOutput));

            StatusText = $"Removed {result.Removed}/{result.TotalSelected} — " +
                         $"{result.Failed} failed, {result.NotFound} not found";

            await ScanBloatwareAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingBloatware = false;
        }
    }

    [RelayCommand]
    private async Task DisableCopilotAsync()
    {
        if (!DialogHelper.Confirm("Disable Microsoft Copilot?\nThis removes the Copilot package and applies registry policies."))
            return;

        IsLoadingBloatware = true;
        _debloatOutputBuilder.Clear();
        DebloatOutput = "";
        StatusText = "Disabling Microsoft Copilot...";

        try
        {
            var success = await _debloatService.DisableCopilotAsync(
                new Progress<string>(AppendDebloatOutput));
            StatusText = success ? "Copilot disabled successfully" : "Copilot disable completed with warnings";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingBloatware = false;
        }
    }

    [RelayCommand]
    private async Task DisableRecallAsync()
    {
        if (!DialogHelper.Confirm("Disable Windows Recall?\nThis applies registry policies to prevent Recall on 24H2 Copilot+ PCs."))
            return;

        IsLoadingBloatware = true;
        _debloatOutputBuilder.Clear();
        DebloatOutput = "";
        StatusText = "Disabling Windows Recall...";

        try
        {
            var success = await _debloatService.DisableRecallAsync(
                new Progress<string>(AppendDebloatOutput));
            StatusText = success ? "Recall disabled successfully" : "Recall disable completed with warnings";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingBloatware = false;
        }
    }

    [RelayCommand]
    private async Task ApplyPrivacyTweaksAsync()
    {
        if (!DialogHelper.Confirm("Apply privacy tweaks?\nThis disables telemetry, advertising ID, Start suggestions, Bing search, and lock screen ads."))
            return;

        IsLoadingBloatware = true;
        _debloatOutputBuilder.Clear();
        DebloatOutput = "";
        StatusText = "Applying privacy tweaks...";

        try
        {
            var success = await _debloatService.ApplyPrivacyTweaksAsync(
                new Progress<string>(AppendDebloatOutput));
            StatusText = success ? "Privacy tweaks applied" : "Privacy tweaks completed with warnings";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingBloatware = false;
        }
    }

    [RelayCommand]
    private void SelectAllSafeBloatware()
    {
        foreach (var pkg in BloatwarePackages.Where(p => p.IsInstalled && p.Safety == DebloatSafety.Safe))
            pkg.IsSelected = true;
        ApplyBloatwareFilter();
        StatusText = $"Selected {BloatwarePackages.Count(p => p.IsSelected)} safe package(s)";
    }

    [RelayCommand]
    private void DeselectAllBloatware()
    {
        foreach (var pkg in BloatwarePackages)
            pkg.IsSelected = false;
        ApplyBloatwareFilter();
        StatusText = "Selection cleared";
    }

    private void ApplyBloatwareFilter()
    {
        FilteredBloatwarePackages.Clear();
        foreach (var pkg in BloatwarePackages)
        {
            if (DebloatCategoryFilter != "All" && pkg.CategoryDisplay != DebloatCategoryFilter)
                continue;
            FilteredBloatwarePackages.Add(pkg);
        }
    }

    private readonly System.Text.StringBuilder _debloatOutputBuilder = new();

    private void AppendDebloatOutput(string line)
    {
        _debloatOutputBuilder.AppendLine(line);
        DebloatOutput = _debloatOutputBuilder.ToString();
    }
}
