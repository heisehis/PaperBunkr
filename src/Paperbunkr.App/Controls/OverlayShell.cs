using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Shared chrome (scrim + centered card + corner close button) for the app's borderless overlay
/// popups, replacing 17 near-identical hand-copied <c>&lt;Border Background="#B0000000"&gt;</c>
/// blocks in <c>MainWindow.axaml</c> (docs/superpowers/specs/2026-09-06-feedback-notification-
/// system-design.md §2). Each overlay keeps its own <c>IsXOverlayOpen</c> bool + dedicated close
/// command - this control only centralizes the chrome around them.
///
/// Code-only <see cref="TemplatedControl"/> (the <c>BrandMark</c>/<c>SplitText</c> pattern -
/// template is an implicit <see cref="Avalonia.Controls.ControlTheme"/> in
/// <c>Styles/Overlays.axaml</c>), deriving from <see cref="ContentControl"/> rather than bare
/// <see cref="TemplatedControl"/> since it needs to host arbitrary child content.
///
/// <see cref="CloseCommand"/> (not a raw <see cref="IsOpen"/> flip) is what scrim-click invokes,
/// so any per-overlay unsaved-changes check the existing close command already runs (e.g.
/// <c>IssuePropertiesScreenViewModel.CancelCommand</c>) stays intact. Escape is deliberately NOT
/// handled here - <c>MainViewModel.Escape()</c> already centrally owns Escape for every overlay,
/// and its own doc comment records a real bug from two uncoordinated Escape handlers fighting
/// over precedence; this control must not reintroduce that.
///
/// <para>
/// <b>Motion</b> (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §5.1): opening and closing run the shared
/// <see cref="Reveal"/> lifecycle - the scrim fades and the card settles from 0.98 on the Reveal tokens (styles in
/// <c>Styles/Overlays.axaml</c>); a close ends interaction at once and collapses after the exit; a reopen mid-exit reverses.
/// </para>
///
/// <para>
/// <b>Focus</b> (2026-11-12 focus audit): an open shell is modal for the keyboard and the controller. It takes focus on open (the first
/// control in its content, never the ✕), Tab cycles inside it, an arrow key nothing inside used moves to the nearest control in the card and
/// otherwise stops there, and a key pressed while focus is outside every open shell (a dialog that swapped its content, a click on the scrim)
/// is redirected into the topmost one (<see cref="TryRedirectKey"/>). Before this, nothing put focus into a dialog, so the first arrow or Tab
/// started from the window's first control - the nav rail - and a Left that nothing used went to the rail as well.
/// </para>
/// </summary>
public class OverlayShell : ContentControl
{
    /// <summary>Open shells in the order they opened: the last one still open in a window is the one on top.</summary>
    private static readonly List<OverlayShell> OpenShells = new();

    /// <summary>Whether the most recent key or pointer press was a key. Set by MainWindow's tunnel handlers: a dialog opened from the
    /// keyboard shows its focus ring at once, one opened with the mouse takes focus quietly.</summary>
    public static bool LastInputWasKeyboard { get; set; }
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<OverlayShell, bool>(nameof(IsOpen));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<OverlayShell, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<bool> AllowDismissProperty =
        AvaloniaProperty.Register<OverlayShell, bool>(nameof(AllowDismiss), defaultValue: true);

    /// <summary>AutomationId for the corner close button, e.g. "MigrationOverlayCloseButton" -
    /// preserves each overlay's existing UI-automation contract (see
    /// Paperbunkr.App.UiTests/AppFixture.cs's ByAutomationId lookup) now that the button is
    /// rendered by this shared control instead of being hand-copied per overlay.</summary>
    public static readonly StyledProperty<string?> CloseButtonAutomationIdProperty =
        AvaloniaProperty.Register<OverlayShell, string?>(nameof(CloseButtonAutomationId));

    /// <summary>Alignment of the content card within the scrim - default Center/Center matches
    /// every overlay except QuickOpenOverlay, which stretches vertically for its results list.</summary>
    public static readonly StyledProperty<HorizontalAlignment> ContentHorizontalAlignmentProperty =
        AvaloniaProperty.Register<OverlayShell, HorizontalAlignment>(nameof(ContentHorizontalAlignment), defaultValue: HorizontalAlignment.Center);

    public static readonly StyledProperty<VerticalAlignment> ContentVerticalAlignmentProperty =
        AvaloniaProperty.Register<OverlayShell, VerticalAlignment>(nameof(ContentVerticalAlignment), defaultValue: VerticalAlignment.Center);

    /// <summary>Default true (every existing overlay had one). QuickOpenOverlay is the one
    /// exception - it had no corner close button before this control existed, closing only via
    /// Escape/selecting a result.</summary>
    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<OverlayShell, bool>(nameof(ShowCloseButton), defaultValue: true);

    private Border? _scrim;
    private bool _deferredContentRealized;
    /// <summary>Runs the collapse after the exit (forwards to <see cref="Reveal.ScheduleAfter"/>). A seam because the headless test
    /// dispatcher never fires timers.</summary>
    internal static Func<Action, TimeSpan, IDisposable> ScheduleAfter
    {
        get => Reveal.ScheduleAfter;
        set => Reveal.ScheduleAfter = value;
    }

