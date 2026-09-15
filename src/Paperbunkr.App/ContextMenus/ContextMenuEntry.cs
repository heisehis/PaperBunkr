using System.Collections.Generic;
using System.Windows.Input;
using FluentIcons.Common;

namespace Paperbunkr.App.ContextMenus;

/// <summary>
/// One row of a context menu, as plain data. Built by an <see cref="IContextMenuProvider"/> and
/// turned into Avalonia <c>MenuItem</c>/<c>Separator</c> controls by <see cref="Controls.ContextMenuHost"/>.
///
/// Deliberately holds no Avalonia control types so the whole menu for a screen can be asserted in
/// plain unit tests. "Not applicable here" is expressed by the builder simply omitting the entry -
/// there is no <c>IsVisible</c>; <see cref="SubMenu"/> returns <see langword="null"/> when asked to
/// be hidden and callers drop nulls.
/// </summary>
public sealed record ContextMenuEntry
{
    public string? Header { get; init; }

    /// <summary>Left-column glyph. Ignored when <see cref="IsChecked"/> is set (the check takes the slot).</summary>
    public Symbol? Icon { get; init; }

    public ICommand? Command { get; init; }
    public object? CommandParameter { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Renders a radio-style tick - used to mark the current value in a "set X" submenu.</summary>
    public bool IsChecked { get; init; }

    /// <summary>Right-aligned hint text, e.g. "Ctrl+I". Display only - no hotkey is registered.</summary>
    public string? InputGesture { get; init; }

    /// <summary>Destructive action - gets a red hover wash and (by convention) lives last.</summary>
    public bool IsDanger { get; init; }

    public IReadOnlyList<ContextMenuEntry>? Children { get; init; }

    public bool IsSeparator { get; init; }

    public static readonly ContextMenuEntry Separator = new() { IsSeparator = true };

    public static ContextMenuEntry Item(
        string header,
        ICommand? command,
        object? parameter = null,
        Symbol? icon = null,
        bool isEnabled = true,
        bool isChecked = false,
        string? inputGesture = null,
        bool isDanger = false) => new()
        {
            Header = header,
            Command = command,
            CommandParameter = parameter,
            Icon = icon,
            IsEnabled = isEnabled,
            IsChecked = isChecked,
            InputGesture = inputGesture,
            IsDanger = isDanger,
        };

    /// <summary>A parent entry with children. Returns <see langword="null"/> when
    /// <paramref name="isVisible"/> is false or <paramref name="children"/> is empty, so a caller can
    /// write <c>entries.AddIfNotNull(ContextMenuEntry.SubMenu(...))</c> and get the omit-entirely
    /// behavior for free.</summary>
    public static ContextMenuEntry? SubMenu(
        string header,
        IEnumerable<ContextMenuEntry?> children,
        Symbol? icon = null,
        bool isVisible = true,
        bool isDanger = false)
    {
        if (!isVisible)
        {
            return null;
        }

        var kept = new List<ContextMenuEntry>();
        foreach (var child in children)
        {
            if (child is not null)
            {
                kept.Add(child);
            }
        }

        return kept.Count == 0
            ? null
            : new ContextMenuEntry { Header = header, Icon = icon, IsDanger = isDanger, Children = kept };
    }

    /// <summary>
    /// Drops nulls (an omitted entry, or a <see cref="SubMenu"/> hidden/empty) and any resulting
    /// leading/trailing/consecutive separators. Every <see cref="IContextMenuProvider.BuildContextMenu"/>
    /// implementation that builds its menu as a flat <c>new[] { ... }</c> literal with conditional
    /// entries needs this - a raw <see langword="null"/> left in the returned list crashes whatever
    /// enumerates it looking for real entries (<see cref="Controls.ContextMenuHost"/>, or a test
    /// asserting on <c>.Header</c>), not just render as an omitted row.
    /// </summary>
    public static IReadOnlyList<ContextMenuEntry> Compact(IEnumerable<ContextMenuEntry?> entries)
    {
        var kept = new List<ContextMenuEntry>();
        foreach (var entry in entries)
        {
            if (entry is not null)
            {
                kept.Add(entry);
            }
        }

        var result = new List<ContextMenuEntry>(kept.Count);
        foreach (var entry in kept)
        {
            if (entry.IsSeparator && (result.Count == 0 || result[^1].IsSeparator))
            {
                continue;
            }

            result.Add(entry);
        }

        while (result.Count > 0 && result[^1].IsSeparator)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
