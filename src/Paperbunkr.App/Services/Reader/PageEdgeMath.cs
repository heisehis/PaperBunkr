using System;

namespace Paperbunkr.App.Services.Reader;

/// <summary>How thick a page-edge stack beside the paper is (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.1): 2 px for
/// none of the book, 24 px for all of it, so the stacks thin on one side and thicken on the other as you read. Shared by the EPUB page script
/// and the PDF reader's <c>PageEdges</c>.</summary>
public static class PageEdgeMath
{
    public const double Min = 2;
    public const double Max = 24;

    public static double Width(double shareOfBook) => Min + ((Max - Min) * Math.Clamp(double.IsFinite(shareOfBook) ? shareOfBook : 0, 0, 1));
}
