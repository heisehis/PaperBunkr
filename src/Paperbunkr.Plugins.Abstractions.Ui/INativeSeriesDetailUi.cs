using Avalonia.Controls;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.Plugins.Abstractions.Ui;

/// <summary>
/// Optional capability a native plugin's module class implements alongside
/// <see cref="INativePluginModule"/> when it wants to replace part of the comic Detail screen's
/// own "Details" tab for a specific series - e.g. a ComicVine-backed scraper standing in for the
/// host's generic External Metadata/Trackers block, which targets manga metadata sources
/// (AniList/MangaBaka/MangaDex) that have little to no real comic coverage. A plugin with no such
/// view simply doesn't implement this interface, same "absent means no capability" shape as
/// <see cref="INativePluginSettingsUi"/>.
/// </summary>
public interface INativeSeriesDetailUi
{
    /// <summary>
    /// Returns a <see cref="Control"/> to show in place of the Detail screen's default External
    /// Metadata/Trackers block for <paramref name="series"/>, or <see langword="null"/> to decline
    /// (the host then falls back to its own default UI for that series) - the plugin decides based
    /// on whatever it wants to look at (typically <see cref="Series.ContentType"/>), the host makes
    /// no assumption about which series this gets asked for. Hosted inline directly in the host's
    /// own screen (not a modal overlay like <see cref="INativePluginSettingsUi.CreateSettingsView"/>),
    /// so the returned view should read as a native part of that screen, not a separate dialog.
    /// </summary>
    Control? CreateSeriesDetailView(INativePluginUiEnvironment environment, Series series);
}
