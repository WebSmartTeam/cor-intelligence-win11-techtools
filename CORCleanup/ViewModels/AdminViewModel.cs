using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly IFirewallService _firewallService;
    private readonly IEnvironmentService _environmentService;
    private readonly IReportService _reportService;

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
    public ObservableCollection<Core.Models.EventLogEntry> EventLogEntries { get; } = new();

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
    // Firewall Rules
    // ================================================================

    [ObservableProperty] private bool _isLoadingFirewall;
    [ObservableProperty] private string _firewallFilter = "";
    [ObservableProperty] private string _firewallDirectionFilter = "All";
    [ObservableProperty] private FirewallRule? _selectedFirewallRule;
    public ObservableCollection<FirewallRule> FirewallRules { get; } = new();
    public ObservableCollection<FirewallRule> FilteredFirewallRules { get; } = new();
    public static string[] FirewallDirectionOptions => ["All", "In", "Out"];

    // ================================================================
    // Environment Variables
    // ================================================================

    [ObservableProperty] private bool _isLoadingEnvVars;
    [ObservableProperty] private string _envVarFilter = "";
    [ObservableProperty] private string _envVarScopeFilter = "All";
    [ObservableProperty] private EnvironmentVariable? _selectedEnvVar;
    [ObservableProperty] private string _newEnvVarName = "";
    [ObservableProperty] private string _newEnvVarValue = "";
    [ObservableProperty] private string _newEnvVarScope = "User";
    public ObservableCollection<EnvironmentVariable> EnvVariables { get; } = new();
    public ObservableCollection<EnvironmentVariable> FilteredEnvVariables { get; } = new();
    public ObservableCollection<string> PathEntries { get; } = new();
    public static string[] EnvVarScopes => ["All", "User", "Machine"];
    public static string[] EnvVarEditScopes => ["User", "Machine"];

    // ================================================================
    // System Summary Report
    // ================================================================

    [ObservableProperty] private bool _isExportingReport;
    [ObservableProperty] private string _exportStatusText = "";

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
        IDebloatService debloatService,
        IFirewallService firewallService,
        IEnvironmentService environmentService,
        IReportService reportService)
    {
        _startupService = startupService;
        _servicesManager = servicesManager;
        _eventLogService = eventLogService;
        _repairService = repairService;
        _printerService = printerService;
        _hostsFileService = hostsFileService;
        _debloatService = debloatService;
        _firewallService = firewallService;
        _environmentService = environmentService;
        _reportService = reportService;
    }

    partial void OnHideMicrosoftEntriesChanged(bool value) => ApplyStartupFilter();
    partial void OnServiceFilterChanged(string value) => ApplyServiceFilter();
    partial void OnDebloatCategoryFilterChanged(string value) => ApplyBloatwareFilter();
    partial void OnFirewallFilterChanged(string value) => ApplyFirewallFilter();
    partial void OnFirewallDirectionFilterChanged(string value) => ApplyFirewallFilter();
    partial void OnEnvVarFilterChanged(string value) => ApplyEnvVarFilter();
    partial void OnEnvVarScopeFilterChanged(string value) => ApplyEnvVarFilter();

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

    // ================================================================
    // Firewall Rules Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadFirewallRulesAsync()
    {
        IsLoadingFirewall = true;
        StatusText = "Loading firewall rules...";

        try
        {
            var rules = await _firewallService.GetAllRulesAsync();
            FirewallRules.Clear();
            foreach (var rule in rules)
                FirewallRules.Add(rule);

            ApplyFirewallFilter();
            StatusText = $"{FirewallRules.Count} firewall rule(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingFirewall = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFirewallRuleAsync()
    {
        if (SelectedFirewallRule is null) return;

        var newState = !SelectedFirewallRule.Enabled;
        var action = newState ? "Enabling" : "Disabling";
        StatusText = $"{action} '{SelectedFirewallRule.Name}'...";

        try
        {
            await _firewallService.SetRuleEnabledAsync(SelectedFirewallRule.Name, newState);
            StatusText = $"'{SelectedFirewallRule.Name}' {(newState ? "enabled" : "disabled")}";
            await LoadFirewallRulesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error toggling rule: {ex.Message}";
        }
    }

    private void ApplyFirewallFilter()
    {
        FilteredFirewallRules.Clear();
        var filter = FirewallFilter?.Trim() ?? "";

        foreach (var rule in FirewallRules)
        {
            if (FirewallDirectionFilter != "All" &&
                !rule.Direction.Equals(FirewallDirectionFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(filter) &&
                !rule.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !rule.Program.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !rule.LocalPort.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            FilteredFirewallRules.Add(rule);
        }
    }

    // ================================================================
    // Environment Variables Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadEnvVariablesAsync()
    {
        IsLoadingEnvVars = true;
        StatusText = "Loading environment variables...";

        try
        {
            var variables = await _environmentService.GetAllVariablesAsync();
            EnvVariables.Clear();
            foreach (var v in variables)
                EnvVariables.Add(v);

            ApplyEnvVarFilter();
            StatusText = $"{EnvVariables.Count} environment variable(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingEnvVars = false;
        }
    }

    [RelayCommand]
    private async Task AddEnvVariableAsync()
    {
        if (string.IsNullOrWhiteSpace(NewEnvVarName))
        {
            StatusText = "Please enter a variable name";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewEnvVarValue))
        {
            StatusText = "Please enter a variable value";
            return;
        }

        try
        {
            await _environmentService.SetVariableAsync(NewEnvVarName.Trim(), NewEnvVarValue.Trim(), NewEnvVarScope);
            StatusText = $"Variable '{NewEnvVarName.Trim()}' set ({NewEnvVarScope})";
            NewEnvVarName = "";
            NewEnvVarValue = "";
            await LoadEnvVariablesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteEnvVariableAsync()
    {
        if (SelectedEnvVar is null) return;
        if (!DialogHelper.Confirm($"Delete environment variable '{SelectedEnvVar.Name}' ({SelectedEnvVar.Scope})?"))
            return;

        try
        {
            await _environmentService.DeleteVariableAsync(SelectedEnvVar.Name, SelectedEnvVar.Scope);
            StatusText = $"Variable '{SelectedEnvVar.Name}' deleted";
            await LoadEnvVariablesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadPathEntries()
    {
        PathEntries.Clear();

        try
        {
            var userEntries = _environmentService.GetPathEntries("User");
            var machineEntries = _environmentService.GetPathEntries("Machine");

            foreach (var entry in machineEntries)
                PathEntries.Add($"[Machine] {entry}");
            foreach (var entry in userEntries)
                PathEntries.Add($"[User] {entry}");

            StatusText = $"{PathEntries.Count} PATH entry/entries loaded";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading PATH: {ex.Message}";
        }
    }

    private void ApplyEnvVarFilter()
    {
        FilteredEnvVariables.Clear();
        var filter = EnvVarFilter?.Trim() ?? "";

        foreach (var v in EnvVariables)
        {
            if (EnvVarScopeFilter != "All" &&
                !v.Scope.Equals(EnvVarScopeFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(filter) &&
                !v.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !v.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            FilteredEnvVariables.Add(v);
        }
    }

    // ================================================================
    // System Summary Report Commands
    // ================================================================

    [RelayCommand]
    private async Task ExportSystemReportAsync()
    {
        IsExportingReport = true;
        ExportStatusText = "Gathering system data...";
        StatusText = "Generating system summary report...";

        try
        {
            var htmlContent = await _reportService.GenerateHtmlReportAsync();

            ExportStatusText = "Report generated — choose save location...";

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save System Summary Report",
                Filter = "HTML Files|*.html",
                DefaultExt = ".html",
                FileName = $"COR-Cleanup-Report-{DateTime.Now:yyyy-MM-dd-HHmm}.html"
            };

            if (dialog.ShowDialog() == true)
            {
                await _reportService.SaveReportAsync(htmlContent, dialog.FileName);
                ExportStatusText = $"Report saved to {dialog.FileName}";
                StatusText = "System summary report exported successfully";

                // Open the report in the default browser
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Non-critical — report is saved even if browser fails to open
                }
            }
            else
            {
                ExportStatusText = "Export cancelled";
                StatusText = "Report export cancelled";
            }
        }
        catch (Exception ex)
        {
            ExportStatusText = $"Error: {ex.Message}";
            StatusText = $"Error generating report: {ex.Message}";
        }
        finally
        {
            IsExportingReport = false;
        }
    }
}
