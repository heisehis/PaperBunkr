using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Services.Input;

/// <summary>Tells the input service whether the control with focus wants the keyboard for itself (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5).</summary>
public interface IInputSuppressionProbe
{
    /// <summary>The suppression level in force for <paramref name="topLevel"/>'s focused element, or <see langword="null"/> when hotkeys may fire.</summary>
    InputSuppression? GetActive(TopLevel? topLevel);
}

/// <summary>
/// The real probe: looks at the focused element (never the event source) and walks up its visual ancestors, returning the level of the nearest
/// <see cref="IInputSuppressor"/>. Avalonia's own text controls cannot implement our interface, so they count as <see cref="InputSuppression.TextEntry"/> here; this
/// is the only place concrete control types are named. Paperbunkr's own controls opt in through the interface.
/// </summary>
public sealed class AvaloniaInputSuppressionProbe : IInputSuppressionProbe
{
    public InputSuppression? GetActive(TopLevel? topLevel)
    {
        if (topLevel?.FocusManager?.GetFocusedElement() is not Visual focused)
        {
            return null;
        }

        for (Visual? visual = focused; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is IInputSuppressor suppressor)
            {
                return suppressor.Suppression;
            }

            if (visual is TextBox or AutoCompleteBox or NumericUpDown)
            {
                return InputSuppression.TextEntry;
            }
        }

        return null;
    }
}

/// <summary>A probe that never suppresses; for tests and headless hosts.</summary>
public sealed class NoInputSuppressionProbe : IInputSuppressionProbe
{
    public static readonly NoInputSuppressionProbe Instance = new();

    public InputSuppression? GetActive(TopLevel? topLevel) => null;
}
