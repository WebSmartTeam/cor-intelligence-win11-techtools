using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class AdminPage : INavigableView<AdminViewModel>
{
    public AdminViewModel ViewModel { get; }

    public AdminPage(AdminViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
