using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One "A comes before B" link read from Wikidata (A followed by B / B follows A).</summary>
public sealed record WikidataEventLink(int EarlierEventId, int LaterEventId, string EvidenceQid);

public sealed record WikidataLinksResult(int Checked, int Matched, IReadOnlyList<int> CheckedEventIds, IReadOnlyList<WikidataEventLink> Links);

/// <summary>
/// The smart connector's Wikidata half (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2, Q19). Matches story events to
/// Wikidata items - by Comic Vine ID (P5905, "4045-{arc id}") when the event has a ComicVine arc id, else by name keys on items of an
/// allowed type with a publisher check - then reads follows/followed by (P155/P156). Coverage is thin (a few dozen storylines) and
/// noisy (Planet Hulk "follows" the placeholder item "nothing"), so only links whose both ends are events in the library count.
/// </summary>
public static class WikidataEventLinks
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(30);

    public const int DefaultBatchSize = 40;

    /// <summary>
    /// Item types a comic event is filed under on Wikidata - they're inconsistent (checked live 2026-09-27: Planet Hulk is a "written
    /// work", World War Hulk a "limited series"): comic book storyline, limited series, comic book series, written work, crossover.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedTypes = new HashSet<string> { "Q115378877", "Q3297186", "Q14406742", "Q47461344", "Q1047299" };

    /// <summary>Placeholder values: Q154242 "nothing".</summary>
    private static readonly IReadOnlySet<string> Placeholders = new HashSet<string> { "Q154242" };

    private const int MaxSearchResults = 5;

    public static async Task<WikidataLinksResult> RunAsync(
        Func<PaperbunkrDbContext> contextFactory, IWikidataLookup wikidata, int batchSize, DateTime nowUtc, CancellationToken cancellationToken)
    {
        List<EventInfo> all;
        using (var context = contextFactory())
        {
            all = LoadEvents(context);
        }

        var due = all
            .Where(e => e.CheckedAt is not DateTime checkedAt || nowUtc - checkedAt >= RecheckAfter)
            .OrderBy(e => e.CheckedAt ?? DateTime.MinValue).ThenBy(e => e.Id)
            .Take(Math.Max(1, batchSize))
            .ToList();

        var publisherLabels = new Dictionary<string, string?>();
        var entities = new Dictionary<string, WikidataEntity?>();
        async Task<WikidataEntity?> Entity(string qid)
        {
            if (!entities.TryGetValue(qid, out var entity))
            {
                entity = await wikidata.GetEntityAsync(qid, cancellationToken).ConfigureAwait(false);
                entities[qid] = entity;
            }

            return entity;
        }

        async Task<bool> PublisherMatches(EventInfo e, WikidataEntity entity)
        {
            if (e.Publisher is null || entity.PublisherQids.Count == 0)
            {
                return true;       // nothing to compare against
            }

            foreach (string qid in entity.PublisherQids)
            {
                if (!publisherLabels.TryGetValue(qid, out string? label))
                {
                    label = (await Entity(qid).ConfigureAwait(false))?.Label;
                    publisherLabels[qid] = label;
                }

                if (label is not null && string.Equals(StoryArcGroupingResolver.NormalizePublisher(label), e.Publisher, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        int matched = 0;
        foreach (var e in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? qid = null;
            if (e.ComicVineArcId is { Length: > 0 } cvId)
            {
                var byId = await wikidata.FindByComicVineIdAsync($"4045-{cvId}", cancellationToken).ConfigureAwait(false);
                if (byId.Count == 1)
                {
                    qid = byId[0];
                }
            }

            if (qid is null)
            {
                var accepted = new List<string>();
                foreach (var query in e.SearchNames)
                {
                    var hits = await wikidata.SearchEntitiesAsync(query, cancellationToken).ConfigureAwait(false);
                    foreach (var hit in hits.Take(MaxSearchResults))
                    {
                        if (accepted.Contains(hit.Qid) || !EventNameKeys.For(hit.Label, e.SeriesNames).Overlaps(e.Keys))
                        {
                            continue;
                        }

                        if (await Entity(hit.Qid).ConfigureAwait(false) is { } entity
                            && entity.InstanceOfQids.Any(AllowedTypes.Contains)
                            && await PublisherMatches(e, entity).ConfigureAwait(false))
                        {
                            accepted.Add(hit.Qid);
                        }
                    }

                    if (accepted.Count > 0)
                    {
                        break;
                    }
                }

                qid = accepted.Count == 1 ? accepted[0] : null;
            }

            e.MatchedQid = qid;
            if (qid is not null)
            {
                matched++;
            }
        }

        // Resolve links for the events checked this run: to events already matched (stored or just now), else by the target's label.
        var byQid = all.Where(e => (e.MatchedQid ?? e.StoredQid) is not null)
            .GroupBy(e => (e.MatchedQid ?? e.StoredQid)!)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        var links = new List<WikidataEventLink>();
        foreach (var e in due.Where(e => e.MatchedQid is not null))
        {
            var entity = await Entity(e.MatchedQid!).ConfigureAwait(false);
            if (entity is null)
            {
                continue;
            }

            foreach (var (other, isLater) in (entity.FollowedByQids ?? Array.Empty<string>()).Select(q => (q, true))
                         .Concat((entity.FollowsQids ?? Array.Empty<string>()).Select(q => (q, false))))
            {
                if (Placeholders.Contains(other) || await ResolveAsync(other).ConfigureAwait(false) is not { } target || target.Id == e.Id)
                {
                    continue;
                }

                links.Add(isLater
                    ? new WikidataEventLink(e.Id, target.Id, e.MatchedQid!)
                    : new WikidataEventLink(target.Id, e.Id, e.MatchedQid!));
            }
        }

        async Task<EventInfo?> ResolveAsync(string qid)
        {
            if (byQid.TryGetValue(qid, out var known))
            {
                return known;
            }

            if (await Entity(qid).ConfigureAwait(false) is not { } entity)
            {
                return null;
            }

            var candidates = all.Where(o => (o.MatchedQid ?? o.StoredQid) is null && EventNameKeys.For(entity.Label, o.SeriesNames).Overlaps(o.Keys)).ToList();
            if (candidates.Count != 1)
            {
                return null;
            }

            candidates[0].MatchedQid = qid;
            byQid[qid] = candidates[0];
            return candidates[0];
        }

        // Save the matches and stamp the checked events.
        using (var context = contextFactory())
        {
            var touched = due.Select(e => e.Id).Concat(all.Where(e => e.MatchedQid is not null).Select(e => e.Id)).Distinct().ToList();
            foreach (var storyEvent in context.StoryEvents.Where(x => touched.Contains(x.Id)).ToList())
            {
                var info = all.Single(e => e.Id == storyEvent.Id);
                if (info.MatchedQid is not null)
                {
                    storyEvent.WikidataQid = info.MatchedQid;
                }

                if (due.Contains(info))
                {
                    storyEvent.ChronologyCheckedAt = nowUtc;
                }
            }

            context.SaveChanges();
        }

        var distinctLinks = links.DistinctBy(l => (l.EarlierEventId, l.LaterEventId)).ToList();
        return new WikidataLinksResult(due.Count, matched, due.Select(e => e.Id).ToList(), distinctLinks);
    }

    private sealed class EventInfo
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? ComicVineArcId { get; init; }

        public string? StoredQid { get; init; }

        public DateTime? CheckedAt { get; init; }

        public string? Publisher { get; init; }

        public List<string> SeriesNames { get; init; } = new();

        public HashSet<string> Keys { get; init; } = new();

        public List<string> SearchNames { get; init; } = new();

        public string? MatchedQid { get; set; }
    }

    private static List<EventInfo> LoadEvents(PaperbunkrDbContext context)
    {
        var rows = context.StoryEvents.AsNoTracking()
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.ComicVineArcId,
                e.WikidataQid,
                e.ChronologyCheckedAt,
                AliasNames = e.Aliases.Select(a => a.Name).ToList(),
                AliasKeys = e.Aliases.Select(a => a.Key).ToList(),
                Series = e.Members.Select(m => m.Issue!.Series!.Name).ToList(),
                Publishers = e.Members.Select(m => m.Issue!.Publisher).ToList(),
            })
            .ToList();

        return rows.Select(r =>
        {
            var series = r.Series.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var publisher = r.Publishers.OfType<string>().Where(p => p.Length > 0)
                .GroupBy(p => StoryArcGroupingResolver.NormalizePublisher(p), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var names = new List<string> { r.Name };
            if (EventNameKeys.WithoutSeriesPrefix(r.Name, series) is { Length: > 0 } stripped)
            {
                names.Add(stripped);
            }

            names.AddRange(r.AliasNames);
            return new EventInfo
            {
                Id = r.Id,
                Name = r.Name,
                ComicVineArcId = string.IsNullOrWhiteSpace(r.ComicVineArcId) ? null : r.ComicVineArcId.Trim(),
                StoredQid = r.WikidataQid,
                CheckedAt = r.ChronologyCheckedAt,
                Publisher = publisher,
                SeriesNames = series,
                Keys = EventNameKeys.For(r.Name, series, r.AliasKeys),
                SearchNames = names.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList(),
            };
        }).ToList();
    }
}
