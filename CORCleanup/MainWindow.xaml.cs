using System.Windows;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup;

public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigationService;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        IServiceProvider serviceProvider,
        INavigationService navigationService)
    {
        ViewModel = viewModel;
        DataContext = this;
        _navigationService = navigationService;

        // Light theme — clean professional look like CCleaner
        ApplicationThemeManager.Apply(ApplicationTheme.Light);

        // COR Intelligence brand accent
        ApplicationAccentColorManager.Apply(
            Color.FromRgb(0x06, 0xB6, 0xD4), ApplicationTheme.Light);

        InitializeComponent();

        NavigationView.SetServiceProvider(serviceProvider);
        navigationService.SetNavigationControl(NavigationView);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate(typeof(Views.HomePage));
    }
}
