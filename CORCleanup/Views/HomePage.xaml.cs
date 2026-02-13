using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class HomePage : INavigableView<HomeViewModel>
{
    public HomeViewModel ViewModel { get; }

    public HomePage(HomeViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
