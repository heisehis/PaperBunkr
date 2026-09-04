namespace Paperbunkr.App.Models;

/// <summary>
/// Read-state badge shown on an <see cref="IssueCardSample"/> tile in the detail screen's
/// Issues / Specials tabs (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-
/// design.md §4). Computed on the card so the state logic is unit-testable rather than buried in
/// XAML converters.
/// </summary>
public enum IssueTileGlyph
{
    /// <summary>Unread, not started - no badge.</summary>
    None,

    /// <summary>Fully read - a filled checkmark.</summary>
    Read,

    /// <summary>Partway through - a half-filled circle.</summary>
    InProgress,
}
