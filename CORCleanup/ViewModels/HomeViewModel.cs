using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

namespace CORCleanup.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public HomeViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public string VersionLine =>
        $"v{Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"} \u2022 COR Intelligence";

    [RelayCommand]
    private void Navigate(string page)
    {
        var pageType = page switch
        {
            "Network" => typeof(Views.NetworkPage),
            "Cleanup" => typeof(Views.CleanupPage),
            "Registry" => typeof(Views.RegistryPage),
            "Uninstaller" => typeof(Views.UninstallerPage),
            "Hardware" => typeof(Views.HardwarePage),
            "Tools" => typeof(Views.ToolsPage),
            "Admin" => typeof(Views.AdminPage),
            "Settings" => typeof(Views.SettingsPage),
            _ => null
        };

        if (pageType is not null)
            _navigationService.Navigate(pageType);
    }
}
