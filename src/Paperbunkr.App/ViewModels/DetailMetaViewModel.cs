using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Credits row (Writer/Artist/Colorist/Letterer), ported from DetailMeta.dc.html (Claude Design
/// project 43c40b25). The source file took no props at all (fully static sample text); this now
/// aggregates each credit across a series' issues (docs/onboarding.md §6, §9 - credits are a
/// per-issue ComicInfo.xml-standard field, not promoted to Series).
/// </summary>
public partial class DetailMetaViewModel : ViewModelBase
{
    public string Writer { get; private set; } = "Unknown";
    public string Artist { get; private set; } = "Unknown";
    public string Colorist { get; private set; } = "Unknown";
    public string Letterer { get; private set; } = "Unknown";

    public void LoadSeries(Series series)
    {
        var issues = series.Issues;
        Writer = CsvFieldAggregator.Join(issues.Select(i => i.Writer));
        Artist = CsvFieldAggregator.Join(issues.Select(i => i.Penciller));
        Colorist = CsvFieldAggregator.Join(issues.Select(i => i.Colorist));
        Letterer = CsvFieldAggregator.Join(issues.Select(i => i.Letterer));

        OnPropertyChanged(nameof(Writer));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(Colorist));
        OnPropertyChanged(nameof(Letterer));
    }
}
