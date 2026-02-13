using CommunityToolkit.Mvvm.ComponentModel;

namespace CORCleanup.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = "COR Cleanup";
}
