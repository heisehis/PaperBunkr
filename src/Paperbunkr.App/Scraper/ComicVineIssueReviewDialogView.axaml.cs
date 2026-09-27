using Avalonia.Controls;
using Avalonia.Input;

namespace Paperbunkr.App.Scraper;

public partial class ComicVineIssueReviewDialogView : UserControl
{
    public ComicVineIssueReviewDialogView() => InitializeComponent();

    /// <summary>Ctrl-held Skip (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-plan.md
    /// Step 16) - see <c>ComicVineMatchReviewDialogView</c>'s own Skip handler for the full rationale
    /// (same pattern, mirrored here).</summary>
    private void OnSkipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ComicVineIssueReviewDialogViewModel vm)
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

    private void OnSkipKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Space or Key.Enter) && DataContext is ComicVineIssueReviewDialogViewModel vm)
        {
            vm.SkipCommand.Execute(null);
            e.Handled = true;
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
