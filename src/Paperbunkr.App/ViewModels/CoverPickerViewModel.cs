using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>One selectable cover source in the picker's "From Series"/"From Reading List" tabs.</summary>
public sealed record CoverPickerCandidate(int IssueId, string Label)
{
    /// <summary><see cref="Views.AsyncCoverImage.SourceIdProperty"/> is typed <c>string?</c> - this avoids relying on implicit int-to-string binding conversion.</summary>
    public string SourceKey => IssueId.ToString();
}

/// <summary>
/// Backs the cover-picker flyout content (docs/superpowers/specs/2026-09-17-reader-save-page-and-
/// cover-picker-design.md) - shown from a <c>Button.Flyout</c> (confirmed working pattern - see
/// <c>ReadingStatusPicker.axaml</c>'s own comment: plain <c>Flyout</c>, never
/// <c>MenuFlyout</c>/<c>ContextMenu</c>). 3 tabs: Browse File (today's only option, unchanged),
/// From Series, From Reading List. Applying any of them calls the same
/// <see cref="CoverThumbnailService.TrySetCustomCover"/> the file-browse path already used before
/// this feature existed - a real copy into the target issue's own custom-cover slot, not a live
/// link to the source.
/// </summary>
public partial class CoverPickerViewModel : ViewModelBase
{
    private readonly int _targetIssueId;
    private readonly Action _onApplied;

    /// <summary>
    /// Non-null only when opened from External Metadata for a provider with a browsable
    /// multi-cover archive (MangaBaka today, via <see cref="IMultiCoverProvider"/>) - AniList/
    /// MangaDex apply their single <c>CoverImageUrl</c> directly instead of opening this picker
    /// (docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §2).
    /// </summary>
    public CoverPickerViewModel(int targetIssueId, int? seriesId, Action onApplied, (ExternalMetadataProvider Provider, string ExternalId)? externalProviderCoverContext = null)
    {
        _targetIssueId = targetIssueId;
        _onApplied = onApplied;
        SeriesCandidates = new ObservableCollection<CoverPickerCandidate>();
        ReadingListCandidates = new ObservableCollection<CoverPickerCandidate>();
        ExternalProviderCandidates = new ObservableCollection<CoverCandidate>();
        HasExternalProviderTab = externalProviderCoverContext is not null;
        Load(seriesId);

        if (externalProviderCoverContext is { } context)
        {
            SelectedTabIndex = 3;
            _ = LoadExternalProviderCandidatesAsync(context);
        }
    }

    public ObservableCollection<CoverPickerCandidate> SeriesCandidates { get; }

    public ObservableCollection<CoverPickerCandidate> ReadingListCandidates { get; }

    public ObservableCollection<CoverCandidate> ExternalProviderCandidates { get; }

    public bool HasSeriesCandidates => SeriesCandidates.Count > 0;

    public bool HasReadingListCandidates => ReadingListCandidates.Count > 0;

    public bool HasExternalProviderTab { get; }

    [ObservableProperty]
    private bool _isLoadingExternalProviderCandidates;

    partial void OnIsLoadingExternalProviderCandidatesChanged(bool value) =>
        OnPropertyChanged(nameof(ShowNoExternalProviderCandidatesMessage));

    public bool HasExternalProviderCandidates => ExternalProviderCandidates.Count > 0;

    /// <summary>Plain bool rather than a multi-binding/converter, matching this class's own
    /// established "bool properties over converters" convention (see <see cref="IsBrowseFileTabActive"/>'s sibling doc comment).</summary>
    public bool ShowNoExternalProviderCandidatesMessage => !IsLoadingExternalProviderCandidates && !HasExternalProviderCandidates;

    [ObservableProperty]
    private int _selectedTabIndex;

    // Bool properties per tab rather than an int + equality converter - matches this codebase's own
    // existing 2-tab toggle precedent (LibrarySection's Folders/Maintenance tabs use a plain bool,
    // no converter class).
    public bool IsBrowseFileTabActive => SelectedTabIndex == 0;

    public bool IsSeriesTabActive => SelectedTabIndex == 1;

    public bool IsReadingListTabActive => SelectedTabIndex == 2;

