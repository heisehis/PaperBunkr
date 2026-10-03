using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class MainWindow : Window
{
    // App-owned replacement for WindowNotificationManager (docs/superpowers/specs/2026-09-07-
    // chrome-content-motion-polish-design.md item 5) - same PbToastView content/positioning/
    // persistent-toast tracking below, only the hosting/animation layer changed.
    private ToastPresenter? _toastPresenter;

    // Close(object content) needs the exact content instance passed to Show(object content, ...) -
    // this maps a live ToastRequest back to the view instance actually shown for it, for any
    // actionable toast (Actions is not null) that closes itself explicitly rather than
    // auto-dismissing (docs/superpowers/specs/2026-09-06-feedback-notification-system-design.md
    // §5 - generalized from the old update-ready-toast-only UpdateReadyToastViewModel/View pair).
    private readonly Dictionary<ToastRequest, Control> _persistentToasts = new();

    /// <summary>
    /// Minimize-to-tray (docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-
    /// design.md §4) - always constructed (cheap, stays invisible) rather than lazily, so there's no
    /// "first minimize after enabling the setting" special case to get wrong.
    /// </summary>
    private readonly TrayIconService _trayIconService = new();

    /// <summary>
    /// Set only by <see cref="ExitFromTray"/> - a second guard alongside
    /// <see cref="WindowCloseReason.WindowClosing"/> below (belt and suspenders: if a future
    /// Avalonia version ever reports that reason differently for a programmatic
    /// <c>IClassicDesktopStyleApplicationLifetime.Shutdown()</c>, this still lets the real exit
    /// through instead of trapping the app in an unclosable tray loop).
    /// </summary>
    private bool _allowRealClose;

    /// <summary>
    /// Real bug, found via manual testing: collapsing the nav rail immediately on PointerExited
    /// fought with its own 150ms width-expand animation - hovering "blank" space (no button
    /// underneath to anchor the hit-test, e.g. the empty Grid.Row="1" gap between the two button
    /// groups) made the rail visibly flicker between collapsed/expanded, because the rail's own
    /// width change while the animation is running reflows content under a stationary cursor and
    /// can cause Avalonia's hit-testing to spuriously re-fire PointerExited/PointerEntered mid-
    /// animation. Debouncing the collapse (only actually collapse if the pointer stays away for
    /// 200ms - longer than the 150ms expand animation) absorbs that jitter regardless of its exact
    /// per-frame cause, rather than trusting every raw pointer event.
    /// </summary>
    private readonly DispatcherTimer _railCollapseTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>Restores keyboard focus to the contextual sidebar when a row it held is removed by a list reload or delete (the list's own
    /// handler detaches the focused row before ours runs, so focus is simply gone). Only fires when focus was last in the sidebar -
    /// see docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md.</summary>
    private readonly FocusReclaimer _sidebarFocus;

    private bool _sidebarHadFocus;
    private int _sidebarLastRowIndex;

    /// <summary>The application's input service, which every key, mouse-button and wheel event in this window is forwarded to.</summary>
    private readonly IInputService _input;

    private readonly InputHost _inputHost;
    private readonly IDisposable _globalInput;
    private readonly AppGamepadHost _gamepadHost;

    public MainWindow()
    {
        InitializeComponent();

        _sidebarFocus = new FocusReclaimer(ContextualSidebar, () => DataContext is MainViewModel { ShowContextualSidebar: true }, FocusSidebarRow);
        AddHandler(GotFocusEvent, OnAnyGotFocus);
        foreach (var name in new[]
                 {
                     "LibraryCollections", "LibraryContentTypes", "BuiltInSmartLists", "CustomSmartLists", "MaintenanceSmartLists",
                     "SeriesSmartLists", "NovelSmartLists", "PluginSmartLists", "ContinuityList", "StoryEvents",
                 })
        {
            if (this.FindControl<ItemsControl>(name) is { } list)
            {
                WatchSidebarList(list);
            }
        }

        // docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md - registered
        // once, here, before any navigation can occur. SharedElementTransitionService.Shared is the
        // single app-wide instance every SharedElement-attached participant registers against.
        SharedElementTransitionService.Shared.RegisterOverlayHost(TransitionOverlay);

        DataContextChanged += OnDataContextChanged;
        _railCollapseTimer.Tick += OnRailCollapseTimerTick;

        _trayIconService.RestoreRequested += RestoreFromTray;
        _trayIconService.ExitRequested += ExitFromTray;
        PropertyChanged += OnWindowPropertyChanged;
        Closing += OnWindowClosing;

        // All keyboard, mouse-button and wheel input goes through the input service (docs/superpowers/specs/2026-10-03-input-service-design.md): three Tunnel handlers at the
        // root forward to it, so a shortcut fires whatever has focus, and the shell-wide actions it resolves (Escape, back/forward, quick open, ...) are handled in
        // OnGlobalInputAction below. This replaced a hardcoded key chain here and a separate horizontal-swipe wheel handler.
        _input = InputServiceLocator.Current;
        _inputHost = InputHost.Attach(this, _input);
        _globalInput = _input.Register(InputScope.Global, OnGlobalInputAction);

        // One controller poller for the whole app, running while the setting is on and this window is active (it used to belong to the comic reader alone).
        _gamepadHost = new AppGamepadHost(this, _input, new XInputSource());
    }

    /// <summary>
    /// The shell-wide actions, mapped onto the same <see cref="MainViewModel"/> commands the old hardcoded key chain called (Escape/Back: docs/superpowers/specs/2026-08-30-app-shell-
    /// navigation-history-design.md; Ctrl+, / Ctrl+Tab: 2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md; Undo/Redo: docs/ce-feature-inventory.md §A; Ctrl+Q:
    /// 2026-09-12-grid-typeahead-rangeselect-quit-design.md). Whether a key reaches here while a text box has focus is the service's call (an action's
    /// <c>FiresInTextInput</c>), not this method's.
    /// </summary>
    private void OnGlobalInputAction(InputActionEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        switch (e.Action.Id)
        {
            case InputActionIds.CloseCurrentView:
                viewModel.EscapeCommand.Execute(null);
                e.Handled = true;
                break;

            // The keyboard key always counts (it did before); a mouse thumb button or a swipe only when there is somewhere to go, so it never swallows an event for nothing.
            case InputActionIds.NavigateBack when e.Device == InputDevice.Keyboard || viewModel.CanNavigateBack:
                viewModel.NavigateBackCommand.Execute(null);
                e.Handled = true;
                break;

            case InputActionIds.NavigateForward when e.Device == InputDevice.Keyboard || viewModel.CanNavigateForward:
                viewModel.NavigateForwardCommand.Execute(null);
                e.Handled = true;
                break;

            // Quick open (docs/superpowers/specs/2026-09-03-quick-open-command-palette-design.md) is inert inside the readers, which keep their own dense keymap.
            case InputActionIds.OpenQuickOpen when viewModel.CurrentScreen is not ("reader" or "bookReader" or "pdfReader"):
                viewModel.OpenQuickOpenCommand.Execute(null);
                e.Handled = true;
                break;

            case InputActionIds.OpenSettings:
                viewModel.GoPreferencesCommand.Execute(null);
                e.Handled = true;
                break;

            case InputActionIds.CycleScreenForward:
                viewModel.CycleScreenForwardCommand.Execute(null);
                e.Handled = true;
                break;

            case InputActionIds.CycleScreenBackward:
                viewModel.CycleScreenBackCommand.Execute(null);
                e.Handled = true;
                break;

            // Metadata-edit Undo/Redo - added 2026-09-05 as this app's first keyboard access to it, replacing the rail's own Undo/Redo buttons.
            case InputActionIds.Undo:
                viewModel.UndoCommand.Execute(null);
                e.Handled = true;
                break;

            case InputActionIds.Redo:
                viewModel.RedoCommand.Execute(null);
                e.Handled = true;
                break;

            // Close() already flows through OnWindowClosing's tray-aware logic, matching CE's own Exit semantics exactly.
            case InputActionIds.Quit:
                Close();
                e.Handled = true;
                break;

            case InputActionIds.ToggleSidebar:
                viewModel.ToggleNavRailPinCommand.Execute(null);
                e.Handled = true;
                break;

            // The "every screen" actions (docs/superpowers/specs/2026-10-03-input-service-design.md §14) send the focused control the key it already understands; a screen with its own
            // meaning for one (tabs, say) registers in its own scope, which is asked first, so what reaches here is the fallback.
            case InputActionIds.TabNext:
                viewModel.CycleScreenForwardCommand.Execute(null);
                e.Handled = true;
                break;

            case InputActionIds.TabPrevious:
                viewModel.CycleScreenBackCommand.Execute(null);
                e.Handled = true;
                break;

            default:
                e.Handled = UiNavigation.TryHandle(this, e);
                break;
        }
    }
    protected override void OnClosed(EventArgs e)
    {
        _gamepadHost?.Dispose();
        _globalInput?.Dispose();
        _inputHost?.Dispose();
        base.OnClosed(e);
    }

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && IsMinimizeToTrayEnabled())
        {
            MinimizeToTray();
        }
    }

    /// <summary>
    /// Only the plain "user clicked this window's own close button" reason gets redirected to the
    /// tray. <see cref="WindowCloseReason.ApplicationShutdown"/>/<see cref="WindowCloseReason.OSShutdown"/>
    /// (session logoff, <c>desktop.Shutdown()</c>) always pass through - redirecting an OS-initiated
    /// shutdown to the tray would hang the OS waiting for a window that's never going to close.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowRealClose || e.CloseReason != WindowCloseReason.WindowClosing)
        {
            return;
        }

        if (IsMinimizeToTrayEnabled())
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        // Confirm-before-close (on-screen feedback) - only reached once minimize-to-tray has already
        // had first refusal above, so this only ever fires for a close that would actually quit the
        // app; confirming an action that just hides to the tray instead would be pure friction.
        if (IsConfirmBeforeCloseEnabled())
        {
            e.Cancel = true;
            _ = ConfirmCloseAndExitAsync();
        }
    }

    private static bool IsConfirmBeforeCloseEnabled()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings().ConfirmBeforeClose;
    }

    /// <summary>Cancelled close above re-issues itself via <see cref="_allowRealClose"/> + <see
    /// cref="Close()"/> once confirmed - same "cancel now, real-close later" shape <see
    /// cref="ExitFromTray"/> already uses for the tray's own Exit path.</summary>
    private async Task ConfirmCloseAndExitAsync()
    {
        bool confirmed = DataContext is MainViewModel vm
            ? await vm.Dialogs.ConfirmAsync("Are you sure you wanna go? (｡•́︿•̀｡)", title: "Confirm Close", confirmLabel: "Close", cancelLabel: "Cancel")
            : true;

        if (confirmed)
        {
            _allowRealClose = true;
            Close();
        }
    }

    private void MinimizeToTray()
    {
        _trayIconService.IsVisible = true;

        // The first-time notice must be shown *before* hiding - a toast raised against an
        // already-hidden window would never actually render. Subsequent minimizes hide immediately,
        // same as CE's own balloon-only-once behavior.
        if (ShowFirstTimeTrayNoticeIfNeeded())
        {
            var hideAfterNotice = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            hideAfterNotice.Tick += (_, _) =>
            {
                hideAfterNotice.Stop();
                Hide();
            };
            hideAfterNotice.Start();
        }
        else
        {
            Hide();
        }
    }

    private void RestoreFromTray()
    {
        _trayIconService.IsVisible = false;
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void ExitFromTray()
    {
        _allowRealClose = true;
        _trayIconService.IsVisible = false;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Close();
        }
    }

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - hover-expand is transient UI state, not worth a full binding round-trip; a plain code-behind handler matches this file's existing event-handler pattern (OnWindowClosing etc).
    /// Gated by AppSettings.NavRailHoverExpandEnabled (Preferences -> Appearance -> Navigation, added
    /// 2026-09-05) - read fresh from the DB on every hover rather than proxied through
    /// PreferencesScreenViewModel, so it's correct even if Preferences has never been opened this
    /// session (that VM's own copy is lazy-loaded). Pinning (NavRailPinned) is a separate mechanism,
    /// unaffected by this toggle.</summary>
    private void RailPointerEntered(object? sender, PointerEventArgs e)
    {
        using var context = PaperbunkrDb.CreateContext();
        if (!context.GetOrCreateAppSettings().NavRailHoverExpandEnabled)
        {
            return;
        }

        _railCollapseTimer.Stop();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsNavRailHoverExpanded = true;
        }
    }

    /// <summary>Doesn't collapse immediately - see <see cref="_railCollapseTimer"/>'s doc comment.</summary>
    private void RailPointerExited(object? sender, PointerEventArgs e)
    {
        _railCollapseTimer.Stop();
        _railCollapseTimer.Start();
    }

    /// <summary>
    /// Up/Down/Home/End movement within the contextual sidebar's item rows (docs/superpowers/specs/
    /// 2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md) - a real gap the P5 baseline and
    /// the sibling keyboard-operability spec both left alone (Tab already reaches every row, but
    /// doesn't move *between* them the way arrow keys already do on the card grids). One handler on
    /// the sidebar's outer Border, not per-screen, since <see cref="GridKeyboardNavigation.Navigate{T}"/>'s
    /// pure core doesn't care which of the four <c>IsLibrary</c>/<c>IsSmart</c>/<c>IsReading</c>/
    /// <c>IsEvents</c> blocks is currently visible - only the live button collection below does, by
    /// construction (a hidden block's buttons report <see cref="Layoutable.IsEffectivelyVisible"/>
    /// false, so they're never in the collected list).
    ///
    /// Left/Right are deliberately NOT wired here, unlike the card grids: <see cref="GridKeyboardNavigation.Navigate{T}"/>'s
    /// Left/Right case is plain previous/next-in-list index math (not spatial column math the way
    /// Up/Down's row search is) - for a single-column list that's identical to what Up/Down already
    /// do, which would make Left/Right silently move focus too. That's real navigation behavior a
    /// linear sidebar list shouldn't have (found by reading <c>Navigate</c>'s actual implementation
    /// while building this, not assumed from the card-grid case where Left/Right and Up/Down clearly
    /// differ).
    ///
    /// e.Source (the control that actually raised the key press, preserved through bubbling) rather
    /// than <paramref name="sender"/> (always this Border) is checked against
    /// <c>Classes.Contains("sideItemButton")</c> - this also naturally excludes Library's inline
    /// collection-rename TextBox: focus there means e.Source is a TextBox, not a sideItemButton, so
    /// the handler no-ops instead of yanking focus out of an active edit.
    ///
    /// Real bug found 2026-09-02 via KBDIAG2 diagnostic logging (startup.log): unlike the card-grid
    /// call sites of <see cref="GridKeyboardNavigation.Navigate{T}"/> - where every container is a
    /// direct child of the same <c>ItemsControl</c> panel, so raw <see cref="Control.Bounds"/>
    /// (which Avalonia defines relative to a control's own immediate parent, not any shared
    /// ancestor) is already comparable across items - the sidebar's Library/Smart/Reading/Events
    /// sections each nest their buttons inside their own StackPanel/Grid. Collecting raw
    /// <c>b.Bounds</c> across those different immediate parents meant several unrelated buttons in
    /// different sections reported the identical local bounds <c>(0, 0, 211, 32)</c> (confirmed in
    /// the log), so <c>Navigate</c>'s row/column spatial math compared coordinates from incompatible
    /// spaces and picked the wrong target (or the current one again). Fixed by translating each
    /// button's origin into the shared <paramref name="sender"/> (<c>sidebarBorder</c>) coordinate
    /// space before building the <see cref="GridKeyboardNavigation.GridItem{T}"/> list.
    /// </summary>
    private void OnSidebarKeyDown(object? sender, KeyEventArgs e)
    {
        GridNavigationDirection? direction = e.Key switch
        {
            Key.Up => GridNavigationDirection.Up,
            Key.Down => GridNavigationDirection.Down,
            Key.Home => GridNavigationDirection.Home,
            Key.End => GridNavigationDirection.End,
            _ => null,
        };

        if (direction is null || sender is not Border sidebarBorder ||
            e.Source is not Button { Classes: var classes } focusedButton || !classes.Contains("sideItemButton"))
        {
            return;
        }

        // The sidebar now stays in the visual tree at Width=0 when collapsed rather than going
        // IsVisible=false (docs/superpowers/specs/2026-09-07-chrome-content-motion-polish-design.md
        // item 3, needed for the Width transition to animate at all) - IsEffectivelyVisible below
        // only checks the IsVisible flag chain, not actual width/clipping, so a button that had
        // focus right before the sidebar collapsed could otherwise still drive this handler. Found
        // via a self-audit after a user flagged real bugs in this same session's other new code.
        if (sidebarBorder.Bounds.Width <= 0)
        {
            return;
        }

        var rows = sidebarBorder.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("sideItemButton") && b.IsEffectivelyVisible && b.IsEffectivelyEnabled)
            .Select(b => new GridKeyboardNavigation.GridItem<Button>(b, BoundsRelativeTo(b, sidebarBorder)))
            .ToList();

        if (rows.Count == 0)
        {
            return;
        }

        GridKeyboardNavigation.Navigate(rows, focusedButton, direction.Value).Focus();
        e.Handled = true;
    }

    private System.Collections.Generic.List<Button> SidebarRows() =>
        ContextualSidebar.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("sideItemButton") && b.IsEffectivelyVisible && b.IsEffectivelyEnabled && b.Focusable)
            .ToList();

    private void OnAnyGotFocus(object? sender, RoutedEventArgs e)
    {
        var inSidebar = e.Source is Visual v && v.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, ContextualSidebar));
        _sidebarHadFocus = inSidebar;
        if (inSidebar && e.Source is Button button && SidebarRows().IndexOf(button) is var index and >= 0)
        {
            _sidebarLastRowIndex = index;
        }
    }

    private void FocusSidebarRow()
    {
        var rows = SidebarRows();
        if (rows.Count > 0)
        {
            rows[Math.Min(_sidebarLastRowIndex, rows.Count - 1)].Focus(NavigationMethod.Directional);
        }
    }

    private void WatchSidebarList(ItemsControl list)
    {
        System.Collections.Specialized.INotifyCollectionChanged? current = null;
        void OnChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_sidebarHadFocus)
            {
                _sidebarFocus.ReclaimIfFocusLost();
            }
        }

        void Attach()
        {
            if (current is not null)
            {
                current.CollectionChanged -= OnChanged;
            }

            current = list.ItemsSource as System.Collections.Specialized.INotifyCollectionChanged;
            if (current is not null)
            {
                current.CollectionChanged += OnChanged;
            }
        }

        list.PropertyChanged += (_, e) =>
        {
            if (e.Property == ItemsControl.ItemsSourceProperty)
            {
                Attach();
                OnChanged(list, null!);
            }
        };
        Attach();
    }

    private static Rect BoundsRelativeTo(Control control, Visual ancestor)
    {
        var origin = control.TranslatePoint(new Point(0, 0), ancestor) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    private void OnRailCollapseTimerTick(object? sender, EventArgs e)
    {
        _railCollapseTimer.Stop();
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsNavRailHoverExpanded = false;
        }
    }

    private static bool IsMinimizeToTrayEnabled()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings().MinimizeToTray;
    }

    /// <summary>
    /// Avalonia's <see cref="TrayIcon"/> has no balloon-tip API (checked against
    /// Avalonia.Controls.xml - only Icon/ToolTipText/Menu/IsVisible/Clicked), so this app-level
    /// toast stands in for CE's balloon, shown exactly once ever (the persisted flag itself is the
    /// "don't show again" - there's no separate checkbox to manage, since there's nothing to
    /// re-enable once it's been seen). Returns whether it was actually shown, so
    /// <see cref="MinimizeToTray"/> knows whether to delay the hide.
    /// </summary>
    private bool ShowFirstTimeTrayNoticeIfNeeded()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        if (settings.MinimizeToTrayNoticeShown)
        {
            return false;
        }

        settings.MinimizeToTrayNoticeShown = true;
        context.SaveChanges();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowMinimizeToTrayNotice();
        }

        return true;
    }

    /// <summary>
    /// Toast host (P6 follow-up, docs/paperbunkr-todo.md) - <see cref="WindowNotificationManager"/> needs
    /// a real attached <c>Window</c>, which doesn't exist yet when <see cref="MainViewModel"/> is
    /// constructed (App.axaml.cs builds the ViewModel before the Window). Wired here once
    /// <c>DataContext</c> is actually set, same pattern <see cref="ReaderScreen"/> already uses for
    /// its own post-construction hookup.
    /// </summary>
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // Shared toast template (docs/superpowers/specs/2026-09-06-feedback-notification-system-
        // design.md §5) - PbToastView renders its own severity icon/accent color, so NotificationType
        // is passed as a uniform Information here rather than mapped from Severity, to avoid a
        // second built-in icon from NotificationCard's own chrome doubling up with it (verify this
        // empirically in a manual GUI pass - Avalonia's NotificationCard behavior with custom
        // content wasn't confirmed via static inspection).
        //
        // A toast with Actions (e.g. update-ready's Restart/Later/What's New) is shown with
        // expiration TimeSpan.Zero (Avalonia treats zero/negative as "don't auto-close") and
        // tracked in _persistentToasts so ToastCloseRequested can close it by identity once one of
        // its actions runs - generalized from the old update-ready-only close mechanism so any
        // future actionable toast gets the same behavior for free.
        if (_toastPresenter is null)
        {
            _toastPresenter = new ToastPresenter(this.FindControl<Panel>("ToastStack")!);
            _toastPresenter.Closed += view =>
            {
                // Prune _persistentToasts on every close path, not just the explicit
                // ToastCloseRequested one - a MaxVisible eviction otherwise leaves this entry (and
                // everything it roots) referenced forever, a real leak found via a user memory-usage
                // report on the very first pass of this feature.
                foreach (var entry in _persistentToasts)
                {
                    if (ReferenceEquals(entry.Value, view))
                    {
                        _persistentToasts.Remove(entry.Key);
                        break;
                    }
                }
            };
        }

        viewModel.ToastRequested += request =>
        {
            var view = new PbToastView { DataContext = request };
            bool persistent = request.Actions is not null;
            if (persistent)
            {
                _persistentToasts[request] = view;
            }

            // 5s approximates WindowNotificationManager's own prior default for a non-actionable
            // toast (its exact internal value wasn't recoverable via static inspection - not
            // exposed as a documented constant) - persistent (actionable) toasts still pass Zero,
            // meaning "don't auto-close," same as before.
            _toastPresenter.Show(view, expiration: persistent ? System.TimeSpan.Zero : System.TimeSpan.FromSeconds(5));
        };
        viewModel.ToastCloseRequested += request =>
        {
            if (_persistentToasts.Remove(request, out var view))
            {
                _toastPresenter.Close(view);
            }
        };
    }
}
