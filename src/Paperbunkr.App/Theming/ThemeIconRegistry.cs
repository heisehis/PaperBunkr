using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Theming;

/// <summary>
/// The semantic icon names a theme may replace (design doc section 7.2), each with the built-in glyph it falls back to. A theme swaps how an
/// icon looks; the action, the command and the accessible name of the control it sits on are not the icon's to change, and do not change.
/// The value is the name of a FluentIcons symbol, kept as text so this registry stays free of UI types.
/// </summary>
public static class ThemeIconRegistry
{
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["navigation.home"] = "Home",
        ["navigation.insights"] = "DataHistogram",
        ["navigation.library"] = "Library",
        ["navigation.books"] = "BookOpen",
        ["navigation.smart"] = "Filter",
        ["navigation.reading"] = "Bookmark",
        ["navigation.continuity"] = "Planet",
        ["navigation.wanted"] = "Search",
        ["navigation.preferences"] = "Settings",
        ["navigation.pin"] = "Pin",
        ["action.search"] = "Search",
        ["action.add"] = "Add",
        ["action.delete"] = "Delete",
        ["action.close"] = "Dismiss",
        ["action.edit"] = "Edit",
        ["action.refresh"] = "ArrowSync",
        ["action.more"] = "MoreVertical",
        ["reader.next"] = "ChevronRight",
        ["reader.previous"] = "ChevronLeft",
        ["reader.bookmark"] = "Bookmark",
        ["reader.settings"] = "Options",
        ["status.success"] = "Checkmark",
        ["status.warning"] = "Warning",
        ["status.info"] = "Info",
    };

    public static IReadOnlyCollection<string> Names => Defaults.Keys;

    public static bool IsKnown(string name) => Defaults.ContainsKey(name);

    /// <summary>The built-in symbol for a semantic icon; a name the registry does not know falls back to a neutral placeholder rather than failing.</summary>
    public static string DefaultSymbol(string name) => Defaults.TryGetValue(name, out var symbol) ? symbol : "Circle";

    public const string SymbolPrefix = "symbol:";
    public const string PathPrefix = "path:";
}

/// <summary>What kind of replacement a theme supplies for one icon.</summary>
public enum ThemeIconKind
{
    /// <summary>No replacement: the built-in glyph.</summary>
    Default,

    /// <summary>Another built-in glyph, by name (<c>symbol:Rocket</c>).</summary>
    Symbol,

    /// <summary>Vector path data drawn in the control's foreground (<c>path:M0 0 ...</c>, or an <c>.svg</c> file's paths).</summary>
    Path,

    /// <summary>A raster image from the theme package, shown as it is (not tinted).</summary>
    Image,
}

/// <summary>A theme's replacement for one icon, already classified. <see cref="Value"/> is the symbol name, the path data, or the package-relative file.</summary>
public sealed record ThemeIconOverride(ThemeIconKind Kind, string Value)
{
    /// <summary>Classifies an <c>icons.&lt;name&gt;</c> value. Null for something unusable, which means "use the built-in glyph".</summary>
    public static ThemeIconOverride? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string value = text.Trim();
        if (value.StartsWith(ThemeIconRegistry.SymbolPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string symbol = value[ThemeIconRegistry.SymbolPrefix.Length..].Trim();
            return symbol.Length > 0 && symbol.All(char.IsLetterOrDigit) ? new ThemeIconOverride(ThemeIconKind.Symbol, symbol) : null;
        }

        if (value.StartsWith(ThemeIconRegistry.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string data = value[ThemeIconRegistry.PathPrefix.Length..].Trim();
            return data.Length is > 0 and <= 16384 ? new ThemeIconOverride(ThemeIconKind.Path, data) : null;
        }

        if (!ThemePackagePaths.TryNormalize(value, out string normalized))
        {
            return null;
        }

        string extension = System.IO.Path.GetExtension(normalized).ToLowerInvariant();
        return extension switch
        {
            ".svg" => new ThemeIconOverride(ThemeIconKind.Path, normalized),
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" => new ThemeIconOverride(ThemeIconKind.Image, normalized),
            _ => null,
        };
    }

    /// <summary>True when <see cref="Value"/> is a file in the package rather than inline data.</summary>
    public bool IsFile => Kind == ThemeIconKind.Image || (Kind == ThemeIconKind.Path && Value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
}
