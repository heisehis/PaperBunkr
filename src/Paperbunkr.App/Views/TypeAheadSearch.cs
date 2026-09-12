using System;
using System.Collections.Generic;
using Avalonia.Controls;
using cYo.Common.Text;

namespace Paperbunkr.App.Views;

/// <summary>
/// Buffered multi-character type-ahead jump-to-item, porting CE's own
/// <c>cYo.Common.Windows.Forms.KeySearch</c> exactly (docs/superpowers/specs/2026-09-12-grid-
/// typeahead-rangeselect-quit-design.md) rather than reinventing the algorithm: a 2.5s idle timeout
/// resets the buffer, backspace shrinks it, matching is a case-insensitive prefix search via
/// <see cref="StringUtility.StartsWith(string, string, StringComparison, bool)"/> (already ported
/// verbatim into this codebase, articles-ignoring included, with zero call sites before this one),
/// and a successful match is a plain jump-to-first-match (clears any existing selection) - not
/// additive, not cycling through repeat matches, matching CE's real behavior exactly.
///
/// Same "pure core, thin live-control wrapper" split as <see cref="GridKeyboardNavigation"/>:
/// <see cref="TryMatch{T}"/> has no Avalonia control types and is unit-testable directly;
/// <see cref="TryHandleTextInput{T}"/> is the live wrapper, reusing <see cref="GridFocusHelper"/> for
/// the same "focus this item, realizing it first if necessary" concern arrow-nav's virtualized path
/// already needed a version of.
/// </summary>
public static class TypeAheadSearch
{
    private const int SearchDelayMs = 2500;

    /// <summary>Mutable buffer state - one instance per grid, held as a field on the owning
    /// screen's code-behind (matches CE's one-<c>KeySearch</c>-per-<c>ItemView</c> model, not a
    /// shared/static instance).</summary>
    public sealed class Buffer
    {
        internal string CurrentText = string.Empty;
        internal long LastTicks;
    }

    /// <summary>
    /// Pure core: advances <paramref name="buffer"/> by one character and returns the first item in
    /// <paramref name="items"/> (in their given order) whose <paramref name="textSelector"/> starts
    /// with the resulting buffer text - articles ignored, case-insensitive. On no match, the buffer
    /// is left unchanged (mirrors <c>KeySearch.Select</c>'s own <c>if (flag) currentText = arg;</c>),
    /// so a stray non-matching keystroke doesn't discard an otherwise-valid in-progress search.
    /// <paramref name="typedChar"/> uses <c>'\b'</c> as CE's own backspace sentinel (shrinks the
    /// buffer by one character instead of appending).
    /// </summary>
    public static T? TryMatch<T>(Buffer buffer, char typedChar, IReadOnlyList<T> items, Func<T, string> textSelector, long nowTicks)
        where T : class
    {
        if (nowTicks > buffer.LastTicks + SearchDelayMs)
        {
            buffer.CurrentText = string.Empty;
        }

        string candidate = typedChar != '\b'
            ? buffer.CurrentText + typedChar
            : (buffer.CurrentText.Length > 0 ? buffer.CurrentText[..^1] : string.Empty);

        T? match = null;
        foreach (var item in items)
        {
            if (StringUtility.StartsWith(textSelector(item), candidate, StringComparison.OrdinalIgnoreCase, ignoreArticles: true))
            {
                match = item;
                break;
            }
        }

        if (match is not null)
        {
            buffer.CurrentText = candidate;
        }

        buffer.LastTicks = nowTicks;
        return match;
    }

    /// <summary>
    /// Live wrapper: matches against <paramref name="itemsControl"/>'s current items (its bound
    /// <see cref="ItemsControl.Items"/>, not just realized containers - unlike arrow-nav, type-ahead
    /// must be able to jump to an off-screen/unrealized item), clears
    /// <paramref name="clearSelection"/>'s selection on a match, then focuses the matched item via
    /// <see cref="GridFocusHelper.FocusItem"/>. Returns whether a match was found and focused (the
    /// caller sets <c>e.Handled</c> on that, matching CE's own <c>e.Handled = ks.Select(e.KeyChar)</c>).
    /// </summary>
    public static bool TryHandleTextInput<T>(Buffer buffer, char typedChar, ItemsControl itemsControl, Func<T, string> textSelector, Action clearSelection)
        where T : class
    {
        var items = new List<T>(itemsControl.Items.Count);
        foreach (var raw in itemsControl.Items)
        {
            if (raw is T typed)
            {
                items.Add(typed);
            }
        }

        var match = TryMatch(buffer, typedChar, items, textSelector, Environment.TickCount64);
        if (match is null)
        {
            return false;
        }

        clearSelection();
        return GridFocusHelper.FocusItem(itemsControl, match);
    }
}
