using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services.ContinuityScreen;

namespace Paperbunkr.App.ViewModels;

public partial class OutsideSeriesRowViewModel(OutsideSeriesInfo info) : ObservableObject
{
    public int SeriesId => info.SeriesId;
    public string Name => info.Name;
    public int IssueCount => info.IssueCount;

    [ObservableProperty]
    private bool _isSelected;
}
