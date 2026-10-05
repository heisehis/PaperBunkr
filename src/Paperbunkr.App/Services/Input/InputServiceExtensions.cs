using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.Input;

public static class InputServiceExtensions
{
    /// <summary>
    /// The action's bindings as the user has them <em>now</em>; or, when the service has never heard of the action (the do-nothing service a test or a design-time host
    /// uses), its shipped defaults, so a tooltip or menu built without a live service still reads as it always did. An action the user has deliberately unbound has no
    /// bindings and shows none.
    /// </summary>
    public static IReadOnlyList<InputBinding> BindingsOrDefaults(this IInputService input, InputAction action) =>
        input.Actions.Find(action) is not null
            ? input.GetBindings(action)
            : InputActions.Core.FirstOrDefault(i => i.Action == action)?.Defaults ?? [];

    /// <summary>
    /// The text a person reads for the action's shortcut (the first keyboard binding, else the first binding of any kind), or null when it has none. Shared by the reader's
    /// tooltips and command palette and the Library's menus and bar so they all show the same thing.
    /// </summary>
    public static string? ShortcutText(this IInputService input, InputAction action)
    {
        var bindings = input.BindingsOrDefaults(action);
        var shown = bindings.FirstOrDefault(b => b.Kind == InputBindingKind.Key);
        if (shown.IsDefined)
        {
            return InputBindingDisplay.Format(shown);
        }

        return bindings.Count > 0 ? InputBindingDisplay.Format(bindings[0]) : null;
    }
}
