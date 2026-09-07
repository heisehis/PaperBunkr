using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A vertical <see cref="StackPanel"/> that animates surviving children's repositioning when one
/// is added or removed (docs/superpowers/specs/2026-09-07-chrome-content-motion-polish-design.md
/// item 5's "smooth reflow" requirement for <c>PbToastHost</c>). FLIP technique (First-Last-Invert-
/// Play): after each arrange, any child whose Y position moved since the previous arrange gets its
/// <see cref="Visual.RenderTransform"/> jumped to the pre-move offset, then released back to
/// identity on the next dispatcher tick - letting whatever <c>Transitions</c> the child already
/// carries (e.g. the shared "entranceReady" style's <c>TransformOperationsTransition</c>) animate
/// the release smoothly. <c>RenderTransform</c> only, never <c>Margin</c>/<c>Height</c>, per the
/// motion skill's performance rule - this is what makes the reflow itself layout-jank-free.
///
/// Skips the jump entirely under Reduced Motion (the release still happens, just with nothing to
/// visibly interpolate since <see cref="MotionTokens"/>'s backing resource is zeroed).
/// </summary>
public sealed class AnimatedStackPanel : StackPanel
{
    // Rebuilt fresh every arrange (not mutated in place) so a removed child's entry is dropped
    // immediately instead of staying rooted forever - the previous version only ever added/updated
    // entries for controls currently in Children, which meant every toast ever shown (and its whole
    // visual tree/DataContext/bound commands) leaked for the life of the app once closed. Found via
    // a user memory-usage report, not caught in review.
    private Dictionary<Control, double> _lastArrangedY = new();

    protected override Size ArrangeOverride(Size finalSize)
    {
        var previous = _lastArrangedY;
        var result = base.ArrangeOverride(finalSize);
        var current = new Dictionary<Control, double>();

        if (MotionTokens.IsReducedMotion())
        {
            foreach (var child in Children)
            {
                current[child] = child.Bounds.Y;
            }

            _lastArrangedY = current;
            return result;
        }

        foreach (var child in Children)
        {
            double newY = child.Bounds.Y;
            if (previous.TryGetValue(child, out var oldY) && oldY != newY)
            {
                double delta = oldY - newY;
                var builder = new TransformOperations.Builder(1);
                builder.AppendTranslate(0, delta);
                child.RenderTransform = builder.Build();

                // Deferred so the transition system observes the jump before the release - setting
                // both values within the same call would coalesce into a no-op.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => child.RenderTransform = TransformOperations.Identity);
            }

            current[child] = newY;
        }

        _lastArrangedY = current;
        return result;
    }
}
