using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Helpers;

namespace CORCleanup.ViewModels;

public partial class CleanupViewModel : ObservableObject
{
    private readonly ICleanupService _cleanupService;
    private readonly IBrowserCleanupService _browserCleanupService;
    private readonly IRegistryCleanerService _registryCleaner;
    private readonly IDebloatService _debloatService;
    private CancellationTokenSource? _cleanCts;
    private CancellationTokenSource? _browserCleanCts;
    private CancellationTokenSource? _regScanCts;

    [ObservableProperty] private string _pageTitle = "System Cleanup";
    [ObservableProperty] private int _cleanupTabIndex;

    // ================================================================
    // System Cleanup State
    // ================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCleanCommand))]
    private bool _isCleaning;

    [ObservableProperty] private string _statusText = "Ready — press Scan to analyse";
    [ObservableProperty] private string _totalSizeText = "";
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _hasScanned;

    public ObservableCollection<CleanupItem> CleanupItems { get; } = new();

    // ================================================================
    // Browser Cleanup State
    // ================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanBrowserDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanBrowserDataCommand))]
    private bool _isScanningBrowsers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanBrowserDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanBrowserDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopBrowserCleanCommand))]
    private bool _isCleaningBrowsers;

    [ObservableProperty] private string _browserStatusText = "Ready — press Scan Browser Data to analyse";
    [ObservableProperty] private string _browserResultText = "";
    [ObservableProperty] private string _browserTotalSize = "";
    [ObservableProperty] private bool _hasBrowserScanned;
    [ObservableProperty] private string _runningBrowserWarning = "";

    public ObservableCollection<BrowserCleanupItem> BrowserCleanupItems { get; } = new();

    // ================================================================
    // Registry Scanner State
    // ================================================================

    [ObservableProperty] private bool _regIsScanning;
    [ObservableProperty] private bool _regHasScanned;
    [ObservableProperty] private string _regStatusText = "Ready — scan to find registry issues";
    [ObservableProperty] private string _regScanProgress = "";
    [ObservableProperty] private string _regResultsSummary = "";

    public ObservableCollection<RegistryIssue> RegIssues { get; } = new();

    // Registry Backup Management
    public ObservableCollection<RegistryBackup> RegBackups { get; } = new();
    [ObservableProperty] private RegistryBackup? _selectedRegBackup;

    // ================================================================
    // Debloat / Bloatware Removal State
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

    public CleanupViewModel(
        ICleanupService cleanupService,
        IBrowserCleanupService browserCleanupService,
        IRegistryCleanerService registryCleaner,
        IDebloatService debloatService)
    {
        _cleanupService = cleanupService;
        _browserCleanupService = browserCleanupService;
        _registryCleaner = registryCleaner;
        _debloatService = debloatService;
    }

    partial void OnDebloatCategoryFilterChanged(string value) => ApplyBloatwareFilter();

    // ================================================================
    // System Cleanup Commands
    // ================================================================

    private bool CanScan() => !IsScanning && !IsCleaning;
    private bool CanClean() => !IsScanning && !IsCleaning && HasScanned;
    private bool CanStopClean() => IsCleaning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        StatusText = "Scanning for cleanable files...";
        CleanupItems.Clear();
        ResultText = "";

        try
        {
            var items = await _cleanupService.ScanAsync();

            foreach (var item in items)
            {
                item.IsSelected = item.IsSelectedByDefault;
                CleanupItems.Add(item);
            }

            var totalSize = items.Sum(i => i.EstimatedSizeBytes);
            TotalSizeText = ByteFormatter.Format(totalSize);
            HasScanned = true;
            StatusText = $"Scan complete — {TotalSizeText} can be cleaned";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selectedCategories = CleanupItems
            .Where(i => i.IsSelected)
            .Select(i => i.Category)
            .ToList();

        if (selectedCategories.Count == 0)
        {
            StatusText = "No items selected for cleaning";
            return;
        }

        var runningBrowsers = _browserCleanupService.GetRunningBrowsers();
        if (runningBrowsers.Count > 0)
        {
            RunningBrowserWarning = $"{string.Join(" and ", runningBrowsers)} running. Close them for a thorough clean.";
        }
        else
        {
            RunningBrowserWarning = "";
        }

        if (!DialogHelper.Confirm($"Clean {selectedCategories.Count} selected category/categories?\nThis will permanently delete temporary files."))
            return;

        _cleanCts = new CancellationTokenSource();
        IsCleaning = true;
        ResultText = "";

        var progress = new Progress<string>(msg => StatusText = msg);

        try
        {
            var result = await _cleanupService.CleanAsync(selectedCategories, progress, _cleanCts.Token);

            var lines = new List<string>
            {
                $"Space reclaimed: {result.TotalFreedFormatted}",
                $"Items cleaned: {result.ItemsCleaned} | Failed: {result.ItemsFailed}",
                $"Duration: {result.Duration.TotalSeconds:F1}s",
                ""
            };

            foreach (var detail in result.Details)
            {
                var status = detail.Success ? "OK" : "FAILED";
                lines.Add($"  {status}: {detail.DisplayName} — {ByteFormatter.Format(detail.BytesFreed)}");
                if (detail.ErrorMessage is not null)
                    lines.Add($"         Error: {detail.ErrorMessage}");
            }

            ResultText = string.Join(Environment.NewLine, lines);
            StatusText = $"Cleanup complete — {result.TotalFreedFormatted} reclaimed";

            // Remove successfully cleaned items from the list
            var cleanedCategories = result.Details
                .Where(d => d.Success)
                .Select(d => d.Category)
                .ToHashSet();

            var toRemove = CleanupItems
                .Where(i => i.IsSelected && cleanedCategories.Contains(i.Category))
                .ToList();

            foreach (var item in toRemove)
                CleanupItems.Remove(item);

            // Update totals for any remaining items
            if (CleanupItems.Count > 0)
            {
                var remaining = CleanupItems.Sum(i => i.EstimatedSizeBytes);
                TotalSizeText = ByteFormatter.Format(remaining);
            }
            else
            {
                TotalSizeText = "";
                HasScanned = false;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cleanup cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Cleanup error: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            _cleanCts?.Dispose();
            _cleanCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopClean))]
    private void StopClean()
    {
        _cleanCts?.Cancel();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in CleanupItems)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in CleanupItems)
            item.IsSelected = false;
    }

    [RelayCommand]
    private void SelectDefaults()
    {
        foreach (var item in CleanupItems)
            item.IsSelected = item.IsSelectedByDefault;
    }

    [RelayCommand]
    private async Task CloseBrowsersAsync()
    {
        var browserProcessNames = new[] { "chrome", "msedge", "firefox", "brave" };
        var closed = new List<string>();

        await Task.Run(() =>
        {
            foreach (var name in browserProcessNames)
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (proc.CloseMainWindow())
                        {
                            proc.WaitForExit(10_000);
                            if (!closed.Contains(proc.ProcessName))
                                closed.Add(proc.ProcessName);
                        }
                    }
                    catch
                    {
                        // Process may have already exited
                    }
                }
            }
        });

        if (closed.Count > 0)
        {
            RunningBrowserWarning = "";
            StatusText = $"Closed: {string.Join(", ", closed)}";
            BrowserStatusText = "Browsers closed — ready for a thorough clean";
        }
        else
        {
            var stillRunning = _browserCleanupService.GetRunningBrowsers();
            if (stillRunning.Count == 0)
            {
                RunningBrowserWarning = "";
                StatusText = "No browsers detected";
            }
            else
            {
                RunningBrowserWarning = $"{string.Join(" and ", stillRunning)} could not be closed. Please close them manually.";
            }
        }
    }

    // ================================================================
    // Browser Cleanup Commands
    // ================================================================

    private bool CanScanBrowser() => !IsScanningBrowsers && !IsCleaningBrowsers;
    private bool CanCleanBrowser() => !IsScanningBrowsers && !IsCleaningBrowsers && HasBrowserScanned;
    private bool CanStopBrowserClean() => IsCleaningBrowsers;

    [RelayCommand(CanExecute = nameof(CanScanBrowser))]
    private async Task ScanBrowserDataAsync()
    {
        IsScanningBrowsers = true;
        BrowserCleanupItems.Clear();
        BrowserResultText = "";

        try
        {
            var running = _browserCleanupService.GetRunningBrowsers();
            if (running.Count > 0)
            {
                BrowserStatusText = $"Warning: {string.Join(", ", running)} running — close for best results. Scanning...";
            }
            else
            {
                BrowserStatusText = "Scanning browser data...";
            }

            var items = await _browserCleanupService.ScanBrowserDataAsync();

            foreach (var item in items)
            {
                item.IsSelected = item.IsSafe;
                BrowserCleanupItems.Add(item);
            }

            var safeTotal = items.Where(i => i.IsSafe).Sum(i => i.SizeBytes);
            BrowserTotalSize = ByteFormatter.Format(safeTotal);
            HasBrowserScanned = true;
            BrowserStatusText = $"Scan complete — {ByteFormatter.Format(items.Sum(i => i.SizeBytes))} found ({BrowserTotalSize} safe to clean)";
        }
        catch (Exception ex)
        {
            BrowserStatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanningBrowsers = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCleanBrowser))]
    private async Task CleanBrowserDataAsync()
    {
        var selected = BrowserCleanupItems.Where(i => i.IsSelected).ToList();

        if (selected.Count == 0)
        {
            BrowserStatusText = "No items selected for cleaning";
            return;
        }

        var running = _browserCleanupService.GetRunningBrowsers();
        var warningPrefix = running.Count > 0
            ? $"Warning: {string.Join(", ", running)} still running — some files may be locked.\n\n"
            : "";

        if (!DialogHelper.Confirm($"{warningPrefix}Clean {selected.Count} selected browser item(s)?\nThis will permanently delete the selected browser data."))
            return;

        _browserCleanCts = new CancellationTokenSource();
        IsCleaningBrowsers = true;
        BrowserResultText = "";
        BrowserStatusText = "Cleaning browser data...";

        try
        {
            var bytesFreed = await _browserCleanupService.CleanSelectedAsync(selected, _browserCleanCts.Token);
            var freedFormatted = ByteFormatter.Format(bytesFreed);
            BrowserResultText = $"Space reclaimed: {freedFormatted}";
            BrowserStatusText = $"Browser cleanup complete — {freedFormatted} reclaimed";
        }
        catch (OperationCanceledException)
        {
            BrowserStatusText = "Browser cleanup cancelled";
        }
        catch (Exception ex)
        {
            BrowserStatusText = $"Browser cleanup error: {ex.Message}";
        }
        finally
        {
            IsCleaningBrowsers = false;
            _browserCleanCts?.Dispose();
            _browserCleanCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopBrowserClean))]
    private void StopBrowserClean()
    {
        _browserCleanCts?.Cancel();
    }

    [RelayCommand]
    private void SelectAllSafeBrowser()
    {
        foreach (var item in BrowserCleanupItems)
            item.IsSelected = item.IsSafe;
    }

    [RelayCommand]
    private void SelectAllBrowser()
    {
        foreach (var item in BrowserCleanupItems)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAllBrowser()
    {
        foreach (var item in BrowserCleanupItems)
            item.IsSelected = false;
    }

    // ================================================================
    // Registry Scanner Commands
    // ================================================================

    [RelayCommand]
    private async Task ScanRegistryAsync()
    {
        _regScanCts?.Cancel();
        _regScanCts = new CancellationTokenSource();

        RegIsScanning = true;
        RegHasScanned = false;
        RegStatusText = "Scanning registry...";
        RegScanProgress = "";
        RegResultsSummary = "";
        RegIssues.Clear();

        try
        {
            var progress = new Progress<(RegistryScanCategory Category, int Found)>(update =>
            {
                var categoryName = update.Category switch
                {
                    RegistryScanCategory.MissingSharedDlls => "Shared DLLs",
                    RegistryScanCategory.UnusedFileExtensions => "File Extensions",
                    RegistryScanCategory.OrphanedComActiveX => "COM/ActiveX",
                    RegistryScanCategory.InvalidApplicationPaths => "App Paths",
                    RegistryScanCategory.ObsoleteSoftwareEntries => "Software Entries",
                    RegistryScanCategory.MissingMuiReferences => "MUI References",
                    RegistryScanCategory.StaleInstallerReferences => "Installer References",
                    RegistryScanCategory.DeadShortcutReferences => "Shortcut References",
                    _ => update.Category.ToString()
                };
                RegScanProgress = $"Checked {categoryName}: {update.Found} issue(s) found";
            });

            var results = await _registryCleaner.ScanAsync(progress, _regScanCts.Token);

            foreach (var issue in results)
            {
                issue.IsSelected = issue.Risk == RegistryRiskLevel.Safe;
                RegIssues.Add(issue);
            }

            RegHasScanned = true;

            var safe = results.Count(i => i.Risk == RegistryRiskLevel.Safe);
            var review = results.Count(i => i.Risk == RegistryRiskLevel.Review);
            var caution = results.Count(i => i.Risk == RegistryRiskLevel.Caution);

            RegResultsSummary = $"{results.Count} issue(s) found — {safe} safe, {review} review, {caution} caution";
            RegStatusText = results.Count == 0
                ? "Registry is clean — no issues found"
                : $"Scan complete: {results.Count} issue(s) found";
        }
        catch (OperationCanceledException)
        {
            RegStatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            RegStatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            RegIsScanning = false;
            RegScanProgress = "";
        }
    }

    [RelayCommand]
    private void StopRegScan()
    {
        _regScanCts?.Cancel();
    }

    [RelayCommand]
    private void RegSelectAll()
    {
        foreach (var issue in RegIssues)
            issue.IsSelected = true;
        RefreshRegIssuesView();
    }

    [RelayCommand]
    private void RegSelectNone()
    {
        foreach (var issue in RegIssues)
            issue.IsSelected = false;
        RefreshRegIssuesView();
    }

    [RelayCommand]
    private void RegSelectSafeOnly()
    {
        foreach (var issue in RegIssues)
            issue.IsSelected = issue.Risk == RegistryRiskLevel.Safe;
        RefreshRegIssuesView();
    }

    private void RefreshRegIssuesView()
    {
        CollectionViewSource.GetDefaultView(RegIssues)?.Refresh();
    }

    [RelayCommand]
    private async Task RegFixSelectedAsync()
    {
        var selected = RegIssues.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            RegStatusText = "No items selected to fix";
            return;
        }

        if (!DialogHelper.Confirm($"Fix {selected.Count} registry issue(s)?\nA backup will be created before changes are made."))
            return;

        RegIsScanning = true;
        RegStatusText = $"Creating backup and fixing {selected.Count} issue(s)...";

        try
        {
            var result = await _registryCleaner.FixSelectedAsync(selected);

            var toRemove = selected
                .Where(issue =>
                {
                    var key = issue.ValueName is not null
                        ? $"{issue.KeyPath}\\{issue.ValueName}"
                        : issue.KeyPath;
                    return result.FixedKeyPaths.Contains(key);
                })
                .ToList();

            foreach (var issue in toRemove)
                RegIssues.Remove(issue);

            var msg = $"Fixed {result.Fixed} of {result.TotalSelected} issue(s)";
            if (result.Failed > 0)
                msg += $" — {result.Failed} failed";
            msg += $" — backup saved to {Path.GetFileName(result.BackupFilePath)}";

            RegStatusText = msg;
            RegResultsSummary = $"{RegIssues.Count} issue(s) remaining";

            await LoadRegBackupsAsync();
        }
        catch (Exception ex)
        {
            RegStatusText = $"Fix error: {ex.Message}";
        }
        finally
        {
            RegIsScanning = false;
        }
    }

    // ================================================================
    // Registry Backup Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadRegBackupsAsync()
    {
        try
        {
            var backups = await _registryCleaner.GetBackupsAsync();
            RegBackups.Clear();
            foreach (var backup in backups)
                RegBackups.Add(backup);

            RegStatusText = $"{RegBackups.Count} backup(s) found";
        }
        catch (Exception ex)
        {
            RegStatusText = $"Error loading backups: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreRegBackupAsync()
    {
        if (SelectedRegBackup is null)
        {
            RegStatusText = "Select a backup to restore";
            return;
        }

        if (!DialogHelper.Confirm($"Restore registry backup '{SelectedRegBackup.FileName}'?\nThis will overwrite current registry values."))
            return;

        RegIsScanning = true;
        RegStatusText = $"Restoring {SelectedRegBackup.FileName}...";

        try
        {
            var success = await _registryCleaner.RestoreBackupAsync(SelectedRegBackup.FilePath);
            RegStatusText = success
                ? $"Successfully restored {SelectedRegBackup.FileName}"
                : $"Failed to restore {SelectedRegBackup.FileName}";
        }
        catch (Exception ex)
        {
            RegStatusText = $"Restore error: {ex.Message}";
        }
        finally
        {
            RegIsScanning = false;
        }
    }

    [RelayCommand]
    private async Task DeleteRegBackupAsync()
    {
        if (SelectedRegBackup is null)
        {
            RegStatusText = "Select a backup to delete";
            return;
        }

        if (!DialogHelper.Confirm($"Permanently delete backup '{SelectedRegBackup.FileName}'?"))
            return;

        var fileName = SelectedRegBackup.FileName;

        try
        {
            var success = await _registryCleaner.DeleteBackupAsync(SelectedRegBackup.FilePath);
            if (success)
            {
                RegBackups.Remove(SelectedRegBackup);
                SelectedRegBackup = null;
                RegStatusText = $"Deleted {fileName}";
            }
            else
            {
                RegStatusText = $"Failed to delete {fileName}";
            }
        }
        catch (Exception ex)
        {
            RegStatusText = $"Delete error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenRegBackupFolder()
    {
        var dir = _registryCleaner.GetBackupDirectory();
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
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
