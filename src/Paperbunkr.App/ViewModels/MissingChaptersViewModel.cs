using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>The chapters MangaDex lists under one volume that the library doesn't have.</summary>
public sealed record MissingChapterGroup(string Label, string Numbers, int Count);

/// <summary>
/// The manga Detail screen's "Missing chapters" card (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md
/// Part 1): chapters MangaDex lists that the library lacks, grouped by volume, with a have/total badge. Opening a series reads the cached
/// MangaDex list only - no network on open; "Check MangaDex" fetches it. Report-only, nothing here edits an issue.
/// </summary>
public sealed partial class MissingChaptersViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly MangaDexAggregateClient _client;
    private readonly Func<DateTime> _nowUtc;
    private int? _seriesId;

    public MissingChaptersViewModel(Func<PaperbunkrDbContext>? contextFactory = null, MangaDexAggregateClient? client = null, Func<DateTime>? nowUtc = null)
    {
        _contextFactory = contextFactory ?? (() => PaperbunkrDb.CreateContext());
        _client = client ?? MangaDexAggregateClient.Shared;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public ObservableCollection<MissingChapterGroup> Groups { get; } = new();

    /// <summary>The series is linked to MangaDex, so the card is worth showing at all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isLinked;

    /// <summary>A MangaDex list has been fetched for this series (at any time).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(Summary), nameof(IsComplete), nameof(NeverChecked), nameof(CanMapVolumes))]
    private bool _hasReport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(Summary), nameof(IsComplete))]
    private int _missingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    private int _remoteCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _extraCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _unitWord = "chapter";

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>What the library's numbers stand for in the current report (chapters, or volumes when the files are volumes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMapVolumes))]
    private AlignmentUnit _unit = AlignmentUnit.Chapters;

    /// <summary>Some file of this series has a chapter range recorded (Phase 7).</summary>
    [ObservableProperty]
    private bool _hasMappings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck), nameof(CanMapVolumes))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    public bool IsVisible => IsLinked;

    /// <summary>The files are numbered by volume and none has a chapter range yet, so "Map volumes to chapters" would help.</summary>
    public bool CanMapVolumes => HasReport && Unit == AlignmentUnit.Volumes && !IsChecking;

    public bool CanCheck => !IsChecking;

    public bool HasStatus => StatusText.Length > 0;

    public bool NeverChecked => IsLinked && !HasReport;

    public bool HasMissing => MissingCount > 0;

    public bool IsComplete => HasReport && RemoteCount > 0 && MissingCount == 0;

    /// <summary>"have/total", e.g. <c>38/42</c>; empty until a MangaDex list exists.</summary>
    public string Badge => HasReport && RemoteCount > 0 ? $"{RemoteCount - MissingCount:N0}/{RemoteCount:N0}" : string.Empty;

    public string Summary
    {
        get
        {
            if (!HasReport)
            {
                return "Not checked against MangaDex yet.";
            }

            if (RemoteCount == 0)
            {
                return "MangaDex lists no chapters for this series in your files' language, so there is nothing to compare. Check the MangaDex link on the Details tab.";
            }

            string plural = MissingCount == 1 ? string.Empty : "s";
            string head = MissingCount == 0
                ? $"You have every {UnitWord} MangaDex lists."
                : $"{MissingCount:N0} {UnitWord}{plural} on MangaDex {(MissingCount == 1 ? "isn't" : "aren't")} in your library.";
            return ExtraCount > 0 ? $"{head} {ExtraCount:N0} of yours {(ExtraCount == 1 ? "isn't" : "aren't")} on MangaDex." : head;
        }
    }

    /// <summary>Loads the cached comparison for a series; hides the card when the series isn't linked to MangaDex.</summary>
    public void Load(int seriesId, bool keepStatus = false)
    {
        _seriesId = seriesId;
        if (!keepStatus)
        {
            StatusText = string.Empty;
        }

        using var context = _contextFactory();
        IsLinked = ChapterAlignmentResolver.FindMangaDexId(context, seriesId) is not null;
        Apply(IsLinked ? ChapterAlignmentResolver.ReadCached(context, seriesId) : null);
        HasMappings = IsLinked && CollectionContentService.HasRanges(context, seriesId);
    }

    /// <summary>
    /// Records, for each volume file, the chapters MangaDex files under that volume (Phase 7), so a library of volumes stops reading as "every
    /// chapter missing". Uses the cached list (fetching it only when there is none) and never touches a file or an issue's own number.
    /// </summary>
    [RelayCommand]
    private async Task MapVolumesAsync()
    {
        if (_seriesId is not int seriesId || IsChecking)
        {
            return;
        }

        IsChecking = true;
        StatusText = "Mapping volumes to chapters…";
        try
        {
            using var context = _contextFactory();
            string? mangaId = ChapterAlignmentResolver.FindMangaDexId(context, seriesId);
            if (mangaId is null)
            {
                StatusText = string.Empty;
                return;
            }

            var issues = context.Issues.Where(i => i.SeriesId == seriesId).ToList();
            var aggregate = await ChapterAlignmentResolver.GetAggregateAsync(
                context, seriesId, mangaId, ChapterAlignmentResolver.PreferredLanguage(issues), _client, _nowUtc());
            var result = CollectionContentService.MapVolumes(context, seriesId, aggregate, _nowUtc());
            await context.SaveChangesAsync();
            StatusText = result.Mapped == 0
                ? "No volume matched a MangaDex volume number, so nothing was mapped."
                : $"Mapped {result.Mapped:N0} volume{(result.Mapped == 1 ? string.Empty : "s")} to chapters"
                  + (result.Unmatched > 0 ? $"; {result.Unmatched:N0} had no matching volume on MangaDex." : ".");
        }
        catch (MetadataProviderUnavailableException)
        {
            StatusText = "MangaDex isn't answering right now; try again later.";
        }
        finally
        {
            IsChecking = false;
        }

        Load(seriesId, keepStatus: true);
    }

    /// <summary>Removes the chapter ranges "Map volumes to chapters" wrote (a range entered by hand stays).</summary>
    [RelayCommand]
    private void ClearMappings()
    {
        if (_seriesId is not int seriesId)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            int removed = CollectionContentService.ClearMapped(context, seriesId);
            context.SaveChanges();
            StatusText = removed == 0 ? "Nothing to clear." : $"Cleared {removed:N0} mapped range{(removed == 1 ? string.Empty : "s")}.";
        }

        Load(seriesId, keepStatus: true);
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_seriesId is not int seriesId || IsChecking)
        {
            return;
        }

        IsChecking = true;
        StatusText = "Checking MangaDex…";
        try
        {
            using var context = _contextFactory();
            // A manual check always asks again, so a cached list can't answer it.
            var report = await ChapterAlignmentResolver.BuildAsync(context, seriesId, _client, _nowUtc(), forceRefresh: true);
            if (_seriesId == seriesId)
            {
                Apply(report);
            }

            StatusText = string.Empty;
        }
        catch (MetadataProviderUnavailableException)
        {
            StatusText = "MangaDex isn't answering right now; try again later.";
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void Apply(AlignmentReport? report)
    {
        Groups.Clear();
        if (report is null)
        {
            HasReport = false;
            MissingCount = 0;
            RemoteCount = 0;
            ExtraCount = 0;
            OnPropertyChanged(nameof(HasMissing));
            return;
        }

        Unit = report.Unit;
        UnitWord = report.Unit == AlignmentUnit.Volumes ? "volume" : "chapter";
        foreach (var group in report.Missing
                     .GroupBy(m => m.Volume)
                     .OrderBy(g => g.Key is null ? float.MaxValue : MangaDexAggregateClient.TryParseNumber(g.Key, out float v) ? v : float.MaxValue - 1))
        {
            string label = report.Unit == AlignmentUnit.Volumes ? "Volumes" : group.Key is null ? "No volume" : $"Volume {group.Key}";
            Groups.Add(new MissingChapterGroup(label, string.Join(", ", group.Select(m => m.Number)), group.Count()));
        }

        RemoteCount = report.RemoteCount;
        MissingCount = report.Missing.Count;
        ExtraCount = report.ExtraLocally.Count;
        HasReport = true;
        OnPropertyChanged(nameof(HasMissing));
    }
}
