using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// The persistent bottom status bar + tier-1 activity peek (docs/superpowers/specs/2026-09-03-
/// activity-center-design.md). DataContext is <c>MainViewModel</c> (inherited from the window);
/// binds its <c>StatusBar</c> / <c>ActivityCenter</c> sub-view-models.
/// </summary>
public partial class StatusBar : UserControl
{
    public StatusBar()
    {
        InitializeComponent();
    }
}
