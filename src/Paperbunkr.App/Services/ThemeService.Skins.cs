using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;

namespace Paperbunkr.App.Services;

/// <summary>
/// Phase 3 of the v2 theme engine (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md): surface materials, component-role
/// skins, typography roles, the scrollbar metrics and the semantic icon set, all added to the same apply transaction as the colour tokens.
/// </summary>
public partial class ThemeService
{
    /// <summary>Decoded material images, kept across applies so toggling True Black or an override does not re-decode a texture.</summary>
    private readonly Dictionary<string, Bitmap> _materialImages = new(StringComparer.OrdinalIgnoreCase);

    private List<ThemeDiagnostic> _applyDiagnostics = new();

    /// <summary>Findings from building the live theme that the pure resolver cannot know (a font that is not installed, an image that would not decode).</summary>
    public IReadOnlyList<ThemeDiagnostic> ApplyDiagnostics => _applyDiagnostics;

    partial void ContributeToPlan(ResolvedTheme theme, ThemeApplyPlan plan)
    {
        var diagnostics = new List<ThemeDiagnostic>();
        var quality = _overrideStore.Load().EffectQuality;
        var materials = new ThemeMaterialBuilder(theme, quality, path => _catalog.OpenAsset(theme.FamilyKey, path), _materialImages);

        ThemeSkinProjector.Project(theme, plan, materials, diagnostics);
        ProjectScrollbar(theme, plan);

        _applyDiagnostics = diagnostics;
        _pendingStateSkins = plan.StateSkins;
        _pendingVariant = plan.Variant;
        _pendingNormals = ThemeComponentRegistry.Roles
            .SelectMany(role => role.Properties.Select(property => (role, property)))
            .Where(pair => plan.Resources.TryGetValue(ThemeComponentRegistry.ResourceKey(pair.role, pair.property), out var value) && value is Avalonia.Media.IBrush)
            .ToDictionary(pair => (pair.role.Role, pair.property.Name), pair => (Avalonia.Media.IBrush)plan.Resources[ThemeComponentRegistry.ResourceKey(pair.role, pair.property)]!);
    }

    private Avalonia.Styling.ThemeVariant _pendingVariant = Avalonia.Styling.ThemeVariant.Dark;
    private Dictionary<(string Role, string Property), Avalonia.Media.IBrush> _pendingNormals = new();

    private IReadOnlyList<ThemeStateSkin> _pendingStateSkins = Array.Empty<ThemeStateSkin>();

    partial void AfterCommit(ResolvedTheme theme)
    {
        // Icons are not resources: ThemedIcon asks for them by name, and redraws when the set changes.
        string familyKey = theme.FamilyKey;
        ThemeIconSource.Set(
            name => ThemeIconOverride.Parse(theme.Text("icons." + name)),
            path => _catalog.OpenAsset(familyKey, path));
        ThemeStateStyleHost.Apply(_pendingStateSkins, _pendingVariant, (role, property) => _pendingNormals.TryGetValue((role, property), out var brush) ? brush : null);
        AfterCommitExtensions(theme);
    }

    /// <summary>Hook for the later phases (layouts, motion) to publish their own non-resource state.</summary>
    partial void AfterCommitExtensions(ResolvedTheme theme);

    /// <summary>
    /// The scrollbar metrics a theme may set (<c>metrics.scrollbarWidth</c>, <c>metrics.scrollbarThumbOpacity</c>). The width goes to the
    /// framework's own <c>ScrollBarSize</c> resource, which its scrollbar template sizes itself from; a theme that sets neither removes
    /// both keys, so the framework's defaults are back rather than a previous theme's values lingering.
    /// </summary>
    private static void ProjectScrollbar(ResolvedTheme theme, ThemeApplyPlan plan)
    {
        if (theme.Number("metrics.scrollbarWidth") is { } width && width is >= 2 and <= 40)
        {
            plan.Resources["ScrollBarSize"] = width;
        }
        else
        {
            plan.Removals.Add("ScrollBarSize");
        }

        if (theme.Number("metrics.scrollbarThumbOpacity") is { } opacity)
        {
            plan.Resources["PbScrollbarThumbOpacity"] = Math.Clamp(opacity, 0.05, 1);
        }
    }

    /// <summary>The semantic icon names a theme can replace, with the built-in glyph each falls back to (for documentation and the Appearance UI).</summary>
    public static IReadOnlyCollection<string> ThemeableIconNames => ThemeIconRegistry.Names;
}
