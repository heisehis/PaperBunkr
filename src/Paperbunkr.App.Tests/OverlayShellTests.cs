using System.Windows.Input;
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
