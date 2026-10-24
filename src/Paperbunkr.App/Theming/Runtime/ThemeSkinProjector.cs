using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace Paperbunkr.App.Theming.Runtime;

/// <summary>
/// Adds the phase-3 layers to a theme's apply plan (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 3): surface
/// materials, component-role skins and typography roles. Everything here is written into the same transaction as the colour tokens, and every
/// default equals what the control was bound to before, so a theme that styles none of it looks exactly as it did.
/// </summary>
public static class ThemeSkinProjector
{
    public const string DefaultDisplayFont = "avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue";
    public const string DefaultMonoFont = "Cascadia Code, Consolas, Courier New, monospace";

    /// <summary>The surfaces a theme may give a material (<c>"surfaces": { "canvas": "paper" }</c>).</summary>
    private static readonly string[] SurfaceNames = { "canvas", "navigation", "recessed", "default", "raised", "overlay" };

    /// <summary>Typography size role to the resource the app's text styles read.</summary>
    private static readonly (string Role, string Key)[] SizeRoles =
    {
        ("body", "PbFontSizeBody"), ("caption", "PbFontSizeCaption"), ("control", "PbFontSizeControl"),
        ("meta", "PbFontSizeMeta"), ("overline", "PbFontSizeOverline"), ("micro", "PbFontSizeMicro"),
    };

    public static void Project(ResolvedTheme theme, ThemeApplyPlan plan, ThemeMaterialBuilder materials, List<ThemeDiagnostic> diagnostics)
    {
        ProjectSurfaceMaterials(theme, plan, materials);
        ProjectComponents(theme, plan, materials);
        ProjectTypography(theme, plan, diagnostics);
        diagnostics.AddRange(materials.Diagnostics);
    }

    // ===================== Materials on surfaces =====================

    private static void ProjectSurfaceMaterials(ResolvedTheme theme, ThemeApplyPlan plan, ThemeMaterialBuilder materials)
    {
        foreach (string surface in SurfaceNames)
        {
            if (theme.Text("surfaces." + surface) is not { } materialName
                || ThemeVocabulary.Find("surface." + surface)?.ResourceKey is not { } resourceKey
                || materials.Build(materialName) is not { } brush)
            {
                continue;
            }

            // The Color resource stays the plain colour (custom-drawn controls and gradients need one); only the Brush becomes the material.
            plan.Resources[$"Pb{resourceKey}Brush"] = brush;
        }
    }

    // ===================== Component skins =====================

    private static void ProjectComponents(ResolvedTheme theme, ThemeApplyPlan plan, ThemeMaterialBuilder materials)
    {
        foreach (var role in ThemeComponentRegistry.Roles)
        {
            foreach (var property in role.Properties)
            {
                var authored = theme.Token(ThemeTokenPaths.Component(role.Role, "normal", property.Name));
                object? normal = Convert(property.Kind, authored?.Value, materials) ?? Default(theme, property);
                string key = ThemeComponentRegistry.ResourceKey(role, property);
                if (normal is not null)
                {
                    plan.Resources[key] = normal;
                }

                bool styled = authored is not null && Convert(property.Kind, authored.Value, materials) is not null;
                Color? normalColor = (normal as ISolidColorBrush)?.Color;

                if (property.HoverDefaultToken is not null)
                {
                    // The key the app's own hover rule has always read: the theme's hover value, else one derived from a styled normal
                    // state, else exactly the token that rule used before component roles existed.
                    object? hover = Convert(property.Kind, theme.Token(ThemeTokenPaths.Component(role.Role, "hover", property.Name))?.Value, materials)
                        ?? (styled && normalColor is { } c ? new SolidColorBrush(DeriveState(theme, c, "hover")) : null)
                        ?? (theme.Color(property.HoverDefaultToken) is { } fallback ? new SolidColorBrush(fallback) : null);
                    if (hover is not null)
                    {
                        plan.Resources[ThemeComponentRegistry.HoverKey(role, property)] = hover;
                    }
                }

                if (!property.HasStates)
                {
                    continue;
                }

                foreach (string state in ThemeComponentRegistry.States)
                {
                    object? value = Convert(property.Kind, theme.Token(ThemeTokenPaths.Component(role.Role, state, property.Name))?.Value, materials);

                    // A state the theme left out is derived only when the theme styled this property at all; an untouched component keeps
                    // the framework's own hover / pressed / disabled visuals.
                    if (value is null && styled && normalColor is { } color && property.Name == "background")
                    {
                        value = new SolidColorBrush(DeriveState(theme, color, state));
                    }

                    if (value is IBrush brush)
                    {
                        plan.StateSkins.Add(new ThemeStateSkin(role.Role, state, property.Name, brush));
                    }
                }
            }
        }
    }

