namespace Paperbunkr.Data.Metadata;

/// <summary>A same-time note attached to an entry in a Comic Book Reading Orders event timeline.</summary>
public sealed record CbroTimelineOverlapNote(string OriginalText, string TargetName, int? TargetYear = null);

/// <summary>One entry in a Comic Book Reading Orders event timeline: its 1-based position in that list, name, start year, page, and source annotations.</summary>
/// <param name="ListKey">"marvel" or "dc": ranks only compare within one list.</param>
public sealed record CbroTimelineEntry(string ListKey, int Rank, string Name, int? Year, string? Url,
    IReadOnlyList<CbroTimelineOverlapNote>? OverlapNotes = null);

/// <summary>The Comic Book Reading Orders event timelines (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4.4).</summary>
public interface ICbroTimeline
{
    /// <summary>Every entry of the Marvel and DC timelines, in page order. Throws <see cref="ReadingLists.Sources.ReadingListSourceException"/> when the site cannot be read.</summary>
    Task<IReadOnlyList<CbroTimelineEntry>> GetEntriesAsync(CancellationToken cancellationToken);
}

/// <summary>A Metron EVENT reading list and the two lists next to it in a reading order; the neighbours are null when Metron has none set.</summary>
public sealed record MetronEventListInfo(string Id, string Name, string? PreviousName, string? NextName);

/// <summary>What Metron's <c>/universe/{id}/</c> says about one universe.</summary>
public sealed record MetronUniverseInfo(int Id, string Name, string? Designation, string? Publisher, string? Description = null);

/// <summary>Metron's universe catalogue (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §3).</summary>
public interface IMetronUniverses
{
    /// <summary>Searches Metron's universe catalogue by a partial universe name.</summary>
    Task<IReadOnlyList<MetronUniverseInfo>> SearchUniversesAsync(string query, CancellationToken cancellationToken);

    /// <summary>The universe, or null when Metron has no such id.</summary>
    Task<MetronUniverseInfo?> GetUniverseAsync(int universeId, CancellationToken cancellationToken);
}

/// <summary>Metron's EVENT reading lists (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4.2).</summary>
public interface IMetronEventLists
{
    /// <summary>The one EVENT list whose name matches <paramref name="eventName"/>, or null when there is none or more than one cannot be told apart.</summary>
    Task<MetronEventListInfo?> FindEventListAsync(string eventName, CancellationToken cancellationToken);
}
