using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class AutoToolPage : INavigableView<AutoToolViewModel>
{
    public AutoToolViewModel ViewModel { get; }

    public AutoToolPage(AutoToolViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
