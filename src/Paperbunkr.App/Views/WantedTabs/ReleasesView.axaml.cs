using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views.WantedTabs;

public partial class ReleasesView : UserControl
{
    public ReleasesView() => InitializeComponent();

    /// <summary>The tile's "…" button offers what right-click offers (Follow, Hide, Restore).</summary>
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ReleaseRowViewModel row } button
            && DataContext is WantedScreenViewModel vm
            && vm.BuildContextMenu(row) is { Count: > 0 } entries)
        {
            ContextMenuHost.ShowMenu(button, entries);
        }
    }
}
