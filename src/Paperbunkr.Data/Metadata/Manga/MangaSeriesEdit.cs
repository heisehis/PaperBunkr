using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>An alternate title the editor wants the series to carry (native, romanized or localized).</summary>
public sealed record MangaTitleEdit(SeriesTitleType Type, string Value);

/// <summary>What happens to a chapter whose own value already differs from the series' when a series value is saved (design D9).</summary>
public enum MangaExceptionChoice
{
    /// <summary>Default and non-destructive: the chapter keeps its value and is marked as an exception.</summary>
    KeepAsExceptions,

    /// <summary>The series value replaces the chapter's value.</summary>
    ReplaceAll,
}

/// <summary>
/// Everything Edit Series edits for one manga-family series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §6): the inheritable fields as
/// <see cref="Values"/>, plus the identity fields saved directly on the series. Built by <see cref="MangaSeriesMetadataService.Load"/>, changed by the editor, and
/// handed back to <see cref="MangaSeriesMetadataService.Save"/>.
/// </summary>
public sealed class MangaSeriesEdit
{
    public int SeriesId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public SeriesStatus Status { get; set; }

    /// <summary>How the series is read (any of the seven modes). Saved straight onto the series; a change that flips right-to-left also marks the chapters as owing a file write.</summary>
    public ReadingMode ReadingMode { get; set; }

    public List<MangaTitleEdit> Titles { get; set; } = new();

    public MangaFieldValues Values { get; set; } = new();

    /// <summary>The series' state when the edit was loaded: version 0 means the user has not confirmed canonical values yet, so every field is confirmed by the first save.</summary>
    public int CanonicalReviewVersion { get; set; }

    /// <summary>Fields the user locked against provider overwrite (before this edit).</summary>
    public HashSet<string> LockedFields { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Fields whose chapters disagreed when the seeder read them: key to the distinct values found, most common first. Empty once confirmed.</summary>
    public Dictionary<string, List<string>> ReviewCandidates { get; set; } = new(StringComparer.Ordinal);

    public bool NeedsReview => ReviewCandidates.Count > 0;

    /// <summary>Fields the user unlocked in this edit, so providers may update them again. A field both unlocked and edited in the same save stays locked.</summary>
    public HashSet<string> UnlockedKeys { get; set; } = new(StringComparer.Ordinal);

    public bool IsRemote { get; set; }

    /// <summary>
    /// Set when the edit applies a value a provider supplied (accepting a candidate in Sources) rather than something the user typed: the changed fields are recorded as
    /// provider-sourced accepted proposals and are not locked, so a later refresh may update them again.
    /// </summary>
    public ExternalMetadataProvider? AppliedFromProvider { get; set; }

    public bool EmbeddedSyncEnabled { get; set; } = true;

    /// <summary>Loaded-state copies the service compares against to find what the user changed. Set by Load; do not edit.</summary>
    public MangaFieldValues? Baseline { get; internal set; }

    internal string? BaselineName { get; set; }

    internal string? BaselineSummary { get; set; }

    internal SeriesStatus BaselineStatus { get; set; }

    internal ReadingMode BaselineReadingMode { get; set; }

    internal string? BaselineTitlesKey { get; set; }

    internal bool BaselineEmbeddedSyncEnabled { get; set; } = true;

    public static string TitlesKey(IEnumerable<MangaTitleEdit> titles) =>
        string.Join("|", titles.OrderBy(t => t.Type).ThenBy(t => t.Value, StringComparer.OrdinalIgnoreCase).Select(t => $"{(int)t.Type}:{t.Value.Trim().ToLowerInvariant()}"));
}

/// <summary>One field the preview found chapters that already differ on.</summary>
public sealed record MangaFieldException(string Key, string Label, int ChapterCount);

/// <summary>What a save did.</summary>
public sealed record MangaSaveResult(bool Saved, IReadOnlyList<int> ChangedIssueIds, IReadOnlyList<string> ChangedKeys)
{
    public static readonly MangaSaveResult Nothing = new(false, Array.Empty<int>(), Array.Empty<string>());
}
