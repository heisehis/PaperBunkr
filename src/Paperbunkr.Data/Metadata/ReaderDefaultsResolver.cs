using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The one place that resolves the comic reader's fit-mode and auto-rotate defaults
/// (<c>issue ?? series ?? global</c>, docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-
/// design.md §2), shared by the Reader and the detail screens so they cannot drift. Reading mode and
/// page layout keep their existing chains (<c>Issue.ReadingModeOverride ?? Series.ReadingMode</c> and
/// <c>Issue.PageLayoutModeOverride ?? Series.PageLayoutMode ?? AppSettings.DefaultPageLayoutMode</c>).
/// </summary>
public static class ReaderDefaultsResolver
{
    public static ImageFitMode EffectiveFitMode(Issue? issue, Series? series, AppSettings settings) =>
        issue?.PageFitModeOverride ?? series?.PageFitModeOverride ?? settings.DefaultPageFitMode;

    public static bool EffectiveAutoRotate(Issue? issue, Series? series, AppSettings settings) =>
        issue?.AutoRotateOverride ?? series?.AutoRotateOverride ?? settings.DefaultAutoRotate;
}
