namespace Paperbunkr.Data.Entities;

/// <summary>
/// The layout one list remembers (docs/superpowers/specs/2026-10-04-list-layouts-design.md) - CE's per-list
/// <c>ComicListItem.Display</c>. One row per screen and <see cref="SelectionKey"/>; written whenever the list's
/// layout changes, read when the list is selected.
/// </summary>
public class ListLayoutAssignment
{
    /// <summary>The screen's default layout: what a list with no row of its own falls back to.</summary>
    public const string DefaultKey = "*";

    public int Id { get; set; }

    public WorkspaceScreen Screen { get; set; }

    /// <summary>
    /// Which list: <c>all</c>, <c>content:&lt;ContentType&gt;</c>, <c>collection:&lt;id&gt;</c>, or
    /// <see cref="DefaultKey"/>. A key whose list no longer exists is simply never asked for again.
    /// </summary>
    public string SelectionKey { get; set; } = string.Empty;

    public string StateJson { get; set; } = "{}";
}
