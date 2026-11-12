using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Two or three covers fanned like a hand of cards (docs/superpowers/specs/2026-11-06-collection-identity-design.md, the "Fan" collection cover style):
/// the first source is the front card, the others peek out behind it at +/- 6 degrees. Drawn directly in one control (no per-image template parts, no
/// animation). Fewer than two sources draws just the one; the layout never leaves an empty slot.
/// </summary>
public sealed class FanCoverStack : Control
{
    public static readonly StyledProperty<IReadOnlyList<IImage?>?> SourcesProperty =
        AvaloniaProperty.Register<FanCoverStack, IReadOnlyList<IImage?>?>(nameof(Sources));

    /// <summary>Front card first. At most three are drawn.</summary>
    public IReadOnlyList<IImage?>? Sources
    {
        get => GetValue(SourcesProperty);
        set => SetValue(SourcesProperty, value);
    }

    static FanCoverStack()
    {
        AffectsRender<FanCoverStack>(SourcesProperty);
    }

    /// <summary>The (angle in degrees, horizontal shift as a fraction of the width) of each card, back card first, for a given count.</summary>
    internal static IReadOnlyList<(double Angle, double Shift)> Layout(int count) => count switch
    {
        <= 1 => new[] { (0.0, 0.0) },
        2 => new[] { (6.0, 0.06), (-2.0, -0.03) },
        _ => new[] { (6.0, 0.08), (-6.0, -0.08), (0.0, 0.0) },
    };

    public override void Render(DrawingContext context)
    {
        var sources = Sources;
        if (sources is null || sources.Count == 0)
        {
            return;
        }

        int count = Math.Min(3, sources.Count);
        var layout = Layout(count);
        double w = Bounds.Width, h = Bounds.Height;
        double cardW = w * 0.78, cardH = h * 0.86;
        var center = new Point(w / 2, h / 2 + h * 0.03);

        // Back card first; sources[0] is the front card, so draw in reverse source order.
        for (int slot = 0; slot < count; slot++)
        {
            int sourceIndex = count - 1 - slot;
            var image = sources[sourceIndex];
            var (angle, shift) = layout[slot];
            var rect = new Rect(center.X - cardW / 2 + shift * w, center.Y - cardH / 2, cardW, cardH);
            var rotation = Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateRotation(angle * Math.PI / 180.0)
                * Matrix.CreateTranslation(center.X, center.Y);

            using (context.PushTransform(rotation))
            using (context.PushClip(new RoundedRect(rect, 4)))
            {
                if (image is null)
                {
                    context.DrawRectangle(Brushes.DimGray, null, rect);
                }
                else
                {
                    var size = image.Size;
                    double scale = Math.Max(cardW / size.Width, cardH / size.Height);
                    var src = new Rect(
                        (size.Width - cardW / scale) / 2, (size.Height - cardH / scale) / 2, cardW / scale, cardH / scale);
                    context.DrawImage(image, src, rect);
                }
            }

            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1), rect, 4, 4);
        }
    }
}
