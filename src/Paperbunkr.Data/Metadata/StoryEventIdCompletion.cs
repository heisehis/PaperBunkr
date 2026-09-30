using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>What one id-completion run did for one event.</summary>
public enum IdCompletionStatus
{
    /// <summary>Checked within 30 days and its members haven't changed.</summary>
    NotDue,

    /// <summary>Ran to the end and was stamped.</summary>
    Checked,

    /// <summary>A network error; left unstamped so the next run retries it.</summary>
    Failed,
}

public sealed record IdCompletionOutcome(IdCompletionStatus Status, int IdsFilled, string? Conflict);

/// <summary>
/// Gives a story event both provider arc ids where it can (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §3).
/// Sources, strongest first: (1) the member issues' own arc credits, (2) Metron's <c>cv_id</c> for its arc, (3) a name search on the
/// provider still missing, confirmed by the arc's issue list. The strongest candidate fills an empty id; sources that disagree set
/// <see cref="StoryEvent.IdentityConflict"/> and never overwrite anything. Once both ids are known, duplicates are simply events that
/// share one - see <see cref="StoryEventIdentityResolver"/>.
/// </summary>
public static class StoryEventIdCompletion
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(30);

    /// <summary>Member issues sampled per provider for source 1.</summary>
    public const int IssueSampleSize = 5;

    /// <summary>Search results whose issue lists are fetched per query, at most (source 3).</summary>
    private const int MaxResultsChecked = 3;

    private const int RankIssueCredits = 1;
    private const int RankCvId = 2;
    private const int RankNameSearch = 3;

    private sealed record Candidate(string Id, int Rank, string? Name);

    /// <summary>Fingerprint of an event's member issue ids (order-free).</summary>
    public static string MemberKey(IEnumerable<int> issueIds)
    {
        string joined = string.Join(",", issueIds.Distinct().OrderBy(i => i));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    /// <summary>True when the event should be (re)checked at <paramref name="nowUtc"/>.</summary>
    public static bool IsDue(StoryEvent storyEvent, IEnumerable<int> memberIssueIds, DateTime nowUtc) =>
        storyEvent.IdentityCheckedAt is not DateTime checkedAt
        || nowUtc - checkedAt >= RecheckAfter
        || storyEvent.IdentityMemberKey != MemberKey(memberIssueIds);

    public static async Task<IdCompletionOutcome> CompleteAsync(
        PaperbunkrDbContext context,
        int storyEventId,
        IReadOnlyDictionary<ComicProvider, IArcIdentitySource> sources,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        bool force = false)
    {
        var storyEvent = context.StoryEvents
            .Include(e => e.Members).ThenInclude(m => m.Issue).ThenInclude(i => i!.Series)
            .Include(e => e.Members).ThenInclude(m => m.Issue).ThenInclude(i => i!.MetadataProposals)
            .Include(e => e.Aliases)
            .Single(e => e.Id == storyEventId);
        var issueIds = storyEvent.Members.Select(m => m.IssueId).ToList();
        if (!force && !IsDue(storyEvent, issueIds, nowUtc))
        {
            return new IdCompletionOutcome(IdCompletionStatus.NotDue, 0, storyEvent.IdentityConflict);
        }

        var series = storyEvent.Members.Select(m => m.Issue?.Series?.Name).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var keys = EventNameKeys.For(storyEvent.Name, series, storyEvent.Aliases.Select(a => a.Key));
        bool NameMatches(string name) => EventNameKeys.For(name, series).Overlaps(keys);

        var candidates = sources.Keys.ToDictionary(p => p, _ => new List<Candidate>());
        bool failed = false;

        // 1. The member issues' own arc credits.
        foreach (var (provider, source) in sources)
        {
            var providerIssueIds = context.ComicMetadataExternalIds
                .Where(x => x.EntityKind == ComicMetadataEntityKind.Issue && x.Provider == provider && issueIds.Contains(x.EntityId))
                .Select(x => x.ExternalId)
                .AsEnumerable()
                .Select(x => int.TryParse(x, out int id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .Take(IssueSampleSize)
                .ToList();

            int sampled = 0;
            var votes = new Dictionary<string, (int Count, string Name)>();
            foreach (int providerIssueId in providerIssueIds)
            {
                IReadOnlyList<ComicVineIdName> arcs;
                try
                {
                    arcs = await source.GetIssueArcsAsync(providerIssueId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    failed = true;
                    continue;
                }

                sampled++;
                foreach (var arc in arcs.Where(a => a.ExternalId is > 0 && NameMatches(a.Name)).DistinctBy(a => a.ExternalId))
                {
                    string id = arc.ExternalId!.Value.ToString();
                    votes[id] = (votes.TryGetValue(id, out var v) ? v.Count + 1 : 1, arc.Name);
                }
            }

            foreach (var (id, (count, name)) in votes.Where(v => v.Value.Count * 2 > sampled))
            {
                candidates[provider].Add(new Candidate(id, RankIssueCredits, name));
            }
        }

        // 2. Metron's cv_id for its arc.
        if (sources.TryGetValue(ComicProvider.Metron, out var metron) && sources.ContainsKey(ComicProvider.ComicVine)
            && (Blank(storyEvent.MetronArcId) ?? Best(candidates[ComicProvider.Metron])?.Id) is { } metronArcId)
        {
            try
            {
                if (await metron.GetComicVineIdForArcAsync(metronArcId, cancellationToken).ConfigureAwait(false) is { Length: > 0 } cvId)
                {
                    candidates[ComicProvider.ComicVine].Add(new Candidate(cvId, RankCvId, null));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed = true;
            }
        }

        // 3. Name search on a provider still missing an id, confirmed by the arc's issue list.
        foreach (var (provider, source) in sources)
        {
            if (StoredId(storyEvent, provider) is not null || candidates[provider].Count > 0)
            {
                continue;
            }

            var result = await SearchByNameAsync(context, storyEvent, series, NameMatches, provider, source, cancellationToken).ConfigureAwait(false);
            failed |= result.Failed;
            if (result.Found is { } found)
            {
                candidates[provider].Add(found);
            }
        }

        // Settle.
        int filled = 0;
        var conflicts = new List<string>();
        foreach (var provider in sources.Keys)
        {
            var list = candidates[provider];
            if (list.Count == 0)
            {
                continue;
            }

            int bestRank = list.Min(c => c.Rank);
            var top = list.Where(c => c.Rank == bestRank).DistinctBy(c => c.Id).ToList();
            string label = ComicProviderFactory.DisplayName(provider);
            if (top.Count > 1)
            {
                conflicts.Add($"{label} arc {top[0].Id} vs {top[1].Id}");
                continue;
            }

            var chosen = top[0];
            if (StoredId(storyEvent, provider) is { } stored)
            {
                if (stored != chosen.Id && chosen.Rank <= RankCvId)
                {
                    conflicts.Add($"{label} arc {stored} vs {chosen.Id}");
                }

                continue;
            }

            SetId(storyEvent, provider, chosen.Id);
            filled++;
            var named = list.FirstOrDefault(c => c.Id == chosen.Id && c.Name is not null);
            if (named?.Name is { } otherSpelling)
            {
                AddProviderAlias(storyEvent, otherSpelling);
            }
        }

        if (failed)
        {
            // Whatever was found is kept, but the event isn't stamped - the next run retries the sources that failed.
            context.SaveChanges();
            return new IdCompletionOutcome(IdCompletionStatus.Failed, filled, storyEvent.IdentityConflict);
        }

        storyEvent.IdentityConflict = conflicts.Count == 0 ? null : Truncate(string.Join("; ", conflicts), 200);
        storyEvent.IdentityCheckedAt = nowUtc;
        storyEvent.IdentityMemberKey = MemberKey(issueIds);
        context.SaveChanges();
        return new IdCompletionOutcome(IdCompletionStatus.Checked, filled, storyEvent.IdentityConflict);
    }

    private static async Task<(Candidate? Found, bool Failed)> SearchByNameAsync(
        PaperbunkrDbContext context,
        StoryEvent storyEvent,
        IReadOnlyList<string> series,
        Func<string, bool> nameMatches,
        ComicProvider provider,
        IArcIdentitySource source,
        CancellationToken cancellationToken)
    {
        var cacheSource = provider == ComicProvider.Metron ? ArcVerificationSource.Metron : ArcVerificationSource.ComicVine;
        var horizon = DateTime.UtcNow - RecheckAfter;
        var queries = new List<string> { storyEvent.Name };
        if (EventNameKeys.WithoutSeriesPrefix(storyEvent.Name, series) is { Length: > 0 } stripped)
        {
            queries.Add(stripped);
        }

        queries.AddRange(storyEvent.Aliases.Select(a => a.Name));
        var members = storyEvent.Members.Where(m => m.Issue is not null).Select(m => m.Issue!).ToList();
        bool failed = false;

        foreach (string query in queries.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
        {
            string trimmed = query.Trim();
            if (context.StoryEventVerificationNegativeCaches.Any(c =>
                    c.ArcName == trimmed && c.Publisher == string.Empty && c.Source == cacheSource && c.CheckedAt >= horizon))
            {
                continue;
            }

            IReadOnlyList<Paperbunkr.Data.ReadingLists.Sources.ArcSearchResult> results;
            try
            {
                results = await source.SearchArcsAsync(trimmed, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed = true;
                continue;
            }

            bool queryFailed = false;
            foreach (var result in results.Where(r => nameMatches(r.Name)).Take(MaxResultsChecked))
            {
                IReadOnlyList<Paperbunkr.Data.ReadingLists.Sources.ArcIssue> arcIssues;
                try
                {
                    arcIssues = await source.GetArcIssuesAsync(result.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    failed = queryFailed = true;
                    continue;
                }

                int matched = members.Count(issue => arcIssues.Any(a => SameIssue(issue, a)));
                if (members.Count > 0 && matched >= Math.Max(1, (members.Count + 1) / 2))
                {
                    return (new Candidate(result.Id, RankNameSearch, result.Name), failed);
                }
            }

            if (!queryFailed)
            {
                RecordMiss(context, trimmed, cacheSource);
            }
        }

        return (null, failed);
    }

    /// <summary>Series name (volume/year-insensitive) and issue number - the arc lists carry no provider issue ids.</summary>
    private static bool SameIssue(Issue issue, Paperbunkr.Data.ReadingLists.Sources.ArcIssue arcIssue) =>
        string.Equals((issue.EffectiveNumber() ?? string.Empty).Trim(), arcIssue.Number.Trim(), StringComparison.OrdinalIgnoreCase)
        && issue.Series is { } series
        && EventNameKeys.For(series.Name, Array.Empty<string>()).Overlaps(EventNameKeys.For(arcIssue.Series, Array.Empty<string>()));

    private static void RecordMiss(PaperbunkrDbContext context, string query, ArcVerificationSource source)
    {
        var existing = context.StoryEventVerificationNegativeCaches.FirstOrDefault(c =>
            c.ArcName == query && c.Publisher == string.Empty && c.Source == source);
        if (existing is null)
        {
            context.StoryEventVerificationNegativeCaches.Add(new StoryEventVerificationNegativeCache { ArcName = query, Publisher = string.Empty, Source = source });
        }
        else
        {
            existing.CheckedAt = DateTime.UtcNow;
        }

        context.SaveChanges();
    }

    private static void AddProviderAlias(StoryEvent storyEvent, string name)
    {
        string key = EventNameKeys.Key(name);
        if (key.Length == 0 || key == EventNameKeys.Key(storyEvent.Name) || storyEvent.Aliases.Any(a => a.Key == key))
        {
            return;
        }

        storyEvent.Aliases.Add(new StoryEventAlias { Name = name.Trim(), Key = key, Source = StoryEventAliasSource.Provider });
    }

    private static Candidate? Best(List<Candidate> list) => list.OrderBy(c => c.Rank).FirstOrDefault();

    private static string? StoredId(StoryEvent e, ComicProvider provider) =>
        Blank(provider == ComicProvider.Metron ? e.MetronArcId : e.ComicVineArcId);

    private static void SetId(StoryEvent e, ComicProvider provider, string id)
    {
        if (provider == ComicProvider.Metron)
        {
            e.MetronArcId = id;
        }
        else
        {
            e.ComicVineArcId = id;
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
