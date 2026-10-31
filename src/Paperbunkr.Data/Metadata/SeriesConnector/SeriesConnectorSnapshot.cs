using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>One series as the connector's rules see it. <see cref="TitleKey"/> is the name with year, volume marker and punctuation folded away.</summary>
public sealed record ConnectorSeries(
    int Id,
    string Name,
    string TitleKey,
    string? PublisherKey,
    ContentType ContentType,
    int? StartYear,
    int? EndYear,
    int IssueCount);

/// <summary>One member issue of a story event, in reading order.</summary>
public sealed record ConnectorEventMember(int SeriesId, string SeriesName, string? Number, float? NumberKey);

public sealed record ConnectorEvent(int Id, string Name, int? SavedSpineSeriesId, DateTime? StartDate, IReadOnlyList<ConnectorEventMember> Members);

/// <summary>
/// A read-only picture of the library for the series connector (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §5.1):
/// loaded once with no tracking, so every inference rule is a pure function over it and can be tested by building one directly.
/// </summary>
public sealed class SeriesConnectorSnapshot
{
    public SeriesConnectorSnapshot(
        IEnumerable<ConnectorSeries> series,
        IEnumerable<ConnectorEvent>? events = null,
        IEnumerable<(int, int)>? userPairs = null,
        IEnumerable<(int, int)>? dismissedPairs = null)
    {
        Series = series.ToDictionary(s => s.Id);
        Events = events?.ToList() ?? new List<ConnectorEvent>();
        UserPairs = (userPairs ?? Array.Empty<(int, int)>()).Select(p => SeriesConnectorGuards.Pair(p.Item1, p.Item2)).ToHashSet();
        DismissedPairs = (dismissedPairs ?? Array.Empty<(int, int)>()).Select(p => SeriesConnectorGuards.Pair(p.Item1, p.Item2)).ToHashSet();
    }

    public IReadOnlyDictionary<int, ConnectorSeries> Series { get; }

    public IReadOnlyList<ConnectorEvent> Events { get; }

    /// <summary>Pairs with any relation you made yourself (<see cref="RelationEvidenceProvider.User"/> evidence), in (low, high) order.</summary>
    public IReadOnlySet<(int Low, int High)> UserPairs { get; }

    /// <summary>Pairs in <see cref="SeriesRelationDismissal"/>, in (low, high) order.</summary>
    public IReadOnlySet<(int Low, int High)> DismissedPairs { get; }

    /// <summary>"Superman (2018)", "Superman Vol. 5", "SUPERMAN" → "superman".</summary>
    public static string TitleKey(string name) =>
        TitleNormalizer.StripDown(TitleNormalizer.StripVolume(EventSpineResolver.StripYear(name))).ToLowerInvariant();

    /// <summary>The "(2018)" disambiguation year a series name carries, or null.</summary>
    public static int? YearFromName(string name)
    {
        var match = NameYear.Match(name);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>A publisher compared case- and punctuation-insensitively, "DC Comics" and "DC" alike.</summary>
    public static string? PublisherKeyFor(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return null;
        }

        string key = TitleNormalizer.StripDown(PublisherSuffix.Replace(publisher.Trim(), string.Empty)).ToLowerInvariant();
        return key.Length == 0 ? null : key;
    }

