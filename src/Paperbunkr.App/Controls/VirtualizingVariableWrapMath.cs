using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Result of packing a variable-width, uniform-height wrapping flow. <see cref="RowOfItem"/> and
/// <see cref="XOfItem"/> are per-item; <see cref="FirstItemOfRow"/> has <see cref="TotalRows"/> + 1
/// entries (last = item count) so a row's item span is <c>[FirstItemOfRow[r], FirstItemOfRow[r+1])</c>.
/// </summary>
public readonly record struct VariableWrapLayout(
    IReadOnlyList<int> RowOfItem,
    IReadOnlyList<double> XOfItem,
    IReadOnlyList<int> FirstItemOfRow,
    int TotalRows,
    double TotalHeight)
{
    public static readonly VariableWrapLayout Empty =
        new(Array.Empty<int>(), Array.Empty<double>(), new[] { 0 }, 0, 0);
}

/// <summary>
/// Pure greedy row-packing math for <see cref="VirtualizingVariableWrapPanel"/> - variable per-item
/// width, uniform row height. Same "extract the geometry, keep the Avalonia caller thin" split as
/// <see cref="VirtualizingWrapGridMath"/> (which stays the uniform-cell fast path the four fixed
/// Library/Books density grids use); this one exists only for Panorama, whose whole point is that
/// each cover renders at its real aspect ratio. Reuses <see cref="RealizedRange"/> from that file.
/// </summary>
public static class VirtualizingVariableWrapMath
{
    /// <summary>
    /// Left-to-right greedy packing: each item stays on the current row unless it would overflow
    /// <paramref name="availableWidth"/> and the row already holds at least one item. Row height is
    /// uniform, so an item's Y is still just <c>row * (itemHeight + lineSpacing)</c>.
    /// </summary>
    public static VariableWrapLayout ComputeLayout(
        IReadOnlyList<double> itemWidths, double availableWidth,
        double itemHeight, double itemSpacing, double lineSpacing)
    {
        int count = itemWidths.Count;
        if (count == 0 || itemHeight <= 0 || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return VariableWrapLayout.Empty;
        }

        var rowOf = new int[count];
        var xOf = new double[count];
        var firstOfRow = new List<int>(Math.Max(4, count / 8)) { 0 };

        int row = 0;
        double x = 0;
        for (int i = 0; i < count; i++)
        {
            double w = Math.Max(1, itemWidths[i]);
            bool rowHasItem = i > firstOfRow[row];
            if (rowHasItem && x + w > availableWidth + 0.5)
            {
                row++;
                x = 0;
                firstOfRow.Add(i);
            }

            rowOf[i] = row;
            xOf[i] = x;
            x += w + itemSpacing;
        }

        firstOfRow.Add(count); // sentinel so row r spans [firstOfRow[r], firstOfRow[r + 1])
        int totalRows = row + 1;
        double rowStride = itemHeight + lineSpacing;
        double totalHeight = totalRows * rowStride - lineSpacing;
        return new VariableWrapLayout(rowOf, xOf, firstOfRow, totalRows, totalHeight);
    }

    /// <summary>
    /// Inclusive item-index range whose rows intersect <c>[viewportTop, viewportBottom]</c> grown
    /// by <paramref name="bufferRows"/> on each side. <see cref="RealizedRange.IsEmpty"/> when the
    /// layout has no rows or the viewport sits past the end.
    /// </summary>
    public static RealizedRange ComputeRealizedRange(
        VariableWrapLayout layout, double viewportTop, double viewportBottom,
        double itemHeight, double lineSpacing, int bufferRows)
    {
        if (layout.TotalRows == 0)
        {
            return new RealizedRange(-1, -1);
        }

        double rowStride = itemHeight + lineSpacing;
        int firstRow = rowStride > 0 ? Math.Max(0, (int)(viewportTop / rowStride) - bufferRows) : 0;
        int lastRow = rowStride > 0
            ? Math.Min(layout.TotalRows - 1, (int)(viewportBottom / rowStride) + bufferRows)
            : layout.TotalRows - 1;

        if (firstRow > lastRow || firstRow >= layout.TotalRows)
        {
            return new RealizedRange(-1, -1);
        }

        int firstIndex = layout.FirstItemOfRow[firstRow];
        int lastIndex = layout.FirstItemOfRow[lastRow + 1] - 1;
        return firstIndex > lastIndex ? new RealizedRange(-1, -1) : new RealizedRange(firstIndex, lastIndex);
    }

    /// <summary>
    /// Index of the item in <paramref name="targetRow"/> whose X is closest to
    /// <paramref name="anchorX"/> - the column-preserving target for an Up/Down arrow-key move.
    /// Returns -1 if <paramref name="targetRow"/> is out of range.
    /// </summary>
    public static int NearestInRow(VariableWrapLayout layout, int targetRow, double anchorX)
    {
        if (targetRow < 0 || targetRow >= layout.TotalRows)
        {
            return -1;
        }

        int start = layout.FirstItemOfRow[targetRow];
        int end = layout.FirstItemOfRow[targetRow + 1];
        int best = start;
        double bestDist = double.MaxValue;
        for (int i = start; i < end; i++)
        {
            double d = Math.Abs(layout.XOfItem[i] - anchorX);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }
}
