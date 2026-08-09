using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.VirtualTags;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Teams/Locations/Genres/Virtual Tags rows, ported from DetailPills.dc.html (Claude Design
/// project 43c40b25). <see cref="LoadSeries"/> aggregates Teams/Locations/Genres across a series'
/// issues (unchanged shape); <see cref="LoadIssue"/> (docs/superpowers/specs/
/// 2026-08-07-detail-screen-issue-focus-design.md §3) shows one issue's own values.
/// <c>Genres</c> now reads real per-issue <see cref="Issue.Genre"/> via
/// <see cref="BulkFieldRegistry.Find"/> instead of the separate <see cref="Series.Genre"/> field -
/// fixes the bug found during bulk-edit manual verification where editing an issue's Genre never
/// showed up here because this row was reading a completely different field.
///
/// <c>VirtualTags</c> (Alpha to-do P2 - previously computed but invisible everywhere) takes the
/// enabled tag definitions as a parameter rather than querying the database itself, unlike most
/// Preferences-owned services - <see cref="DetailScreenViewModel"/> already has a context open at
/// every call site, and keeping this ViewModel DB-free preserves its existing pure-computation
/// shape (and the existing tests' ability to construct it with no database at all).
/// </summary>
public partial class DetailPillsViewModel : ViewModelBase
{
    private static readonly BulkFieldDescriptor TeamsField = BulkFieldRegistry.Find("Teams");
    private static readonly BulkFieldDescriptor LocationsField = BulkFieldRegistry.Find("Locations");
    private static readonly BulkFieldDescriptor GenreField = BulkFieldRegistry.Find("Genre");

    public ObservableCollection<string> Teams { get; } = new();
    public ObservableCollection<string> Locations { get; } = new();
    public ObservableCollection<string> Genres { get; } = new();
    public ObservableCollection<string> VirtualTags { get; } = new();

    public bool HasVirtualTags => VirtualTags.Count > 0;

    public void LoadSeries(Series series, IReadOnlyList<VirtualTagDefinition>? virtualTags = null)
    {
        Fill(Teams, series.Issues.Select(TeamsField.Get));
        Fill(Locations, series.Issues.Select(LocationsField.Get));
        Fill(Genres, series.Issues.Select(GenreField.Get));
        FillVirtualTags(series.Issues.Select(issue => (issue, (Series?)series)), virtualTags);
    }

    public void LoadIssue(Issue issue, IReadOnlyList<VirtualTagDefinition>? virtualTags = null)
    {
        Fill(Teams, new[] { TeamsField.Get(issue) });
        Fill(Locations, new[] { LocationsField.Get(issue) });
        Fill(Genres, new[] { GenreField.Get(issue) });
        FillVirtualTags(new[] { (issue, issue.Series) }, virtualTags);
    }

    private static void Fill(ObservableCollection<string> target, IEnumerable<string?> rawValues)
    {
        target.Clear();
        foreach (string value in CsvFieldAggregator.Distinct(rawValues))
        {
            target.Add(value);
        }
    }

    /// <summary>
    /// Whole-caption dedup, not <see cref="CsvFieldAggregator.Distinct"/> - that splits every value
    /// on commas, correct for CSV-convention ComicInfo fields (Teams/Locations/Genre) but wrong
    /// here, since an evaluated Virtual Tag caption is an arbitrary free-text template result that
    /// may legitimately contain a comma (e.g. "{Series}, Vol. {Volume}").
    /// </summary>
    private void FillVirtualTags(IEnumerable<(Issue Issue, Series? Series)> targets, IReadOnlyList<VirtualTagDefinition>? virtualTags)
    {
        VirtualTags.Clear();
        if (virtualTags is { Count: > 0 })
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (issue, series) in targets)
            {
                foreach (var tag in virtualTags)
                {
                    string value = VirtualTagTemplateEvaluator.Evaluate(tag.CaptionFormat, issue, series);
                    if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                    {
                        VirtualTags.Add(value);
                    }
                }
            }
        }

        OnPropertyChanged(nameof(HasVirtualTags));
    }
}