    private static readonly Regex NameYear = new(@"\(((?:19|20)\d{2})\)\s*$", RegexOptions.Compiled);
    private static readonly Regex PublisherSuffix = new(@"\s+(comics|publishing|publications|entertainment|press|inc\.?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="includeEvents">False skips story events (the suggestions engine doesn't need them).</param>
    public static SeriesConnectorSnapshot Load(PaperbunkrDbContext context, bool includeEvents = true)
    {
        var acceptedNumbers = AcceptedIssueValues(context, MetadataProposalField.Number);
        var acceptedYears = AcceptedIssueValues(context, MetadataProposalField.Year);

        var issues = context.Issues.AsNoTracking()
            .Select(i => new { i.Id, i.SeriesId, i.Number, i.Year, i.Publisher })
            .AsEnumerable()
            .Select(i => new
            {
                i.Id,
                i.SeriesId,
                Number = i.Number ?? acceptedNumbers.GetValueOrDefault(i.Id),
                Year = i.Year ?? (int.TryParse(acceptedYears.GetValueOrDefault(i.Id), out int y) ? y : (int?)null),
                i.Publisher,
            })
            .ToList();
        var bySeries = issues.GroupBy(i => i.SeriesId).ToDictionary(g => g.Key, g => g.ToList());
        var publisherNames = context.Publishers.AsNoTracking().ToDictionary(p => p.Id, p => p.Name);

        var series = context.Series.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Publisher, s.PublisherEntityId, s.ContentType })
            .AsEnumerable()
            .Select(s =>
            {
                var own = bySeries.GetValueOrDefault(s.Id) ?? new();
                var years = own.Where(i => i.Year is > 1800 and < 2200).Select(i => i.Year!.Value).ToList();
                int? nameYear = YearFromName(s.Name);
                string? publisher = s.PublisherEntityId is int pid && publisherNames.TryGetValue(pid, out var pname) ? pname
                    : s.Publisher
                    ?? own.Where(i => !string.IsNullOrWhiteSpace(i.Publisher)).GroupBy(i => PublisherKeyFor(i.Publisher)).OrderByDescending(g => g.Count()).FirstOrDefault()?.First().Publisher;
                return new ConnectorSeries(
                    s.Id, s.Name, TitleKey(s.Name), PublisherKeyFor(publisher), s.ContentType,
                    years.Count > 0 ? Math.Min(years.Min(), nameYear ?? int.MaxValue) : nameYear,
                    years.Count > 0 ? years.Max() : nameYear,
                    own.Count);
            })
            .ToList();
        var names = series.ToDictionary(s => s.Id, s => s.Name);
        var issueById = issues.ToDictionary(i => i.Id);

        var memberships = context.EventMemberships.AsNoTracking().Where(m => includeEvents)
            .Select(m => new { m.StoryEventId, m.IssueId, m.Position })
            .ToList();
        var events = !includeEvents ? new List<ConnectorEvent>() : context.StoryEvents.AsNoTracking()
            .Select(e => new { e.Id, e.Name, e.SpineSeriesId, e.StartDate })
            .AsEnumerable()
            .Select(e => new ConnectorEvent(
                e.Id, e.Name, e.SpineSeriesId, e.StartDate,
                memberships.Where(m => m.StoryEventId == e.Id && issueById.ContainsKey(m.IssueId))
                    .Select(m => (m.Position, Issue: issueById[m.IssueId]))
                    .OrderBy(m => m.Position)
                    .Select(m => new ConnectorEventMember(m.Issue.SeriesId, names.GetValueOrDefault(m.Issue.SeriesId, string.Empty), m.Issue.Number, NumberKey(m.Issue.Number)))
                    .ToList()))
            .ToList();

        var userPairs = context.RelationEvidence.AsNoTracking()
            .Where(e => e.Provider == RelationEvidenceProvider.User
                && e.MediaRelation!.SourceSeriesId != null && e.MediaRelation.TargetSeriesId != null)
            .Select(e => new { e.MediaRelation!.SourceSeriesId, e.MediaRelation.TargetSeriesId })
            .AsEnumerable()
            .Select(p => (p.SourceSeriesId!.Value, p.TargetSeriesId!.Value));
        var dismissed = context.SeriesRelationDismissals.AsNoTracking()
            .Select(d => new { d.LowerSeriesId, d.HigherSeriesId })
            .AsEnumerable()
            .Select(d => (d.LowerSeriesId, d.HigherSeriesId));

        return new SeriesConnectorSnapshot(series, events, userPairs, dismissed);
    }

    /// <summary>The numeric sort key of an issue number ("33", "33.1", "½"), or null when it isn't a number.</summary>
    public static float? NumberKey(string? number) => new Issue { Number = number }.NumberSortKey();

    private static Dictionary<int, string?> AcceptedIssueValues(PaperbunkrDbContext context, MetadataProposalField field) =>
        context.MetadataProposals.AsNoTracking()
            .Where(p => p.IssueId != null && p.Field == field && p.Status == MetadataProposalStatus.Accepted)
            .Select(p => new { IssueId = p.IssueId!.Value, p.ProposedValue, p.CreatedAt })
            .AsEnumerable()
            .GroupBy(p => p.IssueId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First().ProposedValue);
}
