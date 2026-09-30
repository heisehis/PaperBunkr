using System;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Pure width rule behind <see cref="AccordionPanel"/> (docs/superpowers/specs/2026-09-29-home-spotlight-accordion-design.md): one
/// open child at a fixed width, the rest sharing what's left by weight (a hovered one peeks wider), and children that can't get
/// their minimum width dropped from the end - never below three slivers. Widths plus spacing always add up to the full width, so an
/// animation that blends two results never jitters.
/// </summary>
public static class AccordionLayout
{
    public const int MinSlivers = 3;

    /// <returns>One width per child; a dropped child gets 0 (and no spacing is reserved for it).</returns>
    public static double[] Compute(int count, int openIndex, int peekIndex, double width, double openWidth, double minWidth,
        double spacing, double peekWeight)
    {
        var widths = new double[Math.Max(0, count)];
        if (count <= 0 || width <= 0)
        {
            return widths;
        }

        if (count == 1)
        {
            widths[0] = width;
            return widths;
        }

        openIndex = Math.Clamp(openIndex, 0, count - 1);

        // How many closed children fit at their minimum next to the open one - dropping from the end, keeping at least three
        // (or all of them, when there are fewer than three).
        int closedTotal = count - 1;
        int keep = closedTotal;
        double open = Math.Min(openWidth, width);
        while (keep > Math.Min(MinSlivers, closedTotal) && open + (keep * (minWidth + spacing)) > width)
        {
            keep--;
        }

        // Still too narrow with the minimum kept: shrink the open child instead of the slivers.
        double reserved = keep * (minWidth + spacing);
        open = Math.Max(minWidth, Math.Min(open, width - reserved));

        // Which closed children are kept: the first `keep` in order, skipping the open one.
        var kept = new bool[count];
        int taken = 0;
        for (int i = 0; i < count && taken < keep; i++)
        {
            if (i != openIndex)
            {
                kept[i] = true;
                taken++;
            }
        }

        double remaining = width - open - (keep * spacing);
        double totalWeight = 0;
        for (int i = 0; i < count; i++)
        {
            if (kept[i])
            {
                totalWeight += i == peekIndex ? peekWeight : 1;
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (i == openIndex)
            {
                widths[i] = open;
            }
            else if (kept[i] && totalWeight > 0)
            {
                widths[i] = Math.Max(0, remaining * (i == peekIndex ? peekWeight : 1) / totalWeight);
            }
        }

        return widths;
    }
}
