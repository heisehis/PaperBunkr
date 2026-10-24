using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Theming.Layout;

/// <summary>
/// A component supplied by an installed extension that a theme layout may place (design doc sections 3.3 and 14). This is only the attachment
/// point: a provider is registered by host code that is already running and trusted. A theme can name a provider and a component; it cannot
/// install, enable, download or execute one, and a layout naming a provider that is not registered simply shows its fallback.
/// </summary>
public interface IThemeComponentProvider
{
    /// <summary>Stable identity themes refer to ("org.example.weather").</summary>
    string Id { get; }

    /// <summary>The components this provider offers, for validation and documentation.</summary>
    IReadOnlyCollection<string> Components { get; }

    /// <summary>Builds the named component for a layout node, or returns null to decline (the layout's fallback is then shown).</summary>
    Control? Create(string component, ThemeLayoutNode node);
}

/// <summary>The component providers currently registered. Empty in this app: no extension runtime ships with the theme engine.</summary>
public static class ThemeExtensionRegistry
{
    private static readonly Dictionary<string, IThemeComponentProvider> Providers = new(StringComparer.Ordinal);

    public static event Action? Changed;

    public static IReadOnlyCollection<string> ProviderIds => Providers.Keys;

    public static void Register(IThemeComponentProvider provider)
    {
        Providers[provider.Id] = provider;
        Changed?.Invoke();
    }

    public static void Unregister(string providerId)
    {
        if (Providers.Remove(providerId))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The component, or null when the provider is absent or declines. A provider that throws is treated as absent.</summary>
    public static Control? Create(string providerId, string component, ThemeLayoutNode node)
    {
        if (!Providers.TryGetValue(providerId, out var provider))
        {
            return null;
        }

        try
        {
            return provider.Create(component, node);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// The active theme's layouts, published by <c>ThemeService</c> after a successful apply and read by every <see cref="Controls.ThemeLayoutHost"/>.
/// Not a resource: a host asks for its screen's layout by name and rebuilds when this changes.
/// </summary>
public static class ThemeLayoutSource
{
    private static IReadOnlyDictionary<string, JsonObject> s_layouts = new Dictionary<string, JsonObject>();
    private static IReadOnlyDictionary<string, Dictionary<string, JsonObject>> s_patches = new Dictionary<string, Dictionary<string, JsonObject>>();
    private static Func<string, Stream?> s_openAsset = _ => null;
    private static Func<string, IBrush?> s_material = _ => null;
    private static readonly Dictionary<string, IReadOnlyList<ThemeDiagnostic>> ScreenDiagnostics = new(StringComparer.OrdinalIgnoreCase);

    private static string s_signature = string.Empty;

    private static string Signature(bool enabled, IReadOnlyDictionary<string, JsonObject> layouts, IReadOnlyDictionary<string, Dictionary<string, JsonObject>> patches)
    {
        var builder = new System.Text.StringBuilder(enabled ? "on" : "off");
        foreach (var (screen, layout) in layouts.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append('|').Append(screen).Append('=').Append(layout.ToJsonString());
        }

        foreach (var (screen, screenPatches) in patches.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var (nodeId, patch) in screenPatches.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                builder.Append('|').Append(screen).Append('#').Append(nodeId).Append('=').Append(patch.ToJsonString());
            }
        }

        return builder.ToString();
    }

    /// <summary>Raised when the layouts, the user's patches or the "allow theme layouts" switch changed.</summary>
    public static event Action? Changed;

    /// <summary>Whether themes may rearrange screens at all (design doc decision D11). Off until the user turns it on.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>The name of the theme whose layouts these are, for diagnostics.</summary>
    public static string ThemeName { get; private set; } = string.Empty;

    public static void Set(
        bool enabled,
        string themeName,
        IReadOnlyDictionary<string, JsonObject> layouts,
        IReadOnlyDictionary<string, Dictionary<string, JsonObject>> patches,
        Func<string, Stream?> openAsset,
        Func<string, IBrush?> material)
    {
        // A theme is re-applied often (True Black toggles, an override, entering a reader). Rebuilding every hosted screen each time would
        // throw away scroll positions for nothing, so hosts are only told when the layouts themselves, the user's patches or the switch changed.
        string signature = Signature(enabled, layouts, patches);
        bool changed = signature != s_signature;

        Enabled = enabled;
        ThemeName = themeName;
        s_layouts = layouts;
        s_patches = patches;
        s_openAsset = openAsset;
        s_material = material;
        s_signature = signature;

        if (changed)
        {
            ScreenDiagnostics.Clear();
            Changed?.Invoke();
        }
    }

    public static void Clear() => Set(false, string.Empty, new Dictionary<string, JsonObject>(), new Dictionary<string, Dictionary<string, JsonObject>>(), _ => null, _ => null);

    /// <summary>The active theme's layout for a screen, or null when it has none or theme layouts are switched off.</summary>
    public static JsonObject? LayoutFor(string screen) =>
        Enabled && s_layouts.FirstOrDefault(p => string.Equals(p.Key, screen, StringComparison.OrdinalIgnoreCase)).Value is { } layout ? layout : null;

    /// <summary>True when the active theme ships a layout for the screen, whether or not layouts are switched on - so the UI can say one is available.</summary>
    public static bool HasLayoutFor(string screen) => s_layouts.Keys.Any(k => string.Equals(k, screen, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyCollection<string> ScreensWithLayouts => s_layouts.Keys.ToList();

    public static IReadOnlyDictionary<string, JsonObject>? PatchesFor(string screen) =>
        s_patches.FirstOrDefault(p => string.Equals(p.Key, screen, StringComparison.OrdinalIgnoreCase)).Value;

    public static Stream? OpenAsset(string path) => s_openAsset(path);

    public static IBrush? Material(string name) => s_material(name);

    /// <summary>Recorded by a host each time it builds (or refuses) its screen's layout.</summary>
    public static void Report(string screen, IReadOnlyList<ThemeDiagnostic> diagnostics) => ScreenDiagnostics[screen] = diagnostics;

    public static IReadOnlyList<ThemeDiagnostic> Diagnostics => ScreenDiagnostics.Values.SelectMany(d => d).ToList();
}
