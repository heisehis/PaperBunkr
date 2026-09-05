using System;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Behaviors;

/// <summary>
/// Makes an <see cref="AutoCompleteBox"/> autocomplete the <em>last comma-separated segment</em>
/// of its text instead of the whole value - so a "Writer" of <c>"Grant Morrison, Fr"</c> filters
/// suggestions against <c>"Fr"</c> and, on pick, becomes <c>"Grant Morrison, Frank Quitely, "</c>
/// (docs/superpowers/specs/2026-09-05-metadata-editor-affordances-design.md §3.2). CE matches the
/// whole value and offers a separate transfer-list picker for individual names; folding both into
/// one box is the deliberate improvement.
///
/// Plain attached property (no <c>Avalonia.Xaml.Behaviors</c> package in this project) - same
/// registration shape as <see cref="Paperbunkr.App.Controls.ContextMenuHost"/>. Works purely
/// through <see cref="AutoCompleteBox.TextFilter"/> + <see cref="AutoCompleteBox.TextSelector"/>,
/// so there is no event wiring or re-entrancy to guard.
/// </summary>
public static class MultiValueAutoComplete
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBox, bool>(
            "Enabled", typeof(MultiValueAutoComplete));

    public static void SetEnabled(AutoCompleteBox target, bool value) => target.SetValue(EnabledProperty, value);

    public static bool GetEnabled(AutoCompleteBox target) => target.GetValue(EnabledProperty);

    static MultiValueAutoComplete()
    {
        EnabledProperty.Changed.AddClassHandler<AutoCompleteBox>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(AutoCompleteBox box, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            box.FilterMode = AutoCompleteFilterMode.Custom;
            box.TextFilter = SegmentContains;
            box.TextSelector = SpliceSelected;
        }
        else if (e.OldValue is true)
        {
            box.TextFilter = null;
            box.TextSelector = null;
        }
    }

    private static bool SegmentContains(string? search, string? item)
    {
        string segment = LastSegment(search).Segment;
        if (segment.Length == 0)
        {
            return true;
        }

        return item is not null && item.Contains(segment, StringComparison.OrdinalIgnoreCase);
    }

    private static string SpliceSelected(string? search, string? item) =>
        Splice(LastSegment(search).Prefix, item ?? string.Empty);

    /// <summary>Splits <paramref name="text"/> at its final comma. <c>"A, B, cd"</c> -&gt;
    /// (<c>"A, B, "</c>, <c>"cd"</c>); no comma -&gt; (<c>""</c>, whole trimmed-left text).</summary>
    public static (string Prefix, string Segment) LastSegment(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, string.Empty);
        }

        int comma = text.LastIndexOf(',');
        return comma < 0
            ? (string.Empty, text.TrimStart())
            : (text[..(comma + 1)] + " ", text[(comma + 1)..].TrimStart());
    }

    /// <summary>Re-joins a <paramref name="prefix"/> from <see cref="LastSegment"/> with the
    /// <paramref name="chosen"/> value, leaving a trailing <c>", "</c> ready for the next entry.</summary>
    public static string Splice(string prefix, string chosen) => $"{prefix}{chosen.Trim()}, ";
}
