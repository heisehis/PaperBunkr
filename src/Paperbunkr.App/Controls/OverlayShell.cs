using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

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
/// </summary>
public class OverlayShell : ContentControl
{
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

    public OverlayShell()
    {
        // IsOpenProperty.Changed only fires on a real transition, so a freshly constructed shell
        // (IsOpen defaults to false) never runs that handler - without this, it would keep
        // Avalonia's own Visual.IsVisible default of true, showing an unopened overlay.
        IsVisible = IsOpen;
    }

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
        IsOpenProperty.Changed.AddClassHandler<OverlayShell>((shell, e) =>
        {
            bool open = (bool)e.NewValue!;
            shell.IsVisible = open;

            // First open of a DeferContent shell: hand the ContentPresenter a non-null Content so
            // it materializes ContentTemplate now. Content is the shell's own DataContext (the
            // MainViewModel), so the {Binding SomeVm} on the template's root view resolves exactly
            // as it did when the view was an inline child. Cached after the first realize.
            if (open && shell.DeferContent && !shell._deferredContentRealized)
            {
                shell._deferredContentRealized = true;
                shell.Content ??= shell.DataContext;
            }
        });
    }

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

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => RequestCloseFromScrim();

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
