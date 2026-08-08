using System.Collections.Generic;

namespace Paperbunkr.App.Models;

/// <summary>
/// Deserialized <c>theme.json</c> (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §2) - field names map 1:1 onto the existing <c>Pb*</c> token suffixes in App.axaml (e.g.
/// <see cref="SkinColors.Bg"/> → <c>PbBgColor</c>/<c>PbBgBrush</c>).
/// </summary>
public class SkinTheme
{
    public string Name { get; set; } = "Unnamed";

    public SkinColors Colors { get; set; } = new();

    public double SpacingUnit { get; set; } = 4;

    public double Radius { get; set; } = 7;

    /// <summary>Icon key -> relative path within the skin (e.g. "icons/library.png"). Not consumed by any UI yet - see design spec §2.</summary>
    public Dictionary<string, string> Icons { get; set; } = new();
}

public class SkinColors
{
    public string Bg { get; set; } = "#14161B";
    public string Chrome { get; set; } = "#1C1F26";
    public string Border { get; set; } = "#2A2E37";
    public string Text { get; set; } = "#ECE7DB";
    public string TextMuted { get; set; } = "#B3ADA0";
    public string TextFaint { get; set; } = "#77726A";
    public string Accent { get; set; } = "#C9803F";
    public string AccentText { get; set; } = "#E0995A";
    public string AccentSoft { get; set; } = "#29C9803F";
    public string Badge { get; set; } = "#D7AC4C";
    public string BadgeText { get; set; } = "#241505";
    public string Success { get; set; } = "#5FA889";
}
