using Paperbunkr.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Acquisition;

/// <param name="ListsChecked">Followed arc lists processed.</param>
/// <param name="IssuesAdded">New entries the refresh found in the arcs (each becomes a placeholder in its list).</param>
/// <param name="Requested">Wanted issues created for missing entries.</param>
/// <param name="Unresolved">Entries that couldn't be matched to a ComicVine issue.</param>
/// <param name="Problems">Lists that couldn't be processed, with why.</param>
public sealed record ArcFollowResult(int ListsChecked, int IssuesAdded, int Requested, int Unresolved, IReadOnlyList<string> Problems);

/// <summary>
/// "Follow arc" (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §8): for every arc-linked reading list the user follows, re-run
/// the list's Refresh (new entries in the arc become placeholders, in order) and then request whatever is missing. Requesting is idempotent, so a
/// run only ever creates wants for entries that appeared since the last one.
/// </summary>
public static class ArcFollowService
{
    /// <param name="comicVine">Null when there is no ComicVine key: lists are still refreshed, nothing is requested.</param>
    /// <param name="sourceOverride">Test seam: replaces the registered arc source.</param>
    public static async Task<ArcFollowResult> RunAsync(
        PaperbunkrDbContext context, IComicVineClient? comicVine, CancellationToken cancellationToken,
        IReadingListSource? sourceOverride = null, Action<int, int>? progress = null, Func<ComicProvider, IComicVineClient?>? clientFor = null)
    {
        var lists = context.ReadingLists
            .Where(l => l.FollowArc && l.Source != null && l.ArcId != null)
            .OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name })
            .ToList();

        int added = 0, requested = 0, unresolved = 0, checkedCount = 0;
        var problems = new List<string>();

        for (int i = 0; i < lists.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(i, lists.Count);
            var list = lists[i];

            try
            {
                var refresh = await ArcReadingListBuilder.RefreshAsync(context, list.Id, cancellationToken, sourceOverride).ConfigureAwait(false);
                added += refresh.AddedCount;

                if (comicVine is not null || clientFor is not null)
                {
                    var result = await ArcRequestService.RequestMissingAsync(context, list.Id, comicVine, cancellationToken, clientFor).ConfigureAwait(false);
                    requested += result.Requested;
                    unresolved += result.Unresolved.Count;
                }

                context.ReadingLists.Where(l => l.Id == list.Id).ExecuteUpdate(u => u.SetProperty(l => l.LastFollowedAt, DateTime.UtcNow));
                checkedCount++;
            }
            catch (Exception ex) when (ex is ReadingListSourceException or InvalidOperationException)
            {
                problems.Add($"{list.Name}: {ex.Message}");
            }
            catch (ComicVineException ex) when (ex.ApiStatusCode is 100 or 107)
            {
                // A bad key or a rate limit affects every remaining list: stop now instead of failing each in turn.
                problems.Add($"{list.Name}: {ex.Message}");
                break;
            }
            catch (ComicVineException ex)
            {
                problems.Add($"{list.Name}: {ex.Message}");
            }
        }

        progress?.Invoke(lists.Count, lists.Count);
        return new ArcFollowResult(checkedCount, added, requested, unresolved, problems);
    }
}
