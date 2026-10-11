using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The only writer of <see cref="Series.ContentType"/> and its provenance columns (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).
/// Every manual write locks the series so no pipeline ever changes or re-asks it. None of these save; the caller owns the context.
/// </summary>
public static class SeriesContentTypeEditor
{
    /// <summary>Sources a pipeline may still revise (a guess, as opposed to a decision).</summary>
    public static bool IsGuess(ContentTypeSource source) =>
        source is ContentTypeSource.Unset or ContentTypeSource.Embedded or ContentTypeSource.Language or ContentTypeSource.Publisher;

    /// <summary>A person set the type (Library menu, bulk edit, Detail picker, queue Change). Locks; the reading mode is left alone, as it always was.</summary>
    public static void SetManual(Series series, ContentType type)
    {
        series.ContentType = type;
        series.ContentTypeSource = ContentTypeSource.Manual;
        series.ContentTypeLocked = true;
        ClearAuto(series);
    }

    /// <summary>The scanner / publisher sweep classified a series from a local hint. Never touches a locked series; returns whether it wrote.</summary>
    public static bool SetGuess(Series series, ContentType type, ReadingMode readingMode, ContentTypeSource source)
    {
        if (series.ContentTypeLocked)
        {
            return false;
        }

        series.ContentType = type;
        series.ReadingMode = readingMode;
        series.ContentTypeSource = source;
        series.ContentTypeConfidence = null;
        return true;
    }

    /// <summary>The pipeline applied a provider-confirmed type. Remembers the old type and reading mode for <see cref="Undo"/>.</summary>
    public static void ApplyAuto(Series series, ContentType type, double confidence, string? evidenceJson, DateTime nowUtc)
    {
        series.PreviousContentType = series.ContentType;
        series.PreviousReadingMode = series.ReadingMode;
        series.ContentType = type;
        series.ReadingMode = ReadingModeAfter(series.ReadingMode, type);
        series.ContentTypeSource = ContentTypeSource.Provider;
        series.ContentTypeConfidence = confidence;
        series.ContentTypeEvidence = evidenceJson;
        series.ContentTypeAutoAppliedUtc = nowUtc;
        series.ContentTypeSuggestion = null;
        series.ContentTypeCheck = ContentTypeCheck.Matched;
        series.ContentTypeCheckedUtc = nowUtc;
    }

    /// <summary>The pipeline found a plausible type a person must confirm.</summary>
    public static void Queue(Series series, ContentType suggestion, double confidence, string? evidenceJson, DateTime nowUtc)
    {
        series.ContentTypeSuggestion = suggestion;
        series.ContentTypeConfidence = confidence;
        series.ContentTypeEvidence = evidenceJson;
        series.ContentTypeCheck = ContentTypeCheck.Matched;
        series.ContentTypeCheckedUtc = nowUtc;
    }

    public static void MarkNoMatch(Series series, DateTime nowUtc)
    {
        series.ContentTypeSuggestion = null;
        series.ContentTypeCheck = ContentTypeCheck.NoMatch;
        series.ContentTypeCheckedUtc = nowUtc;
    }

    /// <summary>Queue action Accept: apply the suggestion and lock it.</summary>
    public static bool AcceptSuggestion(Series series)
    {
        if (series.ContentTypeSuggestion is not ContentType suggestion)
        {
            return false;
        }

        series.ContentTypeAutoAppliedUtc = null;
        series.PreviousContentType = null;
        series.PreviousReadingMode = null;
        series.ContentType = suggestion;
        series.ReadingMode = ReadingModeAfter(series.ReadingMode, suggestion);
        series.ContentTypeSource = ContentTypeSource.Manual;
        series.ContentTypeLocked = true;
        series.ContentTypeSuggestion = null;
        return true;
    }

    /// <summary>Queue action Keep: lock the current type as it is.</summary>
    public static void Keep(Series series)
    {
        series.ContentTypeSource = ContentTypeSource.Manual;
        series.ContentTypeLocked = true;
        ClearAuto(series);
    }

    /// <summary>Queue action "Not a comic: skip": never searched again, type left as it is.</summary>
    public static void Skip(Series series, DateTime nowUtc)
    {
        series.ContentTypeLocked = true;
        series.ContentTypeCheck = ContentTypeCheck.Skipped;
        series.ContentTypeCheckedUtc = nowUtc;
        ClearAuto(series);
    }

    /// <summary>Restore the type and reading mode from before the last auto-apply, and lock so the same guess is not re-applied.</summary>
    public static bool Undo(Series series)
    {
        if (series.PreviousContentType is not ContentType previous)
        {
            return false;
        }

        series.ContentType = previous;
        series.ReadingMode = series.PreviousReadingMode ?? series.ReadingMode;
        series.ContentTypeSource = ContentTypeSource.Manual;
        series.ContentTypeLocked = true;
        ClearAuto(series);
        return true;
    }

    /// <summary>
    /// "Re-check publisher-classified series": unlocks series whose type could have come from the publisher heuristic (a locked
    /// <see cref="ContentTypeSource.Existing"/> row, or an unlocked <see cref="ContentTypeSource.Publisher"/> one) so the next classify pass
    /// looks at them again. A series whose publisher no longer matches the heuristic, or whose type differs from it, is left alone.
    /// </summary>
    public static int RecheckPublisherClassified(PaperbunkrDbContext context)
    {
        var candidates = context.Series
            .Where(s => s.RemoteSourceId == null
                && (s.ContentTypeSource == ContentTypeSource.Existing || s.ContentTypeSource == ContentTypeSource.Publisher)
                && s.ContentType != ContentType.Unknown)
            .Include(s => s.Issues)
            .ToList();

        int changed = 0;
        foreach (var series in candidates)
        {
            bool publisherSaysSo = series.Issues.Any(i =>
                PublisherContentTypeClassifier.TryClassify(i.Publisher, out var type, out _) && type == series.ContentType);
            if (!publisherSaysSo)
            {
                continue;
            }

            series.ContentTypeLocked = false;
            series.ContentTypeSource = ContentTypeSource.Publisher;
            series.ContentTypeCheck = ContentTypeCheck.None;
            series.ContentTypeCheckedUtc = null;
            series.ContentTypeSuggestion = null;
            changed++;
        }

        if (changed > 0)
        {
            context.SaveChanges();
        }

        return changed;
    }

    /// <summary>The type's reading mode, but only while the series is still on the default direction - a mode a person changed is theirs.</summary>
    public static ReadingMode ReadingModeAfter(ReadingMode current, ContentType type) =>
        current == ReadingMode.LeftToRight ? ProviderContentTypeMapper.ReadingModeFor(type) : current;

    private static void ClearAuto(Series series)
    {
        series.ContentTypeSuggestion = null;
        series.ContentTypeConfidence = null;
        series.ContentTypeEvidence = null;
        series.PreviousContentType = null;
        series.PreviousReadingMode = null;
        series.ContentTypeAutoAppliedUtc = null;
    }
}
