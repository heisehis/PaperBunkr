using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Paperbunkr.App.Theming.Layout;

/// <summary>
/// One node of a validated layout tree (design doc section 8.2). Immutable. <see cref="Type"/> is either a container / primitive from the
/// closed vocabulary (<see cref="ThemeLayoutVocabulary"/>) or a host component the screen's contract publishes ("Home.Sections").
/// <see cref="Properties"/> holds only properties the validator recognised, already checked for type.
/// </summary>
public sealed record ThemeLayoutNode(string Id, string Type, IReadOnlyDictionary<string, JsonNode> Properties, IReadOnlyList<ThemeLayoutNode> Children)
{
    public string? Text(string name) => Properties.TryGetValue(name, out var node) && node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    public double? Number(string name) => Properties.TryGetValue(name, out var node) && node is JsonValue value && value.TryGetValue(out double number) ? number : null;

    public bool? Flag(string name) => Properties.TryGetValue(name, out var node) && node is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;

    public JsonNode? Raw(string name) => Properties.TryGetValue(name, out var node) ? node : null;

    public IEnumerable<ThemeLayoutNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var node in child.SelfAndDescendants())
            {
                yield return node;
            }
        }
    }
}

/// <summary>A width range in which a set of per-node property overrides applies (design doc section 8.5).</summary>
public sealed record ThemeLayoutBreakpoint(string Name, double? MinWidth, double? MaxWidth, IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonNode>> Overrides)
{
    public bool Matches(double width) => (MinWidth is null || width >= MinWidth) && (MaxWidth is null || width <= MaxWidth);
}

/// <summary>A layout that passed validation: its tree and its breakpoints, in declaration order.</summary>
public sealed record ThemeLayoutDocument(string Screen, ThemeLayoutNode Root, IReadOnlyList<ThemeLayoutBreakpoint> Breakpoints)
{
    /// <summary>The first breakpoint (in declaration order) that matches <paramref name="width"/>, or null.</summary>
    public ThemeLayoutBreakpoint? BreakpointFor(double width) => Breakpoints.FirstOrDefault(b => b.Matches(width));

    /// <summary>The tree with one breakpoint's overrides merged into the nodes they name. Ids, types and children never change.</summary>
    public ThemeLayoutNode RootFor(ThemeLayoutBreakpoint? breakpoint) => breakpoint is null ? Root : Apply(Root, breakpoint.Overrides);

    private static ThemeLayoutNode Apply(ThemeLayoutNode node, IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonNode>> overrides)
    {
        var properties = node.Properties;
        if (overrides.TryGetValue(node.Id, out var patch))
        {
            var merged = new Dictionary<string, JsonNode>(node.Properties, StringComparer.Ordinal);
            foreach (var (name, value) in patch)
            {
                merged[name] = value;
            }

            properties = merged;
        }

        return node with { Properties = properties, Children = node.Children.Select(child => Apply(child, overrides)).ToList() };
    }
}

/// <summary>How a host component published by a screen may be used in a layout.</summary>
public sealed record ThemeLayoutSlot(string Type, string Description, bool Unique = true, string? KeyProperty = null, IReadOnlyList<string>? AllowedKeys = null);

/// <summary>
/// The typed, versioned public UI contract of one screen (design doc sections 8.3 and 8.4): the host components a layout may place, the values
/// it may show, the commands it may trigger, and what must be present for the screen to remain usable. A layout can use what is published
/// here and nothing else - it never reaches a view-model, runs an expression, or reimplements an operation.
/// </summary>
public sealed class ThemeLayoutContract
{
    public required string Screen { get; init; }

    /// <summary>Bumped when a slot, binding or command is removed or changes meaning. A layout declaring a newer contract than this is refused.</summary>
    public int Version { get; init; } = 1;

    public required IReadOnlyList<ThemeLayoutSlot> Slots { get; init; }

    /// <summary>Binding name to a short description ("home.greeting" -> "The time-of-day greeting").</summary>
    public IReadOnlyDictionary<string, string> Bindings { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Commands { get; init; } = new Dictionary<string, string>();

    /// <summary>Each inner list is satisfied by any one of its slot types being present; every list must be satisfied. Keeps core functions reachable.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RequiredAnyOf { get; init; } = Array.Empty<IReadOnlyList<string>>();

    public ThemeLayoutSlot? FindSlot(string type) => Slots.FirstOrDefault(s => string.Equals(s.Type, type, StringComparison.Ordinal));
}

/// <summary>What kind of value a layout property takes.</summary>
public enum ThemeLayoutPropertyKind
{
    Number,
    Text,
    Flag,

