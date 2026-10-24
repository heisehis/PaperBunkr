using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Theming.Runtime;

/// <summary>
/// Turns a theme's named materials into brushes (design doc section 7.4): solid, linear and radial gradients, and packaged images or textures.
/// Every material has a solid fallback colour, which is what the Performance quality tier uses and what a material that cannot be built
/// (a missing or undecodable image) degrades to. Images come only from the theme's own package - never from a URL.
///
/// Two things this deliberately does not do. A window backdrop (Mica / Acrylic) is an operating-system feature of the whole window, set by
/// <c>settings.windowBackdrop</c>, not a brush a card can have. And there is no per-element blur: the framework has no general backdrop blur for
/// an arbitrary element, so an <c>acrylic</c> material resolves to its tint colour.
/// </summary>
public sealed class ThemeMaterialBuilder
{
    public const string MaterialPrefix = "material:";

    private readonly ResolvedTheme _theme;
    private readonly ThemeEffectQuality _quality;
    private readonly Func<string, Stream?> _openAsset;
    private readonly Dictionary<string, Bitmap> _imageCache;
    private readonly Dictionary<string, IBrush?> _built = new(StringComparer.Ordinal);

    public ThemeMaterialBuilder(ResolvedTheme theme, ThemeEffectQuality quality, Func<string, Stream?> openAsset, Dictionary<string, Bitmap>? imageCache = null)
    {
        _theme = theme;
        _quality = quality;
        _openAsset = openAsset;
        _imageCache = imageCache ?? new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    }

    public List<ThemeDiagnostic> Diagnostics { get; } = new();

    /// <summary>The material name in a <c>material:paper</c> value, or null when the text is not a material reference.</summary>
    public static string? MaterialName(string? text) =>
        text is not null && text.StartsWith(MaterialPrefix, StringComparison.OrdinalIgnoreCase) ? text[MaterialPrefix.Length..].Trim() : null;

