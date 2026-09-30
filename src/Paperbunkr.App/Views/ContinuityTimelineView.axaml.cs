using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Timeline tab. The code-behind answers the year histogram: <see cref="ContinuityTimelineViewModel.JumpRequested"/> names a cover,
/// and once its era has unfolded and laid out, that cover is brought into view and focused.
/// </summary>
public partial class ContinuityTimelineView : UserControl
{
    private ContinuityTimelineViewModel? _timeline;

    public ContinuityTimelineView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_timeline is not null)
            {
                _timeline.JumpRequested -= OnJumpRequested;
            }

            _timeline = DataContext as ContinuityTimelineViewModel;
            if (_timeline is not null)
            {
                _timeline.JumpRequested += OnJumpRequested;
            }
        };
    }

    private void OnJumpRequested(TimelineIssueCard card) =>
        Dispatcher.UIThread.Post(() =>
        {
            var target = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.DataContext, card));
            if (target is not null)
            {
                target.BringIntoView();
                target.Focus();
            }
        }, DispatcherPriority.Background);
}
