using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private CancellationTokenSource? _cleanCts;
    private CancellationTokenSource? _browserCleanCts;

    [ObservableProperty] private string _pageTitle = "System Cleanup";
    [ObservableProperty] private int _cleanupTabIndex;

    // --- System cleanup state ---

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

    // --- Browser cleanup state ---

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

    public CleanupViewModel(ICleanupService cleanupService, IBrowserCleanupService browserCleanupService)
    {
        _cleanupService = cleanupService;
        _browserCleanupService = browserCleanupService;
    }

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

        // Check for running browsers and warn if detected
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
                        // Graceful close — sends WM_CLOSE, gives the user a chance to save tabs
                        if (proc.CloseMainWindow())
                        {
                            proc.WaitForExit(10_000); // 10-second timeout
                            if (!closed.Contains(proc.ProcessName))
                                closed.Add(proc.ProcessName);
                        }
                    }
                    catch
                    {
                        // Process may have already exited — skip
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
            // Re-check in case they were already closed
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

    // ──────────────────────────────────────────────────────
    //  Browser Cleanup Commands
    // ──────────────────────────────────────────────────────

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
}
