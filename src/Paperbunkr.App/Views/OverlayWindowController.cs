using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Paperbunkr.App.Views;

/// <summary>
/// Shared show/hide/track lifecycle for hosting a Book reader drawer/sheet's content in a real
/// owned <see cref="OverlayHostWindow"/> instead of a Popup - see that class's own doc comment for
/// why. Used identically by <see cref="ReaderListDrawer"/> and <see cref="ReaderSettingsSheet"/>'s
/// code-behind; both hand this controller the same Grid they'd otherwise have put directly inside
/// a Popup, and a way to look up their own OverlayReference (the host screen's real, content-
/// bearing root Grid, e.g. RootGrid) each time it's needed.
/// </summary>
internal sealed class OverlayWindowController
{
    private readonly Control _content;
    private readonly Func<Layoutable?> _getOverlayReference;
    private OverlayHostWindow? _window;
    private Layoutable? _trackedReference;

    public OverlayWindowController(Control content, Func<Layoutable?> getOverlayReference)
    {
        _content = content;
        _getOverlayReference = getOverlayReference;
    }

    public bool IsOpen => _window is not null;

    public void SetOpen(bool open)
    {
        if (open)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void Show()
    {
        if (_window is not null)
        {
            // Already open - just resync in case the reference/owner changed under us.
            SyncBounds();
            return;
        }

        var reference = _getOverlayReference();
        if (reference is null || TopLevel.GetTopLevel(reference) is not Window owner)
        {
            // OverlayReference isn't wired up or isn't attached to a real window yet - nothing to
            // anchor to. Matches the old Popup's own silent no-op in the same situation.
            return;
        }

        _trackedReference = reference;
        _trackedReference.LayoutUpdated += OnReferenceLayoutUpdated;
        owner.PositionChanged += OnOwnerPositionChanged;

        var window = new OverlayHostWindow { Content = _content };
        _window = window;
        SyncBounds();
        window.Show(owner);
    }

    private void Hide()
    {
        if (_trackedReference is not null)
        {
            _trackedReference.LayoutUpdated -= OnReferenceLayoutUpdated;
            if (TopLevel.GetTopLevel(_trackedReference) is Window owner)
            {
                owner.PositionChanged -= OnOwnerPositionChanged;
            }
            _trackedReference = null;
        }

        if (_window is not null)
        {
            // Detach the shared content before closing so Close() doesn't dispose/orphan it -
            // the same Grid gets reused (reparented back in) the next time this opens.
            _window.Content = null;
            _window.Close();
            _window = null;
        }
    }

    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs e) => SyncBounds();

    private void OnReferenceLayoutUpdated(object? sender, EventArgs e) => SyncBounds();

    private void SyncBounds()
    {
        if (_window is null || _trackedReference is null)
        {
            return;
        }

        // PointToScreen/Bounds are both live off the reference Grid's own current layout - same
        // "don't trust this control's own Bounds, use the host screen's real root Grid instead"
        // fix ReaderListDrawer/ReaderSettingsSheet's own OverlayReference already existed for.
        _window.Position = _trackedReference.PointToScreen(new Point(0, 0));
        _window.Width = Math.Max(1, _trackedReference.Bounds.Width);
        _window.Height = Math.Max(1, _trackedReference.Bounds.Height);
    }
}
