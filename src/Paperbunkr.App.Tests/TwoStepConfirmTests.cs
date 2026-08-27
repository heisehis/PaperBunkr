using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="TwoStepConfirm"/> (docs/superpowers/specs/2026-08-22-delete-functionality-
/// design.md) - the shared destructive-delete confirm pattern extracted out of
/// <see cref="MissingFileRowViewModel"/>. Needs <see cref="AvaloniaTestCollection"/> since
/// constructing a <c>DispatcherTimer</c> requires an initialized Avalonia dispatcher. Doesn't wait
/// out the real 3-second auto-revert window (too slow/flaky for a unit test) - only the immediate
/// arm/confirm/cancel state transitions.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class TwoStepConfirmTests
{
    [Fact]
    public void StartsIdle()
    {
        var confirm = new TwoStepConfirm(() => { });
        Assert.Equal("Remove", confirm.Label);
        Assert.False(confirm.IsArmed);
    }

    [Fact]
    public void FirstTrigger_ArmsWithoutConfirming()
    {
        bool confirmed = false;
        var confirm = new TwoStepConfirm(() => confirmed = true);

        confirm.TriggerCommand.Execute(null);

        Assert.True(confirm.IsArmed);
        Assert.Equal("Confirm remove?", confirm.Label);
        Assert.False(confirmed);
    }

    [Fact]
    public void SecondTriggerWhileArmed_Confirms()
    {
        bool confirmed = false;
        var confirm = new TwoStepConfirm(() => confirmed = true);

        confirm.TriggerCommand.Execute(null);
        confirm.TriggerCommand.Execute(null);

        Assert.True(confirmed);
        Assert.False(confirm.IsArmed);
        Assert.Equal("Remove", confirm.Label);
    }

    [Fact]
    public void Cancel_RevertsToIdle_WithoutConfirming()
    {
        bool confirmed = false;
        var confirm = new TwoStepConfirm(() => confirmed = true);
        confirm.TriggerCommand.Execute(null);

        confirm.Cancel();

        Assert.False(confirm.IsArmed);
        Assert.Equal("Remove", confirm.Label);
        Assert.False(confirmed);
    }

    [Fact]
    public void CustomLabels_AreUsedInsteadOfDefaults()
    {
        var confirm = new TwoStepConfirm(() => { }, idleLabel: "Delete List", armedLabel: "Really delete?");

        Assert.Equal("Delete List", confirm.Label);
        confirm.TriggerCommand.Execute(null);
        Assert.Equal("Really delete?", confirm.Label);
    }
}
