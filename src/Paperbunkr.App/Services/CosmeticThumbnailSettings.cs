using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cached snapshot of the cosmetic-thumbnail <see cref="AppSettings"/> toggles (docs/superpowers/
/// specs/2026-09-13-preferences-cosmetic-toggles-design.md) for hot per-tile-realization reads
/// (<see cref="AsyncCoverImage"/>'s fade, the dog-ear peek, the hover tooltip) that can't afford a
/// <see cref="PaperbunkrDb.CreateContext"/> round-trip per call the way a per-navigation settings
/// read (e.g. <see cref="ThemeService.GetReducedMotion"/>) can. Refreshed once at startup
/// (<see cref="RefreshFrom"/>) and pushed again from <c>LibraryScreenViewModel</c> whenever the
/// toolbar toggle changes, so a live edit takes effect on the next tile realization without a
/// restart.
/// </summary>
public static class CosmeticThumbnailSettings
{
    public static bool FadeInThumbnails { get; set; } = true;
    public static bool DogEarThumbnails { get; set; } = true;
    public static bool ShowToolTips { get; set; }
    public static bool NumericRatingThumbnails { get; set; } = true;

    public static void RefreshFrom(AppSettings settings)
    {
        FadeInThumbnails = settings.FadeInThumbnails;
        DogEarThumbnails = settings.DogEarThumbnails;
        ShowToolTips = settings.ShowToolTips;
        NumericRatingThumbnails = settings.NumericRatingThumbnails;
    }
}
