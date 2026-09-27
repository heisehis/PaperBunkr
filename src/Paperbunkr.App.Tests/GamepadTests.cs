using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>The pure gamepad mapper and the poller's slot handling (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 3). Real controllers are the user's on-screen check.</summary>
public class GamepadTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private static GamepadState Pad(GamepadButtons buttons = GamepadButtons.None, byte lt = 0, byte rt = 0, short lx = 0, short ly = 0, short rx = 0, short ry = 0) =>
        new(buttons, lt, rt, lx, ly, rx, ry);

    // --- Buttons ---

    [Theory]
    [InlineData(GamepadButtons.A)]
    [InlineData(GamepadButtons.RightShoulder)]
    public void NextPage_ComesFromAOrTheRightBumper(GamepadButtons button) =>
        Assert.True(new GamepadMapper().Update(Pad(button), Frame).Next);

    [Theory]
    [InlineData(GamepadButtons.B)]
    [InlineData(GamepadButtons.LeftShoulder)]
    public void PreviousPage_ComesFromBOrTheLeftBumper(GamepadButtons button) =>
        Assert.True(new GamepadMapper().Update(Pad(button), Frame).Previous);

    [Fact]
    public void DPad_GivesDirections()
    {
        var mapper = new GamepadMapper();

        Assert.True(mapper.Update(Pad(GamepadButtons.DPadLeft), Frame).Left);
        mapper.Update(Pad(), Frame);
        Assert.True(mapper.Update(Pad(GamepadButtons.DPadRight), Frame).Right);
        mapper.Update(Pad(), Frame);
        Assert.True(mapper.Update(Pad(GamepadButtons.DPadUp), Frame).Up);
        mapper.Update(Pad(), Frame);
        Assert.True(mapper.Update(Pad(GamepadButtons.DPadDown), Frame).Down);
    }

    [Fact]
    public void EdgeOnlyButtons_FireOnceUntilReleased()
    {
        var mapper = new GamepadMapper();

        var first = mapper.Update(Pad(GamepadButtons.Y | GamepadButtons.X | GamepadButtons.Start | GamepadButtons.Back), Frame);
        var held = mapper.Update(Pad(GamepadButtons.Y | GamepadButtons.X | GamepadButtons.Start | GamepadButtons.Back), Frame);
        mapper.Update(Pad(), Frame);
        var again = mapper.Update(Pad(GamepadButtons.Y), Frame);

        Assert.True(first.ToggleChrome && first.Fullscreen && first.Palette && first.Leave);
        Assert.False(held.ToggleChrome || held.Fullscreen || held.Palette || held.Leave);
        Assert.True(again.ToggleChrome);
    }

    // --- Repeat ---

    [Fact]
    public void HeldTurnButton_RepeatsAfterTheInitialDelay_ThenAtTheRepeatRate()
    {
        var mapper = new GamepadMapper();
        var hold = Pad(GamepadButtons.A);
        var step = TimeSpan.FromMilliseconds(10);

        int presses = 0;
        // 0 ms: the press. Then hold for 1 second in 10 ms steps.
        if (mapper.Update(hold, step).Next) presses++;
        for (int i = 0; i < 100; i++)
        {
            if (mapper.Update(hold, step).Next) presses++;
        }

        // 1 press + repeats from 400 ms to 1010 ms at ~90 ms each = 7 or so.
        Assert.InRange(presses, 6, 9);
    }

    [Fact]
    public void HeldTurnButton_DoesNotRepeatBeforeTheInitialDelay()
    {
        var mapper = new GamepadMapper();
        var hold = Pad(GamepadButtons.A);
        mapper.Update(hold, Frame);

        for (int elapsed = 0; elapsed < 350; elapsed += 16)
        {
            Assert.False(mapper.Update(hold, Frame).Next);
        }
    }

    [Fact]
    public void Releasing_RestartsTheRepeatClock()
    {
        var mapper = new GamepadMapper();
        for (int i = 0; i < 40; i++)
        {
            mapper.Update(Pad(GamepadButtons.A), Frame);
        }

        mapper.Update(Pad(), Frame);
        Assert.True(mapper.Update(Pad(GamepadButtons.A), Frame).Next);
        Assert.False(mapper.Update(Pad(GamepadButtons.A), Frame).Next);
    }

    // --- Sticks and triggers ---

    [Fact]
    public void LeftStick_PastHalfway_ActsAsADPad_AndInsideItDoesNothing()
    {
        var mapper = new GamepadMapper();

        Assert.False(mapper.Update(Pad(lx: 12000), Frame).Right);      // 0.37: inside the dead zone of a press
        Assert.True(mapper.Update(Pad(lx: 20000), Frame).Right);       // 0.61
        Assert.True(mapper.Update(Pad(lx: -30000), Frame).Left);
        Assert.True(mapper.Update(Pad(ly: 30000), Frame).Up);
        Assert.True(mapper.Update(Pad(ly: -30000), Frame).Down);
    }

    [Fact]
    public void RightStick_HasARadialDeadZone()
    {
        var mapper = new GamepadMapper();

        var inside = mapper.Update(Pad(rx: 6000, ry: 6000), Frame);   // magnitude ~0.26 per axis component? 0.18 each -> 0.26 total, just outside
        var tiny = mapper.Update(Pad(rx: 3000, ry: 3000), Frame);      // ~0.13 total

        Assert.Equal(0, tiny.PanX);
        Assert.Equal(0, tiny.PanY);
        Assert.True(inside.PanX >= 0);
    }

    [Fact]
    public void RightStick_ScalesFromTheEdgeOfTheDeadZoneToFullDeflection()
    {
        var mapper = new GamepadMapper();

        var full = mapper.Update(Pad(rx: short.MaxValue), Frame);
        var half = mapper.Update(Pad(rx: 16384), Frame);

        Assert.Equal(1.0, full.PanX, 3);
        Assert.InRange(half.PanX, 0.3, 0.4);   // (0.5 - 0.25) / 0.75
    }

    [Fact]
    public void RightStickUp_IsANegativePanY_BecauseDownIsPositive()
    {
        var frame = new GamepadMapper().Update(Pad(ry: short.MaxValue), Frame);

        Assert.Equal(-1.0, frame.PanY, 3);
    }

    [Fact]
    public void Triggers_ZoomInAndOut_WithADeadZone()
    {
        var mapper = new GamepadMapper();

        Assert.Equal(1.0, mapper.Update(Pad(rt: 255), Frame).Zoom, 3);
        Assert.Equal(-1.0, mapper.Update(Pad(lt: 255), Frame).Zoom, 3);
        Assert.Equal(0, mapper.Update(Pad(rt: 20), Frame).Zoom);
        Assert.Equal(0, mapper.Update(Pad(lt: 255, rt: 255), Frame).Zoom, 3);
    }

    [Fact]
    public void Reset_ForgetsHeldButtons()
    {
        var mapper = new GamepadMapper();
        mapper.Update(Pad(GamepadButtons.A | GamepadButtons.Y), Frame);

        mapper.Reset();

        var frame = mapper.Update(Pad(GamepadButtons.A | GamepadButtons.Y), Frame);
        Assert.True(frame.Next);
        Assert.True(frame.ToggleChrome);
    }

    [Fact]
    public void EmptyState_IsAnEmptyFrame() =>
        Assert.False(new GamepadMapper().Update(Pad(), Frame).HasAny);

    // --- The poller (fake source, no timer) ---

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
    public void Poller_FindsAController_AndDeliversFrames()
    {
        var frames = new List<GamepadFrame>();
        var source = new FakeSource { ConnectedSlot = 2, State = Pad(GamepadButtons.A) };
        var poller = new GamepadPoller(source, (f, _) => frames.Add(f));

        poller.Poll(TimeSpan.FromSeconds(2));

        Assert.Equal(2, poller.ConnectedSlot);
        Assert.Single(frames);
        Assert.True(frames[0].Next);
    }

    [Fact]
    public void Poller_DoesNotDeliverEmptyFrames()
    {
        var frames = new List<GamepadFrame>();
        var source = new FakeSource { ConnectedSlot = 0, State = Pad() };
        var poller = new GamepadPoller(source, (f, _) => frames.Add(f));

        poller.Poll(TimeSpan.FromSeconds(2));
        poller.Poll(Frame);

        Assert.Empty(frames);
    }

    [Fact]
    public void Poller_ADisconnectedController_IsForgotten_AndFoundAgainLater()
    {
        var source = new FakeSource { ConnectedSlot = 0, State = Pad() };
        var poller = new GamepadPoller(source, (_, _) => { });
        poller.Poll(TimeSpan.FromSeconds(2));
        Assert.Equal(0, poller.ConnectedSlot);

        source.ConnectedSlot = null;
        poller.Poll(Frame);
        Assert.Equal(-1, poller.ConnectedSlot);

        source.ConnectedSlot = 1;
        poller.Poll(TimeSpan.FromSeconds(2));
        Assert.Equal(1, poller.ConnectedSlot);
    }
}
