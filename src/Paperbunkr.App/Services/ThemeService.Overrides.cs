using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;

namespace Paperbunkr.App.Services;

/// <summary>
/// The user's theme customizations (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 2; design doc sections 6.3
/// and 6.4): values scoped to every theme, to the active family, or to the active variant, kept in the override store and never written into
/// an installed package. Every change is applied live by re-resolving the active theme.
/// </summary>
public partial class ThemeService
{
    /// <summary>Raised after an override, the contrast boost or the effect quality changed and the active theme was re-applied.</summary>
    public event Action? OverridesChanged;

    public UserThemeOverrides GetOverrides() => _overrideStore.Load();

    /// <summary>The family and variant that overrides scoped to "this theme" / "this variant" apply to: the active ones. Null before a theme is applied.</summary>
    public (string FamilyId, string VariantId)? ActiveOverrideTarget
    {
        get
        {
            using var context = _contextFactory();
            string activeKey = context.GetOrCreateAppSettings().ActiveThemeKey;
            return _catalog.TryFind(activeKey, out var document, out var variant) ? (document.Id, variant.Id) : null;
        }
    }

    /// <summary>The user's own value for <paramref name="path"/> at <paramref name="scope"/>, or null when that scope does not set it.</summary>
    public string? GetOverride(ThemeOverrideScope scope, string path)
    {
        if (ActiveOverrideTarget is not { } target)
        {
            return null;
        }

        var layer = _overrideStore.Load().Find(scope, target.FamilyId, target.VariantId);
        return layer is not null && layer.Values.TryGetValue(ThemeTokenPaths.Normalize(path), out var value) ? value : null;
    }

    /// <summary>
    /// Sets (or, with a null or empty <paramref name="value"/>, removes) one override and applies it live. Returns false - and changes
    /// nothing - when the value is not valid for that token (a word where a colour belongs), so a half-typed value never reaches the theme.
    /// </summary>
    public bool SetOverride(ThemeOverrideScope scope, string path, string? value)
    {
        if (ActiveOverrideTarget is not { } target)
        {
            return false;
        }

        string normalized = ThemeTokenPaths.Normalize(path);
        var overrides = _overrideStore.Load();

        if (string.IsNullOrWhiteSpace(value))
        {
            var existing = overrides.Find(scope, target.FamilyId, target.VariantId);
            if (existing is null || !existing.Values.Remove(normalized))
            {
                return true; // nothing to remove: already at the theme's value
            }
        }
        else
        {
            if (!IsValidOverride(normalized, value))
            {
                return false;
            }

            overrides.GetOrAdd(scope, target.FamilyId, target.VariantId).Values[normalized] = value.Trim();
        }

        _overrideStore.Save(overrides);
        RefreshAfterOverrideChange();
        return true;
    }

    /// <summary>Whether <paramref name="value"/> can be used for the token at <paramref name="path"/>: the right type for a known role, any colour or number for a component property.</summary>
    public static bool IsValidOverride(string path, string value)
    {
        var role = ThemeVocabulary.Find(ThemeTokenPaths.Normalize(path));
        var parsed = ThemeValue.FromText(value, role?.Kind);
        if (role is not null)
        {
            return parsed.Kind == role.Kind;
        }

        return parsed.Kind is ThemeValueKind.Color or ThemeValueKind.Number or ThemeValueKind.Flag
            || (parsed.Kind == ThemeValueKind.Text && !value.TrimStart().StartsWith('#'));
    }

    /// <summary>Removes every override in one scope ("Reset to theme" for the variant or family, "Reset to defaults" for all themes).</summary>
    public void ResetOverrides(ThemeOverrideScope scope)
    {
        if (ActiveOverrideTarget is not { } target)
        {
            return;
        }

        var overrides = _overrideStore.Load();
        var layer = overrides.Find(scope, target.FamilyId, target.VariantId);
        if (layer is null || layer.IsEmpty)
        {
            return;
        }

        layer.Values.Clear();
        _overrideStore.Save(overrides);
        RefreshAfterOverrideChange();
    }

    public bool GetBoostContrast() => _overrideStore.Load().BoostContrast;

    /// <summary>The explicit accessibility adjustment (design doc section 13.1): raises the text roles to 4.5:1 on the background. Off by default; never applied silently.</summary>
    public void SetBoostContrast(bool enabled)
    {
        var overrides = _overrideStore.Load();
        if (overrides.BoostContrast == enabled)
        {
            return;
        }

        overrides.BoostContrast = enabled;
        _overrideStore.Save(overrides);
        RefreshAfterOverrideChange();
    }

    public ThemeEffectQuality GetEffectQuality()
    {
        var quality = _overrideStore.Load().EffectQuality;
        Theming.Motion.MotionPolicy.SetEffectQuality(quality);
        return quality;
    }

    public void SetEffectQuality(ThemeEffectQuality quality)
    {
        var overrides = _overrideStore.Load();
        if (overrides.EffectQuality == quality)
        {
            return;
        }

        overrides.EffectQuality = quality;
        _overrideStore.Save(overrides);
        RefreshAfterOverrideChange();
    }

    /// <summary>The contrast notes and fallbacks for the theme as it is currently shown (with the user's overrides).</summary>
    public IReadOnlyList<ThemeDiagnostic> GetActiveDiagnostics() =>
        Current is null
            ? Array.Empty<ThemeDiagnostic>()
            : Current.Diagnostics.Concat(ApplyDiagnostics).Concat(Theming.Layout.ThemeLayoutSource.Diagnostics).Concat(Theming.Motion.ThemeMotionRuntime.Diagnostics).ToList();

    /// <summary>
    /// The live value of a token and where it comes from. For a component property nothing has styled, this is the semantic role it follows
    /// (a primary button's background is the accent), so the Appearance panel can always say what is in effect.
    /// </summary>
    public ResolvedToken? ExplainToken(string path)
    {
        if (Current is not { } current)
        {
            return null;
        }

        string normalized = ThemeTokenPaths.Normalize(path);
        if (current.Token(normalized) is { } token)
        {
            return token;
        }

        if (!ThemeTokenPaths.TryParseComponent(normalized, out string role, out _, out string property)
            || ThemeComponentRegistry.Find(role)?.Find(property) is not { } definition)
        {
            return null;
        }

        if (definition.DefaultToken is not null && current.Token(definition.DefaultToken) is { } followed)
        {
            return new ResolvedToken(normalized, followed.Value, ThemeValueSource.Derived, $"follows {definition.DefaultToken}");
        }

        return definition.DefaultLiteral is null
            ? null
            : new ResolvedToken(normalized, ThemeValue.FromText(definition.DefaultLiteral, definition.Kind == ThemeComponentValueKind.Brush ? ThemeValueKind.Color : ThemeValueKind.Number), ThemeValueSource.BuiltInFallback);
    }

    /// <summary>How many overrides the user has in each scope for the active theme - drives the "Reset" affordances.</summary>
    public int CountOverrides(ThemeOverrideScope scope)
    {
        if (ActiveOverrideTarget is not { } target)
        {
            return 0;
        }

        return _overrideStore.Load().Find(scope, target.FamilyId, target.VariantId)?.Values.Count ?? 0;
    }

    private void RefreshAfterOverrideChange()
    {
        if (Avalonia.Application.Current is not null)
        {
            RefreshActiveTheme();
        }

        OverridesChanged?.Invoke();
    }
}