    /// <summary>One number or "l,t,r,b".</summary>
    Thickness,

    /// <summary>A literal bool, or <c>{ "bind": "name" }</c> / <c>{ "not": "name" }</c> on a boolean binding.</summary>
    Condition,

    /// <summary>A role path ("surface.raised"), a colour, or "material:name".</summary>
    Paint,

    /// <summary>Text that may contain <c>{binding.name}</c> placeholders.</summary>
    Template,
}

/// <summary>
/// The closed vocabulary of layout node types and properties (design doc section 8.2). Everything a layout can say is listed here; an
/// unknown type is an error and an unknown property is ignored with a warning, so a layout can never name an arbitrary control or property.
/// </summary>
public static class ThemeLayoutVocabulary
{
    public const int MaxDepth = 12;
    public const int MaxNodes = 200;
    public const int MaxChildren = 40;
    public const int MaxBreakpoints = 6;

    /// <summary>Container and primitive types with how many children each takes (min, max).</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> BuiltInTypes = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["Stack"] = (0, MaxChildren),
        ["Grid"] = (0, MaxChildren),
        ["Dock"] = (0, MaxChildren),
        ["Split"] = (2, 2),
        ["Tabs"] = (1, 12),
        ["Overlay"] = (1, MaxChildren),
        ["Scroll"] = (1, 1),
        ["Panel"] = (0, 1),
        ["Text"] = (0, 0),
        ["Image"] = (0, 0),
        ["Icon"] = (0, 0),
        ["Button"] = (0, 0),
        ["Spacer"] = (0, 0),
        ["Divider"] = (0, 0),

        // A component supplied by an extension, with one child as its fallback (design doc section 14). Nothing is ever executed from a theme.
        ["Extension"] = (0, 1),
    };

    /// <summary>Properties any node may carry.</summary>
    private static readonly Dictionary<string, ThemeLayoutPropertyKind> Common = new(StringComparer.Ordinal)
    {
        ["width"] = ThemeLayoutPropertyKind.Number,
        ["height"] = ThemeLayoutPropertyKind.Number,
        ["minWidth"] = ThemeLayoutPropertyKind.Number,
        ["maxWidth"] = ThemeLayoutPropertyKind.Number,
        ["minHeight"] = ThemeLayoutPropertyKind.Number,
        ["maxHeight"] = ThemeLayoutPropertyKind.Number,
        ["margin"] = ThemeLayoutPropertyKind.Thickness,
        ["align"] = ThemeLayoutPropertyKind.Text,
        ["valign"] = ThemeLayoutPropertyKind.Text,
        ["visible"] = ThemeLayoutPropertyKind.Condition,
        ["opacity"] = ThemeLayoutPropertyKind.Number,
        ["motion"] = ThemeLayoutPropertyKind.Text,

        // Placement inside a parent container.
        ["dock"] = ThemeLayoutPropertyKind.Text,
        ["column"] = ThemeLayoutPropertyKind.Number,
        ["row"] = ThemeLayoutPropertyKind.Number,
        ["columnSpan"] = ThemeLayoutPropertyKind.Number,
        ["rowSpan"] = ThemeLayoutPropertyKind.Number,
        ["title"] = ThemeLayoutPropertyKind.Template,
    };

