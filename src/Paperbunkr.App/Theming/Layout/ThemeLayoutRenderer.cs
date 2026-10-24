using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Theming.Layout;

/// <summary>What a screen gives the renderer: its host components, the values and commands its contract publishes, and theme services.</summary>
public sealed class ThemeLayoutRenderContext
{
    /// <summary>Creates the host component for a slot node ("Home.Sections"), or null when it cannot.</summary>
    public required Func<ThemeLayoutNode, Control?> CreateSlot { get; init; }

    /// <summary>The current value of a published binding ("home.greeting").</summary>
    public required Func<string, object?> GetBinding { get; init; }

    /// <summary>The command behind a published command name. The command does its own validation and error handling, as it does for the built-in UI.</summary>
    public required Func<string, ICommand?> GetCommand { get; init; }

    /// <summary>Opens a file from the active theme's own package, or null.</summary>
    public Func<string, Stream?> OpenAsset { get; init; } = _ => null;

    /// <summary>A named material from the active theme as a brush, or null.</summary>
    public Func<string, IBrush?> GetMaterial { get; init; } = _ => null;

    /// <summary>A component from an installed extension, or null when no provider offers it (design doc section 14). Never executes anything from the theme.</summary>
    public Func<string, string, ThemeLayoutNode, Control?> CreateExtension { get; init; } = (_, _, _) => null;

    /// <summary>Plays a named theme motion on a control as it appears (phase 5). A no-op until a motion runtime is attached.</summary>
    public Action<Control, string> ApplyMotion { get; init; } = (_, _) => { };
}

/// <summary>A rendered layout: its root and the callbacks that re-read bound values.</summary>
public sealed class ThemeLayoutRenderResult
{
    public required Control Root { get; init; }

    /// <summary>Re-evaluates every template and condition. Call when a published value may have changed.</summary>
    public required Action Refresh { get; init; }

    /// <summary>Node id to the control built for it - for tests, focus restoration and motion.</summary>
    public required IReadOnlyDictionary<string, Control> Controls { get; init; }
}

/// <summary>
/// Builds controls from a validated layout tree (design doc section 8). The vocabulary is closed and everything is built in code: a layout is
/// data, so it cannot name an arbitrary control, bind to a view-model, attach an event handler or run an expression. Colours and backgrounds
/// are bound to the live theme resources, so a rendered layout follows a theme switch like any hand-written view.
/// </summary>
public static class ThemeLayoutRenderer
{
    /// <summary>The id of the layout node a control was built for.</summary>
    public static readonly AttachedProperty<string?> NodeIdProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, string?>("NodeId");

    public static string? GetNodeId(Control control) => control.GetValue(NodeIdProperty);

    public static ThemeLayoutRenderResult Render(ThemeLayoutNode root, ThemeLayoutRenderContext context)
    {
        var refreshers = new List<Action>();
        var controls = new Dictionary<string, Control>(StringComparer.Ordinal);
        var control = Build(root, context, refreshers, controls);

        void Refresh()
        {
            foreach (var refresh in refreshers)
            {
                refresh();
            }
        }

        Refresh();
        return new ThemeLayoutRenderResult { Root = control, Refresh = Refresh, Controls = controls };
    }

    private static Control Build(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        Control control = node.Type switch
        {
            "Stack" => BuildStack(node, context, refreshers, controls),
            "Grid" => BuildGrid(node, context, refreshers, controls),
            "Dock" => BuildDock(node, context, refreshers, controls),
            "Split" => BuildSplit(node, context, refreshers, controls),
            "Tabs" => BuildTabs(node, context, refreshers, controls),
            "Overlay" => BuildOverlay(node, context, refreshers, controls),
            "Scroll" => new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Padding = Thickness(node, "padding"),
                Content = Build(node.Children[0], context, refreshers, controls),
            },
            "Panel" => BuildPanel(node, context, refreshers, controls),
            "Text" => BuildText(node, context, refreshers),
            "Image" => BuildImage(node, context),
            "Icon" => new ThemedIcon { IconName = node.Text("name"), IconSize = node.Number("size") ?? 18 },
            "Button" => BuildButton(node, context, refreshers),
            "Spacer" => new Border { Width = node.Number("size") ?? 0, Height = node.Number("size") ?? 0 },
            "Divider" => BuildDivider(node),
            "Extension" => BuildExtension(node, context, refreshers, controls),
            _ => context.CreateSlot(node) ?? Missing(node),
        };

