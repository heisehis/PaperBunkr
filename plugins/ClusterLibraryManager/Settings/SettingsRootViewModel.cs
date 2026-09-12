namespace ClusterLibraryManager.Settings;

/// <summary>Composes the two tabs of the plugin's settings screen (design doc §2/§7) - returned as
/// one <c>Control</c> from <c>OrganizerScraperPlugin.CreateSettingsView</c>.</summary>
public sealed class SettingsRootViewModel
{
    public SettingsViewModel General { get; }
    public ProfileManagerViewModel Profiles { get; }

    public SettingsRootViewModel(SettingsViewModel general, ProfileManagerViewModel profiles)
    {
        General = general;
        Profiles = profiles;
    }
}
