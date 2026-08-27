using Avalonia.Controls;

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
}
