using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Explicit code-behind for a brand-new View - same MSBuild-ordering rationale as
/// <see cref="EventsScreen"/>'s own doc comment (docs/superpowers/specs/2026-08-18-
/// selectedvaluebinding-xaml-fix-design.md): a real, ordinary partial class here lets
/// <c>CoreCompile</c> produce the type through normal compilation instead of risking the silent
/// XAML-weave skip a purely-generated stub can hit for a genuinely new <c>x:Class</c>.
/// </summary>
public partial class IssueListScreen : UserControl
{
    public IssueListScreen()
    {
        InitializeComponent();
    }
}
