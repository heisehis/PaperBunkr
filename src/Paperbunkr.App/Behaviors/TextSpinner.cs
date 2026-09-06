using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Paperbunkr.App.Behaviors;

/// <summary>
/// Adds a compact up/down spinner to a <see cref="TextBox"/> whose value is usually - but not
/// always - an integer: Number (<c>"1.MU"</c>), Volume (<c>"2001"</c>), Alternate Number,
/// Story Arc Number (docs/superpowers/specs/2026-09-05-metadata-editor-affordances-design.md §3.3).
/// A real <see cref="NumericUpDown"/> would reject the non-numeric cases; this keeps the field a
/// plain text box and nudges whatever number it can find, preserving the rest of the text.
///
/// Plain attached property (no <c>Avalonia.Xaml.Behaviors</c> package) - registration shape from
/// <see cref="Paperbunkr.App.Controls.ContextMenuHost"/>. The spinner is hosted in
/// <see cref="TextBox.InnerRightContent"/>; <see cref="RepeatButton"/>s so press-and-hold repeats.
/// </summary>
public static class TextSpinner
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(TextSpinner));

    public static readonly AttachedProperty<int> MinimumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int>("Minimum", typeof(TextSpinner), 0);

    public static readonly AttachedProperty<int> MaximumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int>("Maximum", typeof(TextSpinner), int.MaxValue);

    public static void SetEnabled(TextBox t, bool v) => t.SetValue(EnabledProperty, v);
    public static bool GetEnabled(TextBox t) => t.GetValue(EnabledProperty);
    public static void SetMinimum(TextBox t, int v) => t.SetValue(MinimumProperty, v);
    public static int GetMinimum(TextBox t) => t.GetValue(MinimumProperty);
    public static void SetMaximum(TextBox t, int v) => t.SetValue(MaximumProperty, v);
    public static int GetMaximum(TextBox t) => t.GetValue(MaximumProperty);

    static TextSpinner()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(TextBox box, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            box.InnerRightContent = BuildSpinner(box);
        }
        else if (e.OldValue is true && box.InnerRightContent is Control { Tag: SpinnerTag })
        {
            box.InnerRightContent = null;
        }
    }

    private sealed class SpinnerTag { }

    private static Control BuildSpinner(TextBox box)
    {
        var up = new RepeatButton
        {
            Classes = { "textSpinner" },
            Content = new SymbolIcon { Symbol = Symbol.ChevronUp },
            [AutomationProperties.NameProperty] = "Increase",
        };
        var down = new RepeatButton
        {
            Classes = { "textSpinner" },
            Content = new SymbolIcon { Symbol = Symbol.ChevronDown },
            [AutomationProperties.NameProperty] = "Decrease",
        };
        up.Click += (_, _) => Nudge(box, +1);
        down.Click += (_, _) => Nudge(box, -1);

        return new StackPanel
        {
            Tag = new SpinnerTag(),
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { up, down },
        };
    }

    private static void Nudge(TextBox box, int delta)
    {
        box.Text = Step(box.Text ?? string.Empty, delta, GetMinimum(box), GetMaximum(box));
        box.CaretIndex = box.Text.Length;
    }

    /// <summary>
    /// Nudge the number in <paramref name="text"/> by <paramref name="delta"/>:
    /// <list type="bullet">
    /// <item>whole text is an integer -&gt; <c>clamp(n + delta)</c>;</item>
    /// <item>ends with a digit run -&gt; increment it, keep the prefix (<c>"Vol 3"</c> -&gt; <c>"Vol 4"</c>);</item>
    /// <item>starts with a digit run -&gt; increment it, keep the suffix (<c>"1.MU"</c> -&gt; <c>"2.MU"</c>);</item>
    /// <item>no digits at all -&gt; <c>min</c> (or <c>1</c> when <c>min</c> is 0).</item>
    /// </list>
    /// Friendlier than CE, which wipes any unparseable value to its default.
    /// </summary>
    public static string Step(string text, int delta, int min, int max)
    {
        string trimmed = text.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
        {
            return Clamp(whole + delta, min, max).ToString(CultureInfo.InvariantCulture);
        }

        int end = trimmed.Length;
        while (end > 0 && char.IsDigit(trimmed[end - 1]))
        {
            end--;
        }
        if (end < trimmed.Length)
        {
            long bumped = Clamp(long.Parse(trimmed[end..], CultureInfo.InvariantCulture) + delta, min, max);
            return trimmed[..end] + bumped.ToString(CultureInfo.InvariantCulture);
        }

        int start = 0;
        while (start < trimmed.Length && char.IsDigit(trimmed[start]))
        {
            start++;
        }
        if (start > 0)
        {
            long bumped = Clamp(long.Parse(trimmed[..start], CultureInfo.InvariantCulture) + delta, min, max);
            return bumped.ToString(CultureInfo.InvariantCulture) + trimmed[start..];
        }

        return (min == 0 ? 1 : min).ToString(CultureInfo.InvariantCulture);
    }

    private static long Clamp(long value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
