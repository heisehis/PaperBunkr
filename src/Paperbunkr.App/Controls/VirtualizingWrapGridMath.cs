using System;

namespace Paperbunkr.App.Controls;

/// <summary>Pure uniform-grid layout math extracted out of <see cref="VirtualizingWrapPanel"/> for direct testing - same "extract the math, keep the Avalonia-touching caller thin" precedent as <c>ZoomPanMath</c>/<c>GridKeyboardNavigation</c>.</summary>
public readonly record struct WrapGridLayout(int ItemsPerRow, int TotalRows, double TotalHeight);

/// <summary>Inclusive index range that should be realized, or (-1, -1) if nothing should be.</summary>
public readonly record struct RealizedRange(int FirstIndex, int LastIndex)
{
    public bool IsEmpty => FirstIndex < 0;
}

public static class VirtualizingWrapGridMath
{
    public static WrapGridLayout ComputeLayout(int itemCount, double availableWidth, double itemWidth, double itemHeight, double itemSpacing, double lineSpacing)
    {
        if (itemWidth <= 0 || itemHeight <= 0 || itemCount == 0 || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return new WrapGridLayout(1, 0, 0);
        }

        int itemsPerRow = Math.Max(1, (int)((availableWidth + itemSpacing) / (itemWidth + itemSpacing)));
        int totalRows = (int)Math.Ceiling(itemCount / (double)itemsPerRow);
        double rowStride = itemHeight + lineSpacing;
        double totalHeight = totalRows > 0 ? totalRows * rowStride - lineSpacing : 0;
        return new WrapGridLayout(itemsPerRow, totalRows, totalHeight);
    }

    public static RealizedRange ComputeRealizedRange(
        int itemCount, WrapGridLayout layout, double viewportTop, double viewportBottom,
        double itemHeight, double lineSpacing, int bufferRows)
    {
        if (itemCount == 0 || layout.TotalRows == 0)
        {
            return new RealizedRange(-1, -1);
        }

        double rowStride = itemHeight + lineSpacing;
        int firstRow = rowStride > 0 ? Math.Max(0, (int)(viewportTop / rowStride) - bufferRows) : 0;
        int lastRow = rowStride > 0
            ? Math.Min(layout.TotalRows - 1, (int)(viewportBottom / rowStride) + bufferRows)
            : layout.TotalRows - 1;

        int firstIndex = Math.Max(0, firstRow * layout.ItemsPerRow);
        int lastIndex = Math.Min(itemCount - 1, (lastRow + 1) * layout.ItemsPerRow - 1);
        return new RealizedRange(firstIndex, lastIndex);
    }

    public static (int Row, int Column) IndexToRowColumn(int index, int itemsPerRow) =>
        (index / itemsPerRow, index % itemsPerRow);
}
