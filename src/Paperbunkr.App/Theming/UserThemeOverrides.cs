using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Paperbunkr.App.Theming;

/// <summary>How much visual effect work the user allows (design doc section 9.3). Gates materials and theme-defined motion.</summary>
public enum ThemeEffectQuality
{
    /// <summary>Everything the theme declares.</summary>
    High,

    /// <summary>Finite animations and static materials; no continuous decorative motion.</summary>
    Balanced,

    /// <summary>Solid fallbacks and no theme motion.</summary>
    Performance,
}

/// <summary>Which of the user's layers an override belongs to (design doc section 6.3, levels 4 to 6).</summary>
public enum ThemeOverrideScope
{
    Global,
    Family,
    Variant,
}

/// <summary>
/// One scope's worth of user values: token path (or component path) to the value as text. Values are stored as the user typed them
/// ("#3366CC", "9") and typed when resolved, so a stored override survives a role changing how it is parsed.
/// </summary>
public sealed class ThemeOverrideLayer
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Values.Count == 0;

    public ThemeOverrideLayer Clone()
    {
        var copy = new ThemeOverrideLayer();
        foreach (var (path, value) in Values)
        {
            copy.Values[path] = value;
        }

        return copy;
    }
}

/// <summary>A family's overrides: values for the whole family, values per variant, and layout patches per screen.</summary>
public sealed class ThemeFamilyOverrides
{
    public ThemeOverrideLayer Family { get; init; } = new();

    public Dictionary<string, ThemeOverrideLayer> Variants { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Screen name, then layout node id, then property: the user's adjustments to a theme's layout, kept as stable-id patches so they survive a theme update (design doc section 8.6).</summary>
    public Dictionary<string, Dictionary<string, JsonObject>> LayoutPatches { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Family.IsEmpty && Variants.Values.All(v => v.IsEmpty) && LayoutPatches.Count == 0;
}

/// <summary>
/// Everything the user has changed about themes, kept apart from the installed packages (which are never modified). Pure data: the store
/// reads and writes it, the resolver consumes it.
/// </summary>
public sealed class UserThemeOverrides
{
    public ThemeOverrideLayer Global { get; init; } = new();

    /// <summary>Keyed by <see cref="ThemeDocument.Id"/>, the family's stable identity.</summary>
    public Dictionary<string, ThemeFamilyOverrides> Families { get; } = new(StringComparer.Ordinal);

    /// <summary>The explicit, user-enabled accessibility adjustment (design doc section 13.1): raise text roles to their contrast target. Off by default.</summary>
    public bool BoostContrast { get; set; }

    public ThemeEffectQuality EffectQuality { get; set; } = ThemeEffectQuality.High;

    /// <summary>
    /// Whether a theme may rearrange screens (design doc decision D11). Off by default: a theme's colours, type and skins always apply, but
    /// its layouts only once the user turns this on - they are the newest and riskiest thing a theme can do.
    /// </summary>
    public bool AllowThemeLayouts { get; set; }

    public ThemeFamilyOverrides? FindFamily(string familyId) => Families.TryGetValue(familyId, out var family) ? family : null;

    public ThemeFamilyOverrides GetOrAddFamily(string familyId)
    {
        if (!Families.TryGetValue(familyId, out var family))
        {
            family = new ThemeFamilyOverrides();
            Families[familyId] = family;
        }

        return family;
    }

    /// <summary>The layer for a scope, or null when nothing has been stored there (never creates one).</summary>
    public ThemeOverrideLayer? Find(ThemeOverrideScope scope, string familyId, string variantId) => scope switch
    {
        ThemeOverrideScope.Global => Global,
        ThemeOverrideScope.Family => FindFamily(familyId)?.Family,
        _ => FindFamily(familyId) is { } family && family.Variants.TryGetValue(variantId, out var variant) ? variant : null,
    };

    public ThemeOverrideLayer GetOrAdd(ThemeOverrideScope scope, string familyId, string variantId)
    {
        if (scope == ThemeOverrideScope.Global)
        {
            return Global;
        }

        var family = GetOrAddFamily(familyId);
        if (scope == ThemeOverrideScope.Family)
        {
            return family.Family;
        }

        if (!family.Variants.TryGetValue(variantId, out var variant))
        {
            variant = new ThemeOverrideLayer();
            family.Variants[variantId] = variant;
        }

        return variant;
    }

    /// <summary>Drops families and variants that hold nothing, so a reset leaves no empty shells behind in the file.</summary>
    public void Compact()
    {
        foreach (var (id, family) in Families.ToList())
        {
            foreach (var (variantId, layer) in family.Variants.ToList())
            {
                if (layer.IsEmpty)
                {
                    family.Variants.Remove(variantId);
                }
            }

            if (family.IsEmpty)
            {
                Families.Remove(id);
            }
        }
    }

    public static UserThemeOverrides Empty => new();
}

/// <summary>What the resolver needs to know about the running app that is not part of the theme or the user's saved overrides.</summary>
public sealed record ThemeEnvironment
{
    /// <summary>The legacy "True black" option. Ignored for a light variant and for a designed OLED variant.</summary>
    public bool TrueBlack { get; init; }

    /// <summary>The pre-v2 global accent override ("#RRGGBB"), still honoured as a user-global value for <c>action.primary</c>.</summary>
    public string? AccentOverrideHex { get; init; }

    public static ThemeEnvironment Default { get; } = new();
}
