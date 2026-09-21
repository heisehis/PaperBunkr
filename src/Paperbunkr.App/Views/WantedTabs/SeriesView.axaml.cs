using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views.WantedTabs;

public partial class SeriesView : UserControl
{
    public SeriesView() => InitializeComponent();

    /// <summary>The row's "…" button offers what right-click offers (Open series, Stop tracking).</summary>
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WatchedSeriesRowViewModel row } button
            && DataContext is WantedScreenViewModel vm
            && vm.BuildContextMenu(row) is { Count: > 0 } entries)
        {
            ContextMenuHost.ShowMenu(button, entries);
        }
    }
}
