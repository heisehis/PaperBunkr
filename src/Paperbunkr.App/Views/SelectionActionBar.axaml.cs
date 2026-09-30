using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.LibraryActions;

namespace Paperbunkr.App.Views;

public partial class SelectionActionBar : UserControl
{
    public SelectionActionBar() => InitializeComponent();

    /// <summary>A plain button runs its entry's command; a ▾ button opens its menu, rebuilt for the current selection so lists and
    /// collections added since the bar was built are there.</summary>
    private void OnBarButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LibraryBarItem { Entry: { } entry } item } button || DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        e.Handled = true;
        if (item.HasChildren && item.ActionId is { } actionId)
        {
            var children = vm.BuildBarMenu(actionId);
            if (children.Count > 0)
            {
                ContextMenuHost.ShowMenu(button, children);
            }

            return;
        }

        if (entry.Command is { } command && command.CanExecute(entry.CommandParameter))
        {
            // Deferred: several actions rebuild the bar (and so this very button) while its click is still routing (CLAUDE.md's detach rule).
            var parameter = entry.CommandParameter;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => command.Execute(parameter));
        }
    }
}
