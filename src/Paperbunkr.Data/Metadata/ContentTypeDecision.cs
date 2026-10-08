using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One source's answer for a series: which entry it matched, how well, and what type that entry implies. Stored as JSON on <see cref="Series.ContentTypeEvidence"/> and shown as a chip in the confirm queue.</summary>
public sealed record ContentTypeEvidenceItem(
    ExternalMetadataProvider Provider,
    string? ExternalId,
    string? MatchedTitle,
    double Score,
    string? Raw,
    ContentType? Type,
    bool QueueOnly);

public static class ContentTypeEvidence
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static string? Serialize(IReadOnlyList<ContentTypeEvidenceItem> items) =>
        items.Count == 0 ? null : JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<ContentTypeEvidenceItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ContentTypeEvidenceItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ContentTypeEvidenceItem>>(json, Options) ?? new List<ContentTypeEvidenceItem>();
        }
        catch (JsonException)
        {
            return Array.Empty<ContentTypeEvidenceItem>();
        }
    }
}

public enum ContentTypeOutcomeKind
{
    /// <summary>No source gave a usable match at or above the review threshold.</summary>
    NoMatch,

    /// <summary>A plausible type exists but a person has to confirm it.</summary>
    Queue,

    /// <summary>Confident enough to apply on its own (never returned when "ask me before changing" is on).</summary>
    AutoApply,
}

/// <param name="Conflict">The usable sources disagree on the type; such a row is never bulk-accepted.</param>
public sealed record ContentTypeOutcome(
    ContentTypeOutcomeKind Kind,
    ContentType? Type,
    double Confidence,
    bool Conflict,
    IReadOnlyList<ContentTypeEvidenceItem> Evidence);

/// <summary>
/// Pure decision rule of the auto-classify pipeline (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).
/// A match is usable at <see cref="TitleMatchScorer.ReviewThreshold"/>; it can vote for an automatic apply only at
/// <see cref="TitleMatchScorer.AutoThreshold"/> and only when its type is not queue-only. An automatic apply needs every usable source to
/// agree, plus corroboration: a second provider voting the same type, or a local guess (publisher heuristic, the series' current unlocked
/// type) that already says the same thing.
/// </summary>
public static class ContentTypeDecision
{
    public static ContentTypeOutcome Decide(
        IReadOnlyList<ContentTypeEvidenceItem> evidence,
        IReadOnlyCollection<ContentType> corroboration,
        bool askBefore)
    {
        var usable = evidence
            .Where(e => e.Type is not null && e.Score >= TitleMatchScorer.ReviewThreshold)
            .ToList();

        if (usable.Count == 0)
        {
            return new ContentTypeOutcome(ContentTypeOutcomeKind.NoMatch, null, 0, false, evidence);
        }

        var voting = usable.Where(e => !e.QueueOnly && e.Score >= TitleMatchScorer.AutoThreshold).ToList();
        bool conflict = usable.Select(e => e.Type).Distinct().Count() > 1;

        // The suggested type: the most-voted type among voters, ties to the best score; with no voters, the best-scoring usable entry.
        ContentType chosen = (voting.Count > 0 ? voting : usable)
            .GroupBy(e => e.Type!.Value)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(e => e.Score))
            .First().Key;

        bool autoApply = !askBefore
            && !conflict
            && voting.Count > 0
            && (voting.Select(v => v.Provider).Distinct().Count() >= 2 || corroboration.Contains(chosen));

        double confidence = autoApply
            ? voting.Min(v => v.Score)
            : usable.Where(e => e.Type == chosen).Max(e => e.Score);

        return new ContentTypeOutcome(
            autoApply ? ContentTypeOutcomeKind.AutoApply : ContentTypeOutcomeKind.Queue,
            chosen, confidence, conflict, evidence);
    }
}
