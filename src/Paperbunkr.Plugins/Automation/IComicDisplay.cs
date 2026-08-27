using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// Deliberately NOT a full port of CE's ~30-member GDI+ <c>IComicDisplay</c> (docs/superpowers/
/// specs/2026-08-24-plugin-api-v2-design.md §4) - scoped to what the shipped reader canvas
/// actually exposes.
/// </summary>
public interface IComicDisplay
{
    Issue? CurrentBook { get; }

    int CurrentPageIndex { get; }

    int PageCount { get; }

    event Action<int>? CurrentPageIndexChanged;

    void NextPage();

    void PreviousPage();

    void GoToPage(int index);
}
