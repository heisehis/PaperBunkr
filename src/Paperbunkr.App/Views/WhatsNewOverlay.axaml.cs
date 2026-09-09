using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// The "What's New" overlay (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-
/// design.md). Hosted by <c>MainWindow.axaml</c>'s <c>OverlayShell</c>, bound to
/// <c>WhatsNewOverlayViewModel</c>; changelog rendering reuses the About accordion's converter.
/// </summary>
public partial class WhatsNewOverlay : UserControl
{
    public WhatsNewOverlay()
    {
        InitializeComponent();
    }
}
