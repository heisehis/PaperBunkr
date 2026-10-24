using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Media;

namespace Paperbunkr.App.Theming;

/// <summary>The type of a theme token. A role in <see cref="ThemeVocabulary"/> declares the kind it expects, so a number where a colour belongs is a diagnostic, not a crash.</summary>
public enum ThemeValueKind
{
    Color,
    Number,
    Text,
    Flag,

    /// <summary>A structured value kept as JSON (gradient stops, glyph lists).</summary>
    Json,
}

/// <summary>
/// Where a resolved value came from, lowest precedence first (design doc section 6.3). Recorded on every resolved token so tests, the
/// inspector and "reset to theme" can explain a value instead of guessing.
/// </summary>
public enum ThemeValueSource
{
    BuiltInFallback = 0,
    FamilyAuthored = 1,
    VariantAuthored = 2,
    UserGlobal = 3,
    UserFamily = 4,
    UserVariant = 5,
    UserComponent = 6,
    Accessibility = 7,

    /// <summary>Computed from other tokens because nothing set it. Not a precedence level: a derived value is used only when no layer has one.</summary>
    Derived = 20,

    /// <summary>The True Black transform (a legacy whole-surface override applied after everything else).</summary>
    TrueBlack = 21,
}

/// <summary>One typed theme value. Exactly one of the payload fields is meaningful, chosen by <see cref="Kind"/>.</summary>
public readonly record struct ThemeValue
{
    private ThemeValue(ThemeValueKind kind, Color color, double number, string? text, bool flag, JsonNode? json)
    {
        Kind = kind;
        Color = color;
        Number = number;
        Text = text;
        Flag = flag;
        Json = json;
    }

    public ThemeValueKind Kind { get; }

    public Color Color { get; }

    public double Number { get; }

    public string? Text { get; }

    public bool Flag { get; }

    public JsonNode? Json { get; }

    public static ThemeValue Of(Color color) => new(ThemeValueKind.Color, color, 0, null, false, null);

    public static ThemeValue Of(double number) => new(ThemeValueKind.Number, default, number, null, false, null);

    public static ThemeValue Of(string text) => new(ThemeValueKind.Text, default, 0, text, false, null);

    public static ThemeValue Of(bool flag) => new(ThemeValueKind.Flag, default, 0, null, flag, null);

    public static ThemeValue OfJson(JsonNode json) => new(ThemeValueKind.Json, default, 0, null, false, json);

    /// <summary>
    /// Reads a literal the way an author writes it: a string starting with '#' (or a colour name) is a colour when <paramref name="expected"/> allows,
    /// a JSON number is a number, a JSON bool a flag, any other string text. Returns null when the node is not a literal (an object or array).
    /// </summary>
    public static ThemeValue? FromLiteral(JsonNode? node, ThemeValueKind? expected = null)
    {
        if (node is not JsonValue value)
        {
            return node is null ? null : OfJson(node);
        }

        if (value.TryGetValue(out bool flag))
        {
            return Of(flag);
        }

        if (value.TryGetValue(out double number))
        {
            return Of(number);
        }

        if (value.TryGetValue(out string? text) && text is not null)
        {
            return FromText(text, expected);
        }

        return null;
    }

    /// <summary>Reads a value typed as text (a user override stored as a string): "#RRGGBB", "7", "true", or plain text.</summary>
    public static ThemeValue FromText(string text, ThemeValueKind? expected = null)
    {
        string trimmed = text.Trim();
        if (expected is null or ThemeValueKind.Color)
        {
            bool looksLikeColor = trimmed.StartsWith('#') || expected == ThemeValueKind.Color;
            if (looksLikeColor && ThemeColorMath.TryParse(trimmed, out var color))
            {
                return Of(color);
            }
        }

        if (expected == ThemeValueKind.Number && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return Of(number);
        }

        if (expected == ThemeValueKind.Flag && bool.TryParse(trimmed, out bool flag))
        {
            return Of(flag);
        }

        return Of(text);
    }

    public override string ToString() => Kind switch
    {
        ThemeValueKind.Color => ThemeColorMath.ToHex(Color),
        ThemeValueKind.Number => Number.ToString("R", CultureInfo.InvariantCulture),
        ThemeValueKind.Flag => Flag ? "true" : "false",
        ThemeValueKind.Json => Json?.ToJsonString() ?? "null",
        _ => Text ?? string.Empty,
    };
}

/// <summary>A token after resolution: its value, which layer supplied it, and (for a derived or adjusted value) a short note saying how.</summary>
public sealed record ResolvedToken(string Path, ThemeValue Value, ThemeValueSource Source, string? Note = null)
{
    public bool IsDerived => Source == ThemeValueSource.Derived;
}

/// <summary>How serious a <see cref="ThemeDiagnostic"/> is: an error means the value or package was rejected, a warning means it works with a caveat.</summary>
public enum ThemeDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// One finding about a theme (design doc section 13.2): a rejected value, a fallback that was used, a contrast pair below its target.
/// Report-only by design - nothing here ever rewrites what an author wrote.
/// </summary>
public sealed record ThemeDiagnostic(
    ThemeDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? PropertyPath = null,
    string? AuthoredValue = null,
    string? ResolvedValue = null,
    string? ThemeId = null,
    string? VariantId = null,
    string? ComponentId = null,
    ThemeValueSource? Source = null,
    bool FallbackUsed = false)
{
    public override string ToString() =>
        $"{Severity} {Code}{(PropertyPath is null ? string.Empty : $" [{PropertyPath}]")}: {Message}";
}

/// <summary>Stable diagnostic codes, so tests and logs can match on something other than wording.</summary>
public static class ThemeDiagnosticCodes
{
    public const string InvalidJson = "TH001";
    public const string ReferenceCycle = "TH002";
    public const string UnknownReference = "TH003";
    public const string TypeMismatch = "TH004";
    public const string UnsupportedSchema = "TH005";
    public const string MissingVariant = "TH006";
    public const string InvalidValue = "TH007";
    public const string LimitExceeded = "TH008";
    public const string LowContrast = "TH020";
    public const string FallbackUsed = "TH030";
    public const string UnsafePackage = "TH040";
    public const string InvalidAsset = "TH041";
    public const string InvalidLayout = "TH050";
    public const string InvalidMotion = "TH060";
    public const string MissingProvider = "TH070";
}
