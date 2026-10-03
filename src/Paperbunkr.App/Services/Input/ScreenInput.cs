using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// The shape almost every screen's input handling takes (docs/superpowers/specs/2026-10-03-input-service-design.md §14): a table from action id to "do it, and say whether it
/// applied", registered while the screen is attached, dormant while it is hidden or focus is in a dialog above it. A handler returns false to leave the action for the next
/// one (the Global fallback, or the native key), so a screen only claims what it can actually do right now.
/// </summary>
public static class ScreenInput
{
    /// <summary>
    /// Registers <paramref name="handlers"/> for <paramref name="scope"/> on <paramref name="owner"/>. The service is the application's unless the owner swaps it through
    /// <see cref="AttachedInputRegistration.Service"/> (tests do).
    /// </summary>
    public static AttachedInputRegistration Attach(Control owner, InputScope scope, IReadOnlyDictionary<string, Func<bool>> handlers, Func<InputContext>? context = null) =>
        new(owner, scope, e =>
        {
            if (handlers.TryGetValue(e.Action.Id, out var run) && run())
            {
                e.Handled = true;
            }
        }, context, InputServiceLocator.Current, focusRoot: () => owner);

    /// <summary>Focuses and selects the visible text box under <paramref name="root"/> with the given automation id (a screen's search box). False when there is none showing.</summary>
    public static bool FocusTextBox(Control root, string automationId)
    {
        var box = root.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible && AutomationProperties.GetAutomationId(t) == automationId);
        if (box is null)
        {
            return false;
        }

        box.Focus();
        box.SelectAll();
        return true;
    }

    /// <summary>
    /// Runs <paramref name="action"/> one dispatcher tick later and reports it as taken. Several screen commands rebuild or clear the collections the focused tile lives in, and doing
    /// that while the key press is still routing detaches the focused control mid-event (CLAUDE.md, "don't remove/detach a control from inside a routed event").
    /// </summary>
    public static bool Deferred(Action action)
    {
        Dispatcher.UIThread.Post(action);
        return true;
    }
}
