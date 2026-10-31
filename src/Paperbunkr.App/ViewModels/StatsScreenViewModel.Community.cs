using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>One line in the "You vs Community" disagreement lists.</summary>
public sealed record CommunityDisagreementRow(int SeriesId, string Name, string Text);

/// <summary>
/// The Insights "You vs Community" card (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 2): your
/// series ratings against community ratings on 0-100, as a scatter plus the biggest disagreements each way. Range-independent, so it loads
/// once per refresh like the other lazy cards, in the Top Lists group; rating changes and a ratings fetch invalidate it.
/// </summary>
public partial class StatsScreenViewModel
{
    /// <summary>Fewer comparable series than this and the scatter says nothing; the card explains what is missing instead.</summary>
    public const int MinCommunityComparable = 3;

    private readonly LazySlot _communitySlot = new();

    [ObservableProperty]
    private CommunityComparison _community = CommunityComparison.Empty;

    /// <summary>Raised on the UI thread when <see cref="Community"/> changes, for the scatter chart to redraw.</summary>
    public event Action<CommunityComparison>? CommunityChanged;

    public bool HasCommunityChart => Community.ComparableSeries >= MinCommunityComparable;

    public bool CommunityIsEmpty => !HasCommunityChart;

    public IReadOnlyList<CommunityDisagreementRow> CommunityHigherRows => Community.HigherThanCommunity.Select(ToDisagreementRow).ToList();

    public IReadOnlyList<CommunityDisagreementRow> CommunityLowerRows => Community.LowerThanCommunity.Select(ToDisagreementRow).ToList();

    /// <summary>How much of your rated library has community data - and the comics gap, since ComicVine reports no score at all.</summary>
    public string CommunityCoverageText => Community.RatedSeries == 0
        ? "Rate a few series and they appear here."
        : $"{Community.ComparableSeries:N0} of {Community.RatedSeries:N0} rated series have community data. "
          + "Manga uses AniList, MangaBaka and MangaDex scores; comics use Metron's issue ratings (ComicVine reports none).";

    public string CommunityEmptyText => Community.RatedSeries == 0
        ? "You haven't rated any series yet."
        : $"Only {Community.ComparableSeries:N0} of your {Community.RatedSeries:N0} rated series have community data, and the chart needs {MinCommunityComparable}. "
          + "Use Fetch community ratings to look up the rest.";

    partial void OnCommunityChanged(CommunityComparison value)
    {
        foreach (var name in new[]
        {
            nameof(HasCommunityChart), nameof(CommunityIsEmpty), nameof(CommunityHigherRows), nameof(CommunityLowerRows),
            nameof(CommunityCoverageText), nameof(CommunityEmptyText),
        })
        {
            OnPropertyChanged(name);
        }

        CommunityChanged?.Invoke(value);
    }

    private static CommunityDisagreementRow ToDisagreementRow(CommunityComparisonRow row) =>
        new(row.SeriesId, row.SeriesName,
            string.Create(CultureInfo.InvariantCulture, $"You {row.User:0} · Community {row.Community:0} ({row.Delta:+0;-0})"));

    [RelayCommand]
    private void OpenCommunitySeries(CommunityDisagreementRow? row)
    {
        if (row is not null)
        {
            GoDetailForSeries?.Invoke(row.SeriesId);
        }
    }

    private void LoadCommunity() =>
        _ = LoadSlotAsync(
            _communitySlot,
            () =>
            {
                using var c = PaperbunkrDb.CreateContext();
                return (Comparison: CommunityComparisonResolver.Build(c), Fetchable: CommunityRatingBackfillService.Candidates(c).Count);
            },
            v =>
            {
                CommunityFetchCandidates = v.Fetchable;
                Community = v.Comparison;
            });

    // --- Fetching community ratings (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 2) ---

    /// <summary>The Activity Center, for the fetch job. Set by <see cref="InsightsScreenViewModel"/>; null in tests that don't need it.</summary>
    public IActivityService? Activity { get; set; }

    /// <summary>The fetcher for a provider; tests replace it so no request leaves the machine.</summary>
    public Func<Paperbunkr.Data.Entities.ExternalMetadataProvider, IMetadataProvider?> CommunityProviderFor { get; set; } = MetadataProviderCatalog.For;

    /// <summary>How many rated series have a linked provider that hasn't had its score fetched yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFetchCommunity), nameof(FetchCommunityLabel))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommunityRatingsCommand))]
    private int _communityFetchCandidates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFetchCommunity), nameof(FetchCommunityLabel))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommunityRatingsCommand))]
    private bool _isFetchingCommunity;

    public bool CanFetchCommunity => !IsFetchingCommunity && CommunityFetchCandidates > 0;

    public string FetchCommunityLabel => IsFetchingCommunity
        ? "Fetching…"
        : CommunityFetchCandidates > 0 ? $"Fetch community ratings ({CommunityFetchCandidates:N0})" : "Community ratings are up to date";

    private CancellationTokenSource? _communityFetch;

    [RelayCommand(CanExecute = nameof(CanFetchCommunity))]
    private async Task FetchCommunityRatingsAsync()
    {
        if (IsFetchingCommunity)
        {
            return;
        }

        IsFetchingCommunity = true;
        _communityFetch = new CancellationTokenSource();
        var job = Activity?.StartJob(Paperbunkr.Data.Entities.ActivityJobKind.SyncMetadata, "Fetching community ratings");
        try
        {
            using var linked = job is null
                ? CancellationTokenSource.CreateLinkedTokenSource(_communityFetch.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(_communityFetch.Token, job.CancellationToken);
            var progress = new InlineProgress(p => job?.Report(p.Done, p.Total));
            var result = await CommunityRatingBackfillService.RunAsync(PaperbunkrDb.CreateContext, CommunityProviderFor, progress, linked.Token);
            job?.Succeed(
                result.StoppedEarly
                    ? $"Stopped after {result.Stored:N0} of {result.Total:N0}: a provider stopped answering. Fetch again later to continue."
                    : $"Fetched {result.Stored:N0} community rating{(result.Stored == 1 ? string.Empty : "s")}"
                      + (result.NoScore > 0 ? $"; {result.NoScore:N0} had no score yet." : "."),
                itemsProcessed: result.Stored);
        }
        catch (OperationCanceledException)
        {
            job?.Succeed("Cancelled.");
        }
        catch (Exception ex)
        {
            job?.Fail("Couldn't fetch community ratings.", ex: ex);
        }
        finally
        {
            job?.Dispose();
            _communityFetch.Dispose();
            _communityFetch = null;
            IsFetchingCommunity = false;
        }

        InvalidateCommunity();
    }

    /// <summary>Reports on the caller's thread at once; <see cref="Progress{T}"/> posts to the captured context, so its last report could land after the job settles.</summary>
    private sealed class InlineProgress : IProgress<(int Done, int Total)>
    {
        private readonly Action<(int Done, int Total)> _report;

        public InlineProgress(Action<(int Done, int Total)> report) => _report = report;

        public void Report((int Done, int Total) value) => _report(value);
    }

    /// <summary>Forces the card to load again next time its group is shown (your ratings or the stored community ratings changed).</summary>
    public void InvalidateCommunity()
    {
        _communitySlot.Loaded = false;
        _communitySlot.Generation++;
        if (IsTopListsGroupSelected)
        {
            LoadCommunity();
        }
    }
}
