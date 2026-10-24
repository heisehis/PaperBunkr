using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Theming;

/// <summary>What kind of resource a component property becomes.</summary>
public enum ThemeComponentValueKind
{
    /// <summary>A colour, or <c>material:&lt;name&gt;</c> for a gradient / texture.</summary>
    Brush,
    CornerRadius,
    Thickness,
    Number,
}

/// <summary>
/// One styleable property of a component role. <paramref name="DefaultToken"/> is the semantic role it follows when a theme does not style it
/// (which is exactly what the control was bound to before component roles existed); <paramref name="DefaultLiteral"/> is used when there is
/// no such role. <paramref name="HoverDefaultToken"/> names what the existing hover rule was bound to, so an unstyled component looks as it always has.
/// </summary>
public sealed record ThemeComponentProperty(
    string Name,
    ThemeComponentValueKind Kind,
    string? DefaultToken = null,
    string? DefaultLiteral = null,
    string? HoverDefaultToken = null,
    bool HasStates = false);

/// <summary>A published, stable component role a theme may style (design doc section 6.1, layer C). <paramref name="ResourcePrefix"/> is the stem of its resource keys.</summary>
public sealed record ThemeComponentRole(string Role, string ResourcePrefix, string Description, IReadOnlyList<ThemeComponentProperty> Properties)
{
    public ThemeComponentProperty? Find(string property) => Properties.FirstOrDefault(p => string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The closed set of component roles themes can style (design doc sections 6.1 and 7.3). Deliberately a registry of named roles with named
/// properties, not a selector language: a theme can restyle what is published here and nothing else, so it cannot reach into a control's
/// private template parts or break when one changes. Each role maps onto style classes the app already uses, so no view had to change.
/// </summary>
public static class ThemeComponentRegistry
{
    /// <summary>The interaction states a theme may style. A state a theme leaves out is derived from the normal state when the theme styled that property, and left to the framework otherwise.</summary>
    public static IReadOnlyList<string> States { get; } = new[] { "hover", "pressed", "disabled" };

    public static IReadOnlyList<ThemeComponentRole> Roles { get; } = new[]
    {
        new ThemeComponentRole("button.primary", "ButtonPrimary", "The main action button.", new ThemeComponentProperty[]
        {
            new("background", ThemeComponentValueKind.Brush, "action.primary", HoverDefaultToken: "text.accent", HasStates: true),
            new("foreground", ThemeComponentValueKind.Brush, "badge.text", HasStates: true),
            new("border", ThemeComponentValueKind.Brush, "action.primary", HasStates: true),
            new("radius", ThemeComponentValueKind.CornerRadius, "metrics.radiusSm"),
        }),
        new ThemeComponentRole("button.secondary", "ButtonSecondary", "A neutral filled button.", new ThemeComponentProperty[]
        {
            new("background", ThemeComponentValueKind.Brush, "surface.raised", HoverDefaultToken: "surface.overlay", HasStates: true),
            new("foreground", ThemeComponentValueKind.Brush, "text.primary", HasStates: true),
            // text.muted (= PbTextFaint), not border.default: outlined controls need a visible edge (2026-10-19 shared controls design Q4).
            new("border", ThemeComponentValueKind.Brush, "text.muted", HasStates: true),
            new("radius", ThemeComponentValueKind.CornerRadius, "metrics.radiusSm"),
        }),
        new ThemeComponentRole("button.ghost", "ButtonGhost", "An outlined, unfilled button.", new ThemeComponentProperty[]
        {
            new("background", ThemeComponentValueKind.Brush, DefaultLiteral: "#00FFFFFF", HasStates: true),
            new("foreground", ThemeComponentValueKind.Brush, "text.secondary", HasStates: true),
            new("border", ThemeComponentValueKind.Brush, "text.muted", HoverDefaultToken: "action.primary", HasStates: true),
            new("radius", ThemeComponentValueKind.CornerRadius, "metrics.radiusSm"),
        }),
        new ThemeComponentRole("chip", "Chip", "Chips and tags. A squircle, never an oval pill.", new ThemeComponentProperty[]
        {
            new("background", ThemeComponentValueKind.Brush, "surface.raised"),
            new("border", ThemeComponentValueKind.Brush, "text.muted", HoverDefaultToken: "badge.background", HasStates: true),
            new("radius", ThemeComponentValueKind.CornerRadius, DefaultLiteral: "9"),
        }),
        new ThemeComponentRole("panel.floating", "PanelFloating", "Popovers and floating panels.", new ThemeComponentProperty[]
        {
            new("background", ThemeComponentValueKind.Brush, "surface.overlay"),
            new("border", ThemeComponentValueKind.Brush, "border.default"),
            new("radius", ThemeComponentValueKind.CornerRadius, "metrics.radiusLg"),
        }),
    };

    public static ThemeComponentRole? Find(string role) => Roles.FirstOrDefault(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase));

    private static string Pascal(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>The resource key of a role's property in its normal state: <c>PbButtonPrimaryBackground</c>.</summary>
    public static string ResourceKey(ThemeComponentRole role, ThemeComponentProperty property) => $"Pb{role.ResourcePrefix}{Pascal(property.Name)}";

    /// <summary>The key the app's long-standing hover rule reads (always written): <c>PbButtonPrimaryBackgroundHover</c>.</summary>
    public static string HoverKey(ThemeComponentRole role, ThemeComponentProperty property) => ResourceKey(role, property) + "Hover";
}
