using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class ToolsPage : INavigableView<ToolsViewModel>
{
    public ToolsViewModel ViewModel { get; }

    public ToolsPage(ToolsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
