using System.Linq;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;
using CORCleanup.Core.Models;
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

    private void ProgramGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Don't update selection tracking during batch uninstall
        // (removing items from the list triggers SelectionChanged)
        if (ViewModel.IsUninstalling) return;

        if (sender is DataGrid grid)
        {
            ViewModel.UpdateSelection(
                grid.SelectedItems.Cast<InstalledProgram>().ToList());
        }
    }
}
