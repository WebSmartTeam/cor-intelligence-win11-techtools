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
    [ObservableProperty] private InstalledProgram? _selectedProgram;

    public ObservableCollection<InstalledProgram> Programs { get; } = new();
    public ObservableCollection<InstalledProgram> FilteredPrograms { get; } = new();

    public UninstallerViewModel(IUninstallService uninstallService)
    {
        _uninstallService = uninstallService;
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadProgramsAsync()
    {
        IsLoading = true;
        StatusText = "Enumerating installed programmes...";

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
        if (SelectedProgram is null) return;

        var program = SelectedProgram;
        if (!DialogHelper.Confirm($"Uninstall '{program.DisplayName}'?"))
            return;

        StatusText = $"Uninstalling {program.DisplayName}...";

        try
        {
            var success = await _uninstallService.UninstallAsync(program);
            if (success)
            {
                Programs.Remove(program);
                ApplyFilter();
                StatusText = $"{program.DisplayName} uninstalled successfully";
            }
            else
            {
                StatusText = $"Uninstall may have been cancelled or failed for {program.DisplayName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Uninstall error: {ex.Message}";
        }
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
