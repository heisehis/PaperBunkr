using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
///
/// Each credit became a <see cref="TagPillViewModel"/> collection (docs/superpowers/specs/2026-08-23-
/// weighted-categorized-tags-design.md's click-to-search, extended past Genre/Tags per direct user
/// follow-up) rather than a plain joined string - reuses the same pill VM Genre/Tags already use,
/// just with no Category and <c>Weight</c> left at its default <c>Unset</c> (renders identically to
/// a plain chip, since credits have no weight concept) and no reweight callback (right-click is
/// Genre/Tags-only).
/// </summary>
public partial class DetailMetaViewModel : ViewModelBase
{
    private static readonly BulkFieldDescriptor WriterField = BulkFieldRegistry.Find("Writer");
    private static readonly BulkFieldDescriptor ArtistField = BulkFieldRegistry.Find("Penciller");
    private static readonly BulkFieldDescriptor CoverArtistField = BulkFieldRegistry.Find("Cover Artist");
    private static readonly BulkFieldDescriptor ColoristField = BulkFieldRegistry.Find("Colorist");
    private static readonly BulkFieldDescriptor LettererField = BulkFieldRegistry.Find("Letterer");

    private readonly Action<string> _goLibraryWithSearch;

    /// <summary>Test-friendly default - most tests build this VM with no navigation wiring at all.</summary>
    public DetailMetaViewModel(Action<string>? goLibraryWithSearch = null)
    {
        _goLibraryWithSearch = goLibraryWithSearch ?? (_ => { });
    }

    public ObservableCollection<TagPillViewModel> Writer { get; } = new();
    public ObservableCollection<TagPillViewModel> Artist { get; } = new();
    public ObservableCollection<TagPillViewModel> CoverArtist { get; } = new();
    public ObservableCollection<TagPillViewModel> Colorist { get; } = new();
    public ObservableCollection<TagPillViewModel> Letterer { get; } = new();

    public void LoadSeries(Series series)
    {
        var issues = series.Issues;
        Fill(Writer, issues.Select(WriterField.Get));
        Fill(Artist, issues.Select(ArtistField.Get));
        Fill(CoverArtist, issues.Select(CoverArtistField.Get));
        Fill(Colorist, issues.Select(ColoristField.Get));
        Fill(Letterer, issues.Select(LettererField.Get));
    }

    public void LoadIssue(Issue issue)
    {
        Fill(Writer, new[] { WriterField.Get(issue) });
        Fill(Artist, new[] { ArtistField.Get(issue) });
        Fill(CoverArtist, new[] { CoverArtistField.Get(issue) });
        Fill(Colorist, new[] { ColoristField.Get(issue) });
        Fill(Letterer, new[] { LettererField.Get(issue) });
    }

    private void Fill(ObservableCollection<TagPillViewModel> target, IEnumerable<string?> rawValues)
    {
        target.Clear();
        foreach (string value in CsvFieldAggregator.Distinct(rawValues))
        {
            target.Add(new TagPillViewModel(value, category: null, IssueTagWeight.Unset, _goLibraryWithSearch, reweight: null));
        }
    }
}
