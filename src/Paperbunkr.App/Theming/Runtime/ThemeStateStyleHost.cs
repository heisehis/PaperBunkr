using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Paperbunkr.App.Theming.Runtime;

/// <summary>One state-specific value for a component role: "a pressed primary button's background is this brush".</summary>
public sealed record ThemeStateSkin(string Role, string State, string Property, IBrush Brush);

/// <summary>
/// Applies the hover / pressed / disabled skins of the component roles a theme styles (design doc section 6.1, layer C).
///
/// Two framework behaviours shape this, both found by test rather than assumed:
/// a state rule bound to a resource key a theme did not provide does not fall through to the framework's own state visual - it blanks it
/// (a pressed button went transparent); and a style removed at runtime is not un-applied from a control's template parts (the pressed button
/// kept the previous theme's colour). So nothing is declared in XAML, and nothing is ever removed. A style for one role-state-property is
/// created the first time any theme skins it, reading one application resource; from then on only that resource's value changes, and it
/// is always present. When the active theme does not skin it, the resource carries the framework's own brush for that state (looked up for
/// the current light/dark variant), so the control looks as it would have with no style at all. An app session that never uses a skinning
/// theme never gets any of these styles.
///
/// The setter is a dynamic resource reference on purpose. An earlier version bound it to an observable on a long-lived holder object, and
/// that kept every button the style had ever touched alive: the holder held each control's subscription and nothing released it when the
/// window closed (ThemeStateStyleLeakTests). A resource reference is owned by the control, like every other DynamicResource in the app.
/// </summary>
public static class ThemeStateStyleHost
{
    private static readonly Dictionary<(string Role, string State, string Property), string> Holders = new();
    private static Styles? s_host;

    /// <summary>The skins the active theme supplies - for tests and diagnostics. Empty when it skins nothing.</summary>
    public static IReadOnlyList<ThemeStateSkin> Current { get; private set; } = Array.Empty<ThemeStateSkin>();

    /// <summary>
    /// Makes <paramref name="skins"/> the active state skins. <paramref name="variant"/> is the theme's light/dark variant, used to find the
    /// framework's own state brush for whatever the theme leaves alone; <paramref name="normal"/> gives a role's normal-state brush, the last
    /// resort if the framework has none under the expected name (never a blank).
    /// </summary>
    public static void Apply(IReadOnlyList<ThemeStateSkin> skins, ThemeVariant variant, Func<string, string, IBrush?> normal)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        Current = skins.ToList();
        if (skins.Count == 0 && Holders.Count == 0)
        {
            return; // nothing has ever been skinned in this session: leave the app's styles untouched
        }

        var byKey = skins.ToDictionary(s => (s.Role, s.State, s.Property), s => s.Brush);

        foreach (var key in byKey.Keys.Where(k => !Holders.ContainsKey(k)).ToList())
        {
            if (BuildStyle(key.Role, key.State, key.Property) is not { } built)
            {
                continue;
            }

            if (s_host is null)
            {
                s_host = new Styles();
                app.Styles.Add(s_host);
            }

            // The value is published before the style that reads it exists, so the reference is never unresolved.
            app.Resources[built.ResourceKey] = byKey[key];
            Holders[key] = built.ResourceKey;
            s_host.Add(built.Style);
        }

        foreach (var (key, resourceKey) in Holders)
        {
            var value = byKey.TryGetValue(key, out var brush)
                ? brush
                : FrameworkBrush(app, key.Property, key.State, variant) ?? normal(key.Role, key.Property);

            // Never removed and never null: a missing value blanks the state instead of falling back (see the summary).
            if (value is not null && !(app.Resources.TryGetValue(resourceKey, out var existing) && ReferenceEquals(existing, value)))
            {
                app.Resources[resourceKey] = value;
            }
        }
    }

    /// <summary>The framework's own brush for a button in a state (WinUI resource names, which both Fluent themes use), or null when there is none under that name.</summary>
    internal static IBrush? FrameworkBrush(Application app, string property, string state, ThemeVariant variant)
    {
        string? stem = property switch
        {
            "background" => "ButtonBackground",
            "foreground" => "ButtonForeground",
            "border" => "ButtonBorderBrush",
            _ => null,
        };
        string? suffix = state switch
        {
            "hover" => "PointerOver",
            "pressed" => "Pressed",
            "disabled" => "Disabled",
            _ => null,
        };

        return stem is not null && suffix is not null && app.TryGetResource(stem + suffix, variant, out var value) ? value as IBrush : null;
    }

    private static string? PseudoClass(string state) => state switch
    {
        "hover" => ":pointerover",
        "pressed" => ":pressed",
        "disabled" => ":disabled",
        _ => null,
    };

    /// <summary>The style class each skinnable role maps to. All of them are buttons, whose state visuals are drawn by the template's content presenter.</summary>
    private static string? ButtonClass(string role) => role switch
    {
        "button.primary" => "primary",
        "button.secondary" => "secondary",
        "button.ghost" => "ghost",
        "chip" => "filterChip", // the clickable chip family (2026-10-19 shared controls); Border.pbChip tags are static and have no states
        _ => null,
    };

    /// <summary>The application resource a state style reads ("PbStateSkin.button.primary.hover.background").</summary>
    public static string ResourceKeyFor(string role, string state, string property) => $"PbStateSkin.{role}.{state}.{property}";

    private static (Style Style, string ResourceKey)? BuildStyle(string role, string state, string property)
    {
        AvaloniaProperty? target = property switch
        {
            "background" => ContentPresenter.BackgroundProperty,
            "foreground" => ContentPresenter.ForegroundProperty,
            "border" => ContentPresenter.BorderBrushProperty,
            _ => null,
        };

        if (ButtonClass(role) is not { } buttonClass || PseudoClass(state) is not { } pseudo || target is null)
        {
            return null;
        }

        string resourceKey = ResourceKeyFor(role, state, property);
        var style = new Style(x => x.OfType<Button>().Class(buttonClass).Class(pseudo).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        style.Setters.Add(new Setter(target, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(resourceKey)));
        return (style, resourceKey);
    }
}
