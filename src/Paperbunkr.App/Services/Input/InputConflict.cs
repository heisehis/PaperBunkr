namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Another action that already uses a binding and could be active at the same time as the one being bound, so pressing it would be ambiguous
/// (the one in the more specific scope or context would silently win). Reported by <see cref="IInputService.FindConflicts"/> for Preferences to show.
/// </summary>
/// <param name="Other">The action that already has the binding.</param>
/// <param name="Binding">The shared binding.</param>
public readonly record struct InputConflict(InputAction Other, InputBinding Binding);
