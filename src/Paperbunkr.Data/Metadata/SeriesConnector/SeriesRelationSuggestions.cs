using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// The weak signals (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.3): worked out for one series when its Related
/// tab opens, never saved unless you accept one. At most <see cref="DefaultLimit"/>, strongest first:
/// <list type="bullet">
/// <item>S1 shared lead - a character in at least half the issues of both series.</item>
/// <item>S2 same writer - credited on at least three issues of each, in overlapping years.</item>
/// <item>S3 spin-off - a series named after a character who was already in the other series before it started.</item>
/// <item>S4 similar title - one title contains the other.</item>
/// </list>
/// A pair that already has any relation, a pair you dismissed, and comics ↔ manga are left out; S1, S2 and S4 need the same publisher.
/// </summary>
public static class SeriesRelationSuggestions
{
    public const int DefaultLimit = 8;
    public const decimal LeadConfidenceFloor = 0.6m;
    public const decimal WriterConfidence = 0.55m;
    public const decimal SpinOffConfidence = 0.65m;
    public const decimal TitleConfidence = 0.5m;

    /// <summary>A character in at least this share of a series' issues is one of its leads.</summary>
    public const double LeadShare = 0.5;

    public const int MinWriterIssues = 3;

    /// <summary>A lead is someone across a run: a one-shot has a "lead" in every issue, which says nothing (seen on the real library, 2026-10-29).</summary>
    public const int MinLeadIssues = 3;

    /// <summary>How long the library-wide reads behind suggestions are reused (a page open re-reading ~8k issues cost ~2 s, 2026-10-29).</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private sealed record LibraryStats(SeriesConnectorSnapshot Snapshot, List<AppearanceStat> Appearances, List<WriterStat> Writers);

    private static readonly object CacheLock = new();
    private static (string Key, DateTime LoadedUtc, LibraryStats Stats)? _cache;

    /// <summary>Drops the cached library reads; the sweep calls it after it saves, scans and scrapes being what change names, years and characters.</summary>
    public static void Invalidate()
    {
        lock (CacheLock)
        {
            _cache = null;
        }
    }

    private static LibraryStats Stats(PaperbunkrDbContext context)
    {
        string key = context.Database.GetConnectionString() ?? string.Empty;
        lock (CacheLock)
        {
            if (_cache is { } cached && cached.Key == key && DateTime.UtcNow - cached.LoadedUtc < CacheFor)
            {
                return cached.Stats;
            }
        }

        var stats = new LibraryStats(SeriesConnectorSnapshot.Load(context, includeEvents: false), AppearanceStats(context), WriterStats(context));
        lock (CacheLock)
        {
            _cache = (key, DateTime.UtcNow, stats);
        }

        return stats;
    }

