using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>Resolution, scopes, suppression, wheel/mouse/gamepad entry points and persistence of <see cref="InputService"/> (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5).</summary>
public class InputServiceTests
{
    private sealed class StubProbe : IInputSuppressionProbe
    {
        public InputSuppression? Level { get; set; }

        public InputSuppression? GetActive(TopLevel? topLevel) => Level;
    }

    private static InputService Create(MemoryKeymapStore? store = null, StubProbe? probe = null, params InputActionInfo[] extra)
    {
        var catalog = InputActionCatalog.CreateWithCoreActions();
        foreach (var info in extra)
        {
            catalog.Register(info);
        }

        return new InputService(catalog, store ?? new MemoryKeymapStore(), probe ?? new StubProbe());
    }

    /// <summary>Registers a handler that records every action it is offered and claims those in <paramref name="claim"/> (all of them when null).</summary>
    private static IDisposable Record(InputService service, InputScope scope, List<string> seen, Func<InputContext>? context = null, params string[] claim) =>
        service.Register(
            scope,
            e =>
            {
                seen.Add(e.Action.Id);
                if (claim.Length == 0 || claim.Contains(e.Action.Id))
                {
                    e.Handled = true;
                }
            },
            context);

    private static Func<InputContext> Ctx(InputContext context) => () => context;

    // ----- Claiming and the Handled contract -----

