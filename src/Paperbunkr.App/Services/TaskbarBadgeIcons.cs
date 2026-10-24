using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Paperbunkr.App.Services;

/// <summary>
/// Draws the taskbar overlay badge (an accent disc with the count, "9+" past nine) and the accent-dotted
/// tray variant for Cosmetics pitch #24. Pure drawing, UI thread only; the label text is the one thing a
/// test can pin without a window.
/// </summary>
public static class TaskbarBadgeIcons
{
    public const int OverlaySize = 32;

    public static string LabelFor(int count) => count <= 0 ? string.Empty : count > 9 ? "9+" : count.ToString(CultureInfo.InvariantCulture);

    private static Color AccentColor()
    {
        if (Application.Current is { } app
            && app.TryFindResource("PbAccentBrush", app.ActualThemeVariant, out var found)
            && found is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return Color.Parse("#3A6EA5");
    }

    public static RenderTargetBitmap RenderOverlay(int count)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(OverlaySize, OverlaySize), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var centre = new Point(OverlaySize / 2.0, OverlaySize / 2.0);
            context.DrawEllipse(new ImmutableSolidColorBrush(AccentColor()), new Pen(Brushes.White, 2), centre, 14, 14);
            var text = new FormattedText(LabelFor(count), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), count > 9 ? 14 : 18, Brushes.White);
            context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
        }

        return bitmap;
    }

    /// <summary>The tray logo with a small accent dot top-right, or the plain logo when <paramref name="withDot"/> is false.</summary>
    public static WindowIcon RenderTray(bool withDot)
    {
        using var logoStream = AssetLoader.Open(new Uri("avares://Paperbunkr.App/Assets/paperbunkr-logo-rail.png"));
        using var logo = new Bitmap(logoStream);
        var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawImage(logo, new Rect(logo.Size), new Rect(0, 0, 32, 32));
            if (withDot)
            {
                context.DrawEllipse(new ImmutableSolidColorBrush(AccentColor()), new Pen(Brushes.White, 2), new Point(25, 7), 6, 6);
            }
        }

        return new WindowIcon(bitmap);
    }
}
