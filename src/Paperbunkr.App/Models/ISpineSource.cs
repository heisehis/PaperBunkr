namespace Paperbunkr.App.Models;

/// <summary>
/// What the Library's bookshelf view needs from an item to draw it as a book spine (Cosmetics pitch #19). Sizes come from
/// <see cref="Services.SpineMetrics"/>, so they are known from the data alone and the virtualizing panel can pack rows without realizing containers.
/// </summary>
public interface ISpineSource
{
    double SpineWidth { get; }

    double SpineHeight { get; }

    /// <summary>Text running down the spine.</summary>
    string SpineTitle { get; }

    /// <summary>Small cap at the foot of the spine (issue number, or a series' issue count); null for none.</summary>
    string? SpineNumber { get; }

    /// <summary>A placeholder for a missing book (the Books "#3 missing" ghost): drawn as a dashed outline, not a spine
    /// (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).</summary>
    bool IsSpineGhost => false;
}
