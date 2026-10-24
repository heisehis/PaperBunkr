namespace Paperbunkr.Data.Entities;

/// <summary>
/// Layout of the detail screen's Issues tab (docs/superpowers/specs/2026-08-28-detail-screens-
/// streaming-redesign-design.md). <see cref="Poster"/> is first so it is the CLR default and, once
/// an <c>AppSettings.DetailIssueViewMode</c> column is added, the EF sentinel too. Saved as part of
/// the Comic detail type's list-layout state on <c>DetailTabsViewModel</c>.
/// </summary>
public enum DetailIssueViewMode
{
    /// <summary>Cover + issue number + arc/title below, on the Library poster-tile primitives.</summary>
    Poster,

    /// <summary>One dense row per issue: number, title, arc, cover date, rating, read state.</summary>
    List,

    /// <summary>Cover + full title + "ISSUE #n" + a Read/Continue button + inline action icons.</summary>
    Card,
}
