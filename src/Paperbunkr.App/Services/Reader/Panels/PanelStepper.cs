using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>What stepping through a page's panels did.</summary>
public enum PanelStepOutcome
{
    /// <summary>Moved to another panel on the same page.</summary>
    Moved,

    /// <summary>Already on the last panel: the caller turns to the next page.</summary>
    PastEnd,

    /// <summary>Already on the first panel: the caller turns to the previous page.</summary>
    PastStart,
}

/// <summary>Pure stepping rules for guided panel view (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 3).</summary>
public static class PanelStepper
{
    /// <summary>Steps forward or back from panel <paramref name="current"/> of <paramref name="count"/>. <paramref name="current"/> may be -1 (no panel shown yet), which steps to the first (forward) or last (back).</summary>
    public static (PanelStepOutcome Outcome, int Index) Step(int count, int current, bool forward)
    {
        if (count <= 0)
        {
            return (forward ? PanelStepOutcome.PastEnd : PanelStepOutcome.PastStart, -1);
        }

        if (current < 0)
        {
            return (PanelStepOutcome.Moved, forward ? 0 : count - 1);
        }

        int next = forward ? current + 1 : current - 1;
        if (next >= count)
        {
            return (PanelStepOutcome.PastEnd, current);
        }

        return next < 0 ? (PanelStepOutcome.PastStart, current) : (PanelStepOutcome.Moved, next);
    }

    /// <summary>The panel to show on arriving at a page by turning forward (its first panel) or back (its last).</summary>
    public static int Landing(int count, bool arrivedGoingForward) => count <= 0 ? -1 : arrivedGoingForward ? 0 : count - 1;

    /// <summary>The panel containing the page point (<paramref name="x"/>, <paramref name="y"/>), else the nearest by centre; -1 when there are none.</summary>
    public static int IndexAt(IReadOnlyList<PanelRect> panels, double x, double y)
    {
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < panels.Count; i++)
        {
            if (panels[i].Contains(x, y))
            {
                return i;
            }

            double dx = panels[i].CenterX - x;
            double dy = panels[i].CenterY - y;
            double distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The panel strictly containing the page point, or -1 (a click in a gutter finds nothing).</summary>
    public static int ContainingIndex(IReadOnlyList<PanelRect> panels, double x, double y)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            if (panels[i].Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>"Panel 3/7" for the label, or an empty string when there are no panels.</summary>
    public static string Label(int index, int count) => count <= 0 || index < 0 ? string.Empty : $"Panel {index + 1}/{count}";
}

/// <summary>
/// The eased move between two views (zoom and pan) that a guided step or a smart double-click plays (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 3). Zoom is
/// interpolated in log space, so the change feels even at any scale; pan linearly; both with the same ease-out. Pure: the canvas feeds it elapsed time.
/// </summary>
public readonly record struct ZoomPanTween(double FromZoom, double FromPanX, double FromPanY, double ToZoom, double ToPanX, double ToPanY, double DurationMs)
{
    /// <summary>The default step length.</summary>
    public const double DefaultDurationMs = 180;

    /// <summary>Cubic ease-out: fast at first, settling gently.</summary>
    public static double EaseOutCubic(double t)
    {
        double u = 1 - Math.Clamp(t, 0, 1);
        return 1 - (u * u * u);
    }

    public bool IsComplete(double elapsedMs) => DurationMs <= 0 || elapsedMs >= DurationMs;

    /// <summary>The view after <paramref name="elapsedMs"/>.</summary>
    public (double Zoom, double PanX, double PanY) Evaluate(double elapsedMs)
    {
        if (IsComplete(elapsedMs))
        {
            return (ToZoom, ToPanX, ToPanY);
        }

        double e = EaseOutCubic(elapsedMs / DurationMs);
        double zoom = FromZoom * Math.Pow(ToZoom / FromZoom, e);
        return (zoom, FromPanX + ((ToPanX - FromPanX) * e), FromPanY + ((ToPanY - FromPanY) * e));
    }
}
