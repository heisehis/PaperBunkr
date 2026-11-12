using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>What a "Check for missing issues" run did.</summary>
public sealed record MissingCheckResult(int Checked, int Saved, int WithoutIdentity, IReadOnlyList<string> Failures);

/// <summary>
/// "Check for missing issues" (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md §1): fetches each event's provider
/// list for its <b>verified</b> arc id only (the stored Metron arc id, or the ComicVine arc id when there's no identity conflict) and saves
/// it with <see cref="EventArcSnapshots.Save"/>. Never matches by name, never touches memberships. At most <see cref="MaxEvents"/> per run.
/// </summary>
public static class EventMissingChecker
{
    public const int MaxEvents = 20;

    public static async Task<MissingCheckResult> CheckAsync(IReadOnlyList<int> storyEventIds, Func<PaperbunkrDbContext> contextFactory,
        Func<PaperbunkrDbContext, string, IReadingListSource?> resolveSource, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        int saved = 0, withoutIdentity = 0, done = 0;
        var failures = new List<string>();
        var ids = storyEventIds.Distinct().Take(MaxEvents).ToList();
        foreach (int eventId in ids)
        {
            ct.ThrowIfCancellationRequested();
            using var context = contextFactory();
            var e = context.StoryEvents.AsNoTracking().Where(x => x.Id == eventId)
                .Select(x => new { x.Name, x.MetronArcId, x.ComicVineArcId, x.IdentityConflict }).FirstOrDefault();
            var arcs = new List<(string Provider, string ArcId)>();
            if (e is not null && !string.IsNullOrWhiteSpace(e.MetronArcId)) arcs.Add((EventArcSnapshots.Metron, e.MetronArcId!));
            if (e is not null && !string.IsNullOrWhiteSpace(e.ComicVineArcId) && e.IdentityConflict is null) arcs.Add((EventArcSnapshots.ComicVine, e.ComicVineArcId!));
            if (arcs.Count == 0)
            {
                withoutIdentity++;
            }

            foreach (var (provider, arcId) in arcs)
            {
                var source = resolveSource(context, provider);
                if (source is null)
                {
                    failures.Add($"{e!.Name}: {provider} isn't connected");
                    continue;
                }

                try
                {
                    var list = await source.GetArcEntriesAsync(arcId, ct).ConfigureAwait(false);
                    EventArcSnapshots.Save(context, eventId, provider, arcId, list);
                    saved++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add($"{e!.Name}: {provider} - {ex.Message}");
                }
            }

            progress?.Report((++done, ids.Count));
        }

        return new MissingCheckResult(ids.Count, saved, withoutIdentity, failures);
    }
}

/// <summary>What one Add to Wanted did, kept so it can be undone.</summary>
public sealed record WantedRequestResult(int Requested, IReadOnlyList<string> Unmatched, IReadOnlyList<int> CreatedWantedIds, IReadOnlyList<int> CreatedWatchedIds);

/// <summary>
/// Add to Wanted for missing issues (§4). A ghost is requested through its provider's issue id under the <see cref="WatchedSeries"/> for its
/// provider series id, which is created when the series isn't tracked yet (the caller confirms that first, see
/// <see cref="SeriesToTrack"/>). An entry without provider ids can't be matched and is reported. Everything created is recorded for
/// <see cref="Undo"/>; nothing half-made is left behind.
/// </summary>
public static class EventWantedRequester
{
    /// <summary>Names of the series a request would start tracking (for the one confirmation).</summary>
    public static IReadOnlyList<string> SeriesToTrack(PaperbunkrDbContext context, IReadOnlyList<EventGhost> ghosts)
    {
        var names = new List<string>();
        foreach (var group in Requestable(ghosts).GroupBy(g => (Provider(g.Ghost), g.SeriesId)))
        {
            var (provider, seriesId) = group.Key;
            if (!context.WatchedSeries.Any(w => w.Provider == provider && w.ExternalVolumeId == seriesId))
            {
                names.Add(group.First().Ghost.Entry.Series);
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static WantedRequestResult Request(PaperbunkrDbContext context, IReadOnlyList<EventGhost> ghosts)
    {
        var createdWanted = new List<int>();
        var createdWatched = new List<int>();
        var unmatched = ghosts.Where(g => g.Kind == EventGhostKind.Missing && Parse(g) is null)
            .Select(g => $"{g.Entry.Series} #{g.Entry.Number}").ToList();
        int requested = 0;
        using var transaction = context.Database.BeginTransaction();
        foreach (var item in Requestable(ghosts))
        {
            var provider = Provider(item.Ghost);
            var watched = context.WatchedSeries.FirstOrDefault(w => w.Provider == provider && w.ExternalVolumeId == item.SeriesId);
            if (watched is null)
            {
                var entry = item.Ghost.Entry;
                watched = WantedService.TrackVolume(context, new ComicVineVolume(item.SeriesId, entry.Series, null, entry.Year > 0 ? entry.Year : null, 0, null),
                    seriesId: null, watchFutureReleases: false, provider: provider);
                createdWatched.Add(watched.Id);
            }

            bool existed = context.WantedIssues.Any(w => w.Provider == provider && w.ExternalIssueId == item.IssueId);
            var wanted = WantedService.Request(context, watched, new ComicVineIssue(item.IssueId, item.Ghost.Entry.Number, item.Ghost.Entry.Title,
                Date(item.Ghost.Entry.StoreDate), Date(item.Ghost.Entry.CoverDate), item.Ghost.Entry.CoverImageUrl, item.SeriesId));
            if (!existed)
            {
                createdWanted.Add(wanted.Id);
            }

            requested++;
        }

        transaction.Commit();
        return new WantedRequestResult(requested, unmatched, createdWanted, createdWatched);
    }

    /// <summary>Removes exactly what <see cref="Request"/> created (wants that already existed stay).</summary>
    public static void Undo(PaperbunkrDbContext context, WantedRequestResult result)
    {
        context.WantedIssues.RemoveRange(context.WantedIssues.Where(w => result.CreatedWantedIds.Contains(w.Id)));
        context.SaveChanges();
        var emptied = context.WatchedSeries.Where(w => result.CreatedWatchedIds.Contains(w.Id) && !context.WantedIssues.Any(x => x.WatchedSeriesId == w.Id));
        context.WatchedSeries.RemoveRange(emptied);
        context.SaveChanges();
    }

    private static IEnumerable<(EventGhost Ghost, int SeriesId, int IssueId)> Requestable(IEnumerable<EventGhost> ghosts)
    {
        foreach (var ghost in ghosts.Where(g => g.Kind == EventGhostKind.Missing))
        {
            if (Parse(ghost) is { } ids)
            {
                yield return (ghost, ids.Series, ids.Issue);
            }
        }
    }

    private static (int Series, int Issue)? Parse(EventGhost ghost) =>
        int.TryParse(ghost.ProviderSeriesId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int series)
        && int.TryParse(ghost.ProviderIssueId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int issue)
            ? (series, issue) : null;

    private static ComicProvider Provider(EventGhost ghost) => ghost.Provider == EventArcSnapshots.Metron ? ComicProvider.Metron : ComicProvider.ComicVine;

    private static DateTime? Date(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
}
