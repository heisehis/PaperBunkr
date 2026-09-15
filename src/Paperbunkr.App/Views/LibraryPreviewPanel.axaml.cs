using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Live preview panel for the Library Master-Detail redesign (docs/superpowers/specs/2026-09-14-
/// library-visual-redesign-design.md §4). Purely declarative - all state comes from
/// <c>LibraryScreenViewModel</c>'s own computed properties, no code-behind logic needed.
/// </summary>
public partial class LibraryPreviewPanel : UserControl
{
    public LibraryPreviewPanel()
    {
        InitializeComponent();
    }
}
