namespace Paperbunkr.App.Models;

/// <summary>
/// Read-state badge shown on an <see cref="IssueCardSample"/> tile in the detail screen's
/// Issues / Specials tabs (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-
/// design.md §4). Computed on the card so the state logic is unit-testable rather than buried in
/// XAML converters.
/// </summary>
public enum IssueTileGlyph
{
    /// <summary>No glyph to show (kept for callers that have nothing to say).</summary>
    None,

    /// <summary>Fully read - a filled checkmark.</summary>
    Read,

    /// <summary>Partway through - a half-filled circle.</summary>
    InProgress,

    /// <summary>Not started - the accent unread dot.</summary>
    Unread,

    /// <summary>Not started and added recently - the "NEW" ribbon.</summary>
    New,
}

/// <summary>
/// The single read-state rule behind every read-state glyph (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md
/// #20): Read beats InProgress beats New beats Unread. "Recently added" is the same 7-day window
/// <c>SeriesCardSample.RecentAddBadgeLabel</c> uses.
/// </summary>
public static class IssueTileGlyphs
{
    public static readonly System.TimeSpan RecentWindow = System.TimeSpan.FromDays(7);

    public static IssueTileGlyph Resolve(bool isRead, bool isInProgress, System.DateTime? addedUtc = null, System.DateTime? nowUtc = null)
    {
        if (isRead)
        {
            return IssueTileGlyph.Read;
        }

        if (isInProgress)
        {
            return IssueTileGlyph.InProgress;
        }

        return addedUtc is { } added && (nowUtc ?? System.DateTime.UtcNow) - added <= RecentWindow
            ? IssueTileGlyph.New
            : IssueTileGlyph.Unread;
    }
}
