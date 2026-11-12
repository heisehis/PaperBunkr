using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The "Fill character images" task (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3): a small batch per run of
/// characters, teams and creators that have a provider id but have never been checked, most appearances first, so strips and chips get images
/// without every page being opened. Stops at the first failure (offline, quota, API error) and leaves the rest for the next run; the pattern of
/// <c>ContinuityUniverseSweep</c>.
/// </summary>
public sealed class EntityImageSweep
{
    public const int DefaultBatchSize = 20;

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly EntityProfileCompletion _completion;

    public EntityImageSweep(Func<PaperbunkrDbContext>? contextFactory = null, EntityProfileCompletion? completion = null)
    {
        _contextFactory = contextFactory ?? (() => PaperbunkrDb.CreateContext());
        _completion = completion ?? new EntityProfileCompletion(_contextFactory);
    }

    public sealed record SweepResult(int Checked, int Remaining, bool StoppedOnFailure)
    {
        public string Describe() => StoppedOnFailure
            ? $"Checked {Checked}; stopped on a provider error, the rest wait for the next run"
            : Checked == 0 ? "No new characters, teams or creators to look up" : $"Checked {Checked} for images";
    }

    /// <summary>The next entities to look up, most appearances first: those with a Metron or ComicVine id and no check stamp.</summary>
    public static IReadOnlyList<(ComicMetadataEntityKind Kind, int Id)> Candidates(PaperbunkrDbContext context, int take)
    {
        var withIds = context.ComicMetadataExternalIds
            .Where(e => e.Provider == ComicProvider.Metron || e.Provider == ComicProvider.ComicVine)
            .Select(e => new { e.EntityKind, e.EntityId })
            .Distinct()
            .ToList()
            .ToLookup(e => e.EntityKind, e => e.EntityId);

        var list = new List<(ComicMetadataEntityKind Kind, int Id, int Count)>();
        var characterIds = withIds[ComicMetadataEntityKind.Character].ToList();
        list.AddRange(context.Characters
            .Where(c => characterIds.Contains(c.Id) && c.ImageCheckedAt == null)
            .Select(c => new { c.Id, Count = c.Appearances.Count })
            .ToList().Select(c => (ComicMetadataEntityKind.Character, c.Id, c.Count)));
        var teamIds = withIds[ComicMetadataEntityKind.Team].ToList();
        list.AddRange(context.Teams
            .Where(t => teamIds.Contains(t.Id) && t.ImageCheckedAt == null)
            .Select(t => new { t.Id, Count = t.Appearances.Count })
            .ToList().Select(t => (ComicMetadataEntityKind.Team, t.Id, t.Count)));
        // Creators with a Metron/ComicVine id, or credited on a series linked to AniList or MangaDex (manga creators rarely have the former).
        var creatorIds = withIds[ComicMetadataEntityKind.Creator].ToList();
        var mangaLinkedSeries = context.ExternalMediaIds
            .Where(e => e.Provider == ExternalMetadataProvider.AniList || e.Provider == ExternalMetadataProvider.MangaDex)
            .Select(e => e.SeriesId);
        list.AddRange(context.Creators
            .Where(c => (creatorIds.Contains(c.Id) || c.Credits.Any(cr => mangaLinkedSeries.Contains(cr.Issue!.SeriesId))) && c.ImageCheckedAt == null)
            .Select(c => new { c.Id, Count = c.Credits.Select(cr => cr.IssueId).Distinct().Count() })
            .ToList().Select(c => (ComicMetadataEntityKind.Creator, c.Id, c.Count)));
        var locationIds = withIds[ComicMetadataEntityKind.Location].ToList();
        list.AddRange(context.Locations
            .Where(l => locationIds.Contains(l.Id) && l.ImageCheckedAt == null)
            .Select(l => new { l.Id, Count = l.Appearances.Count })
            .ToList().Select(l => (ComicMetadataEntityKind.Location, l.Id, l.Count)));

        return list.OrderByDescending(e => e.Count).ThenBy(e => e.Kind).ThenBy(e => e.Id)
            .Take(take).Select(e => (e.Kind, e.Id)).ToList();
    }

    public async Task<SweepResult> RunAsync(IProgress<string>? progress, CancellationToken cancellationToken, int batchSize = DefaultBatchSize)
    {
        IReadOnlyList<(ComicMetadataEntityKind Kind, int Id)> batch;
        using (var context = _contextFactory())
        {
            batch = Candidates(context, batchSize + 1);
        }

        int checkedCount = 0;
        foreach (var (kind, id) in batch.Take(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Looking up {checkedCount + 1} of {Math.Min(batch.Count, batchSize)}…");
            var outcome = await _completion.CompleteAsync(kind, id, cancellationToken).ConfigureAwait(false);
            if (outcome == EntityProfileOutcome.Failed)
            {
                return new SweepResult(checkedCount, batch.Count - checkedCount, StoppedOnFailure: true);
            }

            if (outcome == EntityProfileOutcome.Checked)
            {
                checkedCount++;
            }
        }

        return new SweepResult(checkedCount, Math.Max(0, batch.Count - batchSize), StoppedOnFailure: false);
    }
}
