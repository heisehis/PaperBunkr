using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>Ported from ComicRackCE's <c>IBrowser</c> - wraps the Library screen's navigation/selection state.</summary>
public interface IBrowser
{
    bool OpenNextComic();

    bool OpenPrevComic();

    bool OpenRandomComic();

    void SelectComics(IEnumerable<Issue> books);
}
