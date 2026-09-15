using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="OverlayShell"/> - scrim-click policy (docs/superpowers/specs/2026-09-06-feedback-
/// notification-system-design.md §2 / plan Step 1). Exercises <see cref="OverlayShell.RequestCloseFromScrim"/>
/// directly rather than simulating a real Avalonia pointer event, since the headless test app has
/// no App.axaml styles loaded (so the control's own ControlTheme/template never applies) - this is
/// the same style of "test the resolved state, not the layout pass" the BrandMark tests already use.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class OverlayShellTests
{
    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public int ExecuteCount { get; private set; }
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { ExecuteCount++; _execute(); }
    }

    [Fact]
    public void ScrimClick_WithAllowDismiss_InvokesCloseCommand()
    {
        var invoked = false;
        var command = new RelayCommand(() => invoked = true);
        var shell = new OverlayShell { CloseCommand = command, AllowDismiss = true };

        shell.RequestCloseFromScrim();

        Assert.True(invoked);
        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public void ScrimClick_WithAllowDismissFalse_DoesNotInvokeCloseCommand()
    {
        var command = new RelayCommand(() => { });
        var shell = new OverlayShell { CloseCommand = command, AllowDismiss = false };

        shell.RequestCloseFromScrim();

        Assert.Equal(0, command.ExecuteCount);
    }

    [Fact]
    public void ScrimClick_NeverFlipsIsOpenDirectly_OnlyInvokesTheCommand()
    {
        // The whole point of CloseCommand (vs. a raw IsOpen binding) is that the overlay's own
        // command can run an unsaved-changes check before actually closing. Confirm RequestCloseFromScrim
        // never touches IsOpen itself - that's left entirely to whatever CloseCommand decides to do.
        var command = new RelayCommand(() => { });
        var shell = new OverlayShell { CloseCommand = command, AllowDismiss = true, IsOpen = true };

        shell.RequestCloseFromScrim();

        Assert.True(shell.IsOpen);
        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public void IsOpen_DrivesTheControlsOwnIsVisible()
    {
        var shell = new OverlayShell();

        Assert.False(shell.IsVisible);

        shell.IsOpen = true;
        Assert.True(shell.IsVisible);

        shell.IsOpen = false;
        Assert.False(shell.IsVisible);
    }

    [Fact]
    public void NoCloseCommand_ScrimClick_DoesNotThrow()
    {
        var shell = new OverlayShell { AllowDismiss = true };

        var exception = Record.Exception(shell.RequestCloseFromScrim);

        Assert.Null(exception);
    }

    [Fact]
    public void ContentAlignment_DefaultsToCenterCenter_ButIsSettable()
    {
        // QuickOpenOverlay is the one consumer that needs Stretch instead of the default Center
        // (its results list should fill the available height, not size to content).
        var shell = new OverlayShell();
        Assert.Equal(HorizontalAlignment.Center, shell.ContentHorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, shell.ContentVerticalAlignment);

        shell.ContentVerticalAlignment = VerticalAlignment.Stretch;
        Assert.Equal(VerticalAlignment.Stretch, shell.ContentVerticalAlignment);
    }

    /// <summary>
    /// Regression test for a real bug found live (docs/superpowers/specs/2026-09-13-cluster-scraper-
    /// ui-redesign-design.md's redesigned scraper dialog): a click on plain, non-interactive content
    /// INSIDE the card (no Button/TextBox/etc. of its own to mark the press handled) used to bubble
    /// all the way to the scrim's own PointerPressed handler and close the whole overlay, since the
    /// card content is a visual CHILD of PART_Scrim in the shared template (Styles/Overlays.axaml).
    /// <see cref="OverlayShell.ShouldCloseFromScrimClick"/> is the fix: only close when the routed
    /// event's ORIGINATING element (<c>PointerPressedEventArgs.Source</c>, which survives the bubble)
    /// is the scrim itself - i.e. the click genuinely landed on the backdrop, not on/inside the card.
    /// </summary>
    [Fact]
    public void ShouldCloseFromScrimClick_true_only_when_the_click_originated_on_the_scrim_itself()
    {
        var scrim = new Border();
        var cardContent = new Border(); // stands in for e.g. a plain non-interactive label inside the card

        Assert.True(OverlayShell.ShouldCloseFromScrimClick(scrim, scrim));
        Assert.False(OverlayShell.ShouldCloseFromScrimClick(cardContent, scrim));
    }

    [Fact]
    public void ShouldCloseFromScrimClick_false_when_the_scrim_reference_is_not_yet_set()
    {
        // The shell's own template hasn't applied yet (or never will, in a headless test host with
        // no App.axaml styles loaded) - must not throw or accidentally match on two nulls.
        Assert.False(OverlayShell.ShouldCloseFromScrimClick(null, null));
    }

    [Fact]
    public void ShowCloseButton_DefaultsToTrue_ButIsSettable()
    {
        // QuickOpenOverlay is the one consumer that hides it (closes via Escape/selecting a result).
        var shell = new OverlayShell();
        Assert.True(shell.ShowCloseButton);

        shell.ShowCloseButton = false;
        Assert.False(shell.ShowCloseButton);
    }
}
