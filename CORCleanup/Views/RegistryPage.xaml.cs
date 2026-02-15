using System.Windows;
using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class RegistryPage : INavigableView<RegistryViewModel>
{
    public RegistryViewModel ViewModel { get; }
    private bool _hasAutoScanned;

    public RegistryPage(RegistryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-scan on first navigation — no need for the user to click "Scan"
        if (!_hasAutoScanned && !ViewModel.IsScanning && !ViewModel.HasScanned)
        {
            _hasAutoScanned = true;
            ViewModel.ScanRegistryCommand.Execute(null);
        }
    }
}
