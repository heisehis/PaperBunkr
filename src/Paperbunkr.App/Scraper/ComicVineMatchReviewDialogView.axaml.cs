using Avalonia.Controls;
using Avalonia.Input;

namespace Paperbunkr.App.Scraper;

public partial class ComicVineMatchReviewDialogView : UserControl
{
    public ComicVineMatchReviewDialogView() => InitializeComponent();

    /// <summary>Ctrl-held Skip (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-plan.md
    /// Step 16) - PointerPressed instead of a bound Command, same pattern as LibraryScreen's own
    /// ctrl/shift-click multi-select handlers, so the click's own modifier state picks plain vs
    /// permanent skip.</summary>
    private void OnSkipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ComicVineMatchReviewDialogViewModel vm)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.SkipPermanentlyCommand.Execute(null);
        }
        else
        {
            vm.SkipCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>Keyboard fallback (Tab, then Space/Enter) - CE's own Ctrl-click affordance is mouse-
    /// only, so this always performs a plain skip.</summary>
    private void OnSkipKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Space or Key.Enter) && DataContext is ComicVineMatchReviewDialogViewModel vm)
        {
            vm.SkipCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Live hover-while-Ctrl-held tracking for the button's own label (CE's visual affordance,
    /// verified) - reflects the modifier at each pointer move rather than needing a separate
    /// window-level key listener, since routed key events follow focus, not pointer position.</summary>
    private void OnSkipPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is ComicVineMatchReviewDialogViewModel vm)
        {
            vm.IsCtrlHeldOverSkip = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        }
    }

    private void OnSkipPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is ComicVineMatchReviewDialogViewModel vm)
        {
            vm.IsCtrlHeldOverSkip = false;
        }
    }
}
