using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class HelpPage : INavigableView<HelpViewModel>
{
    public HelpViewModel ViewModel { get; }

    public HelpPage(HelpViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
