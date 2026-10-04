using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Tests;

/// <summary>Shared headless plumbing for the keyboard-focus-reclaim view tests (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md).</summary>
internal static class FocusTestHarness
{
    /// <summary>The Fluent theme plus the app tokens the views' StaticResources need (App.axaml isn't loaded headless). Always overrides and then
    /// restores, because an earlier test in the full suite can leave real values behind.</summary>
    public static void WithThemeAndTokens(Action body)
    {
        TestAppBuilder.EnsureInitialized();
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var resources = Application.Current.Resources;
        var tokens = new Dictionary<string, object>
        {
            ["PbMotionEase"] = new Avalonia.Animation.Easings.CubicEaseOut(),
            ["PbIconSizeXs"] = 14d,
            ["PbIconSizeSm"] = 16d,
            ["PbIconSizeLg"] = 24d,
            ["PbRadiusChip"] = new CornerRadius(6),
            ["PbRadiusSm"] = new CornerRadius(5),
            ["PbElevationShadow"] = BoxShadows.Parse("0 2 8 0 #40000000"),
            ["PbAccentBrush"] = Brushes.OrangeRed,
            ["PbGlowRing"] = BoxShadows.Parse("0 0 0 4 #99E0995A"),
            ["PbDisplayFontFamily"] = new FontFamily("avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue"),
        };
        var hadKey = tokens.Keys.ToDictionary(k => k, k => resources.ContainsKey(k));
        var previous = tokens.Keys.ToDictionary(k => k, k => resources.TryGetResource(k, null, out var v) ? v : null);
        foreach (var key in tokens.Keys)
        {
            resources[key] = tokens[key];
        }

        try
        {
            body();
        }
        finally
        {
            foreach (var key in tokens.Keys)
            {
                if (hadKey[key])
                {
                    resources[key] = previous[key]!;
                }
                else
                {
                    resources.Remove(key);
                }
            }

            Application.Current!.Styles.Remove(theme);
        }
    }

    public static void RunLayout(Window window)
    {
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
    }

    public static Visual? Focused(Window window) => window.FocusManager?.GetFocusedElement() as Visual;

    public static void Press(Window window, Key key)
    {
        string physical = key switch
        {
            Key.Left or Key.Right or Key.Up or Key.Down => $"Arrow{key}",
            Key.Return => "Enter",
            _ => key.ToString(),
        };
        window.KeyPress(key, RawInputModifiers.None, (PhysicalKey)Enum.Parse(typeof(PhysicalKey), physical), null);
        RunLayout(window);
    }

    /// <summary>True when focus is on a live (effectively visible) element that is a descendant of <paramref name="region"/>.</summary>
    public static bool FocusIsInside(Window window, Visual region) =>
        Focused(window) is { IsEffectivelyVisible: true } focused && IsAncestor(region, focused);

    private static bool IsAncestor(Visual ancestor, Visual descendant)
    {
        for (Visual? v = descendant; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Loads the app's Primitives.axaml (which holds the app-wide focus-ring adorner) for the duration of the returned scope.</summary>
    public static IDisposable AppStyles()
    {
        var include = new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/Primitives.axaml") };
        Application.Current!.Styles.Add(include);
        return new Removal(() => Application.Current!.Styles.Remove(include));
    }

    private static ResourceDictionary? _appResources;

    /// <summary>The app's real design tokens (Styles/AppTokens.axaml, which App.axaml merges) for the duration of the returned scope, so a
    /// screen's StaticResources resolve headless without hand-copying tokens. Loaded once and reused.</summary>
    public static IDisposable AppResources()
    {
        _appResources ??= LoadAppResources();
        Application.Current!.Resources.MergedDictionaries.Add(_appResources);
        return new Removal(() => Application.Current!.Resources.MergedDictionaries.Remove(_appResources));
    }

    // Loaded directly rather than through an App instance: building one creates a second FluentAvaloniaTheme, whose platform-event
    // subscription the app's workaround doesn't remove, and it leaked into FluentAvaloniaWorkaroundsTests.
    private static ResourceDictionary LoadAppResources() =>
        new() { MergedDictionaries = { new ResourceInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/AppTokens.axaml") } } };

    private sealed class Removal(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>
    /// Every keyboard-focusable control under <paramref name="scope"/> whose focus ring (the app adorner reaches <paramref name="ring"/> px
    /// past the control) would be cut off by a clipping ancestor while the control itself is fully visible. A control only partly scrolled
    /// into view is skipped - its ring being cut is expected. Controls that draw their own ring (FocusAdorner null) are skipped too.
    /// </summary>
    public static List<string> ClippedFocusRings(Window window, Visual scope, double ring = 6)
    {
        var found = new List<string>();
        foreach (var control in scope.GetVisualDescendants().OfType<Control>())
        {
            if (!control.Focusable || !control.IsEffectivelyVisible || !control.IsEffectivelyEnabled || control.FocusAdorner is null
                || (control.TryFindResource("PbFocusAdornerInside", out var inside) && ReferenceEquals(control.FocusAdorner, inside))
                || control.Bounds.Width <= 0 || control.Bounds.Height <= 0 || WindowRect(control, window) is not { } own)
            {
                continue;
            }

            // The part of a ring past the window's edge can't show anyway, so only the part inside the window has to survive.
            var windowRect = new Rect(window.Bounds.Size);
            var ringRect = own.Inflate(ring).Intersect(windowRect);
            foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
            {
                if (ancestor is Window || !ancestor.ClipToBounds || WindowRect(ancestor, window) is not { } clip)
                {
                    continue;
                }

                if (!clip.Contains(own))
                {
                    break;
                }

                if (!clip.Contains(ringRect))
                {
                    found.Add($"{Describe(control)} at {own} cut by {Describe(ancestor)} at {clip}");
                    break;
                }
            }
        }

        return found;
    }

    private static Rect? WindowRect(Visual visual, Window window) =>
        visual.TranslatePoint(default, window) is { } a && visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), window) is { } b
            ? new Rect(a, b).Normalize()
            : null;

    private static string Describe(Control c)
    {
        string text = c switch
        {
            ContentControl { Content: string t } => $" \"{t}\"",
            _ when Avalonia.Automation.AutomationProperties.GetName(c) is { Length: > 0 } n => $" \"{n}\"",
            _ => "",
        };
        return $"{c.GetType().Name}{(string.IsNullOrEmpty(c.Name) ? "" : "#" + c.Name)}.{string.Join(".", c.Classes.Where(k => !k.StartsWith(':')))}{text}";
    }
}
