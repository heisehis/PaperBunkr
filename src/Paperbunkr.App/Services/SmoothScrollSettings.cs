using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Push-cache of the "Smooth scrolling" preference (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §6),
/// same shape as <see cref="CosmeticThumbnailSettings"/>: a static that the settings load and the toggle write to, read by
/// <c>SmoothScrollViewer</c> on every wheel event without touching the database. Deliberate deviation from ComicRack, which has no
/// such setting: default on.
/// </summary>
public static class SmoothScrollSettings
{
    public static bool Enabled { get; set; } = true;

    public static void Apply(AppSettings settings) => Enabled = settings.SmoothScrolling;
}
