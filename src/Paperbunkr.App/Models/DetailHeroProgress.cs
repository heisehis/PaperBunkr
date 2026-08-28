namespace Paperbunkr.App.Models;

/// <summary>
/// Tracker read-progress for a <c>DetailHero</c>'s corner ring (docs/superpowers/specs/
/// 2026-08-28-detail-screens-streaming-redesign-design.md). Populated only when a series is linked
/// to a tracking service (manga screen); null everywhere else, and the ring is hidden.
/// </summary>
public sealed record DetailHeroProgress(int Current, int Total, string Label)
{
    /// <summary>0..1 for the ring arc; 0 when <see cref="Total"/> is unknown.</summary>
    public double Fraction => Total > 0 ? System.Math.Clamp((double)Current / Total, 0, 1) : 0;
}
