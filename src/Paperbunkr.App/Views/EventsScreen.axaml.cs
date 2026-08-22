using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Explicit code-behind for a brand-new View (docs/superpowers/specs/2026-08-18-
/// selectedvaluebinding-xaml-fix-design.md) - a genuinely new <c>x:Class</c> type with no prior
/// compiled code-behind hits a real MSBuild ordering issue: <c>CompileAvaloniaXaml</c> only runs
/// as a side effect of <c>CoreCompile</c> actually executing, and if that first pass fails (as it
/// does for an unresolvable type), the compiled assembly's timestamp is already newer than every
/// source file, so <c>CoreCompile</c> - and therefore the Avalonia XAML weave - gets silently
/// skipped on every subsequent build, permanently leaving the exe with no embedded XAML resources
/// even though later builds report "0 Errors." A real, ordinary C# partial class here lets
/// <c>CoreCompile</c> produce the type through normal compilation, sidestepping the issue entirely.
/// </summary>
public partial class EventsScreen : UserControl
{
    public EventsScreen()
    {
        InitializeComponent();
    }
}