    /// <summary>Hover moves away from the background (lighter on dark, darker on light), pressed moves the other way, disabled fades.</summary>
    public static Color DeriveState(ResolvedTheme theme, Color normal, string state)
    {
        // A transparent fill (a ghost button) has nothing to lighten: tint it with the text colour instead.
        if (normal.A < 16)
        {
            var ink = theme.Color("text.primary") ?? Colors.White;
            return state switch
            {
                "hover" => ThemeColorMath.WithAlpha(ink, 0x14),
                "pressed" => ThemeColorMath.WithAlpha(ink, 0x24),
                _ => normal,
            };
        }

        return state switch
        {
            "hover" => theme.IsLight ? ThemeColorMath.Darken(normal, 0.08) : ThemeColorMath.Lighten(normal, 0.10),
            "pressed" => theme.IsLight ? ThemeColorMath.Lighten(normal, 0.10) : ThemeColorMath.Darken(normal, 0.12),
            "disabled" => ThemeColorMath.WithAlpha(normal, (byte)(normal.A * 0.4)),
            _ => normal,
        };
    }

    private static object? Default(ResolvedTheme theme, ThemeComponentProperty property)
    {
        if (property.DefaultToken is not null && theme.Token(property.DefaultToken) is { } token)
        {
            return Convert(property.Kind, token.Value, null);
        }

        return property.DefaultLiteral is not null ? Convert(property.Kind, ThemeValue.FromText(property.DefaultLiteral, property.Kind == ThemeComponentValueKind.Brush ? ThemeValueKind.Color : ThemeValueKind.Number), null) : null;
    }

    private static object? Convert(ThemeComponentValueKind kind, ThemeValue? value, ThemeMaterialBuilder? materials)
    {
        if (value is not { } v)
        {
            return null;
        }

        switch (kind)
        {
            case ThemeComponentValueKind.Brush:
                if (v.Kind == ThemeValueKind.Color)
                {
                    return new SolidColorBrush(v.Color);
                }

                return v.Kind == ThemeValueKind.Text && ThemeMaterialBuilder.MaterialName(v.Text) is { } material ? materials?.Build(material) : null;

            case ThemeComponentValueKind.CornerRadius:
                return v.Kind == ThemeValueKind.Number ? new CornerRadius(Math.Clamp(v.Number, 0, 64)) : null;

            case ThemeComponentValueKind.Thickness:
                if (v.Kind == ThemeValueKind.Number)
                {
                    return new Thickness(Math.Clamp(v.Number, 0, 64));
                }

                return v.Kind == ThemeValueKind.Text && TryParseThickness(v.Text, out var thickness) ? thickness : null;

            case ThemeComponentValueKind.Number:
                return v.Kind == ThemeValueKind.Number ? v.Number : null;

            default:
                return null;
        }
    }

    private static bool TryParseThickness(string? text, out Thickness thickness)
    {
        thickness = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<double>();
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number < 0 || number > 128)
            {
                return false;
            }

