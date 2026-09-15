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
    public static bool IsEligible(int? pageCount, bool fileIsMissing, bool hasCustomCover) =>
        pageCount is > 1 && !fileIsMissing && !hasCustomCover;
}
