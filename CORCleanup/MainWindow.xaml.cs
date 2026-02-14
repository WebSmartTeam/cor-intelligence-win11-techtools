using System;
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

        // Force dark theme
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        // COR Intelligence brand accent — professional teal-blue
        ApplicationAccentColorManager.Apply(
            Color.FromRgb(0x06, 0xB6, 0xD4), ApplicationTheme.Dark);

        InitializeComponent();

        NavigationView.SetServiceProvider(serviceProvider);
        navigationService.SetNavigationControl(NavigationView);

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate(typeof(Views.HomePage));
    }

    /// <summary>
    /// Compensates for WPF custom-chrome windows extending ~7px beyond screen edges
    /// when maximized with ExtendsContentIntoTitleBar. Adds border padding so the
    /// title bar, close button, and content stay within the visible screen area.
    /// </summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        BorderThickness = WindowState == WindowState.Maximized
            ? new Thickness(8)
            : new Thickness(0);
    }
}
