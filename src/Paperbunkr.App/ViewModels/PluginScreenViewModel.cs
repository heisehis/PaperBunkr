namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Plugin screen - the "plugins get a full first-class window, not a settings tab" case from
/// docs/wireframe_prototype_prompt.md. Previously shipped a fake "Duplicate Finder" demo (hardcoded
/// duplicate groups, scan stats, dead buttons); no plugin engine exists yet, so this is a genuine
/// empty state until one does (docs/superpowers/specs/2026-08-09-plugin-screen-cleanup-design.md),
/// same treatment as Detail's Related/Activity tabs.
/// </summary>
public partial class PluginScreenViewModel : ViewModelBase
{
}