    [Fact]
    public void AnUnclaimedKey_ReturnsFalse_AndLeavesTheEventUnhandled()
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = service.Register(InputScope.Reader, e => seen.Add(e.Action.Id), Ctx(InputContext.PagedUnzoomed));
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right };

        bool claimed = service.ProcessKeyDown(args);

        Assert.False(claimed);
        Assert.False(args.Handled);
        Assert.Equal([InputActionIds.PageTurnRight], seen);
    }

    [Fact]
    public void AClaimedKey_MarksTheEventHandled()
    {
        var service = Create();
        using var reg = service.Register(InputScope.Reader, e => e.Handled = true, Ctx(InputContext.PagedUnzoomed));
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right };

        Assert.True(service.ProcessKeyDown(args));
        Assert.True(args.Handled);
    }

    [Fact]
    public void AKeyWithNoBinding_DoesNothing()
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen);

        Assert.False(service.ProcessKey(Key.F9, KeyModifiers.Control, null));
        Assert.Empty(seen);
    }

    [Fact]
    public void WhenAHandlerDoesNotClaim_TheNextHandlerIsTried_NewestFirst()
    {
        var service = Create();
        var order = new List<string>();
        using var first = service.Register(InputScope.Reader, e => { order.Add("first"); e.Handled = true; }, Ctx(InputContext.PagedUnzoomed));
        using var second = service.Register(InputScope.Reader, e => order.Add("second"), Ctx(InputContext.PagedUnzoomed));

        Assert.True(service.ProcessKey(Key.Right, KeyModifiers.None, null));

        Assert.Equal(["second", "first"], order);
    }

    // ----- Scopes -----

    [Fact]
    public void AScopeIsActiveOnlyWhileItHasARegistration()
    {
        var service = Create();
        var seen = new List<string>();
        Assert.False(service.ProcessKey(Key.Right, KeyModifiers.None, null));

        var reg = Record(service, InputScope.Reader, seen, Ctx(InputContext.PagedUnzoomed));
        Assert.True(service.ProcessKey(Key.Right, KeyModifiers.None, null));

        reg.Dispose();
        Assert.False(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Equal([InputActionIds.PageTurnRight], seen);
    }

    [Fact]
    public void RegistrationsCanBeDisposedInAnyOrder_AndMoreThanOnce()
    {
        var service = Create();
        var a = new List<string>();
        var b = new List<string>();
        var c = new List<string>();
        var regA = Record(service, InputScope.Reader, a, Ctx(InputContext.PagedUnzoomed));
        var regB = Record(service, InputScope.Reader, b, Ctx(InputContext.PagedUnzoomed));
        var regC = Record(service, InputScope.Library, c);

        regB.Dispose();
        regB.Dispose();
        service.ProcessKey(Key.Right, KeyModifiers.None, null);
        Assert.Single(a);
        Assert.Empty(b);

        regA.Dispose();
        Assert.False(service.ProcessKey(Key.Right, KeyModifiers.None, null));

        // The Library scope is untouched by the Reader registrations coming and going.
        Assert.True(service.ProcessKey(Key.F5, KeyModifiers.None, null));
        Assert.Single(c);

        regC.Dispose();
        regA.Dispose();
        Assert.False(service.ProcessKey(Key.F5, KeyModifiers.None, null));
        Assert.Single(c);
    }

    [Fact]
    public void ARegistrationReportingNone_IsDormant_ItDoesNotActivateItsScopeOrReceiveAnything()
    {
        var service = Create();
        var seen = new List<string>();
        bool visible = false;
        using var reg = Record(service, InputScope.Reader, seen, () => visible ? InputContext.PagedUnzoomed : InputContext.None);

        Assert.False(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Empty(seen);

        visible = true;
        Assert.True(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Equal([InputActionIds.PageTurnRight], seen);
    }

    [Fact]
    public void ADormantModalScope_DoesNotHideTheScreenBehindIt()
    {
        var service = Create();
        var reader = new List<string>();
        bool overlayOpen = false;
        using var readerReg = Record(service, InputScope.Reader, reader, Ctx(InputContext.PagedUnzoomed));
        using var overlayReg = service.Register(InputScope.Overlay, e => e.Handled = true, () => overlayOpen ? InputContext.Always : InputContext.None);

        Assert.True(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Single(reader);

        overlayOpen = true;
        Assert.False(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Single(reader);
    }

    [Fact]
    public void ADormantRegistration_IsSkippedForGlobalActionsToo()
    {
        var service = Create();
        var hidden = new List<string>();
        var shown = new List<string>();
        using var hiddenReg = Record(service, InputScope.Library, hidden, () => InputContext.None);
        using var shownReg = Record(service, InputScope.Books, shown, Ctx(InputContext.Always));

        Assert.True(service.ProcessKey(Key.Escape, KeyModifiers.None, null));

        Assert.Empty(hidden);
        Assert.Equal([InputActionIds.CloseCurrentView], shown);
    }

    [Fact]
    public void AScreenActionOnlyReachesHandlersOfItsOwnScope()
    {
        var service = Create();
        var library = new List<string>();
        using var reg = Record(service, InputScope.Library, library);

        // PageTurnRight belongs to the Reader scope, which is not active.
        Assert.False(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Empty(library);
    }

    [Fact]
    public void AGlobalAction_ReachesTheHandlersOfEveryActiveScope_HighestPriorityFirst()
    {
        var service = Create();
        var order = new List<string>();
        using var global = service.Register(InputScope.Global, e => { order.Add("global"); e.Handled = true; });
        using var library = service.Register(InputScope.Library, e => order.Add("library"));
        using var reader = service.Register(InputScope.Reader, e => order.Add("reader"));

        Assert.True(service.ProcessKey(Key.Escape, KeyModifiers.None, null));

        Assert.Equal(["reader", "library", "global"], order);
    }

    [Fact]
    public void AScreenCanClaimAGlobalActionBeforeGlobalSeesIt()
    {
        var service = Create();
        var globalSeen = new List<string>();
        using var global = Record(service, InputScope.Global, globalSeen);
        using var library = service.Register(InputScope.Library, e =>
        {
            if (e.Action.Id == InputActionIds.FocusSearch)
            {
                e.Handled = true;
            }
        });

        Assert.True(service.ProcessKey(Key.F, KeyModifiers.Control, null));
        Assert.Empty(globalSeen);
    }

    [Fact]
    public void AModalScope_HidesEveryOtherNonGlobalScope_ButNotGlobal()
    {
        var service = Create();
        var reader = new List<string>();
        var global = new List<string>();
        var overlay = new List<string>();
        using var readerReg = Record(service, InputScope.Reader, reader, Ctx(InputContext.PagedUnzoomed));
        using var globalReg = Record(service, InputScope.Global, global, null, InputActionIds.CloseCurrentView);

        Assert.True(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Single(reader);

        using var overlayReg = Record(service, InputScope.Overlay, overlay);
        reader.Clear();

        Assert.False(service.ProcessKey(Key.Right, KeyModifiers.None, null));
        Assert.Empty(reader);
        Assert.True(service.ProcessKey(Key.Escape, KeyModifiers.None, null));
        Assert.Equal([InputActionIds.CloseCurrentView], overlay);
    }

    // ----- Contexts -----

    [Theory]
    [InlineData(InputContext.PagedUnzoomed, InputActionIds.PageTurnRight)]
    [InlineData(InputContext.PagedZoomed, InputActionIds.PanRight)]
    [InlineData(InputContext.Continuous, InputActionIds.ScrollRight)]
    public void TheSameKeyMeansADifferentActionInEachReaderState(InputContext state, string expected)
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen, Ctx(state));

        Assert.True(service.ProcessKey(Key.Right, KeyModifiers.None, null));

        Assert.Equal([expected], seen);
    }

    [Fact]
    public void TheContextIsReadWhenTheKeyIsPressed_NotWhenRegistered()
    {
        var service = Create();
        var state = InputContext.PagedUnzoomed;
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen, () => state);

        service.ProcessKey(Key.Right, KeyModifiers.None, null);
        state = InputContext.Continuous;
        service.ProcessKey(Key.Right, KeyModifiers.None, null);

        Assert.Equal([InputActionIds.PageTurnRight, InputActionIds.ScrollRight], seen);
    }

    [Fact]
    public void AReadingOrderCommandReachesBothPagedStates_ButNotContinuous()
    {
        var service = Create();
        var state = InputContext.PagedZoomed;
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen, () => state);

        service.ProcessKey(Key.PageDown, KeyModifiers.None, null);
        state = InputContext.PagedUnzoomed;
        service.ProcessKey(Key.PageDown, KeyModifiers.None, null);
        state = InputContext.Continuous;
        service.ProcessKey(Key.PageDown, KeyModifiers.None, null);

        Assert.Equal([InputActionIds.NextPage, InputActionIds.NextPage, InputActionIds.ScrollPageDown], seen);
    }

    [Fact]
    public void ARegistrationWithoutAContext_CountsAsAlways()
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen);

        service.ProcessKey(Key.R, KeyModifiers.None, null);

        Assert.Equal([InputActionIds.RotateClockwise], seen);
    }

    // ----- Suppression -----

    [Fact]
    public void WhileTyping_OnlyFiresInTextInputActionsFire()
    {
        var probe = new StubProbe { Level = InputSuppression.TextEntry };
        var service = Create(probe: probe);
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Global, seen);

        Assert.True(service.ProcessKey(Key.Escape, KeyModifiers.None, probe.Level));
        Assert.True(service.ProcessKey(Key.P, KeyModifiers.Control, probe.Level));
        Assert.True(service.ProcessKey(Key.BrowserBack, KeyModifiers.None, probe.Level));
        Assert.False(service.ProcessKey(Key.Z, KeyModifiers.Control, probe.Level));
        Assert.False(service.ProcessKey(Key.Q, KeyModifiers.Control, probe.Level));

        Assert.Equal([InputActionIds.CloseCurrentView, InputActionIds.OpenQuickOpen, InputActionIds.NavigateBack], seen);
    }

    [Fact]
    public void AnAllSuppressor_BlocksEvenEscape()
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Global, seen);

        Assert.False(service.ProcessKey(Key.Escape, KeyModifiers.None, InputSuppression.All));
        Assert.Empty(seen);
    }

    [Fact]
    public void ProcessKeyDown_AsksTheProbe()
    {
        var probe = new StubProbe { Level = InputSuppression.TextEntry };
        var service = Create(probe: probe);
        using var reg = service.Register(InputScope.Global, e => e.Handled = true);

        var undo = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control };
        Assert.False(service.ProcessKeyDown(undo));
        Assert.False(undo.Handled);

        probe.Level = null;
        Assert.True(service.ProcessKeyDown(undo));
        Assert.True(undo.Handled);
    }

    [Fact]
    public void PointerWheelAndGamepad_AreNotSuppressed()
    {
        var probe = new StubProbe { Level = InputSuppression.All };
        var service = Create(probe: probe);
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen);

        Assert.True(service.ProcessWheel(new Vector(0, 1), KeyModifiers.Control, null));
        Assert.True(service.ProcessMouseButton(MouseButton.XButton1, KeyModifiers.None, 1, null));
        Assert.True(service.ProcessGamepad(new GamepadState(GamepadButtons.Y, 0, 0, 0, 0, 0, 0), TimeSpan.FromMilliseconds(16)));
    }

    // ----- Wheel -----

    [Fact]
    public void CtrlWheelUpAndDown_ZoomInAndOut_WithTheDeltaAndAPosition()
    {
        var service = Create();
        var events = new List<InputActionEventArgs>();
        var canvas = new Border();
        using var reg = service.Register(InputScope.Reader, e =>
        {
            events.Add(e);
            e.Handled = true;
        });

        service.ProcessWheel(new Vector(0, 2), KeyModifiers.Control, v => new Point(5, 6));
        service.ProcessWheel(new Vector(0, -1), KeyModifiers.Control, v => new Point(7, 8));

        Assert.Equal([InputActionIds.ZoomIn, InputActionIds.ZoomOut], events.Select(e => e.Action.Id));
        Assert.Equal(new Vector(0, 2), events[0].WheelDelta);
        Assert.Equal(new Vector(0, -1), events[1].WheelDelta);
        Assert.All(events, e => Assert.Equal(KeyModifiers.Control, e.Modifiers));
        Assert.All(events, e => Assert.Equal(InputDevice.Mouse, e.Device));
    }

    [Fact]
    public void ThePositionIsResolvedPerVisual_AndOnlyDuringDispatch()
    {
        var service = Create();
        var canvas = new Border();
        var other = new Border();
        Point? during = null;
        Point? duringOther = null;
        InputActionEventArgs? kept = null;
        using var reg = service.Register(InputScope.Reader, e =>
        {
            during = e.GetPosition(canvas);
            duringOther = e.GetPosition(other);
            kept = e;
            e.Handled = true;
        });

        service.ProcessWheel(new Vector(0, 1), KeyModifiers.Control, v => ReferenceEquals(v, canvas) ? new Point(10, 20) : new Point(1, 2));

        Assert.Equal(new Point(10, 20), during);
        Assert.Equal(new Point(1, 2), duringOther);
        Assert.Null(kept!.GetPosition(canvas));
    }

    [Fact]
    public void APlainVerticalWheel_IsNotAnAction()
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen);

        Assert.False(service.ProcessWheel(new Vector(0, 1), KeyModifiers.None, null));
        Assert.Empty(seen);
    }

    [Theory]
    [InlineData(-1.5, InputActionIds.NavigateBack)]
    [InlineData(-4, InputActionIds.NavigateBack)]
    [InlineData(1.5, InputActionIds.NavigateForward)]
    public void AHorizontalSwipeAtTheThreshold_NavigatesBackOrForward(double deltaX, string expected)
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Global, seen);

        Assert.True(service.ProcessWheel(new Vector(deltaX, 0), KeyModifiers.None, null));
        Assert.Equal([expected], seen);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.49)]
    [InlineData(0)]
    public void ASmallHorizontalScroll_IsNotASwipe(double deltaX)
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Global, seen);

        Assert.False(service.ProcessWheel(new Vector(deltaX, 0), KeyModifiers.None, null));
        Assert.Empty(seen);
    }

    [Fact]
    public void TheSwipeThresholdComesFromTheTuning()
    {
        var store = new MemoryKeymapStore();
        var config = store.Load();
        config.Tuning.SwipeThreshold = 3;
        store.Save(config);
        var service = Create(store);
        using var reg = service.Register(InputScope.Global, e => e.Handled = true);

        Assert.False(service.ProcessWheel(new Vector(-2, 0), KeyModifiers.None, null));
        Assert.True(service.ProcessWheel(new Vector(-3, 0), KeyModifiers.None, null));
    }

    [Fact]
    public void ADiagonalSwipe_FallsBackToTheVerticalBinding_WhenTheHorizontalOneIsUnclaimed()
    {
        var service = Create();
        var seen = new List<string>();
        using var reg = service.Register(InputScope.Reader, e =>
        {
            seen.Add(e.Action.Id);
            e.Handled = e.Action.Id == InputActionIds.ZoomIn;
        });

        Assert.True(service.ProcessWheel(new Vector(-2, 1), KeyModifiers.Control, null));
        Assert.Equal([InputActionIds.ZoomIn], seen);
    }

    // ----- Mouse buttons -----

    [Fact]
    public void TheThumbButtons_TurnPagesInTheReader_WhenTheReaderClaimsThem()
    {
        var service = Create();
        var seen = new List<string>();
        using var global = Record(service, InputScope.Global, seen);
        using var reader = service.Register(InputScope.Reader, e => { seen.Add(e.Action.Id); e.Handled = true; }, Ctx(InputContext.PagedUnzoomed));

        Assert.True(service.ProcessMouseButton(MouseButton.XButton1, KeyModifiers.None, 1, null));
        Assert.True(service.ProcessMouseButton(MouseButton.XButton2, KeyModifiers.None, 1, null));

        Assert.Equal([InputActionIds.PreviousPage, InputActionIds.NextPage], seen);
    }

    [Fact]
    public void TheThumbButtons_FallThroughToNavigation_WhenTheReaderDeclinesThem()
    {
        var service = Create();
        var seen = new List<string>();
        using var global = Record(service, InputScope.Global, seen);
        using var reader = service.Register(InputScope.Reader, _ => { }, Ctx(InputContext.PagedUnzoomed));

        Assert.True(service.ProcessMouseButton(MouseButton.XButton1, KeyModifiers.None, 1, null));
        Assert.True(service.ProcessMouseButton(MouseButton.XButton2, KeyModifiers.None, 1, null));

        Assert.Equal([InputActionIds.NavigateBack, InputActionIds.NavigateForward], seen);
    }

    [Fact]
    public void TheThumbButtons_NavigateOutsideTheReader()
    {
        var service = Create();
        var seen = new List<string>();
        using var global = Record(service, InputScope.Global, seen);

        service.ProcessMouseButton(MouseButton.XButton1, KeyModifiers.None, 1, null);

        Assert.Equal([InputActionIds.NavigateBack], seen);
    }

    [Fact]
    public void TheThumbButtons_ScrollAScreenInContinuousMode()
    {
        var service = Create();
        var seen = new List<string>();
        using var reader = Record(service, InputScope.Reader, seen, Ctx(InputContext.Continuous));

        service.ProcessMouseButton(MouseButton.XButton1, KeyModifiers.None, 1, null);
        service.ProcessMouseButton(MouseButton.XButton2, KeyModifiers.None, 1, null);

        Assert.Equal([InputActionIds.ScrollPageUp, InputActionIds.ScrollPageDown], seen);
    }

    [Fact]
    public void AUserCanBindADoubleClick()
    {
        var service = Create();
        service.SetBindings(InputActionIds.ToggleChrome, [InputBinding.ForMouseButton(MouseButton.Left, clicks: 2)]);
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen);

        Assert.False(service.ProcessMouseButton(MouseButton.Left, KeyModifiers.None, 1, null));
        Assert.True(service.ProcessMouseButton(MouseButton.Left, KeyModifiers.None, 2, null));
        Assert.Equal([InputActionIds.ToggleChrome], seen);
    }

    // ----- Programmatic dispatch and observers -----

    [Fact]
    public void Dispatch_RunsTheSameScopesAndHandlers_WithTheGivenPayload()
    {
        var service = Create();
        InputActionEventArgs? received = null;
        using var reg = service.Register(InputScope.Reader, e => { received = e; e.Handled = true; });

        Assert.True(service.Dispatch(InputActionIds.ZoomIn, new InputPayload(Value: 0.5, Modifiers: KeyModifiers.Alt)));

        Assert.Equal(InputDevice.Programmatic, received!.Device);
        Assert.Equal(0.5, received.Value);
        Assert.Equal(KeyModifiers.Alt, received.Modifiers);
    }

    [Fact]
    public void Dispatch_OfAPlainPress_HasValueOne_AndAnInactiveScopeDoesNotReceiveIt()
    {
        var service = Create();
        Assert.False(service.Dispatch(InputActionIds.ZoomIn));

        double value = 0;
        using var reg = service.Register(InputScope.Reader, e => { value = e.Value; e.Handled = true; });
        Assert.True(service.Dispatch(InputActionIds.ZoomIn));
        Assert.Equal(1, value);
        Assert.False(service.Dispatch("No.Such.Action"));
        Assert.False(service.Dispatch(default));
    }

    [Fact]
    public void ActionTriggered_IsRaisedOnceForAClaimedAction()
    {
        var service = Create();
        var raised = new List<(string Id, bool Handled)>();
        service.ActionTriggered += (_, e) => raised.Add((e.Action.Id, e.Handled));
        using var reg = service.Register(InputScope.Reader, e => e.Handled = true, Ctx(InputContext.PagedUnzoomed));

        service.ProcessKey(Key.Right, KeyModifiers.None, null);

        Assert.Equal([(InputActionIds.PageTurnRight, true)], raised);
    }

    [Fact]
    public void ActionTriggered_IsRaisedOnceForAnUnclaimedAction_WithHandledFalse()
    {
        var service = Create();
        var raised = new List<(string Id, bool Handled)>();
        service.ActionTriggered += (_, e) => raised.Add((e.Action.Id, e.Handled));
        using var reg = service.Register(InputScope.Reader, _ => { }, Ctx(InputContext.PagedUnzoomed));

        service.ProcessKey(Key.Right, KeyModifiers.None, null);

        Assert.Equal([(InputActionIds.PageTurnRight, false)], raised);
    }

    [Fact]
    public void ActionTriggered_IsNotRaised_ForKeysThatResolveToNothing()
    {
        var service = Create();
        int raised = 0;
        service.ActionTriggered += (_, _) => raised++;

        service.ProcessKey(Key.F9, KeyModifiers.None, null);
        service.ProcessKey(Key.Right, KeyModifiers.None, null);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void AHandlerThatThrows_PropagatesTheException_AndStillReleasesThePosition()
    {
        var service = Create();
        InputActionEventArgs? captured = null;
        using var reg = service.Register(InputScope.Reader, e =>
        {
            captured = e;
            throw new InvalidOperationException("boom");
        });

        Assert.Throws<InvalidOperationException>(() => service.ProcessWheel(new Vector(0, 1), KeyModifiers.Control, v => new Point(1, 1)));

        Assert.Null(captured!.GetPosition(new Border()));
    }

    // ----- Gamepad -----

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    [Fact]
    public void AGamepadButton_ResolvesLikeAnyOtherBinding()
    {
        var service = Create();
        var events = new List<InputActionEventArgs>();
        using var reg = service.Register(InputScope.Reader, e => { events.Add(e); e.Handled = true; }, Ctx(InputContext.PagedUnzoomed));

        Assert.True(service.ProcessGamepad(new GamepadState(GamepadButtons.A, 0, 0, 0, 0, 0, 0), Frame));
        Assert.True(service.ProcessGamepad(new GamepadState(GamepadButtons.DPadRight, 0, 0, 0, 0, 0, 0), Frame));

        Assert.Equal([InputActionIds.NextPage, InputActionIds.PageTurnRight], events.Select(e => e.Action.Id));
        Assert.All(events, e => Assert.Equal(InputDevice.Gamepad, e.Device));
    }

    [Fact]
    public void AGamepadAxis_IsDeliveredWithItsValueAndTheFrameTime()
    {
        var service = Create();
        var events = new List<InputActionEventArgs>();
        using var reg = service.Register(InputScope.Reader, e => { events.Add(e); e.Handled = true; });

        service.ProcessGamepad(new GamepadState(GamepadButtons.None, 0, 255, 0, 0, short.MaxValue, 0), TimeSpan.FromMilliseconds(20));

        var pan = events.Single(e => e.Action.Id == InputActionIds.PanHorizontal);
        Assert.Equal(1.0, pan.Value, 3);
        Assert.Equal(TimeSpan.FromMilliseconds(20), pan.Elapsed);
        var zoom = events.Single(e => e.Action.Id == InputActionIds.ZoomAxis);
        Assert.Equal(1.0, zoom.Value, 3);
    }

    [Fact]
    public void AHeldPadButton_RepeatsThroughTheService()
    {
        var service = Create();
        int turns = 0;
        using var reg = service.Register(InputScope.Reader, e => { turns++; e.Handled = true; }, Ctx(InputContext.PagedUnzoomed));
        var hold = new GamepadState(GamepadButtons.A, 0, 0, 0, 0, 0, 0);

        for (int i = 0; i < 100; i++)
        {
            service.ProcessGamepad(hold, TimeSpan.FromMilliseconds(10));
        }

        Assert.InRange(turns, 6, 9);
    }

    [Fact]
    public void ResetGamepad_ForgetsHeldButtons()
    {
        var service = Create();
        int presses = 0;
        using var reg = service.Register(InputScope.Reader, e => { presses++; e.Handled = true; });
        var y = new GamepadState(GamepadButtons.Y, 0, 0, 0, 0, 0, 0);

        service.ProcessGamepad(y, Frame);
        service.ProcessGamepad(y, Frame);
        service.ResetGamepad();
        service.ProcessGamepad(y, Frame);

        Assert.Equal(2, presses);
    }

    // ----- Binding management -----

    [Fact]
    public void SetBindings_TakesEffectImmediately_Persists_AndRaisesBindingsChanged()
    {
        var store = new MemoryKeymapStore();
        var service = Create(store);
        int changes = 0;
        service.BindingsChanged += (_, _) => changes++;
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Reader, seen);

        service.SetBindings(InputActionIds.RotateClockwise, [InputBinding.ForKey(Key.J)]);

        Assert.False(service.ProcessKey(Key.R, KeyModifiers.None, null));
        Assert.True(service.ProcessKey(Key.J, KeyModifiers.None, null));
        Assert.Equal([InputActionIds.RotateClockwise], seen);
        Assert.Equal(1, changes);
        Assert.Equal(["J"], store.Load().Overrides[InputActionIds.RotateClockwise]);
    }

    [Fact]
    public void ResetBindingsAndResetAll_RestoreDefaults_AndPersist()
    {
        var store = new MemoryKeymapStore();
        var service = Create(store);
        service.SetBindings(InputActionIds.RotateClockwise, [InputBinding.ForKey(Key.J)]);
        service.SetBindings(InputActionIds.ZoomIn, []);

        service.ResetBindings(InputActionIds.RotateClockwise);
        Assert.Equal([InputBinding.ForKey(Key.R)], service.GetBindings(InputActionIds.RotateClockwise));
        Assert.Empty(service.GetBindings(InputActionIds.ZoomIn));

        service.ResetAll();
        Assert.NotEmpty(service.GetBindings(InputActionIds.ZoomIn));
        Assert.Empty(store.Load().Overrides);
    }

    [Fact]
    public void SavedOverrides_AreLoadedByANewService()
    {
        var store = new MemoryKeymapStore();
        Create(store).SetBindings(InputActionIds.RotateClockwise, [InputBinding.ForKey(Key.J)]);

        var second = Create(store);

        Assert.Equal([InputBinding.ForKey(Key.J)], second.GetBindings(InputActionIds.RotateClockwise));
    }

    [Fact]
    public void FindConflicts_ComesFromTheKeymap()
    {
        var service = Create();

        var conflicts = service.FindConflicts(InputActionIds.RotateClockwise, InputBinding.ForKey(Key.R, KeyModifiers.Shift));

        Assert.Equal([InputActionIds.RotateCounterClockwise], conflicts.Select(c => c.Other.Id));
    }

    [Fact]
    public void ASaveFailure_DoesNotTakeTheServiceDown()
    {
        var logged = new List<string>();
        var service = new InputService(InputActionCatalog.CreateWithCoreActions(), new ThrowingStore(), log: logged.Add);

        service.SetBindings(InputActionIds.RotateClockwise, [InputBinding.ForKey(Key.J)]);

        Assert.Equal([InputBinding.ForKey(Key.J)], service.GetBindings(InputActionIds.RotateClockwise));
        Assert.Single(logged);
    }

    private sealed class ThrowingStore : IKeymapStore
    {
        public KeymapConfig Load() => new();

        public void Save(KeymapConfig config) => throw new IOException("disk full");
    }

    // ----- Plugin-style dynamic actions -----

    [Fact]
    public void AnActionRegisteredAfterStartup_ResolvesAndRemapsLikeACoreOne()
    {
        var store = new MemoryKeymapStore();
        var service = Create(store);
        var seen = new List<string>();
        using var reg = Record(service, InputScope.Global, seen);

        service.Actions.Register(new InputActionInfo(
            "Plugin.Demo.Hello", "Plugin", "Say hello", InputScope.Global, InputContext.Always, [InputBinding.ForKey(Key.F9, KeyModifiers.Control)]));

        Assert.True(service.ProcessKey(Key.F9, KeyModifiers.Control, null));
        service.SetBindings("Plugin.Demo.Hello", [InputBinding.ForKey(Key.F10)]);
        Assert.False(service.ProcessKey(Key.F9, KeyModifiers.Control, null));
        Assert.True(service.ProcessKey(Key.F10, KeyModifiers.None, null));
        Assert.Equal(["Plugin.Demo.Hello", "Plugin.Demo.Hello"], seen);
        Assert.Equal(["F10"], store.Load().Overrides["Plugin.Demo.Hello"]);
    }

    [Fact]
    public void AnOverrideForAPluginActionNotYetRegistered_AppliesOnceItIsRegistered()
    {
        var store = new MemoryKeymapStore();
        var config = store.Load();
        config.Overrides["Plugin.Demo.Hello"] = ["F10"];
        store.Save(config);
        var service = Create(store);
        using var reg = service.Register(InputScope.Global, e => e.Handled = true);

        service.Actions.Register(new InputActionInfo(
            "Plugin.Demo.Hello", "Plugin", "Say hello", InputScope.Global, InputContext.Always, [InputBinding.ForKey(Key.F9)]));

        Assert.True(service.ProcessKey(Key.F10, KeyModifiers.None, null));
        Assert.False(service.ProcessKey(Key.F9, KeyModifiers.None, null));
    }

    [Fact]
    public void ARenamedAction_KeepsTheUsersBinding_AndTheMigrationIsSaved()
    {
        var store = new MemoryKeymapStore();
        var config = store.Load();
        config.Overrides["Old.Hello"] = ["F10"];
        store.Save(config);
        int savesBefore = store.SaveCount;
        var catalog = new InputActionCatalog();
        catalog.Register(new InputActionInfo("New.Hello", "g", "l", InputScope.Global, InputContext.Always, [InputBinding.ForKey(Key.F9)], FormerIds: ["Old.Hello"]));

        var service = new InputService(catalog, store);

        Assert.Equal([InputBinding.ForKey(Key.F10)], service.GetBindings("New.Hello"));
        Assert.Equal(savesBefore + 1, store.SaveCount);
        Assert.DoesNotContain("Old.Hello", store.Load().Overrides.Keys);
    }
}
