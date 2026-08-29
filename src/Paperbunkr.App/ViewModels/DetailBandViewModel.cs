using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.VirtualTags;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The always-visible band beneath a detail-screen hero (docs/superpowers/specs/
/// 2026-08-28-detail-screens-streaming-redesign-design.md). Replaces the old
/// <c>DetailMetaViewModel</c> + <c>DetailPillsViewModel</c> pair: inline meta row, a clamped
/// synopsis, and a set of tamed metadata groups (each capped at
/// <see cref="DetailBandGroupViewModel.Cap"/> with an inline "+N more"). Stays DB-free - the host
/// screen passes a materialised <see cref="Series"/>/<see cref="Issue"/> and the enabled virtual
/// tag definitions, same shape the two classes it replaces committed to.
///
/// Book mode leaves <see cref="Groups"/> empty ("lite" band - inline meta + synopsis only).
/// </summary>
public partial class DetailBandViewModel : ViewModelBase
{
    /// <summary>ComicVine's ComicInfo exporter dumps its internal ids into &lt;Tags&gt; ("CVDB1073108").
    /// Hidden from the Tags group by default with a reveal affordance - display-only, import/DB untouched.</summary>
    internal static readonly Regex JunkTagPattern = new(@"^CVDB\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly BulkFieldDescriptor WriterField = BulkFieldRegistry.Find("Writer");
    private static readonly BulkFieldDescriptor ArtistField = BulkFieldRegistry.Find("Penciller");
    private static readonly BulkFieldDescriptor TeamsField = BulkFieldRegistry.Find("Teams");
    private static readonly BulkFieldDescriptor LocationsField = BulkFieldRegistry.Find("Locations");

    private readonly Action<string> _goLibraryWithSearch;
    private readonly Action _goToDetailsTab;
    private readonly Action<int, IssueTagField, string, IssueTagWeight>? _reweightTag;

    /// <summary>Test-friendly default - most tests build this with no navigation/reweight wiring.</summary>
    public DetailBandViewModel(Action<string>? goLibraryWithSearch = null, Action? goToDetailsTab = null, Action<int, IssueTagField, string, IssueTagWeight>? reweightTag = null)
    {
        _goLibraryWithSearch = goLibraryWithSearch ?? (_ => { });
        _goToDetailsTab = goToDetailsTab ?? (() => { });
        _reweightTag = reweightTag;
    }

    // --- Inline meta row ---

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _publisherText = string.Empty;

    [ObservableProperty]
    private string _yearText = string.Empty;

    /// <summary>Focused issue's Format / AgeRating (docs/superpowers/specs/
    /// 2026-08-28-brand-metadata-iconography-design.md §4) - rendered as a BrandMark on the meta
    /// row when set, nothing otherwise. Populated by the host screen, like PublisherText.</summary>
    [ObservableProperty]
    private string _formatText = string.Empty;

    [ObservableProperty]
    private string _ageRatingText = string.Empty;

    /// <summary>Short labels for derived "special" marks (manga / B&amp;W), from
    /// <c>MarkResolver.ResolveSpecial</c>.</summary>
    public ObservableCollection<string> SpecialMarks { get; } = new();

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public bool HasPublisher => !string.IsNullOrWhiteSpace(PublisherText);
    public bool HasYear => !string.IsNullOrWhiteSpace(YearText);
    public bool HasFormat => !string.IsNullOrWhiteSpace(FormatText);
    public bool HasAgeRating => !string.IsNullOrWhiteSpace(AgeRatingText);

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnPublisherTextChanged(string value) => OnPropertyChanged(nameof(HasPublisher));
    partial void OnYearTextChanged(string value) => OnPropertyChanged(nameof(HasYear));
    partial void OnFormatTextChanged(string value) => OnPropertyChanged(nameof(HasFormat));
    partial void OnAgeRatingTextChanged(string value) => OnPropertyChanged(nameof(HasAgeRating));

    /// <summary>Sets the derived special-mark labels from a focused issue (empty for series mode /
    /// book mode). Host screens call this alongside setting <see cref="FormatText"/> etc.</summary>
    public void SetSpecialMarks(Data.Entities.Issue? issue)
    {
        SpecialMarks.Clear();
        if (issue is not null)
        {
            foreach (var spec in Services.MarkResolver.Instance.ResolveSpecial(issue))
            {
                if (!string.IsNullOrWhiteSpace(spec.Text))
                {
                    SpecialMarks.Add(spec.Text!);
                }
            }
        }
    }

    // --- Synopsis ---

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _isSynopsisExpanded;

    public string SynopsisToggleLabel => IsSynopsisExpanded ? "less" : "more";

    partial void OnIsSynopsisExpandedChanged(bool value) => OnPropertyChanged(nameof(SynopsisToggleLabel));

    [RelayCommand]
    private void ToggleSynopsis() => IsSynopsisExpanded = !IsSynopsisExpanded;

    // --- Groups ---

    public ObservableCollection<DetailBandGroupViewModel> Groups { get; } = new();

    public ObservableCollection<string> VirtualTags { get; } = new();

    public bool HasVirtualTags => VirtualTags.Count > 0;

    public bool HasGroups => Groups.Count > 0;

    /// <summary>Series-aggregated view: every metadata field aggregated across the series' issues.</summary>
    public void LoadSeries(Series series, IReadOnlyList<VirtualTagDefinition>? virtualTags = null)
    {
        var issues = series.Issues;
        BuildGroups(
            writers: CsvFieldAggregator.Distinct(issues.Select(WriterField.Get)),
            artists: CsvFieldAggregator.Distinct(issues.Select(ArtistField.Get)),
            teams: CsvFieldAggregator.Distinct(issues.Select(TeamsField.Get)),
            locations: CsvFieldAggregator.Distinct(issues.Select(LocationsField.Get)),
            characters: CsvFieldAggregator.Distinct(issues.Select(i => i.Characters)),
            genreTags: issues.SelectMany(i => i.Tags),
            issueId: null);
        FillVirtualTags(issues.Select(i => (i, (Series?)series)), virtualTags);
    }

    /// <summary>Single-issue focus: one issue's own values (Detail-screen issue-focus feature).</summary>
    public void LoadIssue(Issue issue, IReadOnlyList<VirtualTagDefinition>? virtualTags = null)
    {
        BuildGroups(
            writers: CsvFieldAggregator.Distinct(new[] { WriterField.Get(issue) }),
            artists: CsvFieldAggregator.Distinct(new[] { ArtistField.Get(issue) }),
            teams: CsvFieldAggregator.Distinct(new[] { TeamsField.Get(issue) }),
            locations: CsvFieldAggregator.Distinct(new[] { LocationsField.Get(issue) }),
            characters: CsvFieldAggregator.Distinct(new[] { issue.Characters }),
            genreTags: issue.Tags,
            issueId: issue.Id);
        FillVirtualTags(new[] { (issue, issue.Series) }, virtualTags);
    }

    private void BuildGroups(
        IReadOnlyList<string> writers,
        IReadOnlyList<string> artists,
        IReadOnlyList<string> teams,
        IReadOnlyList<string> locations,
        IReadOnlyList<string> characters,
        IEnumerable<IssueTag> genreTags,
        int? issueId)
    {
        Groups.Clear();

        var writerPills = writers.Select(PlainPill).ToList();
        var artistPills = artists.Select(PlainPill).ToList();
        if (writerPills.Count > 0 || artistPills.Count > 0)
        {
            Groups.Add(new DetailBandGroupViewModel(writerPills, artistPills, _goToDetailsTab));
        }

        var tagList = genreTags.ToList();
        AddGroup("Genres & Concepts", TagPills(tagList, IssueTagField.Genre, issueId));
        AddGroup("Teams", teams.Select(PlainPill).ToList());
        AddGroup("Locations", locations.Select(PlainPill).ToList());
        AddGroup("Characters", characters.Select(PlainPill).ToList());

        var (visibleTagPills, hiddenTagPills) = TagPillsSplitJunk(tagList, IssueTagField.Tags, issueId);
        if (visibleTagPills.Count > 0 || hiddenTagPills.Count > 0)
        {
            Groups.Add(new DetailBandGroupViewModel("Tags", visibleTagPills, hiddenTagPills, hiddenNoun: "hidden"));
        }

        OnPropertyChanged(nameof(HasGroups));
    }

    private void AddGroup(string label, IReadOnlyList<TagPillViewModel> chips)
    {
        if (chips.Count > 0)
        {
            Groups.Add(new DetailBandGroupViewModel(label, chips));
        }
    }

    private TagPillViewModel PlainPill(string value) =>
        new(value, category: null, IssueTagWeight.Unset, _goLibraryWithSearch, reweight: null);

    /// <summary>Dedup-by-value + highest-weight-wins, mirrors the old <c>DetailPillsViewModel.FillTagPills</c>.</summary>
    private List<TagPillViewModel> TagPills(IEnumerable<IssueTag> allTags, IssueTagField field, int? issueId)
    {
        return TagRows(allTags, field)
            .Select(r => MakePill(r, field, issueId))
            .ToList();
    }

    private (List<TagPillViewModel> Visible, List<TagPillViewModel> Hidden) TagPillsSplitJunk(IEnumerable<IssueTag> allTags, IssueTagField field, int? issueId)
    {
        var visible = new List<TagPillViewModel>();
        var hidden = new List<TagPillViewModel>();
        foreach (var row in TagRows(allTags, field))
        {
            var pill = MakePill(row, field, issueId);
            (JunkTagPattern.IsMatch(row.Value) ? hidden : visible).Add(pill);
        }

        return (visible, hidden);
    }

    private static IEnumerable<(string Value, string? Category, IssueTagWeight Weight)> TagRows(IEnumerable<IssueTag> allTags, IssueTagField field) =>
        allTags
            .Where(t => t.Field == field)
            .GroupBy(t => t.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Value: g.First().Value, Category: g.First().Category, Weight: g.Max(t => t.Weight)))
            .OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase);

    private TagPillViewModel MakePill((string Value, string? Category, IssueTagWeight Weight) row, IssueTagField field, int? issueId)
    {
        Action<IssueTagWeight>? reweight = issueId is int id && _reweightTag is not null
            ? w => _reweightTag(id, field, row.Value, w)
            : null;
        return new TagPillViewModel(row.Value, row.Category, row.Weight, _goLibraryWithSearch, reweight);
    }

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
