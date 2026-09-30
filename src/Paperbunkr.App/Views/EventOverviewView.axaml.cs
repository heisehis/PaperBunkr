using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// An event's Overview. The code-behind only answers <see cref="EventPageViewModel.ScrollToPanelRequested"/>: a "Needs attention" item
/// expands Issue suggestions or Related events and this brings that panel into view (after the expand has laid out).
/// </summary>
public partial class EventOverviewView : UserControl
{
    private EventPageViewModel? _page;

    public EventOverviewView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_page is not null)
            {
                _page.ScrollToPanelRequested -= OnScrollToPanel;
            }

            _page = DataContext as EventPageViewModel;
            if (_page is not null)
            {
                _page.ScrollToPanelRequested += OnScrollToPanel;
            }
        };
    }

    private void OnScrollToPanel(string panel)
    {
        Control target = string.Equals(panel, "related", StringComparison.Ordinal) ? RelatedPanel : IssuesPanel;
        Dispatcher.UIThread.Post(() => target.BringIntoView(), DispatcherPriority.Background);
    }
}
