using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// The application-wide input service (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5). Physical input arrives through the <c>Process*</c> entry points,
/// is turned into an <see cref="InputBinding"/>, looked up in the <see cref="InputKeymap"/>, filtered by the active scopes, their <see cref="InputContext"/> and the
/// focus-suppression rule, and delivered to registered handlers until one claims it. UI thread only.
/// </summary>
public sealed class InputService : IInputService
{
    private sealed class Registration(InputScope scope, Action<InputActionEventArgs> handler, Func<InputContext>? context, long sequence)
    {
        public InputScope Scope { get; } = scope;

        public Action<InputActionEventArgs> Handler { get; } = handler;

        public Func<InputContext>? Context { get; } = context;

        public long Sequence { get; } = sequence;
    }

    private sealed class ActiveScope(InputScope scope, List<(Registration Registration, InputContext? Context)> registrations)
    {
        public InputScope Scope { get; } = scope;

        /// <summary>Live (non-dormant) registrations, newest first, each with the context it reported for this resolution (null when it has no context provider).</summary>
        public List<(Registration Registration, InputContext? Context)> Entries { get; } = registrations;

        /// <summary>The handlers, newest first.</summary>
        public IEnumerable<Registration> Registrations => Entries.Select(e => e.Registration);

        /// <summary>
        /// The scope's current state: the intersection of what every live registration with a context provider reported (so a screen-level registration reporting
        /// <see cref="InputContext.Always"/> and a canvas reporting <see cref="InputContext.PagedZoomed"/> together mean PagedZoomed), or <see cref="InputContext.Always"/>
        /// when none has one.
        /// </summary>
        public InputContext Context
        {
            get
            {
                var state = InputContext.Always;
                foreach (var (_, reported) in Entries)
                {
                    if (reported is { } value)
                    {
                        state &= value;
                    }
                }

                return state;
            }
        }
    }

    private sealed class Token(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => System.Threading.Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    /// <summary>Wraps the catalog so a registration after startup (a plugin) refreshes the keymap index.</summary>
    private sealed class ObservedCatalog(IInputActionCatalog inner, Action registered) : IInputActionCatalog
    {
        public IReadOnlyList<InputActionInfo> All => inner.All;

        public InputActionInfo? Find(InputAction action) => inner.Find(action);

        public void Register(InputActionInfo info)
        {
            inner.Register(info);
            registered();
        }
    }

    private readonly IInputActionCatalog _catalog;
    private readonly IKeymapStore _store;
    private readonly IInputSuppressionProbe _probe;
    private readonly Action<string>? _log;
    private readonly InputKeymap _keymap;
    private readonly GamepadInputProcessor _pad = new();
    private readonly List<Registration> _registrations = [];
    private readonly ObservedCatalog _observedCatalog;
    private long _sequence;

    /// <param name="catalog">The actions; usually <see cref="InputActionCatalog.CreateWithCoreActions"/>.</param>
    /// <param name="store">Where the user's overrides live.</param>
    /// <param name="probe">Decides whether the focused control suppresses hotkeys; defaults to never.</param>
    /// <param name="log">Receives one-line warnings (a keymap that could not be saved).</param>
    public InputService(IInputActionCatalog catalog, IKeymapStore store, IInputSuppressionProbe? probe = null, Action<string>? log = null)
    {
        _catalog = catalog;
        _store = store;
        _probe = probe ?? NoInputSuppressionProbe.Instance;
        _log = log;
        _keymap = new InputKeymap(catalog, store.Load());
        _keymap.Changed += (_, _) => BindingsChanged?.Invoke(this, EventArgs.Empty);
        _observedCatalog = new ObservedCatalog(catalog, OnCatalogRegistered);
        if (_keymap.Rekeyed)
        {
            Save();
        }
    }

    public event EventHandler<InputActionEventArgs>? ActionTriggered;

    public event EventHandler? BindingsChanged;

    public IInputActionCatalog Actions => _observedCatalog;

    /// <summary>The thresholds and timings in force.</summary>
    public InputTuning Tuning => _keymap.Config.Tuning;

    /// <summary>The live config, for the importer and Preferences; save through <see cref="SaveConfig"/> after changing it directly.</summary>
    internal KeymapConfig Config => _keymap.Config;

    // ----- Avalonia entry points -----

    public bool ProcessKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var topLevel = e.Source is Visual visual ? TopLevel.GetTopLevel(visual) : null;
        bool claimed = ProcessKey(e.Key, e.KeyModifiers, _probe.GetActive(topLevel));
        if (claimed)
        {
            e.Handled = true;
        }

        return claimed;
    }

    public bool ProcessPointerWheel(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (SuppressesPointer(e.Source))
        {
            return false;
        }

        bool claimed = ProcessWheel(e.Delta, e.KeyModifiers, visual => e.GetPosition(visual));
        if (claimed)
        {
            e.Handled = true;
        }

        return claimed;
    }

