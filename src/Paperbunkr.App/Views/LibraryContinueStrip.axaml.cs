using Avalonia.Controls;
using Avalonia.Interactivity;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Library's "Continue reading" strip (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 4). All of its state
/// is <see cref="LibraryScreenViewModel.ContinueStrip"/>; this file only makes a focused card drive the inspector, the way a
/// focused grid tile does.
/// </summary>
public partial class LibraryContinueStrip : UserControl
{
    public LibraryContinueStrip() => InitializeComponent();

    private void OnCardGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SeriesCardSample card } && DataContext is LibraryScreenViewModel vm)
        {
            vm.PreviewContinueCard(card);
        }
    }
}
