using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Theming.Layout;

/// <summary>
/// Turns a theme's layout JSON into a <see cref="ThemeLayoutDocument"/> or refuses it (design doc sections 8.2 to 8.5). Pure: no controls are
/// created here. A structural problem - an unknown component, a duplicate id, a tree that is too deep, a screen left without something it
/// needs - refuses the whole layout, and the host then shows the built-in one. A problem with a single property drops that property and is
/// reported, so a small mistake does not cost the author the whole layout.
/// </summary>
public static partial class ThemeLayoutValidator
{
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.Ordinal) { "id", "type", "children" };

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^(\s*(auto|\*|\d+(\.\d+)?\*?)\s*)(,\s*(auto|\*|\d+(\.\d+)?\*?)\s*){0,23}$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackPattern();

    /// <summary>
    /// Null when the layout cannot be used; <paramref name="diagnostics"/> then says why. <paramref name="userPatches"/> are the user's own
    /// per-node adjustments (design doc section 8.6); a patch that no longer fits the theme's layout is dropped and reported, never applied blindly.
    /// </summary>
    public static ThemeLayoutDocument? Validate(
        JsonObject layout,
        ThemeLayoutContract contract,
        List<ThemeDiagnostic> diagnostics,
        IReadOnlyDictionary<string, JsonObject>? userPatches = null)
    {
        var context = new Context(contract, diagnostics);
        string basePath = $"layouts.{contract.Screen}";

        if (layout["contract"] is JsonValue versionNode && versionNode.TryGetValue(out int version) && version > contract.Version)
        {
            context.Fatal($"This layout was written for a newer version of the {contract.Screen} screen (contract {version}; this app has {contract.Version}).", basePath + ".contract");
            return null;
        }

        if (layout["root"] is not JsonObject rootObject)
        {
            context.Fatal("A layout needs a \"root\" node.", basePath + ".root");
            return null;
        }

        var root = ParseNode(rootObject, depth: 1, basePath + ".root", context);
        if (root is null || context.IsFatal)
        {
            return null;
        }

        var nodes = root.SelfAndDescendants().ToList();
        if (nodes.Count > ThemeLayoutVocabulary.MaxNodes)
        {
            context.Fatal($"The layout has {nodes.Count} nodes; the limit is {ThemeLayoutVocabulary.MaxNodes}.", basePath);
            return null;
        }

        foreach (var duplicate in nodes.GroupBy(n => n.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            context.Fatal($"The id '{duplicate.Key}' is used {duplicate.Count()} times; every node needs its own.", basePath);
        }

        CheckSlots(nodes, contract, context, basePath);
        if (context.IsFatal)
        {
            return null;
        }

        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        if (userPatches is { Count: > 0 })
        {
            // Re-read each patch from its text: a value set in code (an int, say) is not readable as a double the way a parsed one is.
            var patches = ParseOverrides(userPatches.ToDictionary(p => p.Key, p => JsonNode.Parse(p.Value.ToJsonString())), byId, context, basePath + ".userPatches", "Your adjustment");
            root = new ThemeLayoutDocument(contract.Screen, root, Array.Empty<ThemeLayoutBreakpoint>())
                .RootFor(new ThemeLayoutBreakpoint("user", null, null, patches));
        }

        var breakpoints = ParseBreakpoints(layout["breakpoints"], byId, context, basePath + ".breakpoints");
        return new ThemeLayoutDocument(contract.Screen, root, breakpoints);
    }

    private static ThemeLayoutNode? ParseNode(JsonObject source, int depth, string path, Context context)
    {
        if (depth > ThemeLayoutVocabulary.MaxDepth)
        {
            context.Fatal($"The layout is nested more than {ThemeLayoutVocabulary.MaxDepth} levels deep.", path);
            return null;
        }

        string? id = source["id"] is JsonValue idValue && idValue.TryGetValue(out string? idText) ? idText : null;
        if (id is null || !IdPattern().IsMatch(id))
        {
            context.Fatal("Every node needs an \"id\" (letters, digits, '.', '_' or '-', up to 64 characters).", path + ".id");
            return null;
        }

        string? type = source["type"] is JsonValue typeValue && typeValue.TryGetValue(out string? typeText) ? typeText : null;
        var slot = type is null ? null : context.Contract.FindSlot(type);
        if (type is null || (!ThemeLayoutVocabulary.IsBuiltIn(type) && slot is null))
        {
            context.Fatal($"'{type}' is not a component the {context.Contract.Screen} screen offers.", path + ".type");
            return null;
        }

        var children = new List<ThemeLayoutNode>();
        if (source["children"] is JsonArray array)
        {
            if (array.Count > ThemeLayoutVocabulary.MaxChildren)
            {
                context.Fatal($"'{id}' has {array.Count} children; the limit is {ThemeLayoutVocabulary.MaxChildren}.", path + ".children");
                return null;
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject childObject)
                {
                    context.Fatal("A child must be a node object.", $"{path}.children[{i}]");
                    return null;
                }

                var child = ParseNode(childObject, depth + 1, $"{path}.children[{i}]", context);
                if (child is null)
                {
                    return null;
                }

                children.Add(child);
            }
        }

        var (min, max) = slot is not null ? (0, 0) : ThemeLayoutVocabulary.BuiltInTypes[type];
        if (children.Count < min || children.Count > max)
        {
            string expected = min == max ? (min == 0 ? "no children" : $"exactly {min}") : $"between {min} and {max}";
            context.Fatal($"'{id}' ({type}) takes {expected}, but has {children.Count}.", path + ".children");
            return null;
        }

        var properties = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (name, value) in source)
        {
            if (ReservedKeys.Contains(name) || value is null)
            {
                continue;
            }

            if (ValidateProperty(type, slot, name, value, $"{path}.{name}", context, "This property"))
            {
                properties[name] = value.DeepClone();
            }
        }

        return new ThemeLayoutNode(id, type, properties, children);
    }

    /// <summary>True when the value is usable. A false result has already been reported and the property is simply left out.</summary>
    private static bool ValidateProperty(string type, ThemeLayoutSlot? slot, string name, JsonNode value, string path, Context context, string subject)
    {
        var kind = ThemeLayoutVocabulary.PropertyKind(type, name, slot);
        if (kind is null)
        {
            context.Warn($"'{name}' is not a property of {type}; it is ignored.", path);
            return false;
        }

        switch (kind.Value)
        {
            case ThemeLayoutPropertyKind.Number:
                if (value is not JsonValue numberValue || !numberValue.TryGetValue(out double number) || double.IsNaN(number) || double.IsInfinity(number))
                {
                    return context.Drop($"{subject} '{name}' must be a number.", path);
                }

                var (low, high) = NumberRange(name);
                return number >= low && number <= high || context.Drop($"{subject} '{name}' must be between {low} and {high}.", path);

            case ThemeLayoutPropertyKind.Flag:
                return value is JsonValue flagValue && flagValue.TryGetValue(out bool _) || context.Drop($"{subject} '{name}' must be true or false.", path);

            case ThemeLayoutPropertyKind.Thickness:
                return ThemeLayoutVocabulary.TryParseThickness(value, out _) || context.Drop($"{subject} '{name}' must be a number or \"left,top,right,bottom\".", path);

            case ThemeLayoutPropertyKind.Condition:
                return ValidateCondition(value, name, path, context, subject);

            case ThemeLayoutPropertyKind.Paint:
                return value is JsonValue paintValue && paintValue.TryGetValue(out string? paint) && !string.IsNullOrWhiteSpace(paint) || context.Drop($"{subject} '{name}' must be a colour, a role such as \"surface.raised\", or \"material:name\".", path);

            case ThemeLayoutPropertyKind.Template:
                if (value is not JsonValue templateValue || !templateValue.TryGetValue(out string? template) || template is null)
                {
                    return context.Drop($"{subject} '{name}' must be text.", path);
                }

                if (template.Length > 400)
                {
                    return context.Drop($"{subject} '{name}' is longer than 400 characters.", path);
                }

                foreach (string placeholder in ThemeLayoutVocabulary.Placeholders(template))
                {
                    if (!context.Contract.Bindings.ContainsKey(placeholder))
                    {
                        return context.Drop($"'{{{placeholder}}}' is not a value the {context.Contract.Screen} screen offers.", path, error: true);
                    }
                }

                return true;

            default:
                if (value is not JsonValue textValue || !textValue.TryGetValue(out string? text) || string.IsNullOrWhiteSpace(text))
                {
                    return context.Drop($"{subject} '{name}' must be text.", path);
                }

                return ValidateText(type, slot, name, text, path, context, subject);
        }
    }

    private static bool ValidateText(string type, ThemeLayoutSlot? slot, string name, string text, string path, Context context, string subject)
    {
        if (ThemeLayoutVocabulary.Words.TryGetValue(name, out var words) && !words.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            return context.Drop($"{subject} '{name}' must be one of: {string.Join(", ", words)}.", path);
        }

        if (name == "style")
        {
            var styles = type == "Button" ? ThemeLayoutVocabulary.ButtonStyles : ThemeLayoutVocabulary.TextStyles;
            return styles.Contains(text, StringComparer.OrdinalIgnoreCase) || context.Drop($"{subject} 'style' must be one of: {string.Join(", ", styles)}.", path);
        }

        if (name == "command")
        {
            return context.Contract.Commands.ContainsKey(text) || context.Drop($"'{text}' is not a command the {context.Contract.Screen} screen offers.", path, error: true);
        }

        if (name == "source")
        {
            return ThemePackagePaths.TryNormalize(text, out _) || context.Drop($"'{text}' is not a file inside the theme package.", path, error: true);
        }

        if (name is "columns" or "rows")
        {
            return TrackPattern().IsMatch(text) || context.Drop($"{subject} '{name}' must be a list such as \"2*,*,auto,240\".", path);
        }

        if (slot?.KeyProperty == name && slot.AllowedKeys is { } allowed && !allowed.Contains(text, StringComparer.Ordinal))
        {
            return context.Drop($"'{text}' is not one of: {string.Join(", ", allowed)}.", path, error: true);
        }

        return text.Length <= 400 || context.Drop($"{subject} '{name}' is longer than 400 characters.", path);
    }

    private static bool ValidateCondition(JsonNode value, string name, string path, Context context, string subject)
    {
        if (value is JsonValue literal && literal.TryGetValue(out bool _))
        {
            return true;
        }

        if (value is JsonObject { Count: 1 } condition)
        {
            var (key, target) = condition.First();
            if (key is "bind" or "not" && target is JsonValue targetValue && targetValue.TryGetValue(out string? binding) && binding is not null)
            {
                return context.Contract.Bindings.ContainsKey(binding) || context.Drop($"'{binding}' is not a value the {context.Contract.Screen} screen offers.", path, error: true);
            }
        }

        return context.Drop($"{subject} '{name}' must be true, false, {{ \"bind\": \"name\" }} or {{ \"not\": \"name\" }}.", path);
    }

    private static (double Low, double High) NumberRange(string name) => name switch
    {
        "opacity" => (0, 1),
        "ratio" => (0.05, 0.95),
        "column" or "row" => (0, 47),
        "columnSpan" or "rowSpan" => (1, 48),
        "spacing" or "columnSpacing" or "rowSpacing" or "radius" or "borderWidth" => (0, 400),
        "size" => (0, 2000),
        _ => (0, 10000),
    };

    private static void CheckSlots(List<ThemeLayoutNode> nodes, ThemeLayoutContract contract, Context context, string basePath)
    {
        foreach (var slot in contract.Slots.Where(s => s.Unique))
        {
            var uses = nodes.Where(n => n.Type == slot.Type).ToList();
            if (slot.KeyProperty is null)
            {
                if (uses.Count > 1)
                {
                    context.Fatal($"{slot.Type} can appear only once in a layout (it appears {uses.Count} times).", basePath);
                }

                continue;
            }

            foreach (var use in uses.Where(u => u.Text(slot.KeyProperty) is null))
            {
                context.Fatal($"'{use.Id}' ({slot.Type}) needs a \"{slot.KeyProperty}\".", basePath);
            }

            foreach (var repeated in uses.Where(u => u.Text(slot.KeyProperty) is not null).GroupBy(u => u.Text(slot.KeyProperty)!).Where(g => g.Count() > 1))
            {
                context.Fatal($"{slot.Type} '{repeated.Key}' can appear only once in a layout.", basePath);
            }
        }

        var present = nodes.Select(n => n.Type).ToHashSet(StringComparer.Ordinal);
        foreach (var group in contract.RequiredAnyOf)
        {
            if (!group.Any(present.Contains))
            {
                context.Fatal($"The layout leaves out something the {contract.Screen} screen needs: it must include {string.Join(" or ", group)}.", basePath);
            }
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, JsonNode>> ParseOverrides(
        IReadOnlyDictionary<string, JsonNode?> source, Dictionary<string, ThemeLayoutNode> byId, Context context, string path, string subject)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, JsonNode>>(StringComparer.Ordinal);
        foreach (var (nodeId, patchNode) in source)
        {
            if (!byId.TryGetValue(nodeId, out var target))
            {
                context.Warn($"{subject} for '{nodeId}' no longer matches a node in this layout and was left out.", $"{path}.{nodeId}");
                continue;
            }

            if (patchNode is not JsonObject patch)
            {
                continue;
            }

            var properties = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            var slot = context.Contract.FindSlot(target.Type);
            foreach (var (name, value) in patch)
            {
                if (value is null)
                {
                    continue;
                }

                if (ReservedKeys.Contains(name) || slot?.KeyProperty == name)
                {
                    context.Drop($"{subject} cannot change '{name}' of '{nodeId}': a node keeps its identity, its type and its children.", $"{path}.{nodeId}.{name}", error: true);
                    continue;
                }

                if (ValidateProperty(target.Type, slot, name, value, $"{path}.{nodeId}.{name}", context, subject))
                {
                    properties[name] = value.DeepClone();
                }
            }

            if (properties.Count > 0)
            {
                result[nodeId] = properties;
            }
        }

        return result;
    }

    private static List<ThemeLayoutBreakpoint> ParseBreakpoints(JsonNode? node, Dictionary<string, ThemeLayoutNode> byId, Context context, string path)
    {
        var result = new List<ThemeLayoutBreakpoint>();
        if (node is not JsonObject breakpoints)
        {
            return result;
        }

        foreach (var (name, value) in breakpoints)
        {
            if (result.Count >= ThemeLayoutVocabulary.MaxBreakpoints)
            {
                context.Warn($"A layout may declare at most {ThemeLayoutVocabulary.MaxBreakpoints} breakpoints; the rest are ignored.", path);
                break;
            }

            if (value is not JsonObject breakpoint)
            {
                continue;
            }

            double? minWidth = breakpoint["minWidth"] is JsonValue minValue && minValue.TryGetValue(out double min) && min >= 0 ? min : null;
            double? maxWidth = breakpoint["maxWidth"] is JsonValue maxValue && maxValue.TryGetValue(out double max) && max >= 0 ? max : null;
            if (minWidth is null && maxWidth is null)
            {
                context.Warn($"Breakpoint '{name}' has neither a minWidth nor a maxWidth, so it would always apply; it is ignored.", $"{path}.{name}");
                continue;
            }

            if (minWidth > maxWidth)
            {
                context.Warn($"Breakpoint '{name}' has a minWidth above its maxWidth, so it can never apply; it is ignored.", $"{path}.{name}");
                continue;
            }

            var overrides = breakpoint["overrides"] is JsonObject overrideObject
                ? ParseOverrides(overrideObject.ToDictionary(p => p.Key, p => p.Value), byId, context, $"{path}.{name}.overrides", "A breakpoint override")
                : new Dictionary<string, IReadOnlyDictionary<string, JsonNode>>();

            foreach (var earlier in result)
            {
                bool overlap = (earlier.MinWidth ?? 0) <= (maxWidth ?? double.MaxValue) && (minWidth ?? 0) <= (earlier.MaxWidth ?? double.MaxValue);
                if (overlap)
                {
                    context.Info($"Breakpoints '{earlier.Name}' and '{name}' overlap; where both match, '{earlier.Name}' (declared first) is used.", $"{path}.{name}");
                }
            }

            result.Add(new ThemeLayoutBreakpoint(name, minWidth, maxWidth, overrides));
        }

        return result;
    }

    private sealed class Context
    {
        private readonly List<ThemeDiagnostic> _diagnostics;

        public Context(ThemeLayoutContract contract, List<ThemeDiagnostic> diagnostics)
        {
            Contract = contract;
            _diagnostics = diagnostics;
        }

        public ThemeLayoutContract Contract { get; }

        public bool IsFatal { get; private set; }

        public void Fatal(string message, string path)
        {
            IsFatal = true;
            _diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.InvalidLayout, message + " The built-in layout is used.", path, FallbackUsed: true));
        }

        public void Warn(string message, string path) =>
            _diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.InvalidLayout, message, path));

        public void Info(string message, string path) =>
            _diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Information, ThemeDiagnosticCodes.InvalidLayout, message, path));

        /// <summary>Reports a property that is being left out. Always returns false, so it reads as the validation result.</summary>
        public bool Drop(string message, string path, bool error = false)
        {
            _diagnostics.Add(new ThemeDiagnostic(error ? ThemeDiagnosticSeverity.Error : ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.InvalidLayout, message, path));
            return false;
        }
    }
}
