using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Paperbunkr.App.Controls;

/// <summary>
/// App-owned toast host logic (docs/superpowers/specs/2026-09-07-chrome-content-motion-polish-
/// design.md item 5), replacing Avalonia's stock <c>WindowNotificationManager</c> so entrance/exit
/// is <c>PbMotion*</c>-token-wired and follows the Motion setting - the stock control's own animation
/// was confirmed entirely outside app control and untouched by the app's motion policy.
///
/// Deliberately a plain class operating on an existing <see cref="AnimatedStackPanel"/> (declared
/// in <c>MainWindow.axaml</c> as <c>ToastStack</c>), not a custom <see cref="Control"/> subclass of
/// its own - the call site already hands over an already-built content <see cref="Control"/> (a
/// <c>PbToastView</c> per call site, same as the old <c>_notificationManager.Show(view, ...)</c>
/// shape), so there's no templating/data-binding layer this needs to own.
///
/// Motion v2 (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §5.3): a toast is a Reveal - it rises
/// 8 px and fades in on the Reveal tokens (class "toast" then "entered"), and leaves by fading only, on the shorter Reveal exit (class
/// "leaving"), after which it is removed. Styles in Styles/Primitives.axaml.
/// </summary>
public sealed class ToastPresenter
{
    private const int MaxVisible = 3;

    private readonly Panel _stack;
    private readonly List<Control> _visible = new();
    private readonly Dictionary<Control, DispatcherTimer> _autoCloseTimers = new();

    /// <summary>Fires for every close path - explicit, auto-expiry, and MaxVisible eviction alike -
    /// so <c>MainWindow</c>'s own <c>_persistentToasts</c> tracking dictionary can stay in sync even
    /// when a toast disappears without the app ever calling <c>ToastCloseRequested</c> for it (an
    /// eviction leaves that dictionary entry - and everything it roots: the view, its ToastRequest,
    /// any bound ToastAction commands - referenced forever otherwise).</summary>
    public event Action<Control>? Closed;

    public ToastPresenter(Panel stack)
    {
        _stack = stack;
    }

    public void Show(Control view, TimeSpan expiration)
    {
        if (_visible.Count >= MaxVisible)
        {
            Close(_visible[0]);
        }

        view.Classes.Add("toast");
        // Screen readers announce a toast without it taking focus (review checklist: live regions on toasts).
        Avalonia.Automation.AutomationProperties.SetLiveSetting(view, Avalonia.Automation.AutomationLiveSetting.Polite);
        _stack.Children.Add(view);
        _visible.Add(view);

        // Deferred so the transition system observes the initial hidden state before flipping -
        // adding both classes synchronously would coalesce into a no-op, same reasoning as
        // EntranceAnimation.Prepare.
        Dispatcher.UIThread.Post(() => view.Classes.Add("entered"));

        if (expiration > TimeSpan.Zero)
        {
            var timer = new DispatcherTimer { Interval = expiration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Close(view);
            };
            _autoCloseTimers[view] = timer;
            timer.Start();
        }
    }

    public void Close(Control view)
    {
        if (!_visible.Remove(view))
        {
            return;
        }

        if (_autoCloseTimers.Remove(view, out var autoCloseTimer))
        {
            autoCloseTimer.Stop();
        }

        view.Classes.Add("leaving");
        view.Classes.Remove("entered");
        Closed?.Invoke(view);

        // Removed after the exit, from a timer - never inside the routed event that may have closed it (CLAUDE.md).
        DispatcherTimer.RunOnce(() => _stack.Children.Remove(view), MotionTokens.GetRevealExit() > TimeSpan.Zero ? MotionTokens.GetRevealExit() : TimeSpan.FromMilliseconds(1));
    }
}
