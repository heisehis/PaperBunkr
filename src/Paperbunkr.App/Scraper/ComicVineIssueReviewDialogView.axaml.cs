using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Paperbunkr.App.Scraper;

public partial class ComicVineIssueReviewDialogView : UserControl
{
    public ComicVineIssueReviewDialogView()
    {
        InitializeComponent();

        // Tunnelled: a Button swallows the left press and Enter/Space before a XAML handler on it runs (see ComicVineMatchReviewDialogView).
        SkipButton.AddHandler(PointerPressedEvent, OnSkipPointerPressed, RoutingStrategies.Tunnel);
        SkipButton.AddHandler(KeyDownEvent, OnSkipKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Ctrl-held Skip (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-plan.md
    /// Step 16) - see <c>ComicVineMatchReviewDialogView</c>'s own Skip handler for the full rationale
    /// (same pattern, mirrored here).</summary>
    private void OnSkipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ComicVineIssueReviewDialogViewModel vm)
        {
            return;
        }

        if (!e.GetCurrentPoint(SkipButton).Properties.IsLeftButtonPressed)
        {
            return;
        }

        bool permanently = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        e.Handled = true;

        // Skipping closes the dialog: deferred so it doesn't happen mid-route (CLAUDE.md, routed-event detach).
        Dispatcher.UIThread.Post(() =>
        {
            if (permanently)
            {
                vm.SkipPermanentlyCommand.Execute(null);
            }
            else
            {
                vm.SkipCommand.Execute(null);
            }
        });
    }

    private void OnSkipKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Space or Key.Enter) && DataContext is ComicVineIssueReviewDialogViewModel vm)
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() => vm.SkipCommand.Execute(null));
        }
    }

    private void OnSkipPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is ComicVineIssueReviewDialogViewModel vm)
        {
            vm.IsCtrlHeldOverSkip = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        }
    }

    private void OnSkipPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is ComicVineIssueReviewDialogViewModel vm)
        {
            vm.IsCtrlHeldOverSkip = false;
        }
    }
}
