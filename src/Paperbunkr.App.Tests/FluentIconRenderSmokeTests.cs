using Avalonia;
using Avalonia.Controls;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Smoke check that <see cref="SymbolIcon"/> (FluentIcons.Avalonia) actually produces a glyph -
/// i.e. the bundled icon font from FluentIcons.Resources.Avalonia is found without any extra
/// StyleInclude (docs/superpowers/specs/2026-08-28-fluenticons-migration-design.md). If this ever
/// regresses to a zero size, the whole app's icons have gone blank.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FluentIconRenderSmokeTests
{
    [Fact]
    public void SymbolIcon_MeasuresToANonZeroGlyph()
    {
        var icon = new SymbolIcon { Symbol = Symbol.Search, FontSize = 16, IconVariant = IconVariant.Regular };

        icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(icon.DesiredSize.Width > 0, $"width was {icon.DesiredSize.Width}");
        Assert.True(icon.DesiredSize.Height > 0, $"height was {icon.DesiredSize.Height}");
    }

    [Fact]
    public void SymbolIcon_InheritsForegroundFromItsParent()
    {
        var icon = new SymbolIcon { Symbol = Symbol.Search };
        var parent = new Avalonia.Controls.Button
        {
            Foreground = Avalonia.Media.Brushes.Red,
            Content = icon,
        };
        var root = new Avalonia.Controls.ContentControl { Content = parent };
        root.Measure(new Size(500, 500));
        root.Arrange(new Rect(0, 0, 500, 500));

        Assert.Equal(Avalonia.Media.Brushes.Red, icon.Foreground);
    }
}
