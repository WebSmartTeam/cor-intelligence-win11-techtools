using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Helpers;

namespace CORCleanup.ViewModels;

public partial class RegistryViewModel : ObservableObject
{
    private readonly IRegistryCleanerService _registryCleaner;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty] private string _pageTitle = "Registry Cleaner";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasScanned;
    [ObservableProperty] private string _statusText = "Ready — scan to find registry issues";
    [ObservableProperty] private string _scanProgress = "";
    [ObservableProperty] private string _resultsSummary = "";
    [ObservableProperty] private int _selectedTabIndex;

    // Scan results
    public ObservableCollection<RegistryIssue> Issues { get; } = new();

    // Backup management
    public ObservableCollection<RegistryBackup> Backups { get; } = new();
    [ObservableProperty] private RegistryBackup? _selectedBackup;

    public RegistryViewModel(IRegistryCleanerService registryCleaner)
    {
        _registryCleaner = registryCleaner;
    }

    // ----------------------------------------------------------------
    // Scanning
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task ScanRegistryAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        IsScanning = true;
        HasScanned = false;
        StatusText = "Scanning registry...";
        ScanProgress = "";
        ResultsSummary = "";
        Issues.Clear();

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
                ScanProgress = $"Checked {categoryName}: {update.Found} issue(s) found";
            });

            var results = await _registryCleaner.ScanAsync(progress, _scanCts.Token);

            foreach (var issue in results)
            {
                // Pre-select Safe items, leave Review and Caution unselected
                issue.IsSelected = issue.Risk == RegistryRiskLevel.Safe;
                Issues.Add(issue);
            }

            HasScanned = true;

            var safe = results.Count(i => i.Risk == RegistryRiskLevel.Safe);
            var review = results.Count(i => i.Risk == RegistryRiskLevel.Review);
            var caution = results.Count(i => i.Risk == RegistryRiskLevel.Caution);

            ResultsSummary = $"{results.Count} issue(s) found — {safe} safe, {review} review, {caution} caution";
            StatusText = results.Count == 0
                ? "Registry is clean — no issues found"
                : $"Scan complete: {results.Count} issue(s) found";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanProgress = "";
        }
    }

    [RelayCommand]
    private void StopScan()
    {
        _scanCts?.Cancel();
    }

    // ----------------------------------------------------------------
    // Selection helpers
    // ----------------------------------------------------------------

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var issue in Issues)
            issue.IsSelected = true;
        OnPropertyChanged(nameof(Issues));
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var issue in Issues)
            issue.IsSelected = false;
        OnPropertyChanged(nameof(Issues));
    }

    [RelayCommand]
    private void SelectSafeOnly()
    {
        foreach (var issue in Issues)
            issue.IsSelected = issue.Risk == RegistryRiskLevel.Safe;
        OnPropertyChanged(nameof(Issues));
    }

    // ----------------------------------------------------------------
    // Fix selected
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task FixSelectedAsync()
    {
        var selected = Issues.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "No items selected to fix";
            return;
        }

        if (!DialogHelper.Confirm($"Fix {selected.Count} registry issue(s)?\nA backup will be created before changes are made."))
            return;

        IsScanning = true;
        StatusText = $"Creating backup and fixing {selected.Count} issue(s)...";

        try
        {
            var result = await _registryCleaner.FixSelectedAsync(selected);

            // Remove fixed issues from the list using per-issue tracking
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
                Issues.Remove(issue);

            var msg = $"Fixed {result.Fixed} of {result.TotalSelected} issue(s)";
            if (result.Failed > 0)
                msg += $" — {result.Failed} failed";
            msg += $" — backup saved to {Path.GetFileName(result.BackupFilePath)}";

            StatusText = msg;
            ResultsSummary = $"{Issues.Count} issue(s) remaining";

            // Refresh backups tab
            await LoadBackupsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Fix error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    // ----------------------------------------------------------------
    // Backup management
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        try
        {
            var backups = await _registryCleaner.GetBackupsAsync();
            Backups.Clear();
            foreach (var backup in backups)
                Backups.Add(backup);

            StatusText = $"{Backups.Count} backup(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading backups: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup is null)
        {
            StatusText = "Select a backup to restore";
            return;
        }

        if (!DialogHelper.Confirm($"Restore registry backup '{SelectedBackup.FileName}'?\nThis will overwrite current registry values."))
            return;

        IsScanning = true;
        StatusText = $"Restoring {SelectedBackup.FileName}...";

        try
        {
            var success = await _registryCleaner.RestoreBackupAsync(SelectedBackup.FilePath);
            StatusText = success
                ? $"Successfully restored {SelectedBackup.FileName}"
                : $"Failed to restore {SelectedBackup.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Restore error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync()
    {
        if (SelectedBackup is null)
        {
            StatusText = "Select a backup to delete";
            return;
        }

        if (!DialogHelper.Confirm($"Permanently delete backup '{SelectedBackup.FileName}'?"))
            return;

        var fileName = SelectedBackup.FileName;

        try
        {
            var success = await _registryCleaner.DeleteBackupAsync(SelectedBackup.FilePath);
            if (success)
            {
                Backups.Remove(SelectedBackup);
                SelectedBackup = null;
                StatusText = $"Deleted {fileName}";
            }
            else
            {
                StatusText = $"Failed to delete {fileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Delete error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        var dir = _registryCleaner.GetBackupDirectory();
        if (Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
    }
}
