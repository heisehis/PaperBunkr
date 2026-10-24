using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;

namespace Paperbunkr.App.Theming.Runtime;

/// <summary>Raised when committing a theme to the live resources failed and the previous values were put back.</summary>
public sealed class ThemeApplyException : Exception
{
    public ThemeApplyException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Everything one theme application will change, computed before anything live is touched (design doc section 10.1, steps 2 and 3). Building a
/// plan can fail (a bad material, an undecodable image); committing one is a list of dictionary writes, which is what makes the switch safe.
/// </summary>
public sealed class ThemeApplyPlan
{
    public Dictionary<string, object?> Resources { get; } = new(StringComparer.Ordinal);

    /// <summary>Keys this theme does not set and that must not linger from the previous theme (optional metrics, materials).</summary>
    public HashSet<string> Removals { get; } = new(StringComparer.Ordinal);

    public ThemeVariant Variant { get; set; } = ThemeVariant.Dark;

    /// <summary>The accent pushed into FluentAvalonia's own accent system so stock controls match.</summary>
    public Color? Accent { get; set; }

    public Color GlowColor { get; set; }

    /// <summary>Hover / pressed / disabled skins of styled component roles. Applied as styles after the commit (see <see cref="ThemeStateStyleHost"/>).</summary>
    public List<ThemeStateSkin> StateSkins { get; } = new();

    public void SetColor(string key, Color color)
    {
        Resources[$"Pb{key}Color"] = color;
        Resources[$"Pb{key}Brush"] = new SolidColorBrush(color);
    }
}

/// <summary>
/// The legacy <c>Pb*</c> bridge (design doc section 6.5): projects a <see cref="ResolvedTheme"/> onto the resource keys the app's XAML has
/// always consumed, and commits them. The only code that mutates <c>Application.Current.Resources</c> for a theme; it runs on the UI thread,
/// writes only values that actually changed, and restores what it overwrote if a write throws.
/// </summary>
public static class ThemeRuntimeApplier
{
    /// <summary>Pure with respect to the application: reads the theme, produces a plan.</summary>
    public static ThemeApplyPlan BuildPlan(ResolvedTheme theme, Func<Color, ThemeApplyPlan, bool>? addGlow = null)
    {
        var plan = new ThemeApplyPlan
        {
            Variant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark,
            Accent = theme.Color("action.primary"),
        };

        foreach (var role in ThemeVocabulary.Roles)
        {
            if (role.Form == ThemeResourceForm.None || role.ResourceKey is null)
            {
                continue;
            }

            var token = theme.Token(role.Path);
            switch (role.Form)
            {
                case ThemeResourceForm.ColorAndBrush when token is { Value.Kind: ThemeValueKind.Color }:
                    plan.SetColor(role.ResourceKey, token.Value.Color);
                    break;
                case ThemeResourceForm.ColorOnly when token is { Value.Kind: ThemeValueKind.Color }:
                    plan.Resources[$"Pb{role.ResourceKey}Color"] = token.Value.Color;
                    break;
                case ThemeResourceForm.Number when token is { Value.Kind: ThemeValueKind.Number }:
                    plan.Resources[$"Pb{role.ResourceKey}"] = token.Value.Number;
                    break;
                case ThemeResourceForm.CornerRadius when token is { Value.Kind: ThemeValueKind.Number }:
                    // CornerRadius, not double: Border.CornerRadius will not take a bare number from a resource.
                    plan.Resources[$"Pb{role.ResourceKey}"] = new CornerRadius(token.Value.Number);
                    break;
                case ThemeResourceForm.Number:
                    plan.Removals.Add($"Pb{role.ResourceKey}");
                    break;
            }
        }

        plan.Resources["PbThemeIsLight"] = theme.IsLight;

        if (theme.Color("glow.color") is { } glow)
        {
            plan.GlowColor = glow;
            addGlow?.Invoke(glow, plan);
        }

        return plan;
    }

    /// <summary>
    /// Writes the plan into the live resources. All or nothing: if any write throws, every key already written is restored and a
    /// <see cref="ThemeApplyException"/> is thrown. Values equal to what is already there are skipped, so re-applying the same theme
    /// (or a theme that shares most of its palette) does not make every bound control re-evaluate.
    /// </summary>
    public static void Commit(ThemeApplyPlan plan) => Commit(plan, null);

    /// <summary>Test seam: <paramref name="beforeWrite"/> is called with each key just before it is written, so a test can make the Nth write fail.</summary>
    internal static void Commit(ThemeApplyPlan plan, Action<string>? beforeWrite)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var resources = app.Resources;
        var undo = new List<(string Key, bool Existed, object? Value)>();
        var previousVariant = app.RequestedThemeVariant;
        Color? previousAccent = FindFluentTheme(app)?.CustomAccentColor;

        try
        {
            foreach (var (key, value) in plan.Resources)
            {
                bool existed = resources.TryGetValue(key, out object? current);
                if (existed && SameValue(current, value))
                {
                    continue;
                }

                beforeWrite?.Invoke(key);
                undo.Add((key, existed, current));
                resources[key] = value;
            }

            foreach (string key in plan.Removals)
            {
                if (resources.TryGetValue(key, out object? current))
                {
                    undo.Add((key, true, current));
                    resources.Remove(key);
                }
            }

            // RequestedThemeVariant is a StyledProperty and enforces thread ownership; production always calls from the UI thread. The guard
            // only matters for test-runner threads the dispatcher does not own, where skipping is safe (marshaling there deadlocks, as no
            // loop is pumping).
            if (Dispatcher.UIThread.CheckAccess() && app.RequestedThemeVariant != plan.Variant)
            {
                app.RequestedThemeVariant = plan.Variant;
            }

            if (plan.Accent is { } accent && FindFluentTheme(app) is { } fluent && fluent.CustomAccentColor != accent)
            {
                fluent.CustomAccentColor = accent;
            }
        }
        catch (Exception ex)
        {
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                var (key, existed, value) = undo[i];
                try
                {
                    if (existed)
                    {
                        resources[key] = value;
                    }
                    else
                    {
                        resources.Remove(key);
                    }
                }
                catch
                {
                    // Best effort: keep restoring the rest.
                }
            }

            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    app.RequestedThemeVariant = previousVariant;
                }

                if (FindFluentTheme(app) is { } fluent)
                {
                    fluent.CustomAccentColor = previousAccent;
                }
            }
            catch
            {
            }

            throw new ThemeApplyException("The theme could not be applied; the previous one was restored.", ex);
        }
    }

    private static FluentAvaloniaTheme? FindFluentTheme(Application app)
    {
        foreach (var style in app.Styles)
        {
            if (style is FluentAvaloniaTheme theme)
            {
                return theme;
            }
        }

        return null;
    }

    /// <summary>Value equality for the kinds of object a plan holds. A brush is "the same" when it paints the same solid colour.</summary>
    private static bool SameValue(object? current, object? next)
    {
        if (ReferenceEquals(current, next))
        {
            return true;
        }

        if (current is null || next is null)
        {
            return false;
        }

        if (current is ISolidColorBrush a && next is ISolidColorBrush b)
        {
            return a.Color == b.Color && Math.Abs(a.Opacity - b.Opacity) < 0.0001;
        }

        if (current is IBrush || next is IBrush)
        {
            return false; // gradients and images are rebuilt, never compared
        }

        return current.Equals(next);
    }
}
