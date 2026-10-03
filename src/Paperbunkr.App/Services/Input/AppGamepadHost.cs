using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Runs the one controller poller for the whole app (docs/superpowers/specs/2026-10-03-input-service-design.md §14). It polls while <see cref="IInputService.GamepadEnabled"/> is on and
/// the window is the active one, hands every snapshot to the input service, and stops otherwise, so a machine with the setting off, or a window in the background, costs nothing.
/// It used to be the reader screen's own poller, which is why a controller only ever worked inside the reader.
/// </summary>
public sealed class AppGamepadHost : IDisposable
{
    private readonly Window _window;
    private readonly IInputService _input;
    private readonly GamepadPoller _poller;
    private readonly DispatcherTimer _supervisor;
    private bool _disposed;

    /// <param name="window">The main window; the controller is read only while it is active.</param>
    /// <param name="input">The service that receives each snapshot and says whether a controller is wanted.</param>
    /// <param name="source">Where snapshots come from (<see cref="XInputSource"/> in the app).</param>
    public AppGamepadHost(Window window, IInputService input, IGamepadSource source)
    {
        _window = window;
        _input = input;
        _poller = new GamepadPoller(source, (state, elapsed) => _input.ProcessGamepad(state, elapsed), _input.ResetGamepad);
        _input.GamepadEnabledChanged += OnEnabledChanged;
        _window.PropertyChanged += OnWindowPropertyChanged;

        // A window that is minimised or whose active state changed while the poller was between decisions is caught here; this is cheap (a property read twice a second).
        _supervisor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _supervisor.Tick += (_, _) => Update();
        _supervisor.Start();
        Update();
    }

    /// <summary>The poller, for tests that drive it a step at a time.</summary>
    internal GamepadPoller Poller => _poller;

    /// <summary>Starts or stops polling to match the setting and the window.</summary>
    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        if (_input.GamepadEnabled && _window.IsActive)
        {
            _poller.Start();
        }
        else
        {
            _poller.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _supervisor.Stop();
        _poller.Stop();
        _input.GamepadEnabledChanged -= OnEnabledChanged;
        _window.PropertyChanged -= OnWindowPropertyChanged;
    }

    private void OnEnabledChanged(object? sender, EventArgs e) => Update();

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty)
        {
            Update();
        }
    }
}
