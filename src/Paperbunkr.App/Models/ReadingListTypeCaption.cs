using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// The caps word a reading list wears on its gallery card and detail hero (docs/superpowers/specs/2026-10-20-reading-lists-visual-identity-design.md §1):
/// CONTINUITY, ARC, EVENT or its <see cref="ReadingListType"/> in capitals. A plain user list has none. Paperbunkr's own wording - CE's lists have no type.
/// </summary>
public static class ReadingListTypeCaption
{
    public static string? For(ReadingListType type, bool hasSource, bool hasContinuity)
    {
        if (hasContinuity)
        {
            return "CONTINUITY";
        }

        if (hasSource)
        {
            return type == ReadingListType.Event ? "EVENT" : "ARC";
        }

        return type switch
        {
            ReadingListType.User => null,
            ReadingListType.PublicationOrder => "PUBLICATION ORDER",
            _ => type.ToString().ToUpperInvariant(),
        };
    }
}
