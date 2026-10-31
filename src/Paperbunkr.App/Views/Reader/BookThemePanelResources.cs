using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Reader;

/// <summary>
/// Makes a reader panel (Contents, Notes, Search, the Aa sheet) look like part of the page (docs/superpowers/specs/2026-10-27-books-section-
/// redesign-design.md §5.3): the panel's own resources redefine the app's surface, text and border tokens from the book theme's page and
/// text colours, so everything inside that asks for <c>PbTextBrush</c>, <c>PbTextMutedBrush</c>, <c>PbSurface3Brush</c> and so on resolves to
/// a colour that reads on a sepia, light or dark page alike, without restyling a single row. The accent stays the app's.
/// </summary>
public static class BookThemePanelResources
{
    public static void Apply(IResourceDictionary resources, IBrush? background, IBrush? foreground)
    {
        if (background is not ISolidColorBrush bg || foreground is not ISolidColorBrush fg)
        {
            return;
        }

        var page = Color.FromRgb(bg.Color.R, bg.Color.G, bg.Color.B);
        var ink = fg.Color;
        IBrush Ink(byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));

        resources["PbChromeBrush"] = new SolidColorBrush(page);
        resources["PbSurface1Brush"] = new SolidColorBrush(page);
        resources["PbSurface2Brush"] = Ink(0x0F);
        resources["PbSurface3Brush"] = Ink(0x1A);
        resources["PbBorderBrush"] = Ink(0x2E);
        resources["PbTextBrush"] = new SolidColorBrush(ink);
        resources["PbTextMutedBrush"] = Ink(0xC0);
        resources["PbTextFaintBrush"] = Ink(0x80);
    }

    /// <summary>The panel's own (opaque) page colour for a book theme: the reading surface, not the translucent chrome tint.</summary>
    public static IBrush PanelBackground(Paperbunkr.Data.Entities.BookTheme theme) => Models.BookThemeBrushes.ContentBackground(theme);
}
