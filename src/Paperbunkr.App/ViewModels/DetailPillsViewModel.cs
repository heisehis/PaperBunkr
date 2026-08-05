using System.Collections.ObjectModel;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Teams/Locations/Genres tag rows, ported from DetailPills.dc.html (Claude Design project
/// 43c40b25). The source file took no props (fully static sample tags); this now aggregates
/// Teams/Locations across a series' issues and reads Genre from the Series itself (promoted
/// to series-level per docs/onboarding.md §6).
/// </summary>
public partial class DetailPillsViewModel : ViewModelBase
{
    public ObservableCollection<string> Teams { get; } = new();
    public ObservableCollection<string> Locations { get; } = new();
    public ObservableCollection<string> Genres { get; } = new();

    public void LoadSeries(Series series)
    {
        Teams.Clear();
        foreach (string team in CsvFieldAggregator.Distinct(series.Issues.Select(i => i.Teams)))
        {
            Teams.Add(team);
        }

        Locations.Clear();
        foreach (string location in CsvFieldAggregator.Distinct(series.Issues.Select(i => i.Locations)))
        {
            Locations.Add(location);
        }

        Genres.Clear();
        foreach (string genre in CsvFieldAggregator.Distinct(new[] { series.Genre }))
        {
            Genres.Add(genre);
        }
    }
}