    public OverlayShell()
    {
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Bubble);

        // IsOpenProperty.Changed only fires on a real transition, so a freshly constructed shell
        // (IsOpen defaults to false) never runs that handler - without this, it would keep
        // Avalonia's own Visual.IsVisible default of true, showing an unopened overlay.
        IsVisible = IsOpen;
        Classes.Set("open", IsOpen);
    }

    /// <summary>True between a close and the end of its exit animation: still drawn, no longer interactive.</summary>
    public bool IsClosing => Reveal.IsClosing(this);

    /// <summary>
    /// When true, the shell's <see cref="ContentControl.ContentTemplate"/> is not materialized
    /// until the overlay is opened for the first time (then cached - re-open is instant and any
    /// editor state is preserved, matching the old always-built behavior). This keeps ~20 heavy
    /// editor views (Issue/Bulk properties, Migration, …) out of <c>new MainWindow()</c>'s
    /// first-layout pass, which ran on the UI thread while the startup splash was still up.
    /// Set on the shells whose content is both heavy and not needed at launch; the
    /// <see cref="ContentControl.Content"/> is left unset in XAML and the view goes in
    /// <see cref="ContentControl.ContentTemplate"/> instead.
    /// </summary>
    public static readonly StyledProperty<bool> DeferContentProperty =
        AvaloniaProperty.Register<OverlayShell, bool>(nameof(DeferContent));

    public bool DeferContent
    {
        get => GetValue(DeferContentProperty);
        set => SetValue(DeferContentProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public bool AllowDismiss
    {
        get => GetValue(AllowDismissProperty);
        set => SetValue(AllowDismissProperty, value);
    }

    public string? CloseButtonAutomationId
    {
        get => GetValue(CloseButtonAutomationIdProperty);
        set => SetValue(CloseButtonAutomationIdProperty, value);
    }

    public HorizontalAlignment ContentHorizontalAlignment
    {
        get => GetValue(ContentHorizontalAlignmentProperty);
        set => SetValue(ContentHorizontalAlignmentProperty, value);
    }

    public VerticalAlignment ContentVerticalAlignment
    {
        get => GetValue(ContentVerticalAlignmentProperty);
        set => SetValue(ContentVerticalAlignmentProperty, value);
    }

    public bool ShowCloseButton
    {
        get => GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    static OverlayShell()
    {
        IsOpenProperty.Changed.AddClassHandler<OverlayShell>((shell, e) => shell.OnIsOpenChanged((bool)e.NewValue!));
    }

    private void OnIsOpenChanged(bool open)
    {
        ApplyReveal(open);
        OpenShells.Remove(this);
        if (!open)
        {
            return;
        }

        OpenShells.Add(this);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Dispatcher.UIThread.Post(() =>
        {
            if (IsOpen && !IsClosing && !ContainsFocus())
            {
                FocusInitial(LastInputWasKeyboard ? NavigationMethod.Tab : NavigationMethod.Unspecified);
            }
        }, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsOpen && !OpenShells.Contains(this))
        {
            OpenShells.Add(this);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        OpenShells.Remove(this);
    }

    /// <summary>The open shell on top in <paramref name="top"/> (the most recently opened one still open and showing), or null when no dialog is open there.</summary>
    public static OverlayShell? TopmostOpen(TopLevel? top)
    {
        if (top is null)
        {
            return null;
        }

        for (int i = OpenShells.Count - 1; i >= 0; i--)
        {
            if (OpenShells[i] is var shell && shell.IsLive(top))
            {
                return shell;
            }
        }

        return null;
    }

    private bool IsLive(TopLevel top) => IsOpen && !IsClosing && IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(this), top);

    /// <summary>Whether <paramref name="element"/> is inside this shell, counting a dropdown or menu opened from inside it (a popup's content is
    /// not a visual descendant of its anchor, so the walk steps from a popup host to the <see cref="Popup"/> it belongs to).</summary>
    public bool Contains(object? element) => IsWithin(this, element);

    /// <summary>Whether this shell holds keyboard focus (see <see cref="Contains"/>).</summary>
    public bool ContainsFocus() => Contains(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement());

    /// <summary><paramref name="element"/> is <paramref name="region"/> or inside it, crossing popup hosts to their anchors.</summary>
    public static bool IsWithin(Visual region, object? element)
    {
        var current = element as Visual;
        while (current is not null)
        {
            if (ReferenceEquals(current, region))
            {
                return true;
            }

            current = current is OverlayPopupHost or PopupRoot
                ? ((StyledElement)current).Parent as Visual ?? current.GetVisualParent()
                : current.GetVisualParent();
        }

        return false;
    }

    /// <summary>Whether <paramref name="element"/> is inside any open shell of <paramref name="top"/>.</summary>
    public static bool IsInsideAnyOpen(TopLevel top, object? element) => OpenShells.Any(s => s.IsLive(top) && s.Contains(element));

    /// <summary>
    /// The window calls this first for every key (tunnel). While a dialog is open and focus sits outside every open one - the dialog swapped its
    /// content and the focused control went with it, or focus stayed behind on the page - a navigation key puts focus into the topmost dialog
    /// instead of acting on the page or starting from the nav rail. Returns whether the key was used that way.
    /// </summary>
    public static bool TryRedirectKey(TopLevel top, Key key, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0
            || key is not (Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Enter or Key.Space)
            || TopmostOpen(top) is not { } shell
            || IsInsideAnyOpen(top, top.FocusManager?.GetFocusedElement()))
        {
            return false;
        }

        return shell.FocusInitial(NavigationMethod.Tab);
    }

    /// <summary>Puts focus on the first focusable control in the shell's content, in visual order (the shell's and the dialog card's own ✕ only
    /// when there is nothing else). Returns whether anything took focus.</summary>
    public bool FocusInitial(NavigationMethod method)
    {
        var candidates = this.GetVisualDescendants().OfType<Control>()
            .Where(c => c.Focusable && c.IsTabStop && c.IsEffectivelyEnabled && c.IsEffectivelyVisible && c.Bounds.Width > 0)
            .ToList();
        var target = candidates.FirstOrDefault(c => !IsChromeCloseButton(c)) ?? candidates.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        target.Focus(method);
        target.BringIntoView();
        return true;
    }

    private static bool IsChromeCloseButton(Control control) => control is Button && control.TemplatedParent is OverlayShell or DialogCard;

    /// <summary>
    /// An arrow key nothing inside the dialog used: move to the nearest control that way within the shell, and either way keep the key here, so it
    /// never reaches the page behind or the window's "Left goes to the nav rail" fallback.
    /// </summary>
    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !IsOpen || IsClosing || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
        {
            return;
        }

        if (e.Source is Visual source && this.IsVisualAncestorOf(source))
        {
            Paperbunkr.App.Views.FocusReclaimer.TryMoveDirectionally(this, e);
        }

        e.Handled = true;
    }

    private void ApplyReveal(bool open) =>
        Reveal.Apply(this, open, beforeOpenClass: () =>
        {
            // First open of a DeferContent shell: hand the ContentPresenter a non-null Content so
            // it materializes ContentTemplate now. Content is the shell's own DataContext (the
            // MainViewModel), so the {Binding SomeVm} on the template's root view resolves exactly
            // as it did when the view was an inline child. Cached after the first realize.
            if (DeferContent && !_deferredContentRealized)
            {
                _deferredContentRealized = true;
                Content ??= DataContext;
            }
        });

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_scrim is not null)
        {
            _scrim.PointerPressed -= OnScrimPointerPressed;
        }

        _scrim = e.NameScope.Find<Border>("PART_Scrim");
        if (_scrim is not null)
        {
            _scrim.PointerPressed += OnScrimPointerPressed;
        }
    }

    /// <summary>
    /// Only a click that actually originates on the scrim itself dismisses - not any unhandled click
    /// that bubbles up to it from somewhere inside the card's own content. <c>PART_Scrim</c> is the
    /// scrim/card template's outer <see cref="Border"/> (Styles/Overlays.axaml) and the card content
    /// is laid out as ITS visual child, so <see cref="RoutedEventArgs.Source"/> - the element the
    /// event actually started on, which survives the bubble - is the scrim only when nothing inside
    /// the card was hit first (i.e. the click landed in the backdrop margin around the card).
    ///
    /// Before this check, ANY unhandled click anywhere inside a card (a plain, non-interactive
    /// `Border`/`TextBlock` with no click handling of its own - a `Button`/`TextBox`/etc. already
    /// marks its own press handled and never reached here) bubbled all the way to this handler and
    /// closed the whole overlay. Confirmed live (docs/superpowers/specs/2026-09-13-cluster-scraper-
    /// ui-redesign-design.md's redesigned scraper dialog): two purely decorative label pills with no
    /// command at all closed the dialog on click. Latent in every overlay using this shell - just
    /// never exposed before, since no existing overlay had prominent, genuinely non-interactive
    /// content sitting in an area a user would naturally click.
    /// </summary>
    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ShouldCloseFromScrimClick(e.Source, _scrim))
        {
            RequestCloseFromScrim();
        }
    }

    /// <summary>Pure policy check, factored out for the same reason <see cref="RequestCloseFromScrim"/>
    /// is: testable without a real Avalonia <see cref="PointerPressedEventArgs"/>/template pass.</summary>
    internal static bool ShouldCloseFromScrimClick(object? eventSource, Border? scrim) =>
        scrim is not null && ReferenceEquals(eventSource, scrim);

    /// <summary>The scrim-click policy, factored out of the real pointer-event handler so tests can
    /// exercise it directly without simulating an Avalonia <see cref="PointerPressedEventArgs"/>.</summary>
    internal void RequestCloseFromScrim()
    {
        if (AllowDismiss && CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
        }
    }
}
