using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The poller's slot handling and its hand-off to the input service (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 3, docs/superpowers/specs/2026-10-03-input-service-design.md §5.5).
/// The button/stick/trigger mapping itself is tested in <see cref="GamepadInputProcessorTests"/>. Real controllers are the user's on-screen check.
/// </summary>
public class GamepadTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private static GamepadState Pad(GamepadButtons buttons = GamepadButtons.None, byte lt = 0, byte rt = 0, short lx = 0, short ly = 0, short rx = 0, short ry = 0) =>
        new(buttons, lt, rt, lx, ly, rx, ry);

    private sealed class FakeSource : IGamepadSource
    {
        public int? ConnectedSlot;
        public GamepadState State;
        public int Probes;

        public bool TryGetState(int slot, out GamepadState state)
        {
            Probes++;
            state = State;
            return ConnectedSlot == slot;
        }
    }

    [Fact]
    public void Poller_WithNoController_OnlyProbesEveryTwoSeconds()
    {
        var source = new FakeSource();
        var poller = new GamepadPoller(source, (_, _) => { });

        poller.Poll(TimeSpan.FromSeconds(2));       // first probe (all four slots)
        int afterFirst = source.Probes;
        for (int i = 0; i < 100; i++)
        {
            poller.Poll(Frame);                      // 1.6 s of polling: no probing
        }

        Assert.Equal(4, afterFirst);
        Assert.Equal(afterFirst, source.Probes);
        Assert.Equal(-1, poller.ConnectedSlot);
    }

    [Fact]
    public void Poller_FindsAController_AndDeliversEverySnapshot_IncludingEmptyOnes()
    {
        var states = new List<GamepadState>();
        var source = new FakeSource { ConnectedSlot = 2, State = Pad(GamepadButtons.A) };
        var poller = new GamepadPoller(source, (s, _) => states.Add(s));

        poller.Poll(TimeSpan.FromSeconds(2));
        source.State = Pad();
        poller.Poll(Frame);

        Assert.Equal(2, poller.ConnectedSlot);
        // The release matters as much as the press: the service needs it to count the next press as a new one.
        Assert.Equal([GamepadButtons.A, GamepadButtons.None], states.Select(s => s.Buttons));
    }

    [Fact]
    public void Poller_PassesTheElapsedTimeThrough()
    {
        var elapsed = new List<TimeSpan>();
        var source = new FakeSource { ConnectedSlot = 0, State = Pad() };
        var poller = new GamepadPoller(source, (_, t) => elapsed.Add(t));

        poller.Poll(TimeSpan.FromSeconds(2));
        poller.Poll(TimeSpan.FromMilliseconds(20));

        Assert.Equal(TimeSpan.FromMilliseconds(20), elapsed[^1]);
    }

    [Fact]
    public void Poller_ADisconnectedController_IsForgotten_Reset_AndFoundAgainLater()
    {
        int resets = 0;
        var source = new FakeSource { ConnectedSlot = 0, State = Pad() };
        var poller = new GamepadPoller(source, (_, _) => { }, () => resets++);
        poller.Poll(TimeSpan.FromSeconds(2));
        Assert.Equal(0, poller.ConnectedSlot);
        int afterConnect = resets;

        source.ConnectedSlot = null;
        poller.Poll(Frame);
        Assert.Equal(-1, poller.ConnectedSlot);
        Assert.Equal(afterConnect + 1, resets);

        source.ConnectedSlot = 1;
        poller.Poll(TimeSpan.FromSeconds(2));
        Assert.Equal(1, poller.ConnectedSlot);
    }

    [Fact]
    public void Poller_DrivesTheInputService_EndToEnd()
    {
        var service = new InputService(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore());
        var seen = new List<string>();
        using var reg = service.Register(InputScope.Reader, e => { seen.Add(e.Action.Id); e.Handled = true; }, () => InputContext.PagedUnzoomed);
        var source = new FakeSource { ConnectedSlot = 0, State = Pad(GamepadButtons.A) };
        var poller = new GamepadPoller(source, (s, t) => service.ProcessGamepad(s, t), service.ResetGamepad);

        poller.Poll(TimeSpan.FromSeconds(2));   // connects; A is down
        source.State = Pad();
        poller.Poll(Frame);                     // released
        source.State = Pad(GamepadButtons.A);
        poller.Poll(Frame);                     // pressed again: a second turn

        Assert.Equal([InputActionIds.NextPage, InputActionIds.NextPage], seen);
    }

    [Fact]
    public void HasInput_IsFalseForARestingPad_AndTrueForAnyButtonTriggerOrStick()
    {
        var tuning = new InputTuning();

        Assert.False(Pad().HasInput(tuning));
        Assert.False(Pad(lx: 3000, ly: 3000, rx: -3000, lt: 10).HasInput(tuning));   // inside every dead zone
        Assert.True(Pad(GamepadButtons.Start).HasInput(tuning));
        Assert.True(Pad(rt: 255).HasInput(tuning));
        Assert.True(Pad(lx: short.MaxValue).HasInput(tuning));
        Assert.True(Pad(ry: short.MinValue).HasInput(tuning));
    }
}
