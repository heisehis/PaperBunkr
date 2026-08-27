namespace Paperbunkr.Data.Entities;

/// <summary>
/// A folder the user has registered for on-demand library scanning
/// (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md §2) and, per-folder, live
/// watching (docs/superpowers/specs/2026-08-23-live-folder-watch-scanning-design.md) - CE's
/// <c>WatchFolder.Watch</c> flag, now implemented via <c>LiveFolderWatchService</c>
/// (<c>Paperbunkr.App.Services</c>).
/// </summary>
public class WatchedFolder
{
    public int Id { get; set; }

    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// CE parity: per-folder opt-in, defaults to off so existing installs don't start live-watching
    /// a folder nobody deliberately enabled it for.
    /// </summary>
    public bool Watch { get; set; }
}
