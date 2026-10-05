using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Keeps one <see cref="IInputService.Register"/> registration alive for as long as a control is attached to the visual tree, and makes it dormant while the control is not
/// effectively visible (docs/superpowers/specs/2026-10-03-input-service-design.md §5.2, §6). Screens here are hidden rather than destroyed and Avalonia raises no change
/// notification for <c>IsEffectivelyVisible</c>, so visibility is not tracked: it is read through the registration's context provider each time input is resolved, and a
/// hidden screen reports <see cref="InputContext.None"/>. The owner holds this one object; disposing it drops the registration.
/// </summary>
public sealed class AttachedInputRegistration : IDisposable
{
    private readonly Control _owner;
    private readonly InputScope _scope;
    private readonly Action<InputActionEventArgs> _handler;
    private readonly Func<InputContext>? _context;
    private readonly Func<Visual?>? _focusRoot;
    private IInputService _service;
    private IDisposable? _registration;
    private bool _disposed;

    /// <param name="owner">The control whose lifetime and visibility govern the registration.</param>
    /// <param name="scope">The scope the handler belongs to.</param>
    /// <param name="handler">Called for each candidate action in the scope.</param>
    /// <param name="context">The scope's current sub-state while visible (e.g. the reader's paged/zoomed/continuous state); omit for <see cref="InputContext.Always"/>.</param>
    /// <param name="service">The service to register with; can be swapped later through <see cref="Service"/> (a binding sets it after construction).</param>
    /// <param name="focusRoot">
    /// When given, the registration is also dormant while keyboard focus sits <em>outside</em> the visual it returns (an open dialog or popup above the screen). This keeps the reach
    /// the old per-screen key handlers had - they only ever saw keys routed through their own subtree - now that routing happens at the window: Delete or Ctrl+A in the Library must not
    /// act on the selection while a dialog over it has focus. No focus at all counts as inside, so a screen whose focus was lost still answers its shortcuts.
    /// </param>
    public AttachedInputRegistration(
        Control owner, InputScope scope, Action<InputActionEventArgs> handler, Func<InputContext>? context = null, IInputService? service = null, Func<Visual?>? focusRoot = null)
    {
        _owner = owner;
        _scope = scope;
        _handler = handler;
        _context = context;
        _focusRoot = focusRoot;
        _service = service ?? NullInputService.Instance;
        owner.AttachedToVisualTree += OnAttached;
        owner.DetachedFromVisualTree += OnDetached;
        if (TopLevel.GetTopLevel(owner) is not null)
        {
            Register();
        }
    }

    /// <summary>The service in use. Setting it moves a live registration across.</summary>
    public IInputService Service
    {
        get => _service;
        set
        {
            value ??= NullInputService.Instance;
            if (ReferenceEquals(value, _service))
            {
                return;
            }

            bool wasRegistered = _registration is not null;
            Release();
            _service = value;
            if (wasRegistered)
            {
                Register();
            }
        }
    }

    public bool IsRegistered => _registration is not null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _owner.AttachedToVisualTree -= OnAttached;
        _owner.DetachedFromVisualTree -= OnDetached;
        Release();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Register();

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Release();

    private void Register()
    {
        if (_disposed || _registration is not null)
        {
            return;
        }

        _registration = _service.Register(_scope, _handler, CurrentContext);
    }

    private void Release()
    {
        _registration?.Dispose();
        _registration = null;
    }

    private InputContext CurrentContext()
    {
        if (!_owner.IsEffectivelyVisible || FocusIsOutsideRoot())
        {
            return InputContext.None;
        }

        return _context?.Invoke() ?? InputContext.Always;
    }

    private bool FocusIsOutsideRoot()
    {
        if (_focusRoot?.Invoke() is not { } root)
        {
            return false;
        }

        var focused = TopLevel.GetTopLevel(_owner)?.FocusManager?.GetFocusedElement() as Visual;
        return focused is not null and not TopLevel && !ReferenceEquals(focused, root) && !root.IsVisualAncestorOf(focused);
    }
}
