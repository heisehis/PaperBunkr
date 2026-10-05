using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The gamepad edge detection, key-repeat and dead zones (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5), ported from the original
/// <c>GamepadMapper</c> tests: same snapshots, same expectations, now as <see cref="PadSignal"/>s.
/// </summary>
public class GamepadInputProcessorTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);
    private static readonly InputTuning Tuning = new();

    private static GamepadState Pad(GamepadButtons buttons = GamepadButtons.None, byte lt = 0, byte rt = 0, short lx = 0, short ly = 0, short rx = 0, short ry = 0) =>
        new(buttons, lt, rt, lx, ly, rx, ry);

    private static bool Fired(IReadOnlyList<PadSignal> signals, GamepadInput input) => signals.Any(s => s.Input == input);

    private static double Axis(IReadOnlyList<PadSignal> signals, GamepadInput input) => signals.Where(s => s.Input == input).Select(s => s.Value).SingleOrDefault();

    // --- Buttons ---

    [Theory]
    [InlineData(GamepadButtons.A, GamepadInput.A)]
    [InlineData(GamepadButtons.B, GamepadInput.B)]
    [InlineData(GamepadButtons.LeftShoulder, GamepadInput.LeftShoulder)]
    [InlineData(GamepadButtons.RightShoulder, GamepadInput.RightShoulder)]
    [InlineData(GamepadButtons.DPadLeft, GamepadInput.DPadLeft)]
    [InlineData(GamepadButtons.DPadRight, GamepadInput.DPadRight)]
    [InlineData(GamepadButtons.DPadUp, GamepadInput.DPadUp)]
    [InlineData(GamepadButtons.DPadDown, GamepadInput.DPadDown)]
    [InlineData(GamepadButtons.Y, GamepadInput.Y)]
    [InlineData(GamepadButtons.X, GamepadInput.X)]
    [InlineData(GamepadButtons.Start, GamepadInput.Start)]
    [InlineData(GamepadButtons.Back, GamepadInput.Back)]
    [InlineData(GamepadButtons.LeftThumb, GamepadInput.LeftThumb)]
    [InlineData(GamepadButtons.RightThumb, GamepadInput.RightThumb)]
    public void EachButton_FiresItsOwnInput(GamepadButtons button, GamepadInput expected)
    {
        var signals = new GamepadInputProcessor().Update(Pad(button), Frame, Tuning);

        Assert.Equal([expected], signals.Select(s => s.Input));
        Assert.Equal(1, signals[0].Value);
    }

    [Fact]
    public void EdgeOnlyButtons_FireOnceUntilReleased()
    {
        var processor = new GamepadInputProcessor();
        var all = Pad(GamepadButtons.Y | GamepadButtons.X | GamepadButtons.Start | GamepadButtons.Back);

        var first = processor.Update(all, Frame, Tuning);
        var held = processor.Update(all, Frame, Tuning);
        processor.Update(Pad(), Frame, Tuning);
        var again = processor.Update(Pad(GamepadButtons.Y), Frame, Tuning);

        Assert.True(Fired(first, GamepadInput.Y) && Fired(first, GamepadInput.X) && Fired(first, GamepadInput.Start) && Fired(first, GamepadInput.Back));
        Assert.Empty(held);
        Assert.True(Fired(again, GamepadInput.Y));
    }

    // --- Repeat ---

    [Fact]
    public void HeldTurnButton_RepeatsAfterTheInitialDelay_ThenAtTheRepeatRate()
    {
        var processor = new GamepadInputProcessor();
        var hold = Pad(GamepadButtons.A);
        var step = TimeSpan.FromMilliseconds(10);

        int presses = 0;
        for (int i = 0; i < 101; i++)
        {
            if (Fired(processor.Update(hold, step, Tuning), GamepadInput.A))
            {
                presses++;
            }
        }

        // 1 press + repeats from 400 ms to 1010 ms at ~90 ms each = 7 or so.
        Assert.InRange(presses, 6, 9);
    }

    [Fact]
    public void HeldTurnButton_DoesNotRepeatBeforeTheInitialDelay()
    {
        var processor = new GamepadInputProcessor();
        var hold = Pad(GamepadButtons.A);
        processor.Update(hold, Frame, Tuning);

        for (int elapsed = 0; elapsed < 350; elapsed += 16)
        {
            Assert.False(Fired(processor.Update(hold, Frame, Tuning), GamepadInput.A));
        }
    }

    [Fact]
    public void Releasing_RestartsTheRepeatClock()
    {
        var processor = new GamepadInputProcessor();
        for (int i = 0; i < 40; i++)
        {
            processor.Update(Pad(GamepadButtons.A), Frame, Tuning);
        }

        processor.Update(Pad(), Frame, Tuning);
        Assert.True(Fired(processor.Update(Pad(GamepadButtons.A), Frame, Tuning), GamepadInput.A));
        Assert.False(Fired(processor.Update(Pad(GamepadButtons.A), Frame, Tuning), GamepadInput.A));
    }

    [Fact]
    public void TheRepeatTimingsComeFromTheTuning()
    {
        var slow = new InputTuning { RepeatInitialMs = 1000, RepeatIntervalMs = 500 };
        var processor = new GamepadInputProcessor();
        var hold = Pad(GamepadButtons.A);
        var step = TimeSpan.FromMilliseconds(100);

        int presses = 0;
        for (int i = 0; i < 21; i++)
        {
            if (Fired(processor.Update(hold, step, slow), GamepadInput.A))
            {
                presses++;
            }
        }

        // The press, then repeats at 1000, 1500 and 2000 ms: 4 in a little over 2 s.
        Assert.InRange(presses, 3, 4);
    }

    // --- Sticks and triggers ---

    [Fact]
    public void LeftStick_PastHalfway_ActsAsADPad_AndInsideItDoesNothing()
    {
        var processor = new GamepadInputProcessor();

        Assert.False(Fired(processor.Update(Pad(lx: 12000), Frame, Tuning), GamepadInput.LeftStickRight));      // 0.37: not far enough for a press
        Assert.True(Fired(processor.Update(Pad(lx: 20000), Frame, Tuning), GamepadInput.LeftStickRight));       // 0.61
        Assert.True(Fired(processor.Update(Pad(lx: -30000), Frame, Tuning), GamepadInput.LeftStickLeft));
        Assert.True(Fired(processor.Update(Pad(ly: 30000), Frame, Tuning), GamepadInput.LeftStickUp));
        Assert.True(Fired(processor.Update(Pad(ly: -30000), Frame, Tuning), GamepadInput.LeftStickDown));
    }

    [Fact]
    public void RightStick_HasARadialDeadZone()
    {
        var processor = new GamepadInputProcessor();

        var tiny = processor.Update(Pad(rx: 3000, ry: 3000), Frame, Tuning);      // ~0.13 total

        Assert.Equal(0, Axis(tiny, GamepadInput.RightStickX));
        Assert.Equal(0, Axis(tiny, GamepadInput.RightStickY));
        Assert.Empty(tiny);
    }

    [Fact]
    public void RightStick_ScalesFromTheEdgeOfTheDeadZoneToFullDeflection()
    {
        var processor = new GamepadInputProcessor();

        var full = processor.Update(Pad(rx: short.MaxValue), Frame, Tuning);
        var half = processor.Update(Pad(rx: 16384), Frame, Tuning);

        Assert.Equal(1.0, Axis(full, GamepadInput.RightStickX), 3);
        Assert.InRange(Axis(half, GamepadInput.RightStickX), 0.3, 0.4);   // (0.5 - 0.25) / 0.75
    }

    [Fact]
    public void RightStickUp_IsANegativeY_BecauseDownIsPositive()
    {
        var signals = new GamepadInputProcessor().Update(Pad(ry: short.MaxValue), Frame, Tuning);

        Assert.Equal(-1.0, Axis(signals, GamepadInput.RightStickY), 3);
    }

    [Fact]
    public void TheLeftStick_AlsoReportsAnalogueAxes()
    {
        var right = new GamepadInputProcessor().Update(Pad(lx: short.MaxValue), Frame, Tuning);
        var down = new GamepadInputProcessor().Update(Pad(ly: short.MinValue), Frame, Tuning);

        Assert.Equal(1.0, Axis(right, GamepadInput.LeftStickX), 3);
        Assert.Equal(1.0, Axis(down, GamepadInput.LeftStickY), 3);   // stick down is -Y in XInput; the axis is down-positive
    }

    [Fact]
    public void Triggers_ZoomInAndOut_WithADeadZone()
    {
        var processor = new GamepadInputProcessor();

        Assert.Equal(1.0, Axis(processor.Update(Pad(rt: 255), Frame, Tuning), GamepadInput.Triggers), 3);
        Assert.Equal(-1.0, Axis(processor.Update(Pad(lt: 255), Frame, Tuning), GamepadInput.Triggers), 3);
        Assert.Equal(0, Axis(processor.Update(Pad(rt: 20), Frame, Tuning), GamepadInput.Triggers));
        Assert.Equal(0, Axis(processor.Update(Pad(lt: 255, rt: 255), Frame, Tuning), GamepadInput.Triggers), 3);
    }

    [Fact]
    public void TheDeadZonesComeFromTheTuning()
    {
        var wide = new InputTuning { StickDeadZone = 0.9, TriggerDeadZone = 0.9, StickDigitalThreshold = 0.95 };
        var processor = new GamepadInputProcessor();

        var signals = processor.Update(Pad(rx: 20000, rt: 200, lx: 20000), Frame, wide);

        Assert.Empty(signals);
    }

    [Fact]
    public void Reset_ForgetsHeldButtons()
    {
        var processor = new GamepadInputProcessor();
        processor.Update(Pad(GamepadButtons.A | GamepadButtons.Y), Frame, Tuning);

        processor.Reset();

        var signals = processor.Update(Pad(GamepadButtons.A | GamepadButtons.Y), Frame, Tuning);
        Assert.True(Fired(signals, GamepadInput.A));
        Assert.True(Fired(signals, GamepadInput.Y));
    }

    [Fact]
    public void EmptyState_ProducesNoSignals() =>
        Assert.Empty(new GamepadInputProcessor().Update(Pad(), Frame, Tuning));
}
