using Avalonia;

namespace Paperbunkr.App.Services.Reader;

/// <summary>Where a reader cluster sits on its host (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.2).</summary>
public enum ClusterAnchor
{
    TopLeft,
    TopRight,
    BottomCenter,
}

/// <summary>Placement maths for the floating reader clusters: a cluster of a given size, anchored to a corner (or the bottom centre) of the
/// host with a margin, clamped so it never leaves the host.</summary>
public static class ClusterAnchorMath
{
    public const double Margin = 16;

    /// <summary>The cluster's top-left corner in host coordinates.</summary>
    public static Point Place(ClusterAnchor anchor, Size host, Size cluster, double margin = Margin)
    {
        double x = anchor switch
        {
            ClusterAnchor.TopLeft => margin,
            ClusterAnchor.TopRight => host.Width - cluster.Width - margin,
            _ => (host.Width - cluster.Width) / 2,
        };
        double y = anchor == ClusterAnchor.BottomCenter ? host.Height - cluster.Height - margin : margin;

        x = System.Math.Clamp(x, 0, System.Math.Max(0, host.Width - cluster.Width));
        y = System.Math.Clamp(y, 0, System.Math.Max(0, host.Height - cluster.Height));
        return new Point(x, y);
    }
}
