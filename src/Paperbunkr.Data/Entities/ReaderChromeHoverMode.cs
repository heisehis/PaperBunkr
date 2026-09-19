namespace Paperbunkr.Data.Entities;

/// <summary>
/// Which mechanism reveals the comic reader's floating chrome clusters, backing
/// <see cref="AppSettings.ReaderChromeHoverMode"/>. Added 2026-09-16 on direct user request to make
/// the two reveal styles swappable rather than one permanently replacing the other. Not a CE-parity
/// setting - CE has no per-cluster chrome at all, just one always-on toolbar.
/// </summary>
public enum ReaderChromeHoverMode
{
    /// <summary>Each corner cluster reveals only while the pointer is over its own zone (shipped
    /// 2026-09-16, "more reactive... only when I hover above each item"). Default.</summary>
    PerCluster,

    /// <summary>Original behavior: any pointer movement over the reading canvas reveals every
    /// cluster at once, then idle-fades per <see cref="AppSettings.ReaderAutoHideChrome"/>.</summary>
    Ambient
}
