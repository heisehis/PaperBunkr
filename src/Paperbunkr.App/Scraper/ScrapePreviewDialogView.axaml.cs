using Avalonia.Controls;
using Avalonia.Input;

namespace Paperbunkr.App.Scraper;

public partial class ScrapePreviewDialogView : UserControl
{
    public ScrapePreviewDialogView()
    {
        InitializeComponent();

        // Tunnel: the ListBox handles Space itself (selection) before a bubbling handler would see it.
        Comics.AddHandler(KeyDownEvent, OnListKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    // Space on the focused comic ticks or unticks it: the list's own key on its focused item (CLAUDE.md "What stays a control's own key handler").
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && DataContext is ScrapePreviewDialogViewModel vm)
        {
            vm.ToggleSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }
}