    /// <summary>The brush for a named material, or null when the theme does not define it.</summary>
    public IBrush? Build(string name)
    {
        if (_built.TryGetValue(name, out var cached))
        {
            return cached;
        }

        IBrush? brush = _theme.Materials.TryGetValue(name, out var definition) ? BuildCore(name, definition) : null;
        if (brush is null && definition is null)
        {
            Diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.UnknownReference, $"Material '{name}' is not defined; the plain colour is used.", $"materials.{name}", ThemeId: _theme.FamilyId, FallbackUsed: true));
        }

        _built[name] = brush;
        return brush;
    }

    /// <summary>The solid colour a material degrades to: its <c>fallback</c>, else its first stop / colour.</summary>
    public Color? FallbackColor(string name)
    {
        if (!_theme.Materials.TryGetValue(name, out var definition))
        {
            return null;
        }

        return ReadColor(definition["fallback"]) ?? ReadColor(definition["color"]) ?? ReadStops(definition).Select(s => (Color?)s.Color).FirstOrDefault();
    }

    private IBrush? BuildCore(string name, JsonObject definition)
    {
        string type = ReadString(definition, "type") ?? "solid";
        double opacity = Math.Clamp(ReadNumber(definition, "opacity") ?? 1, 0, 1);
        Color? fallback = FallbackColor(name);

        IBrush? Solid() => fallback is { } color ? new SolidColorBrush(color, opacity) : null;

        // The Performance tier keeps every surface a flat colour.
        if (_quality == ThemeEffectQuality.Performance)
        {
            return Solid();
        }

        try
        {
            switch (type.ToLowerInvariant())
            {
                case "solid":
                case "acrylic": // no per-element blur exists; the tint is the honest fallback
                    return Solid();

                case "lineargradient":
                {
                    var stops = ReadStops(definition);
                    if (stops.Count < 2)
                    {
                        return Fallback(name, "needs at least two stops", Solid());
                    }

                    var (start, end) = AnglePoints(ReadNumber(definition, "angle") ?? 180);
                    var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end, Opacity = opacity };
                    brush.GradientStops.AddRange(stops);
                    return brush;
                }

                case "radialgradient":
                {
                    var stops = ReadStops(definition);
                    if (stops.Count < 2)
                    {
                        return Fallback(name, "needs at least two stops", Solid());
                    }

                    double cx = 0.5, cy = 0.5;
                    if (definition["center"] is JsonArray { Count: 2 } center)
                    {
                        cx = center[0]?.GetValue<double>() ?? 0.5;
                        cy = center[1]?.GetValue<double>() ?? 0.5;
                    }

                    double radius = Math.Clamp(ReadNumber(definition, "radius") ?? 0.5, 0.01, 4);
                    var brush = new RadialGradientBrush
                    {
                        Center = new RelativePoint(cx, cy, RelativeUnit.Relative),
                        GradientOrigin = new RelativePoint(cx, cy, RelativeUnit.Relative),
                        RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
                        RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
                        Opacity = opacity,
                    };
                    brush.GradientStops.AddRange(stops);
                    return brush;
                }

                case "image":
                case "texture":
                {
                    string? source = ReadString(definition, "source");
                    if (source is null || !ThemePackagePaths.TryNormalize(source, out string normalized))
                    {
                        return Fallback(name, "has no source inside the theme package", Solid());
                    }

                    var bitmap = LoadImage(normalized);
                    if (bitmap is null)
                    {
                        return Fallback(name, $"could not load '{normalized}'", Solid());
                    }

                    bool tile = type.Equals("texture", StringComparison.OrdinalIgnoreCase) || (definition["tile"] is JsonValue t && t.TryGetValue(out bool flag) && flag);
                    var brush = new ImageBrush(bitmap) { Opacity = opacity };
                    if (tile)
                    {
                        brush.TileMode = TileMode.Tile;
                        brush.Stretch = Stretch.None;
                        brush.DestinationRect = new RelativeRect(0, 0, bitmap.Size.Width, bitmap.Size.Height, RelativeUnit.Absolute);
                    }
                    else
                    {
                        brush.Stretch = (ReadString(definition, "stretch") ?? "uniformToFill").ToLowerInvariant() switch
                        {
                            "fill" => Stretch.Fill,
                            "uniform" => Stretch.Uniform,
                            "none" => Stretch.None,
                            _ => Stretch.UniformToFill,
                        };
                    }

                    return brush;
                }

                default:
                    return Fallback(name, $"has an unknown type '{type}'", Solid());
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or IOException)
        {
            return Fallback(name, ex.Message, Solid());
        }
    }

    private IBrush? Fallback(string name, string reason, IBrush? solid)
    {
        Diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.FallbackUsed, $"Material '{name}' {reason}; its fallback colour is used.", $"materials.{name}", ThemeId: _theme.FamilyId, FallbackUsed: true));
        return solid;
    }

    private Bitmap? LoadImage(string normalizedPath)
    {
        string cacheKey = $"{_theme.FamilyKey}|{normalizedPath}";
        if (_imageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = _openAsset(normalizedPath);
            if (stream is null)
            {
                return null;
            }

            var bitmap = new Bitmap(stream);
            _imageCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>CSS angle convention: 0 = to the top, 90 = to the right, 180 = to the bottom (the default).</summary>
    private static (RelativePoint Start, RelativePoint End) AnglePoints(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        double dx = Math.Sin(radians) / 2;
        double dy = -Math.Cos(radians) / 2;
        return (new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative), new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative));
    }

    private List<GradientStop> ReadStops(JsonObject definition)
    {
        var stops = new List<GradientStop>();
        if (definition["stops"] is not JsonArray array)
        {
            return stops;
        }

        int index = 0;
        foreach (var node in array)
        {
            double fallbackOffset = array.Count <= 1 ? 0 : (double)index / (array.Count - 1);
            index++;
            if (node is JsonObject stop && ReadColor(stop["color"]) is { } color)
            {
                stops.Add(new GradientStop(color, Math.Clamp(ReadNumber(stop, "at") ?? fallbackOffset, 0, 1)));
            }
            else if (ReadColor(node) is { } bare)
            {
                stops.Add(new GradientStop(bare, fallbackOffset));
            }
        }

        return stops;
    }

    /// <summary>A colour literal, or a <c>{ "ref": "..." }</c> to any colour token of the theme.</summary>
    private Color? ReadColor(JsonNode? node)
    {
        if (node is JsonObject { Count: 1 } reference && reference["ref"] is JsonValue target && target.TryGetValue(out string? path))
        {
            return _theme.Color(path);
        }

        return node is JsonValue value && value.TryGetValue(out string? text) && ThemeColorMath.TryParse(text, out var color) ? color : null;
    }

    private static string? ReadString(JsonObject source, string name) =>
        source[name] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static double? ReadNumber(JsonObject source, string name) =>
        source[name] is JsonValue value && value.TryGetValue(out double number) ? number : null;
}
