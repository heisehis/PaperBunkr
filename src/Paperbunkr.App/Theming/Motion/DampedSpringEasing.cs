using System;
using Avalonia.Animation.Easings;

namespace Paperbunkr.App.Theming.Motion;

/// <summary>
/// A real damped spring (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §3.2): the underdamped step
/// response x(t) = 1 − e^(−ζωt)·(cos ω_d t + (ζω/ω_d)·sin ω_d t), ω_d = ω√(1−ζ²), in normalised time. With ζ = 0.745 and ω = 7.6 it
/// overshoots once by about 3 %, peaks near 62 % of the duration and is within 0.3 % of rest at the end, where it returns exactly 1.
/// This replaces <c>BackEaseOut</c>, which overshoots and comes back on every input instead of settling once.
/// Used only for hover lift and panel arrival (Q1). Exposed as <c>PbMotionSpring</c>.
/// </summary>
public sealed class DampedSpringEasing : Easing
{
    public double Damping { get; set; } = 0.745;

    public double Frequency { get; set; } = 7.6;

    public override double Ease(double progress)
    {
        if (progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        double zeta = Damping;
        double omega = Frequency;
        double omegaD = omega * Math.Sqrt(1 - (zeta * zeta));
        double envelope = Math.Exp(-zeta * omega * progress);
        return 1 - (envelope * (Math.Cos(omegaD * progress) + (zeta * omega / omegaD * Math.Sin(omegaD * progress))));
    }
}
