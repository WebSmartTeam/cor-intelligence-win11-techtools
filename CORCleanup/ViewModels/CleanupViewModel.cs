using System.Collections.ObjectModel;
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
    private CancellationTokenSource? _cleanCts;

    [ObservableProperty] private string _pageTitle = "System Cleanup";

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

    public CleanupViewModel(ICleanupService cleanupService)
    {
        _cleanupService = cleanupService;
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
}
