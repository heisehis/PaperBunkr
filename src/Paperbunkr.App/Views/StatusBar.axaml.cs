using Avalonia.Controls;
using Avalonia.Threading;

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

        // Peek popover entrance (docs/superpowers/specs/2026-09-07-chrome-content-motion-polish-
        // design.md item 6) - PeekCard persists as one instance across opens (Popup doesn't
        // recreate its content), so "open" must be explicitly removed on detach or the second open
        // would start already-faded-in with no visible entrance.
        var peekCard = this.FindControl<Border>("PeekCard");
        if (peekCard is not null)
        {
            peekCard.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => peekCard.Classes.Add("open"));
            peekCard.DetachedFromVisualTree += (_, _) => peekCard.Classes.Remove("open");
        }
    }
}
