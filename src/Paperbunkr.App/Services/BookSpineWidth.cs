using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// Spine thickness for the Books bookshelf view (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §5): a longer book is
/// thicker. Length is the parsed character count turned into estimated pages (the same estimate Insights uses), then the Library's issue-spine
/// width curve; PDFs and books never opened (no count yet) get the default thin spine.
/// </summary>
public static class BookSpineWidth
{
    public static double For(int? characterCount, BookFormat format)
    {
        if (format == BookFormat.Pdf || characterCount is not > 0)
        {
            return SpineMetrics.WidthForPages(null);
        }

        return SpineMetrics.WidthForPages(ReadingPageMath.EstimatePagesFromChars(characterCount.Value));
    }
}
