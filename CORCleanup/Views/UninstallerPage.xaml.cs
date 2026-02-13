using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class UninstallerPage : INavigableView<UninstallerViewModel>
{
    public UninstallerViewModel ViewModel { get; }

    public UninstallerPage(UninstallerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
