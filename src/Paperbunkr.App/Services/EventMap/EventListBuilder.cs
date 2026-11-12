using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>One section of the List view: a spine chapter, a group of ten reading positions, or a continuity block.</summary>
public sealed record EventListSection(int Key, string Label, IReadOnlyList<int> Indices);

/// <summary>The List view's sections, in reading order (docs/superpowers/specs/2026-11-11-continuity-list-atlas-views-design.md §1).</summary>
public sealed record EventListModel(IReadOnlyList<EventListSection> Sections)
{
    public static EventListModel Empty { get; } = new(Array.Empty<EventListSection>());
}

/// <summary>
/// Splits a map layout into List sections. Rows keep exact reading order (cell index order). Sections follow the map's own structure:
/// spine segments ("Before", "Prologue", "Chapter 1 · Crisis #1" …), continuity blocks (event name, "Between · years", the year), or,
/// for a relay event, groups of ten reading positions.
/// </summary>
public static class EventListBuilder
{
    public const int RelayGroupSize = 10;

    public static EventListModel Build(EventMapLayoutResult layout)
    {
        if (layout.Cells.Count == 0)
        {
            return EventListModel.Empty;
        }

        var ordered = layout.Cells.OrderBy(c => c.Index).ToList();
        var sections = new List<EventListSection>();
        if (layout.IsBlockMode)
        {
            foreach (var block in layout.Blocks)
            {
                var indices = ordered.Where(c => c.Row.BlockIndex == block.Index).Select(c => c.Index).ToArray();
                if (indices.Length > 0)
                {
                    sections.Add(new EventListSection(block.Index, block.Label, indices));
                }
            }
        }
        else if (layout.IsSpineMode)
        {
            int chapter = 0;
            foreach (var segment in layout.Segments)
            {
                var indices = ordered.Where(c => c.Segment == segment.Index).Select(c => c.Index).ToArray();
                if (indices.Length == 0)
                {
                    continue;
                }

                string label;
                if (segment.TrunkCell is not int trunk)
                {
                    label = "Before";
                }
                else
                {
                    var anchor = layout.Cells[trunk].Row;
                    label = anchor.Role switch
                    {
                        EventMembershipRole.Prologue => "Prologue",
                        EventMembershipRole.Epilogue => "Epilogue",
                        _ => $"Chapter {++chapter} · {Title(anchor)}",
                    };
                }

                sections.Add(new EventListSection(segment.Index, label, indices));
            }
        }
        else
        {
            for (int start = 0; start < ordered.Count; start += RelayGroupSize)
            {
                var indices = ordered.Skip(start).Take(RelayGroupSize).Select(c => c.Index).ToArray();
                sections.Add(new EventListSection(start / RelayGroupSize, $"{start + 1}–{start + indices.Length}", indices));
            }
        }

        return new EventListModel(sections);
    }

    /// <summary>The trunk card a spine-mode row ties into ("ties into Crisis #3"), or null for trunk rows and other modes.</summary>
    public static string? TiesInto(EventMapLayoutResult layout, int index)
    {
        if (!layout.IsSpineMode || index < 0 || index >= layout.Cells.Count)
        {
            return null;
        }

        var cell = layout.Cells[index];
        if (cell.IsTrunk)
        {
            return null;
        }

        var segment = layout.Segments.FirstOrDefault(s => s.Index == cell.Segment);
        return segment?.TrunkCell is int trunk ? $"ties into {Title(layout.Cells[trunk].Row)}" : null;
    }

    private static string Title(EventMapRow row) => string.IsNullOrEmpty(row.Number) ? row.SeriesName : $"{row.SeriesName} #{row.Number}";
}
