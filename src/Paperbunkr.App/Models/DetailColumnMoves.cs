using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Models;

/// <summary>Column reordering for a detail list's header drag (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md, Q7).</summary>
public static class DetailColumnMoves
{
    /// <summary>
    /// Moves <paramref name="field"/> to where <paramref name="targetField"/> is: in front of it when dragging left, behind it when
    /// dragging right - the Library Details header's rule. Hidden columns keep their places; unknown keys change nothing.
    /// </summary>
    public static IReadOnlyList<DetailListColumn> Move(IReadOnlyList<DetailListColumn> columns, string field, string targetField)
    {
        var list = columns.ToList();
        int from = list.FindIndex(c => c.Field == field);
        int to = list.FindIndex(c => c.Field == targetField);
        if (from < 0 || to < 0 || from == to)
        {
            return list;
        }

        var moving = list[from];
        list.RemoveAt(from);
        list.Insert(to, moving);
        return list;
    }
}
