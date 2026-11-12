using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Metadata.EventEnrichment;

/// <summary>Reads existing identities and evidence. Does not import issues or change memberships.</summary>
public sealed class EventEvidenceCache
{
    internal Dictionary<(string, string), ArcOverviewInfo?> Overviews { get; } = new();
    internal Dictionary<(string, string), ArcEntryList> Entries { get; } = new();

    /// <summary>The issue list fetched for a provider arc in this operation, if any (the continuity map saves it, M4 §1).</summary>
    public bool TryGetEntries(string provider, string arcId, out ArcEntryList list)
    {
        if (Entries.TryGetValue((provider, arcId), out var found))
        {
            list = found;
            return true;
        }

        list = null!;
        return false;
    }
}

public sealed class EventEvidenceCollector(Func<string, IReadingListSource?> sources, GcdDataStore? gcd = null, EventEvidenceCache? cache = null)
{
    private readonly Dictionary<(string, string), ArcOverviewInfo?> _overviews = (cache ?? new()).Overviews;
    private readonly Dictionary<(string, string), ArcEntryList> _entries = (cache ?? new()).Entries;

    public async Task<EventEnrichmentProposal> CollectAsync(PaperbunkrDbContext db, int eventId, CancellationToken ct)
    {
        var e = EventMetadataStore.Query(db).Single(x => x.Id == eventId);
        var snapshot = EventMetadataStore.Capture(db, e);
        var evidence = new List<EventEvidence>(); var statuses = new List<EventSourceStatus>();
        var candidates = new List<(string Provider, string? Id)> { ("Metron", e.MetronArcId) };
        if (e.IdentityConflict is null) candidates.Add(("ComicVine", e.ComicVineArcId));
        else statuses.Add(new("Identity", "Needs comparison", e.IdentityConflict));
        for (int index = 0; index < candidates.Count; index++)
        {
            var (provider, id) = candidates[index];
            if (string.IsNullOrWhiteSpace(id)) continue;
            var source = sources(provider);
            if (source is null) { statuses.Add(new(provider, "Unavailable", "Connection is not configured.")); continue; }
            string link = provider == "Metron" ? $"https://metron.cloud/api/arc/{Uri.EscapeDataString(id)}/" : $"https://comicvine.gamespot.com/story-arc/4045-{Uri.EscapeDataString(id)}/";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(35));
            try
            {
                if (!_overviews.TryGetValue((provider, id), out var info))
                    _overviews[(provider, id)] = info = await source.GetArcOverviewAsync(id, timeout.Token).ConfigureAwait(false);
                if (info is not null)
                {
                    AddOverview(evidence, info, provider, id, link);
                    if (provider == "Metron" && info.ComicVineId is { Length: > 0 } mapped && e.ComicVineArcId is { Length: > 0 } stored && mapped != stored)
                    {
                        candidates.RemoveAll(c => c.Provider == "ComicVine");
                        statuses.Add(new("Identity", "Needs comparison", "Metron's ComicVine arc ID disagrees with the stored link; ComicVine evidence was excluded."));
                    }
                    if (provider == "Metron" && e.IdentityConflict is null && e.ComicVineArcId is null && info.ComicVineId is { Length: > 0 } cv)
                        candidates.Add(("ComicVine", cv));
                }
                statuses.Add(new(provider, info is null ? "No data" : "Checked", "Verified arc overview."));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { statuses.Add(new(provider, "Failed", SafeMessage(ex))); }
            try
            {
                if (!_entries.TryGetValue((provider, id), out var entries))
                    _entries[(provider, id)] = entries = await source.GetArcEntriesAsync(id, timeout.Token).ConfigureAwait(false);
                bool complete = entries.SkippedLines.Count == 0 && (entries.ReportedCount is null || entries.ReportedCount == entries.Issues.Count);
                string coverage = $"{entries.Issues.Count}/{entries.ReportedCount?.ToString() ?? "unknown"} source entries; original editions not independently verified";
                var dates = entries.Issues.Select(i => EventPublicationDate.Parse(i.StoreDate, provider + " on-sale")
                    ?? EventPublicationDate.Parse(i.CoverDate, provider + " cover date")).ToArray();
                // Provider arc lists can include editions/reprints. Keep unverified spans for explicit comparison.
                AddDates(evidence, dates, complete, coverage, provider, id, link, automatic: false);
                AddIssueCovers(db, e, evidence, entries, complete, provider, id, link);
                if (!complete) statuses.Add(new(provider, "Incomplete", coverage));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { statuses.Add(new(provider, "Failed", "Issue dates: " + SafeMessage(ex))); }
        }
        AddGcd(db, e, evidence, statuses);
        foreach (var retained in db.EventMetadataChanges.AsNoTracking().Where(c => c.StoryEventId == eventId && c.MergeInvalidated && c.AfterValue != null).OrderByDescending(c => c.Id).Take(100))
            evidence.Add(new(retained.Field, retained.AfterValue!, "Merged event", retained.OperationId.ToString(), "Alternative retained from a merged event", Automatic: false));
        return new(snapshot, evidence, statuses);
    }

    private void AddGcd(PaperbunkrDbContext db, StoryEvent e, List<EventEvidence> evidence, List<EventSourceStatus> statuses)
    {
        if (gcd is null) { statuses.Add(new("GCD", "Unavailable", "Optional data is not installed.")); return; }
        var issueIds = db.EventMemberships.Where(m => m.StoryEventId == e.Id && m.Issue!.GcdIssueId != null)
            .Select(m => m.Issue!.GcdIssueId!.Value).Distinct().ToArray();
        if (!gcd.HasEventData)
        {
            AddDates(evidence, gcd.LegacyEventDates(issueIds).ToArray(), false,
                "Schema 1 local subset; original editions and uncertainty flags not retained", "GCD", "members", "https://www.comics.org/", false);
            statuses.Add(new("GCD", "Basic data", "Installed schema 1 has issue dates but no arc evidence; update the optional data.")); return;
        }
        var arcs = gcd.EventArcs(issueIds, e.GcdArcId);
        var matches = e.GcdArcId.HasValue ? arcs : arcs.Where(a => string.Equals(a.Name.Trim(), e.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            && gcd.EventIssues([], a.Id).Select(i => i.Id).ToHashSet().IsSupersetOf(issueIds)).ToList();
        GcdArcInfo? arc = matches.Count == 1 ? matches[0] : null;
        if (matches.Count > 1) statuses.Add(new("GCD", "Needs comparison", "Multiple arcs match verified members; no arc selected."));
        if (arc is null)
        {
            foreach (var alternative in arcs.Take(20))
            {
                var alternatives = new List<EventEvidence>();
                AddOverview(alternatives, new(alternative.Description, null, Name: alternative.Name), "GCD", alternative.Id.ToString(), $"https://www.comics.org/story_arc/{alternative.Id}/");
                evidence.AddRange(alternatives.Select(c => c with { Automatic = false, Reason = "Arc candidate from member overlap; review identity before choosing a value" }));
            }
        }
        if (arc is not null)
        {
            string id = arc.Id.ToString(), link = $"https://www.comics.org/story_arc/{arc.Id}/";
            AddOverview(evidence, new(arc.Description, null, Name: arc.Name), "GCD", id, link);
            if (arc.FirstYear is int year && year is > 0 and <= 9999)
                evidence.Add(new(EventMetadataField.StartDate, JsonSerializer.Serialize(new EventPublicationDate(year, Uncertain: arc.YearUncertain, Provisional: true, Basis: "GCD arc year")), "GCD", id, "Arc first-publication year", link, Quality: 0));
        }
        var entries = gcd.EventIssues(issueIds, arc?.Id);
        var originals = entries.Where(i => !i.Collected && !i.Reprint).ToArray();
        bool complete = arc is not null && entries.Count > 0;
        string coverage = $"{originals.Length} verified original issues; {originals.Count(i => i.Date is not null)} dated; "
            + (complete ? "GCD arc membership snapshot" : "local member subset, full span not established");
        AddDates(evidence, originals.Select(i => i.Date).ToArray(), complete, coverage, "GCD", arc?.Id.ToString() ?? "members", "https://www.comics.org/", automatic: true);
        if (complete && originals.Length > 0 && originals.Select(i => i.SeriesId).Distinct().Count() == 1
            && string.Equals(originals[0].Series, e.Name, StringComparison.OrdinalIgnoreCase))
        {
            var type = originals[0].Format?.Trim().ToLowerInvariant() switch
            { "limited series" => StoryEventType.LimitedSeries, "one-shot" when originals.Length == 1 => StoryEventType.OneShotOrSpecial, _ => (StoryEventType?)null };
            if (type is not null)
                evidence.Add(new(EventMetadataField.Types, JsonSerializer.Serialize(new[] { StoryEventType.StoryArc, type.Value }.Order().ToArray()), "GCD", arc!.Id.ToString(), "Whole arc matches an explicitly classified original series", Quality: 2));
        }
        statuses.Add(new("GCD", originals.Length == 0 && arc is null ? "No data" : "Checked", $"Snapshot {gcd.DumpDate}: {coverage}"));
    }

    private static void AddOverview(List<EventEvidence> output, ArcOverviewInfo info, string provider, string id, string link)
    {
        if (!string.IsNullOrWhiteSpace(info.Name)) output.Add(new(EventMetadataField.Name, info.Name.Trim(), provider, id, "Verified arc name", link));
        foreach (var (text, label) in new[] { (info.Deck, "Short synopsis"), (info.Description, "Arc description") })
        {
            var plain = EventDescriptionText.Plain(text); if (plain.Length == 0) continue;
            var overview = EventDescriptionText.Overview(text);
            output.Add(new(EventMetadataField.Description, overview ?? plain, provider, id,
                overview is null ? "Bibliographic/placeholder text; review before using" : label, link, overview is not null, overview is null ? 0 : 1));
        }
        if (!string.IsNullOrWhiteSpace(info.CoverImageUrl))
            output.Add(new(EventMetadataField.Cover, JsonSerializer.Serialize(new EventCoverValue(info.CoverImageUrl,
                provider == "Metron" ? StoryEventCoverSource.Metron : StoryEventCoverSource.ComicVine)), provider, id, "Dedicated arc artwork", link, Quality: 2));
        output.Add(new(EventMetadataField.Types, JsonSerializer.Serialize(new[] { StoryEventType.StoryArc }), provider, id, "Verified story-arc record", link));
    }

    private void AddIssueCovers(PaperbunkrDbContext db, StoryEvent storyEvent, List<EventEvidence> output,
        ArcEntryList entries, bool complete, string provider, string arcId, string link)
    {
        var mapped = new Dictionary<string, GcdEventIssue>();
        if (gcd?.HasEventData == true)
        {
            var members = db.EventMemberships.Where(m => m.StoryEventId == storyEvent.Id && m.Issue!.GcdIssueId != null)
                .Select(m => new { m.IssueId, GcdId = m.Issue!.GcdIssueId!.Value }).ToList();
            var local = members.ToDictionary(m => m.IssueId, m => m.GcdId);
            var originals = gcd.EventIssues(local.Values).ToDictionary(i => i.Id);
            var kind = provider == "Metron" ? ComicProvider.Metron : ComicProvider.ComicVine;
            foreach (var external in db.ComicMetadataExternalIds.Where(x => x.EntityKind == ComicMetadataEntityKind.Issue && x.Provider == kind && local.Keys.Contains(x.EntityId)))
                if (originals.TryGetValue(local[external.EntityId], out var issue)) mapped[external.ExternalId] = issue;
        }
        string? Id(ArcIssue issue) => provider == "Metron" ? issue.MetronIssueId : issue.ComicVineIssueId;
        bool verified = complete && entries.Issues.Count > 0 && entries.Issues.All(i => Id(i) is { } id && mapped.ContainsKey(id));
        var originalEntries = entries.Issues.Where(i => Id(i) is { } id && mapped.TryGetValue(id, out var m) && !m.Collected && !m.Reprint).DistinctBy(Id).ToArray();
        if (verified)
            AddDates(output, originalEntries.Select(i => mapped[Id(i)!].Date).ToArray(), true,
                $"{originalEntries.Length} original issues verified by GCD; complete provider arc list", "GCD", $"{provider}:{arcId}", link, true);
        ArcIssue? first = null;
        if (verified && originalEntries.All(i => mapped[Id(i)!].Date is not null))
            first = originalEntries.SingleOrDefault(i => originalEntries.Where(other => other != i)
                .All(other => mapped[Id(i)!].Date!.Upper < mapped[Id(other)!].Date!.Lower));
        foreach (var issue in entries.Issues.Where(i => !string.IsNullOrWhiteSpace(i.CoverImageUrl)).Take(8))
        {
            bool automatic = issue == first;
            output.Add(new(EventMetadataField.Cover, JsonSerializer.Serialize(new EventCoverValue(issue.CoverImageUrl,
                provider == "Metron" ? StoryEventCoverSource.Metron : StoryEventCoverSource.ComicVine)), provider, arcId,
                $"Issue cover: {issue.Series} #{issue.Number}" + (automatic ? " · verified first original issue" : " · first original issue not established"),
                link, automatic, 0, IssueCover: true));
        }
    }

    private static void AddDates(List<EventEvidence> output, EventPublicationDate?[] dates, bool complete, string coverage,
        string provider, string id, string link, bool automatic)
    {
        foreach (bool end in new[] { false, true })
            if (EventPublicationDate.Bound(dates, end, complete, coverage) is { } bound)
                output.Add(new(end ? EventMetadataField.EndDate : EventMetadataField.StartDate, JsonSerializer.Serialize(bound),
                    provider, id, "Derived publication range: " + coverage, link, automatic, complete ? 2 : 1));
    }
    private static string SafeMessage(Exception exception) => exception switch
    {
        OperationCanceledException => "Source timed out; other sources can still contribute.",
        ReadingListSourceException => "Source request failed; check connection or quota and retry.",
        _ => "Source was unavailable; retry later.",
    };
}