            numbers.Add(number);
        }

        switch (numbers.Count)
        {
            case 1: thickness = new Thickness(numbers[0]); return true;
            case 2: thickness = new Thickness(numbers[0], numbers[1]); return true;
            case 4: thickness = new Thickness(numbers[0], numbers[1], numbers[2], numbers[3]); return true;
            default: return false;
        }
    }

    // ===================== Typography roles =====================

    private static void ProjectTypography(ResolvedTheme theme, ThemeApplyPlan plan, List<ThemeDiagnostic> diagnostics)
    {
        string display = Family(theme, "display", diagnostics) ?? DefaultDisplayFont;
        string heading = Family(theme, "heading", diagnostics) ?? display;

        plan.Resources["PbDisplayFontFamily"] = new FontFamily(display);
        plan.Resources["PbHeadingFontFamily"] = new FontFamily(heading);
        plan.Resources["PbMonoFontFamily"] = new FontFamily(Family(theme, "monospace", diagnostics) ?? DefaultMonoFont);

        plan.Resources["PbFontSizeHero"] = Size(theme, "display") ?? 42d;
        plan.Resources["PbFontSizeHeading"] = Size(theme, "heading") ?? 22d;
        plan.Resources["PbDisplayFontWeight"] = Weight(theme, "display");
        plan.Resources["PbHeadingFontWeight"] = Weight(theme, "heading");

        foreach (var (role, key) in SizeRoles)
        {
            if (Size(theme, role) is { } size)
            {
                plan.Resources[key] = size;
            }
            else
            {
                plan.Removals.Add(key); // back to the app's own type scale (AppTokens)
            }
        }
    }

    private static double? Size(ResolvedTheme theme, string role) =>
        theme.Number($"typography.{role}.size") is { } size && size is >= 6 and <= 96 ? size : null;

    private static FontWeight Weight(ResolvedTheme theme, string role)
    {
        if (theme.Number($"typography.{role}.weight") is { } number && number is >= 100 and <= 950)
        {
            return (FontWeight)(int)number;
        }

        return theme.Text($"typography.{role}.weight") is { } text && Enum.TryParse<FontWeight>(text, ignoreCase: true, out var weight) ? weight : FontWeight.Normal;
    }

    /// <summary>
    /// The family a role asks for, or null when it asks for none. A single named family that is not installed is reported and dropped, so the
    /// role falls back to the app's bundled font instead of to whatever the system substitutes; a list ("A, B, serif") carries its own fallbacks.
    /// </summary>
    private static string? Family(ResolvedTheme theme, string role, List<ThemeDiagnostic> diagnostics)
    {
        if (theme.Text($"typography.{role}.family") is not { } family || string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        // The two fonts that ship with the app are named like any other but live in its resources, not in the system's font list.
        if (string.Equals(family.Trim(), "Bebas Neue", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultDisplayFont;
        }

        if (string.Equals(family.Trim(), "Source Serif 4", StringComparison.OrdinalIgnoreCase))
        {
            return "avares://Paperbunkr.App/Assets/Fonts/#Source Serif 4, Georgia, serif";
        }

        bool single = !family.Contains(',') && !family.Contains("://", StringComparison.Ordinal) && !family.Contains('#');
        if (single && !IsInstalled(family))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.FallbackUsed,
                $"The font '{family}' is not installed; the {role} text uses the built-in font.", $"typography.{role}.family", family, ThemeId: theme.FamilyId, VariantId: theme.VariantId, FallbackUsed: true));
            return null;
        }

        return family;
    }

    private static HashSet<string>? s_installedFonts;

    internal static bool IsInstalled(string family)
    {
        try
        {
            s_installedFonts ??= new HashSet<string>(SKFontManager.Default.FontFamilies, StringComparer.OrdinalIgnoreCase);
            return s_installedFonts.Contains(family.Trim()) || family.Trim() is "Source Serif 4" or "Bebas Neue";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            return true; // cannot tell: do not second-guess the theme
        }
    }
}
