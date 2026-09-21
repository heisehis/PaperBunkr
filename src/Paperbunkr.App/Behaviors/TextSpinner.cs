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

    public static readonly AttachedProperty<decimal> MinimumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, decimal>("Minimum", typeof(TextSpinner), 0);

    public static readonly AttachedProperty<decimal> MaximumProperty =
        AvaloniaProperty.RegisterAttached<TextBox, decimal>("Maximum", typeof(TextSpinner), int.MaxValue);

    /// <summary>How much one click nudges the value - default <c>1</c> (every pre-existing caller:
    /// Number/Volume/Alternate Number/Story Arc Number, all whole-number fields). A fractional step
    /// (e.g. <c>0.1</c> for the per-tracker Score field, docs/superpowers/specs/2026-09-18-per-
    /// tracker-score-and-finish-date-design.md) only ever applies to the plain-number branch of
    /// <see cref="Step"/> - the mixed-text branches ("1.MU", "Vol 3") always nudge by a whole
    /// number, since those fields never set this to a fractional value.</summary>
    public static readonly AttachedProperty<decimal> StepProperty =
        AvaloniaProperty.RegisterAttached<TextBox, decimal>("Step", typeof(TextSpinner), 1);

    /// <summary>
    /// Opt-in: when the box loses focus and its whole text is a plain number outside <see cref="MinimumProperty"/>..<see cref="MaximumProperty"/>,
    /// pull it back into range - what a <see cref="NumericUpDown"/> did on commit. Off by default because the mixed-text fields
    /// (Number "1.MU", negative issue numbers) must keep whatever the user typed. Text that isn't a plain number is left alone.
    /// </summary>
    public static readonly AttachedProperty<bool> ClampTypedProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("ClampTyped", typeof(TextSpinner));

    public static void SetClampTyped(TextBox t, bool v) => t.SetValue(ClampTypedProperty, v);
    public static bool GetClampTyped(TextBox t) => t.GetValue(ClampTypedProperty);

    public static void SetEnabled(TextBox t, bool v) => t.SetValue(EnabledProperty, v);
    public static bool GetEnabled(TextBox t) => t.GetValue(EnabledProperty);
    public static void SetMinimum(TextBox t, decimal v) => t.SetValue(MinimumProperty, v);
    public static decimal GetMinimum(TextBox t) => t.GetValue(MinimumProperty);
    public static void SetMaximum(TextBox t, decimal v) => t.SetValue(MaximumProperty, v);
    public static decimal GetMaximum(TextBox t) => t.GetValue(MaximumProperty);
    public static void SetStep(TextBox t, decimal v) => t.SetValue(StepProperty, v);
    public static decimal GetStep(TextBox t) => t.GetValue(StepProperty);

    static TextSpinner()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>(OnEnabledChanged);
        ClampTypedProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.LostFocus -= OnClampLostFocus;
            if (e.NewValue is true)
            {
                box.LostFocus += OnClampLostFocus;
            }
        });
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

    private static void OnClampLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            string? clamped = ClampWholeNumber(box.Text, GetMinimum(box), GetMaximum(box));
            if (clamped is not null)
            {
                box.Text = clamped;
            }
        }
    }

    /// <summary>The clamped text when <paramref name="text"/> is a plain number outside the range; null when it is in range or isn't a plain number.</summary>
    public static string? ClampWholeNumber(string? text, decimal min, decimal max)
    {
        if (!decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return null;
        }

        decimal clamped = Clamp(value, min, max);
        return clamped == value ? null : clamped.ToString(CultureInfo.InvariantCulture);
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
        up.Click += (_, _) => Nudge(box, GetStep(box));
        down.Click += (_, _) => Nudge(box, -GetStep(box));

        // Bordered pill container (docs/superpowers/specs/2026-09-14-metadata-editors-redesign-
        // design.md §4) - the two RepeatButtons share one rounded container with a divider between
        // them, rather than floating borderless as before.
        var divider = new Border { Classes = { "textSpinnerDivider" } };

        return new Border
        {
            Tag = new SpinnerTag(),
            Classes = { "textSpinnerContainer" },
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children = { up, divider, down },
            },
        };
    }

    private static void Nudge(TextBox box, decimal delta)
    {
        box.Text = Step(box.Text ?? string.Empty, delta, GetMinimum(box), GetMaximum(box));
        box.CaretIndex = box.Text.Length;
    }

    /// <summary>
    /// Nudge the number in <paramref name="text"/> by <paramref name="delta"/>:
    /// <list type="bullet">
    /// <item>whole text is a plain number -&gt; <c>clamp(n + delta)</c>, decimal arithmetic so a
    /// fractional <paramref name="delta"/> (e.g. Score's <c>0.1</c>) works exactly, no binary-float
    /// rounding drift;</item>
    /// <item>ends with a digit run -&gt; increment it, keep the prefix (<c>"Vol 3"</c> -&gt; <c>"Vol 4"</c>);</item>
    /// <item>starts with a digit run -&gt; increment it, keep the suffix (<c>"1.MU"</c> -&gt; <c>"2.MU"</c>);</item>
    /// <item>no digits at all -&gt; <c>min</c> (or <c>1</c> when <c>min</c> is 0).</item>
    /// </list>
    /// The digit-run branches stay whole-number (<see cref="long"/>) arithmetic - only the
    /// plain-number branch needs decimal, since that's the only one a fractional Step ever reaches
    /// (mixed alphanumeric fields never set a fractional Step). Friendlier than CE, which wipes any
    /// unparseable value to its default.
    /// </summary>
    public static string Step(string text, decimal delta, decimal min, decimal max)
    {
        string trimmed = text.Trim();

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal whole))
        {
            return Clamp(whole + delta, min, max).ToString(CultureInfo.InvariantCulture);
        }

        int intDelta = (int)delta;
        int end = trimmed.Length;
        while (end > 0 && char.IsDigit(trimmed[end - 1]))
        {
            end--;
        }
        if (end < trimmed.Length)
        {
            long bumped = Clamp(long.Parse(trimmed[end..], CultureInfo.InvariantCulture) + intDelta, (long)min, (long)max);
            return trimmed[..end] + bumped.ToString(CultureInfo.InvariantCulture);
        }

        int start = 0;
        while (start < trimmed.Length && char.IsDigit(trimmed[start]))
        {
            start++;
        }
        if (start > 0)
        {
            long bumped = Clamp(long.Parse(trimmed[..start], CultureInfo.InvariantCulture) + intDelta, (long)min, (long)max);
            return bumped.ToString(CultureInfo.InvariantCulture) + trimmed[start..];
        }

        return (min == 0 ? 1 : min).ToString(CultureInfo.InvariantCulture);
    }

    private static long Clamp(long value, long min, long max) => Math.Max(min, Math.Min(max, value));

    private static decimal Clamp(decimal value, decimal min, decimal max) => Math.Max(min, Math.Min(max, value));
}
