using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Credits row (Writer/Artist/Cover Artist/Colorist/Letterer), ported from DetailMeta.dc.html
/// (Claude Design project 43c40b25). Field access goes through <see cref="BulkFieldRegistry.Find"/>
/// (docs/superpowers/specs/2026-08-07-detail-screen-issue-focus-design.md §3) - the same registry
/// the bulk editor uses, doubling as this display's "gathering tool" - instead of hardcoded
/// per-field lambdas. <see cref="LoadSeries"/> aggregates across a series' issues (unchanged
/// behavior); <see cref="LoadIssue"/> (new) shows one issue's own values when exactly one issue is
/// selected on the Detail screen.
/// </summary>
public partial class DetailMetaViewModel : ViewModelBase
{
    private static readonly BulkFieldDescriptor WriterField = BulkFieldRegistry.Find("Writer");
    private static readonly BulkFieldDescriptor ArtistField = BulkFieldRegistry.Find("Penciller");
    private static readonly BulkFieldDescriptor CoverArtistField = BulkFieldRegistry.Find("Cover Artist");
    private static readonly BulkFieldDescriptor ColoristField = BulkFieldRegistry.Find("Colorist");
    private static readonly BulkFieldDescriptor LettererField = BulkFieldRegistry.Find("Letterer");

    public string Writer { get; private set; } = "Unknown";
    public string Artist { get; private set; } = "Unknown";
    public string CoverArtist { get; private set; } = "Unknown";
    public string Colorist { get; private set; } = "Unknown";
    public string Letterer { get; private set; } = "Unknown";

    public void LoadSeries(Series series)
    {
        var issues = series.Issues;
        Writer = CsvFieldAggregator.Join(issues.Select(WriterField.Get));
        Artist = CsvFieldAggregator.Join(issues.Select(ArtistField.Get));
        CoverArtist = CsvFieldAggregator.Join(issues.Select(CoverArtistField.Get));
        Colorist = CsvFieldAggregator.Join(issues.Select(ColoristField.Get));
        Letterer = CsvFieldAggregator.Join(issues.Select(LettererField.Get));

        RaiseAllChanged();
    }

    public void LoadIssue(Issue issue)
    {
        Writer = CsvFieldAggregator.Join(new[] { WriterField.Get(issue) });
        Artist = CsvFieldAggregator.Join(new[] { ArtistField.Get(issue) });
        CoverArtist = CsvFieldAggregator.Join(new[] { CoverArtistField.Get(issue) });
        Colorist = CsvFieldAggregator.Join(new[] { ColoristField.Get(issue) });
        Letterer = CsvFieldAggregator.Join(new[] { LettererField.Get(issue) });

        RaiseAllChanged();
    }

    private void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(Writer));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(CoverArtist));
        OnPropertyChanged(nameof(Colorist));
        OnPropertyChanged(nameof(Letterer));
    }
}
