using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Abstractions.Ui;

namespace ClusterLibraryManager;

/// <summary>
/// Entry point for the Cluster Library Manager native plugin (docs/superpowers/specs/2026-09-11-
/// cluster-library-manager-design.md §2, implementation plan Phase 3 Step 3.1). Minimal scaffold for
/// now - <see cref="Initialize"/>/<see cref="RegisterCommands"/>/<see cref="CreateSettingsView"/> are
/// filled in incrementally as each of ComicVineService (Step 3.2), the token engine (3.3),
/// LibraryOrganizerService (3.4), the collision/match-review dialogs (3.5), and the settings UI (3.6)
/// land - this step's own job is just proving the plugin loads through <c>PluginLoadContext</c>/
/// <c>PluginEngine</c> like the Phase 1 fixture did.
/// </summary>
public sealed class OrganizerScraperPlugin : INativePluginModule, INativePluginSettingsUi
{
    private INativePluginEnvironment? _environment;

    public void Initialize(INativePluginEnvironment environment)
    {
        _environment = environment;
    }

    public void RegisterCommands(INativeCommandRegistrar registrar)
    {
        registrar.OnStartup("cluster-library-manager.startup", "Cluster Library Manager Activated",
            _ => Task.FromResult<object?>("Cluster Library Manager is active for this session."));
    }

    public Avalonia.Controls.Control? CreateSettingsView(INativePluginUiEnvironment environment) => null;
}
