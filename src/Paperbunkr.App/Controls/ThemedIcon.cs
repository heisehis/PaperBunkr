using System;
using System.IO;
using Path = Avalonia.Controls.Shapes.Path;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Paperbunkr.App.Theming;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Where <see cref="ThemedIcon"/> gets the active theme's icon replacements. Set by <c>ThemeService</c> on every apply; before that (and in a
/// host with no theme) every icon is its built-in glyph.
/// </summary>
public static class ThemeIconSource
{
    private static Func<string, ThemeIconOverride?>? s_resolve;
    private static Func<string, Stream?>? s_open;

    /// <summary>Raised when the active theme's icon set changed, so icons on screen redraw.</summary>
    public static event Action? Changed;

    public static void Set(Func<string, ThemeIconOverride?>? resolve, Func<string, Stream?>? open)
    {
        s_resolve = resolve;
        s_open = open;
        Changed?.Invoke();
    }

    public static ThemeIconOverride? Resolve(string name) => s_resolve?.Invoke(name);

    public static Stream? Open(string packagePath) => s_open?.Invoke(packagePath);
}

/// <summary>
/// An icon named by what it means (<c>navigation.home</c>) rather than by its glyph, so a theme can replace it (design doc section 7.2).
/// The built-in glyph is always the fallback: a missing, malformed or undecodable replacement shows the normal icon, never a blank. Only the
/// picture changes - the control it sits in keeps its command, tooltip and accessible name.
/// </summary>
public sealed partial class ThemedIcon : Decorator
{
    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<ThemedIcon, string?>(nameof(IconName));

    public static readonly StyledProperty<IconVariant> IconVariantProperty =
        AvaloniaProperty.Register<ThemedIcon, IconVariant>(nameof(IconVariant), IconVariant.Regular);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<ThemedIcon, double>(nameof(IconSize), 22);

    private const int MaxSvgBytes = 256 * 1024;

    [GeneratedRegex("<path\\b[^>]*?\\sd\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SvgPathData();

    /// <summary>The semantic name, from <see cref="ThemeIconRegistry"/>.</summary>
    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    /// <summary>Regular or Filled, for the built-in glyphs (a nav button fills its icon while its screen is showing).</summary>
    public IconVariant IconVariant
    {
        get => GetValue(IconVariantProperty);
        set => SetValue(IconVariantProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>What is currently drawn - for tests.</summary>
    public ThemeIconKind ShownKind { get; private set; } = ThemeIconKind.Default;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconNameProperty || change.Property == IconSizeProperty)
        {
            Rebuild();
        }
        else if (change.Property == IconVariantProperty && Child is SymbolIcon symbol)
        {
            symbol.IconVariant = IconVariant;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeIconSource.Changed += Rebuild;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeIconSource.Changed -= Rebuild;
        base.OnDetachedFromVisualTree(e);
    }

    private void Rebuild()
    {
        string name = IconName ?? string.Empty;
        var replacement = name.Length == 0 ? null : ThemeIconSource.Resolve(name);

        Control? content = replacement?.Kind switch
        {
            ThemeIconKind.Symbol => BuildSymbol(replacement.Value),
            ThemeIconKind.Path => BuildPath(replacement),
            ThemeIconKind.Image => BuildImage(replacement.Value),
            _ => null,
        };

        ShownKind = content is null ? ThemeIconKind.Default : replacement!.Kind;
        Child = content ?? BuildSymbol(ThemeIconRegistry.DefaultSymbol(name)) ?? new SymbolIcon { Symbol = Symbol.Circle, FontSize = IconSize };
    }

    private SymbolIcon? BuildSymbol(string symbolName) =>
        Enum.TryParse<Symbol>(symbolName, ignoreCase: false, out var symbol)
            ? new SymbolIcon { Symbol = symbol, IconVariant = IconVariant, FontSize = IconSize }
            : null;

    private Control? BuildPath(ThemeIconOverride replacement)
    {
        try
        {
            string? data = replacement.IsFile ? ReadSvgPathData(replacement.Value) : replacement.Value;
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            var path = new Path
            {
                Data = Geometry.Parse(data),
                Stretch = Stretch.Uniform,
                Width = IconSize,
                Height = IconSize,
            };

            // Drawn in the surrounding control's foreground, like a glyph, so hover / active colours still apply.
            path.Bind(Shape.FillProperty, this.GetObservable(TextElement.ForegroundProperty));
            return path;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Only the path outlines of an SVG are used. Nothing else in the file (scripts, links, external references) is ever read or followed.</summary>
    private static string? ReadSvgPathData(string packagePath)
    {
        using var stream = ThemeIconSource.Open(packagePath);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var buffer = new char[MaxSvgBytes];
        int read = reader.ReadBlock(buffer, 0, buffer.Length);
        var matches = SvgPathData().Matches(new string(buffer, 0, read));
        return matches.Count == 0 ? null : string.Join(' ', matches.Select(m => m.Groups[1].Value));
    }

    private Control? BuildImage(string packagePath)
    {
        try
        {
            using var stream = ThemeIconSource.Open(packagePath);
            if (stream is null)
            {
                return null;
            }

            return new Image { Source = new Bitmap(stream), Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform };
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
