namespace Paperbunkr.App.Services;

/// <summary>
/// Pure gate condition for the <c>DogEarThumbnails</c> hover/selected page-2 peek (docs/superpowers/
/// specs/2026-09-13-preferences-cosmetic-toggles-design.md) - mirrors CE's own
/// <c>CoverViewItem.cs:556</c> condition exactly: hover-or-selected (checked by the caller via the
/// XAML pseudoclass/IsSelected, not here), more than one page, no custom cover override, and the
/// file isn't missing. Extracted as a pure function so the branch logic is unit-testable without a
/// real decode.
/// </summary>
public static class DogEarEligibility
{
    /// <remarks>Deviation: CE requires <c>PageCount &gt; 1</c> because CE always knows the count; Paperbunkr only
    /// learns it from ComicInfo.xml or a reader visit, so an unknown (null) count is eligible and the page-2 decode
    /// decides - it returns nothing for a one-page book (2026-09-26 library audit).</remarks>
    public static bool IsEligible(int? pageCount, bool fileIsMissing, bool hasCustomCover) =>
        pageCount is (null or > 1) && !fileIsMissing && !hasCustomCover;
}
