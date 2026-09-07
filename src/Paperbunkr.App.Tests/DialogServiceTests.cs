using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="DialogService"/> / <see cref="ConfirmDialogViewModel"/> (docs/superpowers/specs/
/// 2026-09-06-feedback-notification-system-design.md §2 / plan Step 2).
/// </summary>
public class DialogServiceTests
{
    [Fact]
    public async Task ShowAsync_PrimaryClicked_ResolvesZero()
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        var task = service.ShowAsync(new ConfirmDialogRequest("Are you sure?"));
        Assert.True(vm.IsOpen);

        vm.PrimaryCommand.Execute(null);

        Assert.Equal(0, await task);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task ShowAsync_SecondaryClicked_ResolvesOne()
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        var task = service.ShowAsync(new ConfirmDialogRequest("Are you sure?"));
        vm.SecondaryCommand.Execute(null);

        Assert.Equal(1, await task);
    }

    [Fact]
    public async Task ShowAsync_Dismissed_ResolvesNegativeOne()
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        var task = service.ShowAsync(new ConfirmDialogRequest("Are you sure?"));
        vm.DismissCommand.Execute(null);

        Assert.Equal(-1, await task);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public async Task ConfirmAsync_MapsAnswerToBool(int answer, bool expected)
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        var task = service.ConfirmAsync("Are you sure?");
        switch (answer)
        {
            case 0: vm.PrimaryCommand.Execute(null); break;
            case 1: vm.SecondaryCommand.Execute(null); break;
            default: vm.DismissCommand.Execute(null); break;
        }

        Assert.Equal(expected, await task);
    }

    [Fact]
    public void Request_WithItems_SetsHasItemsTrue()
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        _ = service.ShowAsync(new ConfirmDialogRequest("Remove these?", Items: new[] { "a.cbz", "b.cbz" }));

        Assert.True(vm.HasItems);
        Assert.Equal(2, vm.Items!.Count);
    }

    [Fact]
    public void Request_WithoutItems_SetsHasItemsFalse()
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        _ = service.ShowAsync(new ConfirmDialogRequest("Are you sure?"));

        Assert.False(vm.HasItems);
    }

    [Fact]
    public void Request_WithNullSecondaryLabel_SetsHasSecondaryFalse()
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        _ = service.ShowAsync(new ConfirmDialogRequest("OK?", SecondaryLabel: null));

        Assert.False(vm.HasSecondary);
    }

    [Fact]
    public async Task SecondCall_WhileFirstStillPending_IsQueuedNotClobbered()
    {
        var vm = new ConfirmDialogViewModel();
        var service = new DialogService(vm);

        var first = service.ShowAsync(new ConfirmDialogRequest("First"));
        var second = service.ShowAsync(new ConfirmDialogRequest("Second"));

        // The second request must not have overwritten the first's still-visible content.
        Assert.Equal("First", vm.Message);

        vm.PrimaryCommand.Execute(null);
        Assert.Equal(0, await first);

        // Resolving the first should have presented the queued second request.
        Assert.Equal("Second", vm.Message);
        Assert.True(vm.IsOpen);

        vm.PrimaryCommand.Execute(null);
        Assert.Equal(0, await second);
    }
}
