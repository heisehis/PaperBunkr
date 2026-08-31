using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using Paperbunkr.App.ContextMenus;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Attach <c>controls:ContextMenuHost.Provider="{Binding}"</c> to a screen root. On a right-click
/// anywhere under it, walks the visual tree from the clicked element, asks the
/// <see cref="IContextMenuProvider"/> to build a menu for the nearest data context, and shows it as
/// a <see cref="MenuFlyout"/> at the pointer. Also reachable via the keyboard (Menu key / Shift+F10,
/// docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - same menu, same provider
/// lookup, just rooted at the focused element instead of the pointer.
///
/// Replaces per-item <c>&lt;Button.ContextMenu&gt;</c> markup whose commands bound through
/// <c>$parent[UserControl]</c> - unresolvable across a menu popup's own visual tree, which is why
/// the old menus' commands were dead. Here items are built in code from live <c>ICommand</c>
/// references.
///
/// A <see cref="MenuFlyout"/>, not a <see cref="ContextMenu"/>: a plain <c>ContextMenu</c> popup
/// does not render at all in this Avalonia 12 + FluentAvalonia build (its <c>Opening</c> event
/// fires but nothing appears) - the same failure the old <c>Button.ContextMenu</c> menus hit.
/// </summary>
public sealed class ContextMenuHost
{
    private ContextMenuHost()
    {
    }

    public static readonly AttachedProperty<IContextMenuProvider?> ProviderProperty =
        AvaloniaProperty.RegisterAttached<ContextMenuHost, Control, IContextMenuProvider?>("Provider");

    private static readonly ConditionalWeakTable<Control, HostState> s_state = new();

    static ContextMenuHost()
    {
        ProviderProperty.Changed.AddClassHandler<Control>(OnProviderChanged);
    }

    public static void SetProvider(Control target, IContextMenuProvider? value) =>
        target.SetValue(ProviderProperty, value);

    public static IContextMenuProvider? GetProvider(Control target) =>
        target.GetValue(ProviderProperty);

    private static void OnProviderChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not IContextMenuProvider provider)
        {
            return;
        }

        if (s_state.TryGetValue(host, out var existing))
        {
            existing.Provider = provider;
            return;
        }

        var state = new HostState { Provider = provider };
        host.AddHandler(InputElement.PointerReleasedEvent, (_, pe) => OnPointerReleased(host, state, pe),
            RoutingStrategies.Bubble, handledEventsToo: true);
        host.AddHandler(InputElement.KeyDownEvent, (_, ke) => OnKeyDown(host, state, ke),
            RoutingStrategies.Bubble, handledEventsToo: true);
        s_state.Add(host, state);
    }

    private static void OnPointerReleased(Control host, HostState state, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }

        if (!TryBuildEntries(state, e.Source as Visual, out var entries))
        {
            return;
        }

        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };
        foreach (var item in Build(entries))
        {
            flyout.Items.Add(item);
        }

        e.Handled = true;
        flyout.ShowAt(host, showAtPointer: true);
    }

    /// <summary>Menu key / Shift+F10 (docs/superpowers/specs/2026-08-31-keyboard-operability-
    /// design.md) - same menu-building/showing logic as <see cref="OnPointerReleased"/>, just rooted
    /// at the focused element instead of the pointer's click target, and anchored there rather than
    /// at a pointer position that doesn't exist for a keyboard-triggered menu.</summary>
    private static void OnKeyDown(Control host, HostState state, KeyEventArgs e)
    {
        bool isMenuGesture = e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift);
        if (!isMenuGesture)
        {
            return;
        }

        if (TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() is not Control focused)
        {
            return;
        }

        if (!TryBuildEntries(state, focused, out var entries))
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var item in Build(entries))
        {
            flyout.Items.Add(item);
        }

        e.Handled = true;
        flyout.ShowAt(focused);
    }

    /// <summary>Shared provider-lookup step behind both <see cref="OnPointerReleased"/> and
    /// <see cref="OnKeyDown"/> - walks <paramref name="source"/>'s <see cref="DataContextChain"/>
    /// asking the provider for entries at each level, falling back to a <see langword="null"/>-target
    /// call (the "empty space" menu) if nothing along the chain has any.</summary>
    private static bool TryBuildEntries(HostState state, Visual? source, out IReadOnlyList<ContextMenuEntry> entries)
    {
        IReadOnlyList<ContextMenuEntry>? found = null;
        foreach (var candidate in DataContextChain(source))
        {
            found = state.Provider.BuildContextMenu(candidate);
            if (found is { Count: > 0 })
            {
                break;
            }
        }

        found ??= state.Provider.BuildContextMenu(null);
        entries = found ?? System.Array.Empty<ContextMenuEntry>();
        return found is { Count: > 0 };
    }

    /// <summary>Distinct data contexts from the clicked element up to the root, nearest first.</summary>
    private static IEnumerable<object> DataContextChain(Visual? source)
    {
        if (source is null)
        {
            yield break;
        }

        object? last = null;
        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is StyledElement { DataContext: { } dc } && !ReferenceEquals(dc, last))
            {
                last = dc;
                yield return dc;
            }
        }
    }

    private static IEnumerable<Control> Build(IEnumerable<ContextMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                yield return new Separator();
                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Header,
                Command = entry.Command,
                CommandParameter = entry.CommandParameter,
                IsEnabled = entry.IsEnabled,
            };

            if (entry.IsChecked)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = true;
            }
            else if (entry.Icon is { } symbol)
            {
                item.Icon = new SymbolIcon { Symbol = symbol, FontSize = 16 };
            }

            if (entry.IsDanger)
            {
                item.Classes.Add("danger");
            }

            if (!string.IsNullOrWhiteSpace(entry.InputGesture) &&
                TryParseGesture(entry.InputGesture!, out var gesture))
            {
                item.InputGesture = gesture;
            }

            if (entry.Children is { Count: > 0 } children)
            {
                foreach (var child in Build(children))
                {
                    item.Items.Add(child);
                }
            }

            yield return item;
        }
    }

    private static bool TryParseGesture(string text, out KeyGesture? gesture)
    {
        try
        {
            gesture = KeyGesture.Parse(text);
            return true;
        }
        catch (Exception)
        {
            gesture = null;
            return false;
        }
    }

    private sealed class HostState
    {
        public IContextMenuProvider Provider = null!;
    }
}
