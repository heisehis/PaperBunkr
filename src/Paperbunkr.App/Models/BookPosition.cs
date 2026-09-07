namespace Paperbunkr.App.Models;

/// <summary>
/// A reading position within a book (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §5; anchor reworked by docs/superpowers/specs/2026-09-07-books-reader-pagination-and-position-fix-
/// design.md to match the block-ID scheme <c>BookHighlight</c> already uses). <see cref="BlockId"/> is
/// a <c>BlockIdInjector</c>-assigned <c>id="pb-p&lt;n&gt;"</c> on a block-level element within
/// <see cref="ChapterIndex"/> - null/empty means "no specific block, land at the chapter start" (the
/// default for plain chapter navigation; only resume-on-load and bookmark jumps carry a real value).
/// <see cref="ProgressionFraction"/> (0-1, the scroll/page fraction at capture time) is a coarse
/// fallback landing spot for the rare case <see cref="BlockId"/> can't be resolved on reload (e.g. a
/// re-parse producing different HTML) - survives font-size/window-size changes and reflows, unlike a
/// stored pixel offset would.
/// </summary>
public readonly record struct BookPosition(int ChapterIndex, string? BlockId = null, double ProgressionFraction = 0)
{
    public static readonly BookPosition Start = new(0);
}
