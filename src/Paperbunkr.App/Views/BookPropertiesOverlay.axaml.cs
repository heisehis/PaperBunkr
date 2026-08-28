using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for <see cref="BookPropertiesOverlay"/> - intentionally minimal. Present from the
/// first commit alongside the .axaml so <c>CompileAvaloniaXamlTask</c> has a compiled partial class
/// to bind the <c>x:Class</c> to (CLAUDE.md's AVLN2000 build gotcha).
/// </summary>
public partial class BookPropertiesOverlay : UserControl
{
    public BookPropertiesOverlay()
    {
        InitializeComponent();
    }
}
