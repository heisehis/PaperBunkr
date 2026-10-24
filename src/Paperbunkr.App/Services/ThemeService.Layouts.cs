using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Layout;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Phases 4 and 5 of the v2 theme engine (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md): publishes the active
/// theme's layouts and motion to the screens that host them, once the theme's resources have been committed. Neither is a resource, so
/// neither can half-apply: a screen asks for its layout by name and falls back to its built-in one if that layout is refused.
/// </summary>
public partial class ThemeService
{
    partial void AfterCommitExtensions(ResolvedTheme theme)
    {
        var overrides = _overrideStore.Load();
        var settingsMotion = ReadMotionPreference();
        MotionPolicy.RefreshSystemPreference();
        ApplyMotionResources(settingsMotion);

        var patches = new Dictionary<string, Dictionary<string, JsonObject>>(StringComparer.OrdinalIgnoreCase);
        if (overrides.FindFamily(theme.FamilyId) is { } family)
        {
            foreach (var (screen, screenPatches) in family.LayoutPatches)
            {
                patches[screen] = screenPatches;
            }
        }

        string familyKey = theme.FamilyKey;
        var quality = overrides.EffectQuality;
        var materials = new ThemeMaterialBuilder(theme, quality, path => _catalog.OpenAsset(familyKey, path), _materialImages);

        ThemeMotionRuntime.Configure(theme.Motion, quality, settingsMotion);
        ThemeLayoutSource.Set(
            overrides.AllowThemeLayouts,
            theme.Document.Name,
            theme.Layouts,
            patches,
            path => _catalog.OpenAsset(familyKey, path),
            name => materials.Build(name));
    }

    private MotionPreferenceMode ReadMotionPreference()
    {
        try
        {
            using var context = _contextFactory();
            return context.GetOrCreateAppSettings().MotionPreference;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return MotionPreferenceMode.System;
        }
    }

    public bool GetAllowThemeLayouts() => _overrideStore.Load().AllowThemeLayouts;

    /// <summary>Turns theme layouts on or off (design doc decision D11). Screens showing a theme's layout go back to their built-in one immediately when it is turned off.</summary>
    public void SetAllowThemeLayouts(bool allow)
    {
        var overrides = _overrideStore.Load();
        if (overrides.AllowThemeLayouts == allow)
        {
            return;
        }

        overrides.AllowThemeLayouts = allow;
        _overrideStore.Save(overrides);
        RefreshAfterOverrideChange();
    }

    /// <summary>The screens the active theme ships a layout for ("Home"). Empty for most themes; of the built-ins only Newsprint and Arcade ship one.</summary>
    public IReadOnlyCollection<string> ActiveThemeLayoutScreens => Current?.Layouts.Keys.ToList() ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>
    /// Sets (or, with a null <paramref name="patch"/>, removes) the user's own adjustment to one node of the active theme's layout for a
    /// screen (design doc section 8.6). Stored per theme family by node id, so it survives a theme update as long as that node still exists.
    /// </summary>
    public void SetLayoutPatch(string screen, string nodeId, JsonObject? patch)
    {
        if (ActiveOverrideTarget is not { } target)
        {
            return;
        }

        var overrides = _overrideStore.Load();
        var family = overrides.GetOrAddFamily(target.FamilyId);
        if (patch is null || patch.Count == 0)
        {
            if (family.LayoutPatches.TryGetValue(screen, out var existing))
            {
                existing.Remove(nodeId);
                if (existing.Count == 0)
                {
                    family.LayoutPatches.Remove(screen);
                }
            }
        }
        else
        {
            if (!family.LayoutPatches.TryGetValue(screen, out var screenPatches))
            {
                screenPatches = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                family.LayoutPatches[screen] = screenPatches;
            }

            screenPatches[nodeId] = (JsonObject)patch.DeepClone();
        }

        _overrideStore.Save(overrides);
        RefreshAfterOverrideChange();
    }

    /// <summary>Removes every layout adjustment the user made to the active theme family ("Reset layout").</summary>
    public void ResetLayoutPatches()
    {
        if (ActiveOverrideTarget is not { } target)
        {
            return;
        }

        var overrides = _overrideStore.Load();
        if (overrides.FindFamily(target.FamilyId) is not { LayoutPatches.Count: > 0 } family)
        {
            return;
        }

        family.LayoutPatches.Clear();
        _overrideStore.Save(overrides);
        RefreshAfterOverrideChange();
    }

    public int CountLayoutPatches() =>
        ActiveOverrideTarget is { } target ? _overrideStore.Load().FindFamily(target.FamilyId)?.LayoutPatches.Values.Sum(p => p.Count) ?? 0 : 0;
}
