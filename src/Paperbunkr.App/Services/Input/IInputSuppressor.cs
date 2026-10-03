namespace Paperbunkr.App.Services.Input;

/// <summary>How much of the keyboard a focused control claims for itself.</summary>
public enum InputSuppression
{
    /// <summary>
    /// Ordinary typing: keyboard actions are ignored, except the few whose <see cref="InputActionInfo.FiresInTextInput"/> is set (Escape, the
    /// browser-back key, quick open).
    /// </summary>
    TextEntry,

    /// <summary>
    /// The control must see every key, including Escape and the <see cref="InputActionInfo.FiresInTextInput"/> actions, and every wheel turn and mouse-button
    /// press too: a "press your combination" capture box, or a custom canvas that edits text.
    /// </summary>
    All,
}

/// <summary>
/// Implemented by a control that wants raw keys while it has focus (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5). The service looks at the
/// focused element and its visual ancestors and honours the nearest implementer, so a composite control opts in once at its root. Avalonia's own text
/// controls cannot implement this, so the service's probe treats them as <see cref="InputSuppression.TextEntry"/> itself; every Paperbunkr control opts in
/// through this interface instead. Under <see cref="InputSuppression.TextEntry"/> only the keyboard is affected; under <see cref="InputSuppression.All"/> the wheel and mouse buttons are too. Gamepad input is never suppressed.
/// </summary>
public interface IInputSuppressor
{
    InputSuppression Suppression { get; }
}
