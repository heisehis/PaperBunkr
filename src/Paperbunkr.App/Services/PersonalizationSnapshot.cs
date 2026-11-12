using System;
using System.Text.Json.Nodes;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The Personalize values as the <c>personalization</c> block of an exported look (docs/superpowers/specs/2026-11-06-looks-and-presets-design.md). The corners are not
/// here: they are Global overrides, which the same file already carries. Data only; every value is validated and normalised on the way in.
/// </summary>
public sealed record PersonalizationSnapshot(
    string? Accent,
    int FontScale,
    string? HeadingFont,
    int CardElevation,
    SidebarStyle SidebarStyle,
    int FocusRingThickness,
    string? BodyFont,
    int RingStrength)
{
    public const string BlockName = "personalization";

    /// <summary>The block's own format; the file's root <c>version</c> is left alone so an older build can still read the theme part.</summary>
    public const int FormatVersion = 1;

    public static PersonalizationSnapshot From(AppSettings settings) => new(
        settings.AccentOverrideHex,
        settings.FontScale,
        settings.HeadingFontFamily,
        settings.CardElevation,
        settings.SidebarStyle,
        settings.FocusRingThickness,
        settings.SelectedFontFamily,
        settings.GlowTier);

    public JsonObject ToJson() => new()
    {
        ["formatVersion"] = FormatVersion,
        ["accent"] = Accent,
        ["fontScale"] = FontScale,
        ["headingFont"] = HeadingFont,
        ["cardElevation"] = CardElevation,
        ["sidebarStyle"] = SidebarStyle.ToString(),
        ["focusRingThickness"] = FocusRingThickness,
        ["bodyFont"] = BodyFont,
        ["ringStrength"] = RingStrength,
    };

    public enum ReadResult
    {
        /// <summary>The file has no block (every look exported before this slice).</summary>
        Absent,

        Read,

        /// <summary>The block was written by a newer format; it is skipped, the rest of the file still imports.</summary>
        NewerFormat,
    }

    /// <summary>Reads the block from an exported file's root. Missing or wrong-typed values fall back to the defaults; unknown keys are ignored.</summary>
    public static ReadResult TryRead(JsonObject root, out PersonalizationSnapshot? snapshot)
    {
        snapshot = null;
        if (root[BlockName] is not JsonObject block)
        {
            return ReadResult.Absent;
        }

        if (Int(block, "formatVersion", FormatVersion) > FormatVersion)
        {
            return ReadResult.NewerFormat;
        }

        string? accent = Text(block, "accent") is { } hex && AccentColorInput.TryParseHex(hex, out var color) ? AccentColorInput.ToHex(color) : null;
        var sidebar = Enum.TryParse<SidebarStyle>(Text(block, "sidebarStyle"), ignoreCase: true, out var style) && Enum.IsDefined(style) ? style : SidebarStyle.Standard;

        snapshot = new PersonalizationSnapshot(
            accent,
            PersonalizationMath.NormalizeFontScale(Int(block, "fontScale", 100)),
            Text(block, "headingFont"),
            PersonalizationMath.NormalizeCardElevation(Int(block, "cardElevation", 1)),
            sidebar,
            PersonalizationMath.NormalizeFocusThickness(Int(block, "focusRingThickness", 1)),
            Text(block, "bodyFont"),
            ThemeService.NormalizeGlowTier(Int(block, "ringStrength", ThemeService.GlowTierNormal)));
        return ReadResult.Read;
    }

    private static string? Text(JsonObject block, string key) =>
        block[key] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static int Int(JsonObject block, string key, int fallback)
    {
        if (block[key] is not JsonValue value)
        {
            return fallback;
        }

        // A JsonValue made from an int in code does not TryGetValue<double>; parse from text so both kinds work (theme-system note).
        return int.TryParse(value.ToJsonString().Trim('"'), out int number) ? number : fallback;
    }
}
