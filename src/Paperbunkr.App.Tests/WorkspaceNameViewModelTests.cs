using Paperbunkr.App.ViewModels;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The tiny "name this workspace" overlay VM (docs/superpowers/specs/2026-09-03-library-saved-
/// workspaces-design.md).
/// </summary>
public class WorkspaceNameViewModelTests
{
    [Fact]
    public void Save_IsDisabled_UntilANonBlankNameIsEntered()
    {
        var vm = new WorkspaceNameViewModel(() => { });
        vm.Begin(null, _ => { });

        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Name = "   ";
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Name = "Weekly";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_TrimsAndInvokesTheCallbackOnce()
    {
        int calls = 0;
        string? captured = null;
        var vm = new WorkspaceNameViewModel(() => { });
        vm.Begin(null, name => { calls++; captured = name; });

        vm.Name = "  Weekly pull  ";
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, calls);
        Assert.Equal("Weekly pull", captured);
    }

    [Fact]
    public void Begin_WithInitialText_PrefillsForARename()
    {
        var vm = new WorkspaceNameViewModel(() => { });
        vm.Begin("Old name", _ => { });
        Assert.Equal("Old name", vm.Name);
    }

    [Fact]
    public void Cancel_InvokesTheCancelCallback()
    {
        bool cancelled = false;
        var vm = new WorkspaceNameViewModel(() => cancelled = true);
        vm.Begin(null, _ => { });

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }
}
