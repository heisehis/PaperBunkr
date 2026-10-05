using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Steps through a screen's tab strip (docs/superpowers/specs/2026-10-03-input-service-design.md §14). Every strip in the app is the same shape: a row of buttons classed <c>tab</c> (or
/// <c>segToggle</c>, the pill variant; <c>ipTab</c> in the issue editor; <c>prefNavItem</c> for Preferences' section list) whose current one also has <c>active</c> (or <c>on</c>), each bound to a command that selects it. So one routine serves the detail screens,
/// Insights, Wanted and Continuity: find the visible strip that has a current button, run the command of the next (or previous) visible button, wrapping at the ends.
/// </summary>
public static class TabStrip
{
    /// <summary>
    /// Selects the tab <paramref name="delta"/> places after the current one in the first visible strip under <paramref name="root"/>. Returns true when a tab command was found and run (even
    /// if it was the only tab), false when the screen has no strip, so the caller can leave the action for the fallback.
    /// </summary>
    public static bool Step(Control root, int delta)
    {
        var tabs = root.GetVisualDescendants().OfType<Button>().Where(IsTab).ToList();
        foreach (var strip in tabs.GroupBy(t => t.GetVisualParent()))
        {
            var buttons = strip.ToList();
            int current = buttons.FindIndex(IsCurrent);
            if (current < 0)
            {
                continue;
            }

            var target = buttons[((current + delta) % buttons.Count + buttons.Count) % buttons.Count];
            if (ReferenceEquals(target, buttons[current]))
            {
                return true;
            }

            if (target.Command is { } command && command.CanExecute(target.CommandParameter))
            {
                command.Execute(target.CommandParameter);
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsTab(Button button) =>
        button.IsEffectivelyVisible && (button.Classes.Contains("tab") || button.Classes.Contains("segToggle") || button.Classes.Contains("ipTab") || button.Classes.Contains("prefNavItem"));

    private static bool IsCurrent(Button button) => button.Classes.Contains("active") || button.Classes.Contains("on");
}
