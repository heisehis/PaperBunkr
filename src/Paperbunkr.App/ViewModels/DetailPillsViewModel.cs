using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.VirtualTags;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Teams/Locations/Genres/Tags/Virtual Tags rows, ported from DetailPills.dc.html (Claude Design
/// project 43c40b25). <see cref="LoadSeries"/> aggregates Teams/Locations/Genres/Tags across a
/// series' issues (unchanged shape); <see cref="LoadIssue"/> (docs/superpowers/specs/
/// 2026-08-07-detail-screen-issue-focus-design.md §3) shows one issue's own values.
/// <c>Genres</c> now reads real per-issue <see cref="Issue.Genre"/> via
/// <see cref="BulkFieldRegistry.Find"/> instead of the separate <see cref="Series.Genre"/> field -
/// fixes the bug found during bulk-edit manual verification where editing an issue's Genre never
/// showed up here because this row was reading a completely different field.
///
/// <c>Genres</c>/<c>Tags</c> became <see cref="TagPillViewModel"/> collections (docs/superpowers/
/// specs/2026-08-23-weighted-categorized-tags-design.md) - they now read <see cref="Issue.Tags"/>
/// directly rather than through <see cref="BulkFieldRegistry"/>'s joined-string Get, since Category/
/// Weight need to survive per pill. <c>goLibraryWithSearch</c>/<c>reweightTag</c> are the only DB-
/// touching seams this ViewModel takes - it stays otherwise pure-computation (no DB access of its
/// own), same shape the class doc comment already committed to; <c>reweightTag</c> is only
/// supplied with a real issue id from <see cref="LoadIssue"/> (a right-click reweight on a series-
/// aggregated pill has no single Issue row to write to, so <see cref="LoadSeries"/> pills leave it
/// null and <see cref="TagPillViewModel.CanReweight"/> is false there).
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

    private readonly Action<string> _goLibraryWithSearch;
    private readonly Action<int, IssueTagField, string, IssueTagWeight>? _reweightTag;

    /// <summary>Test-friendly default - most tests build this VM with no navigation/reweight wiring at all.</summary>
    public DetailPillsViewModel(Action<string>? goLibraryWithSearch = null, Action<int, IssueTagField, string, IssueTagWeight>? reweightTag = null)
    {
        _goLibraryWithSearch = goLibraryWithSearch ?? (_ => { });
        _reweightTag = reweightTag;
    }

    public ObservableCollection<TagPillViewModel> Teams { get; } = new();
    public ObservableCollection<TagPillViewModel> Locations { get; } = new();
    public ObservableCollection<TagPillViewModel> Genres { get; } = new();
    public ObservableCollection<TagPillViewModel> Tags { get; } = new();
    public ObservableCollection<string> VirtualTags { get; } = new();

    public bool HasVirtualTags => VirtualTags.Count > 0;

    public void LoadSeries(Series series, IReadOnlyList<VirtualTagDefinition>? virtualTags = null)
    {
        Fill(Teams, series.Issues.Select(TeamsField.Get));
        Fill(Locations, series.Issues.Select(LocationsField.Get));
        FillTagPills(Genres, IssueTagField.Genre, series.Issues.SelectMany(i => i.Tags), issueId: null);
        FillTagPills(Tags, IssueTagField.Tags, series.Issues.SelectMany(i => i.Tags), issueId: null);
        FillVirtualTags(series.Issues.Select(issue => (issue, (Series?)series)), virtualTags);
    }

    public void LoadIssue(Issue issue, IReadOnlyList<VirtualTagDefinition>? virtualTags = null)
    {
        Fill(Teams, new[] { TeamsField.Get(issue) });
        Fill(Locations, new[] { LocationsField.Get(issue) });
        FillTagPills(Genres, IssueTagField.Genre, issue.Tags, issueId: issue.Id);
        FillTagPills(Tags, IssueTagField.Tags, issue.Tags, issueId: issue.Id);
        FillVirtualTags(new[] { (issue, issue.Series) }, virtualTags);
    }

    private void Fill(ObservableCollection<TagPillViewModel> target, IEnumerable<string?> rawValues)
    {
        target.Clear();
        foreach (string value in CsvFieldAggregator.Distinct(rawValues))
        {
            target.Add(new TagPillViewModel(value, category: null, IssueTagWeight.Unset, _goLibraryWithSearch, reweight: null));
        }
    }

    /// <summary>
    /// Dedupes by <see cref="IssueTag.Value"/> (case-insensitive) - when the same value appears on
    /// multiple issues (series-aggregated view) with different weights, the highest weight wins
    /// (<see cref="IssueTagWeight"/>'s declaration order is ascending significance, so a plain max
    /// works). <paramref name="issueId"/> null means series-aggregated - no single Issue row a
    /// reweight could write to, so every pill's <see cref="TagPillViewModel.CanReweight"/> is false.
    /// </summary>
    private void FillTagPills(ObservableCollection<TagPillViewModel> target, IssueTagField field, IEnumerable<IssueTag> allTags, int? issueId)
    {
        target.Clear();
        var byValue = allTags
            .Where(t => t.Field == field)
            .GroupBy(t => t.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Value: g.First().Value, Category: g.First().Category, Weight: g.Max(t => t.Weight)))
            .OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var (value, category, weight) in byValue)
        {
            Action<IssueTagWeight>? reweight = issueId is int id && _reweightTag is not null
                ? w => _reweightTag(id, field, value, w)
                : null;
            target.Add(new TagPillViewModel(value, category, weight, _goLibraryWithSearch, reweight));
        }
    }

    /// <summary>
    /// Whole-caption dedup, not <see cref="CsvFieldAggregator.Distinct"/> - that splits every value
    /// on commas, correct for CSV-convention ComicInfo fields (Teams/Locations) but wrong here,
    /// since an evaluated Virtual Tag caption is an arbitrary free-text template result that may
    /// legitimately contain a comma (e.g. "{Series}, Vol. {Volume}").
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