    public static IReadOnlyList<InferredSeriesRelation> For(PaperbunkrDbContext context, int seriesId, int limit = DefaultLimit)
    {
        var stats = Stats(context);
        var snapshot = stats.Snapshot;
        if (!snapshot.Series.TryGetValue(seriesId, out var self))
        {
            return [];
        }

        // Read fresh every time, never from the cache: what you related, dismissed or accepted a moment ago must count at once.
        var related = context.MediaRelations.AsNoTracking()
            .Where(m => m.SourceSeriesId != null && m.TargetSeriesId != null && (m.SourceSeriesId == seriesId || m.TargetSeriesId == seriesId))
            .Select(m => m.SourceSeriesId == seriesId ? m.TargetSeriesId!.Value : m.SourceSeriesId!.Value)
            .ToHashSet();
        var dismissed = context.SeriesRelationDismissals.AsNoTracking()
            .Where(d => d.LowerSeriesId == seriesId || d.HigherSeriesId == seriesId)
            .Select(d => d.LowerSeriesId == seriesId ? d.HigherSeriesId : d.LowerSeriesId)
            .ToHashSet();

        bool Eligible(ConnectorSeries other, bool needSamePublisher) =>
            other.Id != seriesId
            && !related.Contains(other.Id)
            && !dismissed.Contains(other.Id)
            && SeriesConnectorGuards.SameContentFamily(self, other)
            && (!needSamePublisher || SeriesConnectorGuards.SamePublisher(self, other));

        var candidates = new List<InferredSeriesRelation>();
        candidates.AddRange(SharedLeads(context, snapshot, self, stats.Appearances, s => Eligible(s, needSamePublisher: true)));
        candidates.AddRange(SameWriter(context, snapshot, self, stats.Writers, s => Eligible(s, needSamePublisher: true)));
        candidates.AddRange(SpinOffs(context, snapshot, self, stats.Appearances, s => Eligible(s, needSamePublisher: false)));
        candidates.AddRange(SimilarTitles(snapshot, self, s => Eligible(s, needSamePublisher: true)));

        // One suggestion per other series: its strongest.
        return candidates
            .GroupBy(c => c.SourceSeriesId == seriesId ? c.TargetSeriesId : c.SourceSeriesId)
            .Select(g => g.OrderByDescending(c => c.Confidence).First())
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => snapshot.Series[c.SourceSeriesId == seriesId ? c.TargetSeriesId : c.SourceSeriesId].IssueCount)
            .ThenBy(c => snapshot.Series[c.SourceSeriesId == seriesId ? c.TargetSeriesId : c.SourceSeriesId].Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>Per character and series: how many issues the character is in, and the earliest year (null when no issue has one).</summary>
    private sealed record AppearanceStat(int CharacterId, int SeriesId, int Count, int? First);

    private static List<AppearanceStat> AppearanceStats(PaperbunkrDbContext context) =>
        context.CharacterAppearances.AsNoTracking()
            .GroupBy(a => new { a.CharacterId, a.Issue!.SeriesId })
            .Select(g => new { g.Key.CharacterId, g.Key.SeriesId, Count = g.Select(a => a.IssueId).Distinct().Count(), First = g.Min(a => a.Issue!.Year) })
            .AsEnumerable()
            .Select(x => new AppearanceStat(x.CharacterId, x.SeriesId, x.Count, x.First))
            .ToList();

    // ----- S1 -----

    private static IEnumerable<InferredSeriesRelation> SharedLeads(
        PaperbunkrDbContext context, SeriesConnectorSnapshot snapshot, ConnectorSeries self, List<AppearanceStat> counts, Func<ConnectorSeries, bool> eligible)
    {

        double Share(int series, int count) => snapshot.Series.TryGetValue(series, out var s) && s.IssueCount >= MinLeadIssues ? (double)count / s.IssueCount : 0;

        if (self.IssueCount < MinLeadIssues)
        {
            yield break;
        }

        var myLeads = counts.Where(c => c.SeriesId == self.Id)
            .Select(c => (c.CharacterId, Share: Share(c.SeriesId, c.Count)))
            .Where(c => c.Share >= LeadShare)
            .ToDictionary(c => c.CharacterId, c => c.Share);
        if (myLeads.Count == 0)
        {
            yield break;
        }

        // How many series each of my leads leads: the rarest shared lead is the one worth naming ("Lois Lane" says more than "Superman" only
        // if fewer series star her).
        var leadsSeries = counts.Where(c => myLeads.ContainsKey(c.CharacterId) && Share(c.SeriesId, c.Count) >= LeadShare)
            .GroupBy(c => c.CharacterId)
            .ToDictionary(g => g.Key, g => g.Count());
        var names = context.Characters.AsNoTracking().Where(c => myLeads.Keys.Contains(c.Id)).ToDictionary(c => c.Id, c => c.Name);
        foreach (var group in counts.Where(c => c.SeriesId != self.Id && myLeads.ContainsKey(c.CharacterId)).GroupBy(c => c.SeriesId))
        {
            if (!snapshot.Series.TryGetValue(group.Key, out var other) || !eligible(other))
            {
                continue;
            }

            var shared = group
                .Select(c => (c.CharacterId, Share: Math.Min(myLeads[c.CharacterId], Share(c.SeriesId, c.Count))))
                .Where(c => c.Share >= LeadShare)
                .ToList();
            if (shared.Count == 0)
            {
                continue;
            }

            // 0.6 at a 50 % share rising to 0.68 when the lead is in every issue of both, plus 0.01 for each further shared lead (capped at 0.7).
            double best = shared.Max(c => c.Share);
            decimal confidence = Math.Min(0.7m,
                LeadConfidenceFloor + (decimal)Math.Round((best - LeadShare) / (1 - LeadShare) * 0.08, 2) + 0.01m * (shared.Count - 1));
            var named = shared.OrderBy(c => leadsSeries.GetValueOrDefault(c.CharacterId, int.MaxValue)).ThenByDescending(c => c.Share).First();
            string reason = shared.Count == 1
                ? $"Shares {names.GetValueOrDefault(named.CharacterId, "a lead")} as lead"
                : $"Shares {names.GetValueOrDefault(named.CharacterId, "a lead")} and {shared.Count - 1} more as leads";
            yield return new InferredSeriesRelation(self.Id, other.Id, RelationType.SharedCharacters, confidence, reason);
        }
    }

    // ----- S2 -----

    private sealed record WriterStat(int SeriesId, int CreatorId, int Count);

    private static List<WriterStat> WriterStats(PaperbunkrDbContext context) =>
        context.CreatorCredits.AsNoTracking()
            .Where(c => c.Role != null && EF.Functions.Like(c.Role, "%writ%"))
            .GroupBy(c => new { c.Issue!.SeriesId, c.CreatorId })
            .Select(g => new { g.Key.SeriesId, g.Key.CreatorId, Count = g.Select(c => c.IssueId).Distinct().Count() })
            .Where(g => g.Count >= MinWriterIssues)
            .AsEnumerable()
            .Select(g => new WriterStat(g.SeriesId, g.CreatorId, g.Count))
            .ToList();

    private static IEnumerable<InferredSeriesRelation> SameWriter(
        PaperbunkrDbContext context, SeriesConnectorSnapshot snapshot, ConnectorSeries self, List<WriterStat> writerCredits, Func<ConnectorSeries, bool> eligible)
    {
        var mine = writerCredits.Where(c => c.SeriesId == self.Id).Select(c => c.CreatorId).ToHashSet();
        if (mine.Count == 0)
        {
            yield break;
        }

        var names = context.Creators.AsNoTracking().Where(c => mine.Contains(c.Id)).ToDictionary(c => c.Id, c => c.Name);
        foreach (var group in writerCredits.Where(c => c.SeriesId != self.Id && mine.Contains(c.CreatorId)).GroupBy(c => c.SeriesId))
        {
            if (!snapshot.Series.TryGetValue(group.Key, out var other) || !eligible(other) || SeriesConnectorGuards.OverlapYears(self, other) == 0)
            {
                continue;
            }

            var writer = group.OrderByDescending(c => c.Count).First();
            yield return new InferredSeriesRelation(self.Id, other.Id, RelationType.Similar, WriterConfidence,
                $"Same writer · {names.GetValueOrDefault(writer.CreatorId, "unknown")}");
        }
    }

    // ----- S3 -----

    /// <summary>
    /// A spin-off is a character's first title of their own, and it comes from where the character first appeared: for each character the
    /// library has, the earliest series named after them (the spin-off) and the series holding their earliest appearance (the origin),
    /// when those differ and the origin came first. Checked against the real library on 2026-10-29: the looser "named after anyone who
    /// was in an earlier series" called Superman Unlimited a spin-off of Justice League.
    /// </summary>
    private static IEnumerable<InferredSeriesRelation> SpinOffs(
        PaperbunkrDbContext context, SeriesConnectorSnapshot snapshot, ConnectorSeries self, List<AppearanceStat> stats, Func<ConnectorSeries, bool> eligible)
    {
        var appearances = stats.Where(a => a.First is not null).ToList();

        var byCharacter = appearances.GroupBy(a => a.CharacterId).ToDictionary(g => g.Key, g => g.ToList());

        // A character's own titles - not team-ups or companions, which name several people or lean on another series. Bare lower-case
        // names once, so each character is a cheap substring test before the word-boundary check.
        var ownTitles = snapshot.Series.Values
            .Where(s => s.StartYear is not null && SlashTitleInference.Parts(s.Name).Count == 0 && CompanionSuffixInference.Parse(s.Name) is null)
            .Select(s => (Series: s, Bare: EventSpineResolver.StripYear(s.Name).ToLowerInvariant()))
            .OrderBy(x => x.Series.StartYear).ThenBy(x => x.Series.Id)
            .ToList();
        string selfBare = EventSpineResolver.StripYear(self.Name).ToLowerInvariant();

        // Only characters who appear in this series or whose name this series carries can involve it.
        var mine = appearances.Where(a => a.SeriesId == self.Id).Select(a => a.CharacterId).ToHashSet();
        var characterNames = context.Characters.AsNoTracking()
            .Where(c => c.Name.Length >= 4)
            .Select(c => new { c.Id, c.Name })
            .AsEnumerable()
            .Where(c => mine.Contains(c.Id) || Names(selfBare, c.Name))
            .ToList();

        foreach (var character in characterNames)
        {
            var spinOff = ownTitles.FirstOrDefault(x => Names(x.Bare, character.Name)).Series;
            var first = (byCharacter.GetValueOrDefault(character.Id) ?? new())
                .OrderBy(a => a.First).ThenBy(a => snapshot.Series.TryGetValue(a.SeriesId, out var s) ? s.StartYear ?? int.MaxValue : int.MaxValue)
                .FirstOrDefault();
            if (spinOff is null || first is null || !snapshot.Series.TryGetValue(first.SeriesId, out var origin)
                || origin.Id == spinOff.Id || Names(EventSpineResolver.StripYear(origin.Name).ToLowerInvariant(), character.Name) || first.First >= spinOff.StartYear
                || origin.TitleKey == spinOff.TitleKey
                || (self.Id != origin.Id && self.Id != spinOff.Id))
            {
                continue;
            }

            var other = self.Id == origin.Id ? spinOff : origin;
            if (eligible(other))
            {
                yield return new InferredSeriesRelation(spinOff.Id, origin.Id, RelationType.SpinOff, SpinOffConfidence, $"Spin-off · {character.Name}");
            }
        }
    }

    /// <summary>Whether a bare lower-case title names <paramref name="character"/> as a whole word.</summary>
    private static bool Names(string bareLowerTitle, string character)
    {
        string name = character.ToLowerInvariant();
        int at = bareLowerTitle.IndexOf(name, StringComparison.Ordinal);
        while (at >= 0)
        {
            int end = at + name.Length;
            bool startOk = at == 0 || !char.IsLetterOrDigit(bareLowerTitle[at - 1]);
            bool endOk = end == bareLowerTitle.Length || !char.IsLetterOrDigit(bareLowerTitle[end]);
            if (startOk && endOk)
            {
                return true;
            }

            at = bareLowerTitle.IndexOf(name, at + 1, StringComparison.Ordinal);
        }

        return false;
    }

    // ----- S4 -----

    private static IEnumerable<InferredSeriesRelation> SimilarTitles(SeriesConnectorSnapshot snapshot, ConnectorSeries self, Func<ConnectorSeries, bool> eligible)
    {
        const int MinKey = 4;
        foreach (var other in snapshot.Series.Values)
        {
            if (other.TitleKey == self.TitleKey || !eligible(other))
            {
                continue;
            }

            var (shorter, longer) = self.TitleKey.Length <= other.TitleKey.Length ? (self, other) : (other, self);
            if (shorter.TitleKey.Length < MinKey || !longer.TitleKey.StartsWith(shorter.TitleKey, StringComparison.Ordinal) && !longer.TitleKey.Contains(shorter.TitleKey, StringComparison.Ordinal))
            {
                continue;
            }

            // The strong rules already cover companions and team-ups.
            if (CompanionSuffixInference.Parse(longer.Name) is not null || SlashTitleInference.Parts(longer.Name).Count > 0)
            {
                continue;
            }

            yield return new InferredSeriesRelation(longer.Id, shorter.Id, RelationType.Related, TitleConfidence, "Similar title");
        }
    }
}
