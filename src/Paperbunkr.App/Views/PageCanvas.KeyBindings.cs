using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Views;

/// <summary>Reader commands that are only "a key runs a command" (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md), so they share one property instead of a gesture and a command property each.</summary>
public partial class PageCanvas
{
    public static readonly StyledProperty<IReadOnlyList<KeyCommandBinding>> ExtraKeyBindingsProperty =
        AvaloniaProperty.Register<PageCanvas, IReadOnlyList<KeyCommandBinding>>(nameof(ExtraKeyBindings), defaultValue: []);

    public IReadOnlyList<KeyCommandBinding> ExtraKeyBindings
    {
        get => GetValue(ExtraKeyBindingsProperty);
        set => SetValue(ExtraKeyBindingsProperty, value);
    }

    /// <summary>Runs the command of the first binding whose gesture matches <paramref name="e"/>. Returns whether a command ran.</summary>
    private bool TryRunExtraKeyBinding(KeyEventArgs e)
    {
        foreach (var binding in ExtraKeyBindings)
        {
            if (AnyMatches(binding.Gestures, e) && TryExecute(binding.Command))
            {
                return true;
            }
        }

        return false;
    }
}
