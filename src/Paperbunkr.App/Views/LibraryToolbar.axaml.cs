using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Library screen's toolbar - the two selection action bars, search, and the sort/group/display/add
/// popups (docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-rework-design.md §1).
/// Split out of <see cref="LibraryScreen"/> as a pure move; <c>DataContext</c> is inherited, so it
/// binds the same <see cref="ViewModels.LibraryScreenViewModel"/> instance the screen does - no
/// ViewModel split, matching how <c>IssueListScreen</c> is composed today.
/// </summary>
public partial class LibraryToolbar : UserControl
{
    public LibraryToolbar()
    {
        InitializeComponent();
    }

    /// <summary>Search suggestions popup (docs/superpowers/specs/2026-08-31-library-search-
    /// suggestions-design.md) - focus is a UI-side event with no XAML command equivalent, so this
    /// and the two handlers below relay straight into the ViewModel's own methods.</summary>
    private void OnSearchBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        (DataContext as LibraryScreenViewModel)?.OnSearchBoxGotFocus();
    }

    private void OnSearchBoxLostFocus(object? sender, FocusChangedEventArgs e)
    {
        (DataContext as LibraryScreenViewModel)?.OnSearchBoxLostFocus();
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSuggestionSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSuggestionSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.CommitSearchBox();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CloseSuggestionsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
