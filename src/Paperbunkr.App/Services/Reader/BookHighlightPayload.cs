using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The JSON the page's <c>pbApplyHighlights</c> receives (docs/superpowers/specs/2026-10-27-books-slice3c-reader-annotations-design.md §2). A highlight with a
/// non-blank note carries <c>hasNote: true</c>, which the page turns into the <c>pb-has-note</c> class and its marker.
/// </summary>
public static class BookHighlightPayload
{
    public static string Build(IEnumerable<BookHighlightSummary> highlights) =>
        JsonSerializer.Serialize(highlights.Select(h => new
        {
            id = h.Id,
            blockId = h.BlockId,
            startOffset = h.StartOffset,
            length = h.Length,
            color = h.Color.ToString(),
            hasNote = !string.IsNullOrWhiteSpace(h.Note),
        }));
}
