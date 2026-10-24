using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Paperbunkr.App.Scraper;

public partial class ScrapeReviewQueueDialogView : UserControl
{
    public ScrapeReviewQueueDialogView()
    {
        InitializeComponent();

        // Enter and Delete act on the focused series - the list's own keys on its focused item (CLAUDE.md "What stays a control's own key
        // handler"); tunnelled because the ListBox handles keys itself first. The row buttons stay mouse-only so the arrows move by series.
        SeriesList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => SeriesList.Focus(NavigationMethod.Tab);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ScrapeReviewQueueDialogViewModel vm || vm.SelectedGroup is not { } group || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.ReviewCommand.Execute(group);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            vm.SkipCommand.Execute(group);
            e.Handled = true;
        }
    }
}
