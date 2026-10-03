using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// An <see cref="IInputService"/> that does nothing: every <c>Process*</c> call returns false, registrations are no-ops, there are no actions or bindings. It is what
/// consumers fall back to when no real service was supplied, so the many existing tests that construct a view model with its default constructor keep compiling and
/// behave as before.
/// </summary>
public sealed class NullInputService : IInputService
{
    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    public static readonly NullInputService Instance = new();

    private static readonly NoopDisposable Noop = new();

    private NullInputService()
    {
    }

#pragma warning disable CS0067 // Never raised: nothing ever triggers an action here.
    public event EventHandler<InputActionEventArgs>? ActionTriggered;

    public event EventHandler? BindingsChanged;
#pragma warning restore CS0067

    public IInputActionCatalog Actions { get; } = new InputActionCatalog();

    public InputTuning Tuning { get; } = new();

    public bool ProcessKeyDown(KeyEventArgs e) => false;

    public bool ProcessKey(Key key, KeyModifiers modifiers) => false;

    public bool ProcessPointerWheel(PointerWheelEventArgs e) => false;

    public bool ProcessPointerPressed(PointerPressedEventArgs e) => false;

    public bool ProcessGamepad(GamepadState state, TimeSpan elapsed) => false;

    public void ResetGamepad()
    {
    }

    public bool Dispatch(InputAction action, InputPayload payload = default) => false;

    public IDisposable Register(InputScope scope, Action<InputActionEventArgs> handler, Func<InputContext>? context = null) => Noop;

    public IReadOnlyList<InputBinding> GetBindings(InputAction action) => [];

    public void SetBindings(InputAction action, IReadOnlyList<InputBinding> bindings)
    {
    }

    public void ResetBindings(InputAction action)
    {
    }

    public void ResetAll()
    {
    }

    public IReadOnlyList<InputConflict> FindConflicts(InputAction action, InputBinding binding) => [];
}

/// <summary>
/// The app's single <see cref="IInputService"/>, set once by the composition root (<c>App.axaml.cs</c>) before any window is built. There is no DI container in this app,
/// so consumers that cannot be handed the service through a constructor (controls created from XAML) read it here; everything else takes it as an optional constructor
/// parameter defaulting to this. Until startup sets it, it is the do-nothing <see cref="NullInputService"/>, which is also what headless tests see.
/// </summary>
public static class InputServiceLocator
{
    public static IInputService Current { get; set; } = NullInputService.Instance;
}
