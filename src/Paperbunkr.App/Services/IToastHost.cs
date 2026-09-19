using System;
using Avalonia.Threading;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// Shows/closes a toast from anywhere - including background threads. <c>MainViewModel.ShowToast</c>
/// is private and its handler adds to the toast panel on the calling thread, so any caller off the UI
/// thread needs this marshalling (docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md
/// §4). Screen ViewModels only ever received a plain title/message callback, so none could show an
/// actionable toast; this is the seam that fixes that.
/// </summary>
public interface IToastHost
{
    void Show(ToastRequest toast);

    void Close(ToastRequest toast);
}

/// <summary>Wraps show/close delegates, always dispatching onto <see cref="Dispatcher.UIThread"/>.
/// <paramref name="isOnUiThread"/>/<paramref name="post"/> are test seams (default: the real Avalonia
/// dispatcher) - a headless test can't reliably pump the real UI queue from an arbitrary xUnit thread.</summary>
public sealed class DispatcherToastHost : IToastHost
{
    private readonly Action<ToastRequest> _show;
    private readonly Action<ToastRequest> _close;
    private readonly Func<bool> _isOnUiThread;
    private readonly Action<Action> _post;

    public DispatcherToastHost(Action<ToastRequest> show, Action<ToastRequest> close, Func<bool>? isOnUiThread = null, Action<Action>? post = null)
    {
        _show = show;
        _close = close;
        _isOnUiThread = isOnUiThread ?? (() => Dispatcher.UIThread.CheckAccess());
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
    }

    public void Show(ToastRequest toast) => Run(() => _show(toast));

    public void Close(ToastRequest toast) => Run(() => _close(toast));

    private void Run(Action action)
    {
        if (_isOnUiThread())
        {
            action();
        }
        else
        {
            _post(action);
        }
    }
}
