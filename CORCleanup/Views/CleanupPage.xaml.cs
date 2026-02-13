using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class CleanupPage : INavigableView<CleanupViewModel>
{
    public CleanupViewModel ViewModel { get; }

    public CleanupPage(CleanupViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
