namespace Paperbunkr.Data.Entities;

/// <summary>How a reading-list entry came to point at the issue it points at.</summary>
public enum MatchMethod
{
    /// <summary>Not linked to a real comic (a placeholder stands in).</summary>
    None,

    /// <summary>A provider id the source supplied.</summary>
    ProviderId,

    /// <summary>Series, number and whatever else the source gave.</summary>
    Metadata,

    /// <summary>The user picked the comic (Find &amp; link). Never re-evaluated.</summary>
    User,

    /// <summary>Record rebuilt from a placeholder that existed before source records did.</summary>
    Backfilled,
}

/// <summary>Where an entry stands. Only meaningful while the entry points at a placeholder; a linked entry is simply linked.</summary>
public enum MatchState
{
    Linked,

    /// <summary>The library has one or more comics that could be it, and the source did not say enough to pick.</summary>
    NeedsChoice,

    /// <summary>Nothing in the library fits.</summary>
    Unresolved,
}

/// <summary>
/// What the source asked for when a <see cref="ReadingListItem"/> was created (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-
/// design.md §4), kept apart from the issue the entry is linked to so it survives a placeholder being replaced. One-to-one with the item
/// and deleted with it. Items made by hand, and entries linked before this existed, have none. The "asked for" fields are written once
/// and never overwritten by a relink; a provider id is present only when the source supplied it.
/// </summary>
public class ReadingListItemSource
{
    public int Id { get; set; }

    public int ReadingListItemId { get; set; }

    public ReadingListItem? Item { get; set; }

    public string Series { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string? Volume { get; set; }

    public int? Year { get; set; }

    public string? Format { get; set; }

    public string? Title { get; set; }

    /// <summary><c>SeriesKeys.Of(Series)</c> / <c>IssueNumberKey.Of(Number)</c>, stored so a newly arrived comic finds the entries waiting for it by index.</summary>
    public string SeriesKey { get; set; } = string.Empty;

    public string NumberKey { get; set; } = string.Empty;

    /// <summary>The reading-list source ("ComicVine", "Metron", ...), "cbl" or "csv"; null when unknown (backfilled).</summary>
    public string? SourceKey { get; set; }

    public string? SourceEntryId { get; set; }

    public string? ComicVineIssueId { get; set; }

    public string? ComicVineSeriesId { get; set; }

    public string? MetronIssueId { get; set; }

    public string? MetronSeriesId { get; set; }

    public MatchMethod Method { get; set; }

    public MatchState State { get; set; }

    public DateTime? LastCheckedUtc { get; set; }
}
