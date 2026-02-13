using Wpf.Ui.Abstractions.Controls;
using CORCleanup.ViewModels;

namespace CORCleanup.Views;

public partial class NetworkPage : INavigableView<NetworkViewModel>
{
    public NetworkViewModel ViewModel { get; }

    public NetworkPage(NetworkViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
