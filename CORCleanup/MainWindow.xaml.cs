using System;
using System.Windows;
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

        InitializeComponent();

        ApplicationThemeManager.Apply(this);
        SystemThemeWatcher.Watch(this);

        NavigationView.SetServiceProvider(serviceProvider);
        navigationService.SetNavigationControl(NavigationView);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Navigate to Network page on startup
        _navigationService.Navigate(typeof(Views.NetworkPage));
    }
}
