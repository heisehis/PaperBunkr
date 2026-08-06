using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Drives the app shell's navigation (rail nav, contextual sidebar, active screen).
/// Layout/tokens follow the "Paperbunkr App" wireframe (Claude Design project 43c40b25),
/// default variant: rail nav, pills toolbar, stacked detail layout, separate lists.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        Library = new LibraryScreenViewModel(GoDetailForSeries);
        Detail = new DetailScreenViewModel(GoLibrary, GoReaderForIssue);
        Reader = new ReaderScreenViewModel(GoDetail);
        Smart = new SmartScreenViewModel();
        Reading = new ReadingScreenViewModel(new FilePickerService());
        Plugin = new PluginScreenViewModel();
    }

    public LibraryScreenViewModel Library { get; }
    public DetailScreenViewModel Detail { get; }
    public ReaderScreenViewModel Reader { get; }
    public SmartScreenViewModel Smart { get; }
    public ReadingScreenViewModel Reading { get; }
    public PluginScreenViewModel Plugin { get; }

    [ObservableProperty]
    private string _currentScreen = "library";

    public bool IsLibrary => CurrentScreen == "library";
    public bool IsDetail => CurrentScreen == "detail";
    public bool IsSmart => CurrentScreen == "smart";
    public bool IsReading => CurrentScreen == "reading";
    public bool IsPlugin => CurrentScreen == "plugin";
    public bool IsReader => CurrentScreen == "reader";

    public bool ShowContextualSidebar => IsLibrary || IsSmart || IsReading;

    partial void OnCurrentScreenChanged(string value)
    {
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsSmart));
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(IsPlugin));
        OnPropertyChanged(nameof(IsReader));
        OnPropertyChanged(nameof(ShowContextualSidebar));
    }

    [RelayCommand]
    private void GoLibrary() => CurrentScreen = "library";

    [RelayCommand]
    private void GoSmart()
    {
        Smart.EnsureListLoaded();
        CurrentScreen = "smart";
    }

    [RelayCommand]
    private void GoReading()
    {
        Reading.EnsureListLoaded();
        CurrentScreen = "reading";
    }

    [RelayCommand]
    private void GoPlugin() => CurrentScreen = "plugin";

    [RelayCommand]
    private void GoReader()
    {
        Reader.EnsureIssueLoaded();
        CurrentScreen = "reader";
    }

    private void GoDetail() => CurrentScreen = "detail";

    private void GoDetailForSeries(int seriesId)
    {
        Detail.LoadSeries(seriesId);
        CurrentScreen = "detail";
    }

    private void GoReaderForIssue(int issueId)
    {
        Reader.LoadIssue(issueId);
        CurrentScreen = "reader";
    }
}
