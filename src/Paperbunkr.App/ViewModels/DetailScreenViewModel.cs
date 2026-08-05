using System;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Series Detail screen, "stacked" layout variant (the default selected in the parent
/// "Paperbunkr App" wireframe), ported from DetailScreen.dc.html. Header content is sample
/// data for the "Brass Horizon" series used throughout the wireframe.
/// </summary>
public partial class DetailScreenViewModel : ViewModelBase
{
    public DetailScreenViewModel(Action goBack)
    {
        _goBack = goBack;
        CoverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f");
        Tabs = new DetailTabsViewModel();
    }

    private readonly Action _goBack;

    public DetailTabsViewModel Tabs { get; }

    public IBrush CoverBrush { get; }
    public string SeriesTitle => "Brass Horizon";
    public string CoverTitle => "BRASS\nHORIZON";
    public string ContentTypeLabel => "Comic";
    public string StatusLabel => "Ongoing";
    public string IssueCountLabel => "42 Issues";
    public string Summary => "Salvage crews trade rumors and rivets on the last free ring above a drowned Earth.";
    public string ContinueLabel => "Continue — Issue #12";

    [RelayCommand]
    private void GoBack() => _goBack();
}
