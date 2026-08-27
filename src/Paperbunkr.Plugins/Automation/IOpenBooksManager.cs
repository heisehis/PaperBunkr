using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// Ported from ComicRackCE's <c>IOpenBooksManager</c>, with a real behavioral difference from the
/// original (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §4): CE is MDI-style
/// (multiple books open in separate slots, hence <c>Open(cb, inNewSlot, page)</c>). Paperbunkr's
/// <c>MainViewModel</c> is single-screen - there is no second slot, so <c>inNewSlot</c> is dropped.
/// </summary>
public interface IOpenBooksManager
{
    bool Open(Issue issue, int page);

    bool OpenFile(string file, int page);

    /// <summary>True only when <paramref name="issue"/> is the book currently shown by the Reader screen.</summary>
    bool IsOpen(Issue issue);
}
