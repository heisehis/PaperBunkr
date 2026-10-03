using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Feeds a window's physical input to an <see cref="IInputService"/> (docs/superpowers/specs/2026-10-03-input-service-design.md §5.6). The three <see cref="RoutingStrategies.Tunnel"/>
/// handlers run at the root before any control sees the event, so a shortcut fires whatever has focus; the service decides what a focused text box keeps for itself. The
/// application's main window and the headless test hosts attach through here so they cannot drift apart.
/// </summary>
public sealed class InputHost : IDisposable
{
    private readonly TopLevel _topLevel;
    private readonly IInputService _service;
    private bool _disposed;

    private InputHost(TopLevel topLevel, IInputService service)
    {
        _topLevel = topLevel;
        _service = service;
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>Starts forwarding <paramref name="topLevel"/>'s key, wheel and pointer-pressed events to <paramref name="service"/> until the result is disposed.</summary>
    public static InputHost Attach(TopLevel topLevel, IInputService service) => new(topLevel, service);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _topLevel.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        _topLevel.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled)
        {
            _service.ProcessKeyDown(e);
        }
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.Handled)
        {
            _service.ProcessPointerWheel(e);
        }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Handled)
        {
            _service.ProcessPointerPressed(e);
        }
    }
}
