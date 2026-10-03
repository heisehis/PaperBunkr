using Avalonia.Controls;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Compare screen (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11). Its keys are input-service actions in the Compare scope (docs/superpowers/specs/2026-10-03-input-service-design.md
/// §9), so they are remappable in Preferences and, being resolved at the window before any control sees the key, the page canvases cannot swallow them: Left/Right and PageUp/PageDown
/// turn A's page, Space or F flips the flicker, M switches the mode, 0 resets the view, 1 and 2 keep A or B. Escape is the shell's.
/// </summary>
public partial class CompareScreen : UserControl
{
    private readonly AttachedInputRegistration _compareInput;

    public CompareScreen()
    {
        InitializeComponent();
        Focusable = true;
        FocusAdorner = null;
        _compareInput = new AttachedInputRegistration(this, InputScope.Compare, OnCompareInputAction, service: InputServiceLocator.Current, focusRoot: () => this);
        AttachedToVisualTree += (_, _) => Focus();
    }

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _compareInput.Service;
        set => _compareInput.Service = value;
    }

    private void OnCompareInputAction(InputActionEventArgs e)
    {
        // Plain keys only, as before: Ctrl+1 and the like are not the screen's.
        if (DataContext is not CompareScreenViewModel vm || e.Modifiers != Avalonia.Input.KeyModifiers.None)
        {
            return;
        }

        switch (e.Action.Id)
        {
            case InputActionIds.CompareNextPage:
                vm.NextPageCommand.Execute(null);
                break;
            case InputActionIds.ComparePreviousPage:
                vm.PreviousPageCommand.Execute(null);
                break;
            case InputActionIds.CompareFirstPage:
                vm.GoToFirstPageCommand.Execute(null);
                break;
            case InputActionIds.CompareFlip:
                vm.FlipCommand.Execute(null);
                break;
            case InputActionIds.CompareToggleMode:
                vm.ToggleModeCommand.Execute(null);
                break;
            case InputActionIds.CompareResetView:
                vm.ResetViewCommand.Execute(null);
                break;
            case InputActionIds.CompareKeepA:
                vm.KeepACommand.Execute(null);
                break;
            case InputActionIds.CompareKeepB:
                vm.KeepBCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
