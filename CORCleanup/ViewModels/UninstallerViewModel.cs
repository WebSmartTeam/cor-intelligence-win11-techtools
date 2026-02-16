using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Helpers;

namespace CORCleanup.ViewModels;

public partial class UninstallerViewModel : ObservableObject
{
    private readonly IUninstallService _uninstallService;

    [ObservableProperty] private string _pageTitle = "Programme Uninstaller";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _searchFilter = "";
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _hasLeftovers;
    [ObservableProperty] private bool _isUninstalling;

    public ObservableCollection<InstalledProgram> Programs { get; } = new();
    public ObservableCollection<InstalledProgram> FilteredPrograms { get; } = new();
    public ObservableCollection<UninstallLeftover> Leftovers { get; } = new();

    /// <summary>Updated from code-behind via DataGrid.SelectionChanged.</summary>
    public List<InstalledProgram> SelectedItems { get; set; } = new();

    public UninstallerViewModel(IUninstallService uninstallService)
    {
        _uninstallService = uninstallService;
    }

    /// <summary>Called from code-behind when DataGrid selection changes.</summary>
    public void UpdateSelection(List<InstalledProgram> selected)
    {
        SelectedItems = selected;
        SelectedCount = selected.Count;
    }

    partial void OnSearchFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadProgramsAsync()
    {
        IsLoading = true;
        StatusText = "Enumerating installed programmes...";
        HasLeftovers = false;
        Leftovers.Clear();

        try
        {
            var programs = await _uninstallService.GetInstalledProgramsAsync();
            Programs.Clear();
            foreach (var program in programs)
                Programs.Add(program);

            ApplyFilter();
            StatusText = $"{Programs.Count} programme(s) found";
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
    private async Task UninstallSelectedAsync()
    {
        if (SelectedItems.Count == 0) return;

        var toUninstall = SelectedItems.ToList(); // snapshot — selection may change
        var count = toUninstall.Count;

        // Build confirmation message with programme names
        string message;
        if (count == 1)
        {
            message = $"Uninstall '{toUninstall[0].DisplayName}'?";
        }
        else
        {
            var names = string.Join("\n",
                toUninstall.Take(10).Select(p => $"  \u2022 {p.DisplayName}"));
            var overflow = count > 10 ? $"\n  ...and {count - 10} more" : "";
            message = $"Uninstall {count} programmes?\n\n{names}{overflow}";
        }

        if (!DialogHelper.Confirm(message))
            return;

        IsUninstalling = true;
        HasLeftovers = false;
        Leftovers.Clear();

        var uninstalled = new List<InstalledProgram>();

        try
        {
            for (int i = 0; i < toUninstall.Count; i++)
            {
                var program = toUninstall[i];
                StatusText = $"Uninstalling ({i + 1}/{count}): {program.DisplayName}...";

                var success = await _uninstallService.UninstallAsync(program);
                if (success)
                {
                    uninstalled.Add(program);
                    Programs.Remove(program);
                    FilteredPrograms.Remove(program);
                }
                else
                {
                    StatusText = $"Uninstall may have failed for {program.DisplayName} \u2014 continuing...";
                    await Task.Delay(500); // Brief pause so user sees the message
                }
            }

            if (uninstalled.Count > 0)
            {
                // Scan for leftovers across all uninstalled programmes
                StatusText = $"Scanning for leftovers from {uninstalled.Count} programme(s)...";

                foreach (var program in uninstalled)
                {
                    var leftovers = await _uninstallService.ScanLeftoversAsync(program);
                    foreach (var leftover in leftovers)
                        Leftovers.Add(leftover);
                }

                HasLeftovers = Leftovers.Count > 0;
                StatusText = Leftovers.Count > 0
                    ? $"{uninstalled.Count} programme(s) uninstalled. Found {Leftovers.Count} leftover(s) \u2014 review and remove below."
                    : $"{uninstalled.Count} programme(s) uninstalled cleanly \u2014 no leftovers found.";
            }
            else
            {
                StatusText = "No programmes were successfully uninstalled.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error during uninstall: {ex.Message}";
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    [RelayCommand]
    private async Task RemoveLeftoversAsync()
    {
        var selected = Leftovers.Where(l => l.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "No leftover items selected for removal.";
            return;
        }

        if (!DialogHelper.Confirm(
            $"Remove {selected.Count} leftover item(s)?\n\nFiles and folders will be sent to the Recycle Bin."))
            return;

        StatusText = "Removing leftovers...";

        try
        {
            var removed = await _uninstallService.RemoveLeftoversAsync(selected);

            // Remove successfully processed items from the display list
            foreach (var item in selected)
                Leftovers.Remove(item);

            HasLeftovers = Leftovers.Count > 0;
            StatusText = HasLeftovers
                ? $"{removed} leftover(s) removed. {Leftovers.Count} remaining."
                : $"{removed} leftover(s) removed. All clean.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error removing leftovers: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DismissLeftovers()
    {
        Leftovers.Clear();
        HasLeftovers = false;
        StatusText = "Leftovers dismissed.";
    }

    private void ApplyFilter()
    {
        FilteredPrograms.Clear();
        var filter = SearchFilter?.Trim() ?? "";

        foreach (var program in Programs)
        {
            if (string.IsNullOrEmpty(filter) ||
                program.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (program.Publisher?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                FilteredPrograms.Add(program);
            }
        }
    }
}
