using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The root of the app-wide keyboard focus ring (Styles/Primitives.axaml, the <c>FocusAdorner</c> template). Avalonia clips an adorner to its adorned control's clip, and a Button clips to its own
/// bounds, so a ring painted outside the control was cut down to a corner speck; the template therefore turns that clipping off (<c>AdornerLayer.IsClipEnabled</c>). That alone lets the ring of a
/// control that has scrolled out of its list draw over whatever is at that spot on screen (the page header, say), so this control puts back the one clip that matters: the viewport of each scroll
/// viewer the adorned control sits in. It does not clip to the control itself, so the ring still shows around it.
/// </summary>
public sealed class FocusRingAdorner : Border
{
    public FocusRingAdorner()
    {
        LayoutUpdated += (_, _) => UpdateClip();
    }

    private void UpdateClip()
    {
        if (AdornerLayer.GetAdornedElement(this) is not { } adorned)
        {
            return;
        }

        Rect? visible = null;
        foreach (var ancestor in adorned.GetVisualAncestors())
        {
            if (ancestor is not ScrollViewer viewer || viewer.TranslatePoint(default, adorned) is not { } origin)
            {
                continue;
            }

            var viewport = new Rect(origin, viewer.Bounds.Size);
            visible = visible is { } so ? so.Intersect(viewport) : viewport;
        }

        // The clip is in this control's own space, whose origin is the adorned control's top-left shifted by this control's (negative) margin: the template inflates it so the ring paints inside bounds.
        var clip = visible is { } rect ? new RectangleGeometry(rect.Translate(new Vector(-Margin.Left, -Margin.Top))) : null;
        if (!SameClip(Clip as RectangleGeometry, clip))
        {
            Clip = clip;
        }
    }

    private static bool SameClip(RectangleGeometry? a, RectangleGeometry? b) =>
        (a is null && b is null) || (a is not null && b is not null && a.Rect == b.Rect);
}