    public bool ProcessPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (SuppressesPointer(e.Source))
        {
            return false;
        }

        var button = e.GetCurrentPoint(null).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => MouseButton.Left,
            PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
            PointerUpdateKind.RightButtonPressed => MouseButton.Right,
            PointerUpdateKind.XButton1Pressed => MouseButton.XButton1,
            PointerUpdateKind.XButton2Pressed => MouseButton.XButton2,
            _ => MouseButton.None,
        };

        if (button == MouseButton.None)
        {
            return false;
        }

        bool claimed = ProcessMouseButton(button, e.KeyModifiers, e.ClickCount, visual => e.GetPosition(visual));
        if (claimed)
        {
            e.Handled = true;
        }

        return claimed;
    }

    /// <summary>
    /// True when the focused control wants raw input of every kind (<see cref="InputSuppression.All"/>: a "press your combination" capture box), so a wheel turn or a thumb-button press
    /// reaches it instead of firing an action. Ordinary text entry never suppresses the pointer.
    /// </summary>
    private bool SuppressesPointer(object? source) =>
        _probe.GetActive(source is Visual visual ? TopLevel.GetTopLevel(visual) : null) == InputSuppression.All;

    // ----- The same entry points without Avalonia event objects (what the adapters above call, and what tests drive directly) -----

    public bool ProcessKey(Key key, KeyModifiers modifiers) => ProcessKey(key, modifiers, null);

    internal bool ProcessKey(Key key, KeyModifiers modifiers, InputSuppression? suppression) =>
        DeliverBinding(InputBinding.ForKey(key, modifiers), InputDevice.Keyboard, suppression, 1, default, modifiers, null, default);

    internal bool ProcessWheel(Vector delta, KeyModifiers modifiers, Func<Visual, Point>? position)
    {
        // A horizontal swipe has to be one big delta (not accumulated sub-detent scrolling) to count as a left/right binding; an ordinary vertical wheel reports X == 0.
        if (Math.Abs(delta.X) >= Tuning.SwipeThreshold)
        {
            var direction = delta.X < 0 ? WheelDirection.Left : WheelDirection.Right;
            if (DeliverBinding(InputBinding.ForWheel(direction, modifiers), InputDevice.Mouse, null, 1, delta, modifiers, position, default))
            {
                return true;
            }
        }

        if (delta.Y != 0)
        {
            var direction = delta.Y > 0 ? WheelDirection.Up : WheelDirection.Down;
            return DeliverBinding(InputBinding.ForWheel(direction, modifiers), InputDevice.Mouse, null, 1, delta, modifiers, position, default);
        }

        return false;
    }

    internal bool ProcessMouseButton(MouseButton button, KeyModifiers modifiers, int clicks, Func<Visual, Point>? position) =>
        DeliverBinding(InputBinding.ForMouseButton(button, modifiers, clicks), InputDevice.Mouse, null, 1, default, modifiers, position, default);

    public bool ProcessGamepad(GamepadState state, TimeSpan elapsed)
    {
        bool claimed = false;
        foreach (var signal in _pad.Update(state, elapsed, Tuning))
        {
            claimed |= DeliverBinding(InputBinding.ForPad(signal.Input), InputDevice.Gamepad, null, signal.Value, default, KeyModifiers.None, null, elapsed);
        }

        return claimed;
    }

    public void ResetGamepad() => _pad.Reset();

    public bool Dispatch(InputAction action, InputPayload payload = default) =>
        Deliver([action], default, InputDevice.Programmatic, null, payload.Value ?? 1, payload.WheelDelta, payload.Modifiers, payload.PositionResolver, default);

    // ----- Registration -----

    public IDisposable Register(InputScope scope, Action<InputActionEventArgs> handler, Func<InputContext>? context = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new Registration(scope, handler, context, ++_sequence);
        _registrations.Add(registration);
        return new Token(() => _registrations.Remove(registration));
    }

    // ----- Binding management -----

    public IReadOnlyList<InputBinding> GetBindings(InputAction action) => _keymap.GetBindings(action);

    public void SetBindings(InputAction action, IReadOnlyList<InputBinding> bindings)
    {
        _keymap.Set(action, bindings);
        Save();
    }

    public void ResetBindings(InputAction action)
    {
        _keymap.Reset(action);
        Save();
    }

    public void ResetAll()
    {
        _keymap.ResetAll();
        Save();
    }

    public IReadOnlyList<InputConflict> FindConflicts(InputAction action, InputBinding binding) => _keymap.FindConflicts(action, binding);

    /// <summary>True when the user has replaced this action's default bindings.</summary>
    public bool IsCustomised(InputAction action) => _keymap.IsCustomised(action);

    /// <summary>Rebuilds the index and saves, after the config was changed directly (the legacy importer).</summary>
    internal void SaveConfig()
    {
        _keymap.Rebuild();
        Save();
        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- Resolution -----

    private void OnCatalogRegistered()
    {
        if (_keymap.Rebuild())
        {
            Save();
        }

        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            _store.Save(_keymap.Config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A keymap that cannot be saved still works for this session; losing the write must not take the UI down.
            _log?.Invoke($"keymap.json could not be saved: {ex.Message}");
        }
    }

    /// <summary>The scopes currently consulted, highest priority first, <see cref="InputScope.Global"/> last. An active modal scope hides every other non-global one.</summary>
    private List<ActiveScope> GetActiveScopes()
    {
        // Each registration's context provider is read once here. One that reports InputContext.None is dormant (a hidden screen): it neither activates its scope
        // nor receives anything, so a screen that is merely hidden, not destroyed, can stay registered without ever being asked.
        var active = _registrations
            .Select(r => (Registration: r, Context: r.Context?.Invoke()))
            .Where(e => e.Context != InputContext.None)
            .GroupBy(e => e.Registration.Scope.Name)
            .Select(g => new ActiveScope(g.First().Registration.Scope, g.OrderByDescending(e => e.Registration.Sequence).ToList()))
            .ToList();

        if (!active.Any(s => s.Scope.IsGlobal))
        {
            active.Add(new ActiveScope(InputScope.Global, []));
        }

        var topModal = active.Where(s => s.Scope.IsModal).OrderByDescending(s => s.Scope.Priority).FirstOrDefault();
        if (topModal is not null)
        {
            active.RemoveAll(s => !s.Scope.IsGlobal && s != topModal);
        }

        // Highest priority first; among equal priorities the scope registered most recently first (the screen the user has just opened).
        active.Sort((a, b) =>
        {
            int byPriority = b.Scope.Priority.CompareTo(a.Scope.Priority);
            return byPriority != 0 ? byPriority : NewestSequence(b).CompareTo(NewestSequence(a));
        });
        return active;

        static long NewestSequence(ActiveScope scope) => scope.Entries.Count > 0 ? scope.Entries[0].Registration.Sequence : -1;
    }

    /// <summary>
    /// Offers each candidate action to its handlers, highest-priority scope first, until one sets <see cref="InputActionEventArgs.Handled"/>. An action in a screen scope goes
    /// to that scope's handlers; a Global action goes to the handlers of every active scope (highest priority first), so the active screen can refine it (Refresh, FocusSearch).
    /// </summary>
    private bool DeliverBinding(
        InputBinding binding,
        InputDevice device,
        InputSuppression? suppression,
        double value,
        Vector wheelDelta,
        KeyModifiers modifiers,
        Func<Visual, Point>? position,
        TimeSpan elapsed) =>
        Deliver(_keymap.Resolve(binding), binding, device, suppression, value, wheelDelta, modifiers, position, elapsed);

    private bool Deliver(
        IReadOnlyList<InputAction> candidates,
        InputBinding binding,
        InputDevice device,
        InputSuppression? suppression,
        double value,
        Vector wheelDelta,
        KeyModifiers modifiers,
        Func<Visual, Point>? position,
        TimeSpan elapsed)
    {
        if (candidates.Count == 0 || suppression == InputSuppression.All)
        {
            return false;
        }

        var scopes = GetActiveScopes();
        var eligible = new List<(InputActionInfo Info, ActiveScope Scope)>();
        foreach (var action in candidates)
        {
            if (_catalog.Find(action) is not { } info)
            {
                continue;
            }

            if (suppression == InputSuppression.TextEntry && !info.FiresInTextInput)
            {
                continue;
            }

            var scope = scopes.FirstOrDefault(s => s.Scope.Name == info.Scope.Name);
            if (scope is null || !scope.Context.Overlaps(info.Context))
            {
                continue;
            }

            eligible.Add((info, scope));
        }

        if (eligible.Count == 0)
        {
            return false;
        }

        // Highest-priority scope first; the sort is stable, so actions in one scope keep catalog order.
        eligible = eligible.OrderByDescending(e => e.Scope.Scope.Priority).ToList();

        InputActionEventArgs? first = null;
        foreach (var (info, scope) in eligible)
        {
            var args = new InputActionEventArgs(info.Action, device, value, wheelDelta, modifiers, position, elapsed, binding);
            first ??= args;
            try
            {
                var handlers = info.Scope.IsGlobal
                    ? scopes.SelectMany(s => s.Registrations).ToList()
                    : scope.Registrations;
                foreach (var registration in handlers)
                {
                    registration.Handler(args);
                    if (args.Handled)
                    {
                        break;
                    }
                }

                if (args.Handled)
                {
                    ActionTriggered?.Invoke(this, args);
                    return true;
                }
            }
            finally
            {
                // Dropped after observers ran: a late GetPosition call must not read a stale pointer.
                args.Complete();
            }
        }

        // Nobody took it: tell observers once, about the most specific candidate, and leave the physical event alone.
        ActionTriggered?.Invoke(this, first!);
        return false;
    }
}