    private static readonly Dictionary<string, Dictionary<string, ThemeLayoutPropertyKind>> PerType = new(StringComparer.Ordinal)
    {
        ["Stack"] = new() { ["orientation"] = ThemeLayoutPropertyKind.Text, ["spacing"] = ThemeLayoutPropertyKind.Number, ["padding"] = ThemeLayoutPropertyKind.Thickness },
        ["Grid"] = new() { ["columns"] = ThemeLayoutPropertyKind.Text, ["rows"] = ThemeLayoutPropertyKind.Text, ["columnSpacing"] = ThemeLayoutPropertyKind.Number, ["rowSpacing"] = ThemeLayoutPropertyKind.Number, ["padding"] = ThemeLayoutPropertyKind.Thickness },
        ["Dock"] = new() { ["lastChildFill"] = ThemeLayoutPropertyKind.Flag, ["padding"] = ThemeLayoutPropertyKind.Thickness },
        ["Split"] = new() { ["orientation"] = ThemeLayoutPropertyKind.Text, ["ratio"] = ThemeLayoutPropertyKind.Number, ["resizable"] = ThemeLayoutPropertyKind.Flag },
        ["Tabs"] = new(),
        ["Overlay"] = new(),
        ["Scroll"] = new() { ["padding"] = ThemeLayoutPropertyKind.Thickness },
        ["Panel"] = new() { ["background"] = ThemeLayoutPropertyKind.Paint, ["border"] = ThemeLayoutPropertyKind.Paint, ["borderWidth"] = ThemeLayoutPropertyKind.Number, ["radius"] = ThemeLayoutPropertyKind.Number, ["padding"] = ThemeLayoutPropertyKind.Thickness },
        ["Text"] = new() { ["text"] = ThemeLayoutPropertyKind.Template, ["style"] = ThemeLayoutPropertyKind.Text, ["color"] = ThemeLayoutPropertyKind.Paint, ["wrap"] = ThemeLayoutPropertyKind.Flag },
        ["Image"] = new() { ["source"] = ThemeLayoutPropertyKind.Text, ["stretch"] = ThemeLayoutPropertyKind.Text },
        ["Icon"] = new() { ["name"] = ThemeLayoutPropertyKind.Text, ["size"] = ThemeLayoutPropertyKind.Number },
        ["Button"] = new() { ["text"] = ThemeLayoutPropertyKind.Template, ["command"] = ThemeLayoutPropertyKind.Text, ["icon"] = ThemeLayoutPropertyKind.Text, ["style"] = ThemeLayoutPropertyKind.Text },
        ["Spacer"] = new() { ["size"] = ThemeLayoutPropertyKind.Number },
        ["Divider"] = new() { ["orientation"] = ThemeLayoutPropertyKind.Text },
        ["Extension"] = new() { ["provider"] = ThemeLayoutPropertyKind.Text, ["component"] = ThemeLayoutPropertyKind.Text },
    };

    /// <summary>Allowed words for the enumerated text properties.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Words = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["align"] = new[] { "left", "center", "right", "stretch" },
        ["valign"] = new[] { "top", "center", "bottom", "stretch" },
        ["dock"] = new[] { "left", "top", "right", "bottom", "fill" },
        ["orientation"] = new[] { "vertical", "horizontal" },
        ["stretch"] = new[] { "fill", "uniform", "uniformToFill", "none" },
    };

    public static readonly IReadOnlyList<string> TextStyles = new[] { "hero", "heading", "body", "caption" };

    public static readonly IReadOnlyList<string> ButtonStyles = new[] { "primary", "secondary", "ghost" };

    public static bool IsBuiltIn(string type) => BuiltInTypes.ContainsKey(type);

    /// <summary>The kind a property takes on a node of this type, or null when the vocabulary has no such property there.</summary>
    public static ThemeLayoutPropertyKind? PropertyKind(string type, string property, ThemeLayoutSlot? slot)
    {
        if (Common.TryGetValue(property, out var common))
        {
            return common;
        }

        if (PerType.TryGetValue(type, out var own) && own.TryGetValue(property, out var kind))
        {
            return kind;
        }

        // A host slot takes its key property ("section": "ContinueReading") and nothing else of its own.
        return slot?.KeyProperty is { } key && key == property ? ThemeLayoutPropertyKind.Text : null;
    }

    /// <summary>Parses "12" or "8,4" or "l,t,r,b" into four non-negative numbers.</summary>
    public static bool TryParseThickness(JsonNode node, out (double Left, double Top, double Right, double Bottom) thickness)
    {
        thickness = default;
        if (node is JsonValue value && value.TryGetValue(out double uniform))
        {
            if (uniform is < 0 or > 400)
            {
                return false;
            }

            thickness = (uniform, uniform, uniform, uniform);
            return true;
        }

        if (node is not JsonValue textValue || !textValue.TryGetValue(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = new List<double>();
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number is < 0 or > 400)
            {
                return false;
            }

            parts.Add(number);
        }

        switch (parts.Count)
        {
            case 1: thickness = (parts[0], parts[0], parts[0], parts[0]); return true;
            case 2: thickness = (parts[0], parts[1], parts[0], parts[1]); return true;
            case 4: thickness = (parts[0], parts[1], parts[2], parts[3]); return true;
            default: return false;
        }
    }

    /// <summary>The binding names used by <c>{name}</c> placeholders in a template string.</summary>
    public static IEnumerable<string> Placeholders(string template)
    {
        int index = 0;
        while (index < template.Length)
        {
            int open = template.IndexOf('{', index);
            if (open < 0)
            {
                yield break;
            }

            int close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                yield break;
            }

            string name = template[(open + 1)..close].Trim();
            if (name.Length > 0)
            {
                yield return name;
            }

            index = close + 1;
        }
    }
}
