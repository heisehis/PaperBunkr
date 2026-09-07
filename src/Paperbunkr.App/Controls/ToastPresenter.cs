using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Paperbunkr.App.Controls;

/// <summary>
/// App-owned toast host logic (docs/superpowers/specs/2026-09-07-chrome-content-motion-polish-
/// design.md item 5), replacing Avalonia's stock <c>WindowNotificationManager</c> so entrance/exit
/// is <c>PbMotion*</c>-token-wired and Reduced-Motion-respecting - the stock control's own animation
/// was confirmed entirely outside app control and untouched by <c>SkinService.ApplyReducedMotion</c>.
///
/// Deliberately a plain class operating on an existing <see cref="AnimatedStackPanel"/> (declared
/// in <c>MainWindow.axaml</c> as <c>ToastStack</c>), not a custom <see cref="Control"/> subclass of
/// its own - the call site already hands over an already-built content <see cref="Control"/> (a
/// <c>PbToastView</c> per call site, same as the old <c>_notificationManager.Show(view, ...)</c>
/// shape), so there's no templating/data-binding layer this needs to own.
///
/// Reuses the same "entranceReady"/"entered" class pair Library's staggered entrance uses
/// (Styles/Primitives.axaml) for each toast's own show/hide - entrance and exit share one duration
/// (PbMotionStandard) rather than the motion skill's "exit ~70%" convention, an accepted v1
/// simplification since building a second timed state purely for a faster exit wasn't worth a third
/// style variant.
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

        view.Classes.Add("entranceReady");
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

        view.Classes.Remove("entered");
        Closed?.Invoke(view);

        var removeTimer = new DispatcherTimer { Interval = MotionTokens.GetStandard() };
        removeTimer.Tick += (_, _) =>
        {
            removeTimer.Stop();
            _stack.Children.Remove(view);
        };
        removeTimer.Start();
    }
}
