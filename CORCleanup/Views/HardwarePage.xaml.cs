using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class HardwarePage : INavigableView<HardwareViewModel>
{
    public HardwareViewModel ViewModel { get; }

    public HardwarePage(HardwareViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Auto-load COR Spec on first visit (Tab 0)
        if (ViewModel.CorSpecReport is null)
        {
            await ViewModel.LoadCorSpecCommand.ExecuteAsync(null);
        }
    }
}
