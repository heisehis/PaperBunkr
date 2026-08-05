using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Paperbunkr — app shell online, Engine wired in.";
}
