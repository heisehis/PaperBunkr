using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One shared screen for all five first-class metadata entities (Character/Team/Location/Creator/
/// Publisher, docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md) rather than five
/// near-identical ViewModel classes - <see cref="Kind"/> picks which table <see cref="Load"/> reads
/// from, but the shape (name + "appears in" list) is the same for all five. Publisher is the
/// exception: it has no appearance join table (single-valued per issue/series, not a list - see
/// <see cref="Entities.Publisher"/>'s own doc comment), so it shows a series list instead.
///
/// Deliberately minimal: no bio/image/description fields exist on any of these entities today - this
/// pass never built the lazy per-entity hydration (fetching Metron's <c>/creator/{id}/</c>,
/// <c>/publisher/{id}/</c>, etc.) the design doc's Phase 1 flagged as a future consideration. What
/// ships here is real and useful (browsable, not just decorative text, per the design's Round 2 Q4
/// reasoning) - name plus every issue/series it's actually linked to - without fabricating UI for
/// data that isn't fetched.
/// </summary>
public partial class MetadataEntityDetailScreenViewModel : ViewModelBase
{
    private readonly Action _navigateBack;
    private readonly Action<int> _goDetailForSeries;
    private readonly Action<int> _goReaderForIssue;

    public MetadataEntityDetailScreenViewModel(Action navigateBack, Action<int> goDetailForSeries, Action<int> goReaderForIssue)
    {
        _navigateBack = navigateBack;
        _goDetailForSeries = goDetailForSeries;
        _goReaderForIssue = goReaderForIssue;
    }

    [ObservableProperty]
    private ComicMetadataEntityKind _kind;

    [ObservableProperty]
    private int _entityId;

    [ObservableProperty]
    private string _name = string.Empty;

    public string KindLabel => Kind switch
    {
        ComicMetadataEntityKind.Character => "Character",
        ComicMetadataEntityKind.Team => "Team",
        ComicMetadataEntityKind.Location => "Location",
        ComicMetadataEntityKind.Creator => "Creator",
        ComicMetadataEntityKind.Publisher => "Publisher",
        _ => string.Empty,
    };

    public bool IsPublisher => Kind == ComicMetadataEntityKind.Publisher;

    public ObservableCollection<MetadataEntityAppearanceRow> Appearances { get; } = new();

    public ObservableCollection<MetadataEntitySeriesRow> PublishedSeries { get; } = new();

    public bool HasAppearances => Appearances.Count > 0;

    public bool HasPublishedSeries => PublishedSeries.Count > 0;

    public void Load(ComicMetadataEntityKind kind, int entityId)
    {
        Kind = kind;
        EntityId = entityId;
        Appearances.Clear();
        PublishedSeries.Clear();

        using var context = PaperbunkrDb.CreateContext();
        switch (kind)
        {
            case ComicMetadataEntityKind.Character:
                var character = context.Characters.FirstOrDefault(c => c.Id == entityId);
                Name = character?.Name ?? string.Empty;
                foreach (var row in context.CharacterAppearances
                             .Where(a => a.CharacterId == entityId)
                             .Select(a => new { a.Issue!.SeriesId, SeriesName = a.Issue.Series!.Name, IssueId = a.IssueId, a.Issue.Number })
                             .ToList()
                             .OrderBy(r => r.SeriesName).ThenBy(r => r.Number))
                {
                    Appearances.Add(new MetadataEntityAppearanceRow(row.SeriesId, row.SeriesName, row.IssueId, IssueLabel(row.SeriesName, row.Number), null));
                }

                break;

            case ComicMetadataEntityKind.Team:
                var team = context.Teams.FirstOrDefault(t => t.Id == entityId);
                Name = team?.Name ?? string.Empty;
                foreach (var row in context.TeamAppearances
                             .Where(a => a.TeamId == entityId)
                             .Select(a => new { a.Issue!.SeriesId, SeriesName = a.Issue.Series!.Name, IssueId = a.IssueId, a.Issue.Number })
                             .ToList()
                             .OrderBy(r => r.SeriesName).ThenBy(r => r.Number))
                {
                    Appearances.Add(new MetadataEntityAppearanceRow(row.SeriesId, row.SeriesName, row.IssueId, IssueLabel(row.SeriesName, row.Number), null));
                }

                break;

            case ComicMetadataEntityKind.Location:
                var location = context.Locations.FirstOrDefault(l => l.Id == entityId);
                Name = location?.Name ?? string.Empty;
                foreach (var row in context.LocationAppearances
                             .Where(a => a.LocationId == entityId)
                             .Select(a => new { a.Issue!.SeriesId, SeriesName = a.Issue.Series!.Name, IssueId = a.IssueId, a.Issue.Number })
                             .ToList()
                             .OrderBy(r => r.SeriesName).ThenBy(r => r.Number))
                {
                    Appearances.Add(new MetadataEntityAppearanceRow(row.SeriesId, row.SeriesName, row.IssueId, IssueLabel(row.SeriesName, row.Number), null));
                }

                break;

            case ComicMetadataEntityKind.Creator:
                var creator = context.Creators.FirstOrDefault(c => c.Id == entityId);
                Name = creator?.Name ?? string.Empty;
                foreach (var row in context.CreatorCredits
                             .Where(c => c.CreatorId == entityId)
                             .Select(c => new { c.Issue!.SeriesId, SeriesName = c.Issue.Series!.Name, IssueId = c.IssueId, c.Issue.Number, c.Role })
                             .ToList()
                             .OrderBy(r => r.SeriesName).ThenBy(r => r.Number))
                {
                    Appearances.Add(new MetadataEntityAppearanceRow(row.SeriesId, row.SeriesName, row.IssueId, IssueLabel(row.SeriesName, row.Number), row.Role));
                }

                break;

            case ComicMetadataEntityKind.Publisher:
                var publisher = context.Publishers.FirstOrDefault(p => p.Id == entityId);
                Name = publisher?.Name ?? string.Empty;
                foreach (var row in context.Series
                             .Where(s => s.PublisherEntityId == entityId)
                             .Select(s => new { s.Id, s.Name })
                             .ToList()
                             .OrderBy(r => r.Name))
                {
                    PublishedSeries.Add(new MetadataEntitySeriesRow(row.Id, row.Name));
                }

                break;
        }

        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(IsPublisher));
        OnPropertyChanged(nameof(HasAppearances));
        OnPropertyChanged(nameof(HasPublishedSeries));
    }

    private static string IssueLabel(string seriesName, string? number) =>
        string.IsNullOrWhiteSpace(number) ? seriesName : $"{seriesName} #{number}";

    [RelayCommand]
    private void NavigateBack() => _navigateBack();

    [RelayCommand]
    private void OpenSeries(int seriesId) => _goDetailForSeries(seriesId);

    [RelayCommand]
    private void OpenIssue(int issueId) => _goReaderForIssue(issueId);
}
