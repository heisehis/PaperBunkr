using System.Collections.ObjectModel;
using ClusterLibraryManager.ComicVine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClusterLibraryManager.Settings;

/// <summary>One of CE's 20 field-scrape toggles (design doc §4), bindable to a CheckBox.</summary>
public sealed partial class ScrapeFieldToggleViewModel : ObservableObject
{
    public ScrapeField Field { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isEnabled;

    public ScrapeFieldToggleViewModel(ScrapeField field, bool isEnabled)
    {
        Field = field;
        Label = field.ToString();
        _isEnabled = isEnabled;
    }
}

/// <summary>
/// Backs the "General" tab of <see cref="SettingsView"/> (the settings screen returned from
/// <c>OrganizerScraperPlugin.CreateSettingsView</c>, design doc §2) - a real, plugin-authored settings
/// screen, not a declarative schema the host renders generically. Naming templates live per-
/// <c>OrganizerProfile</c> (grilling Q16=B), edited on the "Profiles" tab
/// (<see cref="ProfileManagerViewModel"/>) instead of here - this tab owns the plugin-wide settings
/// (API key, apply-behavior toggles, the 20 CE field-scrape toggles, self-contained automation).
/// Both tabs live in one screen (see <see cref="SettingsRootViewModel"/>) rather than the Profiles
/// tab opening as a second nested modal - the generic native-plugin modal host shows one modal at a
/// time (a second concurrent <c>ShowModalAsync</c> call queues rather than stacking), so nesting a
/// modal launch inside an already-open settings modal wouldn't show it until the first closed anyway.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PluginSettingsStore _settingsStore;
    private readonly Func<string, ComicVineService> _createComicVineService;

    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private bool _overwriteExisting;

    [ObservableProperty]
    private bool _ignoreBlankValues;

    [ObservableProperty]
    private bool _autoChooseTopMatch;

    [ObservableProperty]
    private bool _automaticScrapeEnabled;

    [ObservableProperty]
    private int _automaticScrapeIntervalHours;

    [ObservableProperty]
    private bool _automaticOrganizeEnabled;

    [ObservableProperty]
    private int _automaticOrganizeIntervalHours;

    [ObservableProperty]
    private string? _testConnectionStatus;

    [ObservableProperty]
    private bool _isTestingConnection;

    public ObservableCollection<ScrapeFieldToggleViewModel> ScrapeFieldToggles { get; } = new();

    public SettingsViewModel(PluginSettingsStore settingsStore, Func<string, ComicVineService> createComicVineService)
    {
        _settingsStore = settingsStore;
        _createComicVineService = createComicVineService;

        PluginSettings settings = settingsStore.Load();
        _apiKey = settings.ApiKey;
        _overwriteExisting = settings.OverwriteExisting;
        _ignoreBlankValues = settings.IgnoreBlankValues;
        _autoChooseTopMatch = settings.AutoChooseTopMatch;
        _automaticScrapeEnabled = settings.AutomaticScrapeEnabled;
        _automaticScrapeIntervalHours = settings.AutomaticScrapeIntervalHours;
        _automaticOrganizeEnabled = settings.AutomaticOrganizeEnabled;
        _automaticOrganizeIntervalHours = settings.AutomaticOrganizeIntervalHours;

        foreach (ScrapeField field in Enum.GetValues<ScrapeField>())
        {
            ScrapeFieldToggles.Add(new ScrapeFieldToggleViewModel(field, settings.EnabledScrapeFields.Contains(field)));
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            TestConnectionStatus = "Enter an API key first.";
            return;
        }

        IsTestingConnection = true;
        TestConnectionStatus = null;
        try
        {
            ComicVineService service = _createComicVineService(ApiKey);
            bool ok = await service.TestConnectionAsync();
            TestConnectionStatus = ok ? "Connection successful." : "Connection failed - check your API key.";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var settings = new PluginSettings
        {
            ApiKey = ApiKey,
            OverwriteExisting = OverwriteExisting,
            IgnoreBlankValues = IgnoreBlankValues,
            AutoChooseTopMatch = AutoChooseTopMatch,
            EnabledScrapeFields = ScrapeFieldToggles.Where(t => t.IsEnabled).Select(t => t.Field).ToHashSet(),
            AutomaticScrapeEnabled = AutomaticScrapeEnabled,
            AutomaticScrapeIntervalHours = AutomaticScrapeIntervalHours,
            AutomaticOrganizeEnabled = AutomaticOrganizeEnabled,
            AutomaticOrganizeIntervalHours = AutomaticOrganizeIntervalHours,
        };
        _settingsStore.Save(settings);
    }
}