        ApplyCommon(control, node, context, refreshers);
        control.SetValue(NodeIdProperty, node.Id);
        if (string.IsNullOrEmpty(AutomationProperties.GetAutomationId(control)))
        {
            AutomationProperties.SetAutomationId(control, "Layout." + node.Id);
        }

        controls[node.Id] = control;
        return control;
    }

    // ===================== Containers =====================

    private static Control BuildStack(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        var panel = new StackPanel
        {
            Orientation = IsHorizontal(node) ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = node.Number("spacing") ?? 0,
        };
        panel.Children.AddRange(node.Children.Select(child => Build(child, context, refreshers, controls)));
        return Padded(panel, node);
    }

    private static Control BuildGrid(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse(node.Text("columns") ?? "*"),
            RowDefinitions = RowDefinitions.Parse(node.Text("rows") ?? "auto"),
            ColumnSpacing = node.Number("columnSpacing") ?? 0,
            RowSpacing = node.Number("rowSpacing") ?? 0,
        };

        foreach (var childNode in node.Children)
        {
            var child = Build(childNode, context, refreshers, controls);

            // Clamp to the tracks that exist: a cell outside the grid would silently pile up in the last one.
            int column = Math.Min((int)(childNode.Number("column") ?? 0), Math.Max(0, grid.ColumnDefinitions.Count - 1));
            int row = (int)(childNode.Number("row") ?? 0);
            while (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            Grid.SetColumn(child, column);
            Grid.SetRow(child, row);
            Grid.SetColumnSpan(child, Math.Max(1, (int)(childNode.Number("columnSpan") ?? 1)));
            Grid.SetRowSpan(child, Math.Max(1, (int)(childNode.Number("rowSpan") ?? 1)));
            grid.Children.Add(child);
        }

        return Padded(grid, node);
    }

    private static Control BuildDock(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        var panel = new DockPanel { LastChildFill = node.Flag("lastChildFill") ?? true };

        // A "fill" child takes the remaining space, so it has to be added last whatever order the author wrote.
        foreach (var childNode in node.Children.OrderBy(c => string.Equals(c.Text("dock"), "fill", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            var child = Build(childNode, context, refreshers, controls);
            DockPanel.SetDock(child, (childNode.Text("dock") ?? "top").ToLowerInvariant() switch
            {
                "left" => Dock.Left,
                "right" => Dock.Right,
                "bottom" => Dock.Bottom,
                _ => Dock.Top,
            });
            panel.Children.Add(child);
        }

        return Padded(panel, node);
    }

    private static Control BuildSplit(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        bool horizontal = !string.Equals(node.Text("orientation"), "vertical", StringComparison.OrdinalIgnoreCase);
        double ratio = node.Number("ratio") ?? 0.5;
        bool resizable = node.Flag("resizable") ?? false;
        var first = Build(node.Children[0], context, refreshers, controls);
        var second = Build(node.Children[1], context, refreshers, controls);
        var grid = new Grid();

        var firstLength = new GridLength(ratio, GridUnitType.Star);
        var secondLength = new GridLength(1 - ratio, GridUnitType.Star);
        var splitter = new GridSplitter { IsVisible = resizable, Background = Brushes.Transparent };
        AutomationProperties.SetName(splitter, "Resize panes");

        if (horizontal)
        {
            grid.ColumnDefinitions = new ColumnDefinitions { new(firstLength) { MinWidth = 120 }, new(GridLength.Auto), new(secondLength) { MinWidth = 120 } };
            splitter.Width = resizable ? 6 : 0;
            splitter.ResizeDirection = GridResizeDirection.Columns;
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);
        }
        else
        {
            grid.RowDefinitions = new RowDefinitions { new(firstLength) { MinHeight = 80 }, new(GridLength.Auto), new(secondLength) { MinHeight = 80 } };
            splitter.Height = resizable ? 6 : 0;
            splitter.ResizeDirection = GridResizeDirection.Rows;
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);
        }

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);
        return grid;
    }

    private static Control BuildTabs(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        var tabs = new TabControl();
        foreach (var childNode in node.Children)
        {
            var item = new TabItem { Content = Build(childNode, context, refreshers, controls) };
            string template = childNode.Text("title") ?? childNode.Id;
            refreshers.Add(() => item.Header = Format(template, context));
            tabs.Items.Add(item);
        }

        return tabs;
    }

    private static Control BuildOverlay(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        var panel = new Panel();
        panel.Children.AddRange(node.Children.Select(child => Build(child, context, refreshers, controls)));
        return panel;
    }

    private static Control BuildPanel(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        var border = new Border
        {
            Padding = Thickness(node, "padding"),
            CornerRadius = new CornerRadius(node.Number("radius") ?? 0),
            BorderThickness = new Thickness(node.Number("borderWidth") ?? (node.Text("border") is null ? 0 : 1)),
            Child = node.Children.Count == 1 ? Build(node.Children[0], context, refreshers, controls) : null,
        };
        Paint(border, Border.BackgroundProperty, node.Text("background"), context);
        Paint(border, Border.BorderBrushProperty, node.Text("border"), context);
        return border;
    }

    // ===================== Primitives =====================

    private static Control BuildText(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers)
    {
        var text = new TextBlock { TextWrapping = node.Flag("wrap") ?? true ? TextWrapping.Wrap : TextWrapping.NoWrap };
        text.Classes.Add((node.Text("style") ?? "body").ToLowerInvariant() switch
        {
            "hero" => "pbTextHero",
            "heading" => "pbTextHeading",
            "caption" => "pbTextCaption",
            _ => "pbTextBody",
        });
        Paint(text, TextBlock.ForegroundProperty, node.Text("color"), context);

        string template = node.Text("text") ?? string.Empty;
        refreshers.Add(() => text.Text = Format(template, context));
        return text;
    }

    private static Control BuildImage(ThemeLayoutNode node, ThemeLayoutRenderContext context)
    {
        var image = new Image
        {
            Stretch = (node.Text("stretch") ?? "uniform").ToLowerInvariant() switch
            {
                "fill" => Stretch.Fill,
                "uniformtofill" => Stretch.UniformToFill,
                "none" => Stretch.None,
                _ => Stretch.Uniform,
            },
        };
        AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw); // decorative

        if (node.Text("source") is { } source && ThemePackagePaths.TryNormalize(source, out string normalized))
        {
            try
            {
                using var stream = context.OpenAsset(normalized);
                if (stream is not null)
                {
                    image.Source = new Bitmap(stream);
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                // A missing or broken decorative image is simply not drawn.
            }
        }

        return image;
    }

    private static Control BuildButton(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers)
    {
        var button = new Button { Command = node.Text("command") is { } command ? context.GetCommand(command) : null };
        button.Classes.Add((node.Text("style") ?? "secondary").ToLowerInvariant());

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        string template = node.Text("text") ?? string.Empty;
        refreshers.Add(() =>
        {
            label.Text = Format(template, context);
            label.IsVisible = label.Text.Length > 0;

            // The accessible name is the button's own text, whatever icon a theme draws on it.
            AutomationProperties.SetName(button, label.Text.Length > 0 ? label.Text : node.Text("command") ?? node.Id);
        });

        if (node.Text("icon") is { } icon)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new ThemedIcon { IconName = icon, IconSize = 16 });
            content.Children.Add(label);
            button.Content = content;
        }
        else
        {
            button.Content = label;
        }

        // A button whose command the screen did not supply does nothing; show that honestly rather than as a live control.
        button.IsEnabled = button.Command is not null;
        return button;
    }

    private static Control BuildDivider(ThemeLayoutNode node)
    {
        var line = new Border();
        if (IsHorizontal(node))
        {
            line.Width = 1;
        }
        else
        {
            line.Height = 1;
        }

        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("PbBorderBrush"));
        return line;
    }

    private static Control BuildExtension(ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers, Dictionary<string, Control> controls)
    {
        string provider = node.Text("provider") ?? string.Empty;
        string component = node.Text("component") ?? string.Empty;
        try
        {
            if (provider.Length > 0 && component.Length > 0 && context.CreateExtension(provider, component, node) is { } supplied)
            {
                return supplied;
            }
        }
        catch (Exception)
        {
            // A provider that fails is treated as absent: the fallback below is shown.
        }

        // No provider (or it declined): the theme's own fallback, or nothing. Nothing is installed, fetched or run on a theme's say-so.
        return node.Children.Count == 1 ? Build(node.Children[0], context, refreshers, controls) : new Border();
    }

    private static Control Missing(ThemeLayoutNode node)
    {
        var note = new TextBlock { Text = $"{node.Type} is not available", Opacity = 0.6, Margin = new Thickness(8) };
        note.Classes.Add("pbTextCaption");
        return note;
    }

    // ===================== Shared =====================

    private static void ApplyCommon(Control control, ThemeLayoutNode node, ThemeLayoutRenderContext context, List<Action> refreshers)
    {
        if (node.Number("width") is { } width) control.Width = width;
        if (node.Number("height") is { } height) control.Height = height;
        if (node.Number("minWidth") is { } minWidth) control.MinWidth = minWidth;
        if (node.Number("maxWidth") is { } maxWidth) control.MaxWidth = maxWidth;
        if (node.Number("minHeight") is { } minHeight) control.MinHeight = minHeight;
        if (node.Number("maxHeight") is { } maxHeight) control.MaxHeight = maxHeight;
        if (node.Raw("margin") is not null) control.Margin = Thickness(node, "margin");
        if (node.Number("opacity") is { } opacity) control.Opacity = opacity;

        if (node.Text("align") is { } align)
        {
            control.HorizontalAlignment = align.ToLowerInvariant() switch
            {
                "left" => HorizontalAlignment.Left,
                "center" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Stretch,
            };
        }

        if (node.Text("valign") is { } valign)
        {
            control.VerticalAlignment = valign.ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Stretch,
            };
        }

        if (node.Raw("visible") is { } visible)
        {
            refreshers.Add(() => control.IsVisible = Evaluate(visible, context));
        }

        if (node.Text("motion") is { } motion)
        {
            context.ApplyMotion(control, motion);
        }
    }

    private static bool IsHorizontal(ThemeLayoutNode node) => string.Equals(node.Text("orientation"), "horizontal", StringComparison.OrdinalIgnoreCase);

    private static Thickness Thickness(ThemeLayoutNode node, string name) =>
        node.Raw(name) is { } raw && ThemeLayoutVocabulary.TryParseThickness(raw, out var t) ? new Thickness(t.Left, t.Top, t.Right, t.Bottom) : default;

    private static Control Padded(Panel panel, ThemeLayoutNode node)
    {
        if (node.Raw("padding") is null)
        {
            return panel;
        }

        return new Border { Padding = Thickness(node, "padding"), Child = panel };
    }

    /// <summary>A condition is a literal, a boolean binding, or its negation. Anything that is not exactly true is false.</summary>
    private static bool Evaluate(JsonNode condition, ThemeLayoutRenderContext context)
    {
        if (condition is JsonValue literal && literal.TryGetValue(out bool flag))
        {
            return flag;
        }

        if (condition is JsonObject { Count: 1 } expression)
        {
            var (key, target) = expression.First();
            bool value = target is JsonValue targetValue && targetValue.TryGetValue(out string? name) && name is not null && context.GetBinding(name) is true;
            return key == "not" ? !value : value;
        }

        return true;
    }

    /// <summary>Replaces each <c>{name}</c> with the published value's text. No expressions, no formatting language - just substitution.</summary>
    public static string Format(string template, ThemeLayoutRenderContext context)
    {
        if (template.IndexOf('{') < 0)
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
        int index = 0;
        while (index < template.Length)
        {
            int open = template.IndexOf('{', index);
            int close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            result.Append(template, index, open - index);
            string name = template[(open + 1)..close].Trim();
            result.Append(System.Convert.ToString(context.GetBinding(name), System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty);
            index = close + 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// Sets a brush property from a paint value: "material:name", a semantic role (bound to the live <c>Pb*Brush</c> resource so it follows
    /// theme changes), or a literal colour.
    /// </summary>
    private static void Paint(Control control, AvaloniaProperty property, string? paint, ThemeLayoutRenderContext context)
    {
        if (string.IsNullOrWhiteSpace(paint))
        {
            return;
        }

        if (Runtime.ThemeMaterialBuilder.MaterialName(paint) is { } material)
        {
            if (context.GetMaterial(material) is { } brush)
            {
                control.SetValue(property, brush);
            }

            return;
        }

        if (ThemeVocabulary.Find(ThemeTokenPaths.Normalize(paint)) is { Kind: ThemeValueKind.Color, Form: ThemeResourceForm.ColorAndBrush, ResourceKey: { } key })
        {
            control.Bind(property, control.GetResourceObservable($"Pb{key}Brush"));
            return;
        }

        if (ThemeColorMath.TryParse(paint, out var color))
        {
            control.SetValue(property, new SolidColorBrush(color));
        }
    }
}
