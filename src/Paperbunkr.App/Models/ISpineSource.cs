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
}