    public bool IsExternalProviderTabActive => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBrowseFileTabActive));
        OnPropertyChanged(nameof(IsSeriesTabActive));
        OnPropertyChanged(nameof(IsReadingListTabActive));
        OnPropertyChanged(nameof(IsExternalProviderTabActive));
    }

    [RelayCommand]
    private void ShowBrowseFileTab() => SelectedTabIndex = 0;

    [RelayCommand]
    private void ShowSeriesTab() => SelectedTabIndex = 1;

    [RelayCommand]
    private void ShowReadingListTab() => SelectedTabIndex = 2;

    [RelayCommand]
    private void ShowExternalProviderTab() => SelectedTabIndex = 3;

    private async Task LoadExternalProviderCandidatesAsync((ExternalMetadataProvider Provider, string ExternalId) context)
    {
        IMultiCoverProvider? provider = context.Provider switch
        {
            ExternalMetadataProvider.MangaBaka => MangaBakaMetadataProvider.Shared,
            _ => null,
        };

        if (provider is null)
        {
            return;
        }

        IsLoadingExternalProviderCandidates = true;
        try
        {
            var candidates = await provider.GetCoverCandidatesAsync(context.ExternalId, CancellationToken.None);
            foreach (var candidate in candidates)
            {
                ExternalProviderCandidates.Add(candidate);
            }

            OnPropertyChanged(nameof(HasExternalProviderCandidates));
            OnPropertyChanged(nameof(ShowNoExternalProviderCandidatesMessage));
        }
        finally
        {
            IsLoadingExternalProviderCandidates = false;
        }
    }

    [RelayCommand]
    private async Task SelectExternalCandidateAsync(CoverCandidate candidate)
    {
        byte[]? bytes = await ProviderCoverCandidateCache.DownloadBytesAsync(candidate.FullUrl, CancellationToken.None);
        if (bytes is not null && new CoverThumbnailService().TrySetCustomCoverFromBytes(_targetIssueId, bytes))
        {
            _onApplied();
        }
    }

    private void Load(int? seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();

        var seriesIssueIds = new System.Collections.Generic.HashSet<int>();
        if (seriesId is int sid)
        {
            foreach (var issue in context.Issues.Where(i => i.SeriesId == sid && i.Id != _targetIssueId).OrderBy(i => i.Number))
            {
                if (CoverThumbnailService.GetEffectiveCoverPath(issue.Id) is null)
                {
                    continue;
                }

                seriesIssueIds.Add(issue.Id);
                SeriesCandidates.Add(new CoverPickerCandidate(issue.Id, $"#{issue.EffectiveNumber()}"));
            }
        }

        var myReadingListIds = context.ReadingListItems
            .Where(ri => ri.IssueId == _targetIssueId)
            .Select(ri => ri.ReadingListId)
            .ToList();

        if (myReadingListIds.Count > 0)
        {
            var candidateIssueIds = context.ReadingListItems
                .Where(ri => myReadingListIds.Contains(ri.ReadingListId) && ri.IssueId != _targetIssueId)
                .Select(ri => ri.IssueId)
                .Distinct()
                .Where(id => !seriesIssueIds.Contains(id))
                .ToList();

            foreach (int issueId in candidateIssueIds)
            {
                if (CoverThumbnailService.GetEffectiveCoverPath(issueId) is null)
                {
                    continue;
                }

                var issue = context.Issues.Find(issueId);
                var seriesName = issue?.SeriesId is int otherSeriesId ? context.Series.Find(otherSeriesId)?.Name : null;
                ReadingListCandidates.Add(new CoverPickerCandidate(issueId, $"{seriesName ?? "Unknown"} #{issue?.EffectiveNumber()}"));
            }
        }
    }

    [RelayCommand]
    private void SelectCandidate(CoverPickerCandidate candidate)
    {
        string? path = CoverThumbnailService.GetEffectiveCoverPath(candidate.IssueId);
        if (path is not null && new CoverThumbnailService().TrySetCustomCover(_targetIssueId, path))
        {
            _onApplied();
        }
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        string? path = await new FilePickerService().PickImageFileAsync("Choose Cover Image");
        if (path is not null && new CoverThumbnailService().TrySetCustomCover(_targetIssueId, path))
        {
            _onApplied();
        }
    }
}
