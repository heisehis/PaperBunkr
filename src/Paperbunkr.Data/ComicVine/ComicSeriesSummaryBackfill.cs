using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.Data.ComicVine;

public sealed record ComicSeriesSummaryBackfillResult(
    int Candidates, int Filled, int WithoutLink, int WithoutCredentials, int WithoutDescription, int Failed)
{
    public string Describe() =>
        $"Filled {Filled} of {Candidates} missing comic series summaries; " +
        $"{WithoutLink} without a linked series ID, {WithoutCredentials} without provider credentials, " +
        $"{WithoutDescription} without a provider description, {Failed} lookup failures.";
}

/// <summary>
/// Fills old, blank comic-series summaries from the series identities a previous scrape already confirmed.
/// An issue synopsis and a title-only provider search are never substitutes for a series description.
/// </summary>
public static class ComicSeriesSummaryBackfill
{
    public static async Task<ComicSeriesSummaryBackfillResult> RunAsync(
        Func<PaperbunkrDbContext> contextFactory,
        Func<ComicProvider, IComicVineClient?> clientFor,
        Action<int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<(int Id, string? LockedFields)> candidates;
        Dictionary<int, List<ComicMetadataExternalId>> links;
        using (var context = contextFactory())
        {
            var rows = await context.Series.AsNoTracking()
                .Where(s => s.ContentType == ContentType.Comic || s.ContentType == ContentType.Unknown)
                .Where(s => s.Summary == null || s.Summary.Trim() == "")
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.LockedFields })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            candidates = rows.Select(s => (s.Id, s.LockedFields)).ToList();
            var ids = candidates.Select(s => s.Id).ToArray();
            links = (await context.ComicMetadataExternalIds.AsNoTracking()
                .Where(l => l.EntityKind == ComicMetadataEntityKind.Series && ids.Contains(l.EntityId))
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                .GroupBy(l => l.EntityId).ToDictionary(g => g.Key, g => g.ToList());
        }

        candidates.RemoveAll(s => MangaFieldLocks.Parse(s.LockedFields).Contains(MangaFieldLocks.SummaryKey));
        var clients = new Dictionary<ComicProvider, IComicVineClient?>();
        int filled = 0, withoutLink = 0, withoutCredentials = 0, withoutDescription = 0, failed = 0;

        for (int index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int seriesId = candidates[index].Id;
            var linked = links.GetValueOrDefault(seriesId)?
                .Where(l => int.TryParse(l.ExternalId, out int id) && id > 0)
                .OrderBy(l => l.Provider == ComicProvider.Metron ? 0 : 1)
                .ToList();
            if (linked is null or { Count: 0 })
            {
                withoutLink++;
                progress?.Invoke(index + 1, candidates.Count);
                continue;
            }

            bool available = false, lookupFailed = false, foundDescription = false;
            foreach (var link in linked)
            {
                if (!clients.TryGetValue(link.Provider, out var client))
                {
                    client = clientFor(link.Provider);
                    clients.Add(link.Provider, client);
                }
                if (client is null) continue;
                available = true;

                try
                {
                    var volume = await client.GetVolumeAsync(int.Parse(link.ExternalId), cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(volume?.Description)) continue;
                    foundDescription = true;

                    // The user can edit or lock the summary while the network request is in flight.
                    using var write = contextFactory();
                    var series = await write.Series.FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken).ConfigureAwait(false);
                    if (series is not null && string.IsNullOrWhiteSpace(series.Summary)
                        && !MangaFieldLocks.IsLocked(series, MangaFieldLocks.SummaryKey))
                    {
                        series.Summary = volume.Description;
                        await write.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        filled++;
                    }
                    break;
                }
                catch (ComicVineException)
                {
                    lookupFailed = true;
                }
                catch (HttpRequestException)
                {
                    lookupFailed = true;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lookupFailed = true;
                }
            }

            if (!available) withoutCredentials++;
            else if (!foundDescription && lookupFailed) failed++;
            else if (!foundDescription) withoutDescription++;
            progress?.Invoke(index + 1, candidates.Count);
        }

        return new ComicSeriesSummaryBackfillResult(candidates.Count, filled, withoutLink, withoutCredentials, withoutDescription, failed);
    }
}
