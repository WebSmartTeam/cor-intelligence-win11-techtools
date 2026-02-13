using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class RegistryPage : INavigableView<RegistryViewModel>
{
    public RegistryViewModel ViewModel { get; }

    public RegistryPage(RegistryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
