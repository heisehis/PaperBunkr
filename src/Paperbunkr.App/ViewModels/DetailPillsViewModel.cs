using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Teams/Locations/Genres tag rows, ported from DetailPills.dc.html (Claude Design project
/// 43c40b25). <see cref="LoadSeries"/> aggregates Teams/Locations/Genres across a series' issues
/// (unchanged shape); <see cref="LoadIssue"/> (new, docs/superpowers/specs/
/// 2026-08-07-detail-screen-issue-focus-design.md §3) shows one issue's own values.
/// <c>Genres</c> now reads real per-issue <see cref="Issue.Genre"/> via
/// <see cref="BulkFieldRegistry.Find"/> instead of the separate <see cref="Series.Genre"/> field -
/// fixes the bug found during bulk-edit manual verification where editing an issue's Genre never
/// showed up here because this row was reading a completely different field.
/// </summary>
public partial class DetailPillsViewModel : ViewModelBase
{
    private static readonly BulkFieldDescriptor TeamsField = BulkFieldRegistry.Find("Teams");
    private static readonly BulkFieldDescriptor LocationsField = BulkFieldRegistry.Find("Locations");
    private static readonly BulkFieldDescriptor GenreField = BulkFieldRegistry.Find("Genre");

    public ObservableCollection<string> Teams { get; } = new();
    public ObservableCollection<string> Locations { get; } = new();
    public ObservableCollection<string> Genres { get; } = new();

    public void LoadSeries(Series series)
    {
        Fill(Teams, series.Issues.Select(TeamsField.Get));
        Fill(Locations, series.Issues.Select(LocationsField.Get));
        Fill(Genres, series.Issues.Select(GenreField.Get));
    }

    public void LoadIssue(Issue issue)
    {
        Fill(Teams, new[] { TeamsField.Get(issue) });
        Fill(Locations, new[] { LocationsField.Get(issue) });
        Fill(Genres, new[] { GenreField.Get(issue) });
    }

    private static void Fill(ObservableCollection<string> target, IEnumerable<string?> rawValues)
    {
        target.Clear();
        foreach (string value in CsvFieldAggregator.Distinct(rawValues))
        {
            target.Add(value);
        }
    }
}
