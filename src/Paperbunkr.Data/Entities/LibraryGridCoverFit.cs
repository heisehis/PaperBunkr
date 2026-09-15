namespace Paperbunkr.Data.Entities;

/// <summary>
/// Cover-fit/tile-style toggle within <see cref="LibraryViewMode.PosterGrid"/> (docs/superpowers/specs/
/// 2026-09-14-library-visual-redesign-design.md §2) - replaces the old separate <c>PanoramaGrid</c>
/// and <c>Tiles</c> view modes. <see cref="Poster"/> keeps uniform tile sizing; <see cref="Panorama"/>
/// keeps per-cover aspect ratio via the existing <c>VirtualizingVariableWrapPanel</c>;
/// <see cref="Tiles"/> keeps the old <c>Tiles</c> mode's smaller wrapping cards
/// (<c>ItemsControl</c>/<c>VirtualizingWrapPanel</c>). All three share the same underlying
/// wrap-grid rendering family - unlike <see cref="LibraryViewMode.List"/>, which is a genuinely
/// different container (a real <c>ListBox</c>), <c>Tiles</c> was never actually a List variant
/// despite reading that way at a glance; it belongs here, not as a List density toggle (an earlier
/// draft of this design got that wrong - corrected during implementation after reading the real
/// container types in <c>LibraryScreen.axaml</c>). <see cref="Poster"/> is first so it's the CLR
/// default (0) and the desired default at once.
/// </summary>
public enum LibraryGridCoverFit
{
    Poster,
    Panorama,
    Tiles,
}
