using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One checkbox in the "which fields a scrape may write" list.</summary>
public sealed partial class ScrapeFieldToggle(ScrapeField field) : ObservableObject
{
    public ScrapeField Field { get; } = field;

    public string Name { get; } = Humanize(field);

    [ObservableProperty]
    private bool _isEnabled = true;

    private static string Humanize(ScrapeField field) => field switch
    {
        ScrapeField.Crossovers => "Story arcs (crossovers)",
        ScrapeField.CoverArtist => "Cover artist",
        ScrapeField.Webpage => "Web page",
        ScrapeField.Published => "Published date",
        ScrapeField.Released => "Released date",
        ScrapeField.Count => "Issue count",
        ScrapeField.MainCharacterOrTeam => "Main character / team",
        ScrapeField.Concepts => "Tags (concepts)",
        ScrapeField.SeriesGroup => "Series group",
        ScrapeField.StoryArcOrder => "Story arc order",
        _ => field.ToString(),
    };
}

/// <summary>
/// Preferences → Organize &amp; Scrape (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 8): how a ComicVine scrape behaves. No secrets live here; the
/// ComicVine key is under Connections. One Save writes the whole <see cref="ScrapeSettings"/> row.
/// </summary>
public sealed partial class OrganizeScrapeSettingsViewModel : ViewModelBase
{
    private readonly Func<PaperbunkrDbContext> _createContext;
    private readonly Action _openConnections;

    public OrganizeScrapeSettingsViewModel(Func<PaperbunkrDbContext> createContext, Action openConnections, Scraper.ProfileManagerViewModel? profiles = null)
    {
        _createContext = createContext;
        _openConnections = openConnections;
        Profiles = profiles;
        foreach (var field in Enum.GetValues<ScrapeField>())
        {
            FieldToggles.Add(new ScrapeFieldToggle(field));
        }

        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsTabs.SelectedKey))
            {
                OnPropertyChanged(nameof(IsSaveFooterVisible));
            }
        };
    }

    /// <summary>Tab keys; the same strings are <c>PreferenceIndexEntry.SubTab</c> values and the section's panel keys.</summary>
    public static class TabKeys
    {
        public const string Scrape = "scrape";
        public const string Filters = "filters";
        public const string Organize = "organize";
    }

    /// <summary>Scrape / Filters &amp; names / Organize (docs/superpowers/specs/2026-09-26-preferences-reader-organize-tabs-design.md). In memory only.</summary>
    public SettingsTabs Tabs { get; } = new(new SettingsTabItem[]
    {
        new(TabKeys.Scrape, "Scrape"),
        new(TabKeys.Filters, "Filters & names"),
        new(TabKeys.Organize, "Organize"),
    });

    /// <summary>Save writes the scrape settings, which live on the first two tabs; the Organize tab's profiles manage themselves.</summary>
    public bool IsSaveFooterVisible => Tabs.SelectedKey != TabKeys.Organize;

    public ObservableCollection<ScrapeFieldToggle> FieldToggles { get; } = new();

    /// <summary>Organizer profiles (create, edit, undo); null in tests that only exercise the scrape settings.</summary>
    public Scraper.ProfileManagerViewModel? Profiles { get; }

    [ObservableProperty] private bool _autoChooseTopMatch;
    [ObservableProperty] private bool _confirmIssueMatch = true;
    [ObservableProperty] private bool _overwriteExisting = true;
    [ObservableProperty] private bool _ignoreBlankValues;
    [ObservableProperty] private string _maxSearchResults = "100";
    [ObservableProperty] private string _ignoreBeforeYear = string.Empty;
    [ObservableProperty] private string _ignoreAfterYear = string.Empty;
    [ObservableProperty] private string _neverIgnoreThreshold = string.Empty;

    /// <summary>One publisher per line.</summary>
    [ObservableProperty] private string _ignoredPublishers = string.Empty;

    /// <summary>One word per line, stripped from the search sent to ComicVine.</summary>
    [ObservableProperty] private string _ignoredSearchTerms = string.Empty;

    /// <summary>One <c>Imprint --&gt; Parent publisher</c> per line.</summary>
    [ObservableProperty] private string _imprintOverrides = string.Empty;

    /// <summary>CE's <c>convert_imprints_b</c>; false reproduces its real off-behavior (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md Phase 3).</summary>
    [ObservableProperty] private bool _convertImprints = true;

    /// <summary>CE's <c>force_series_art_b</c>: false falls back to the specific issue's own cover in the review dialog instead of always the series art.</summary>
    [ObservableProperty] private bool _forceSeriesArt = true;

    /// <summary>CE's <c>show_covers_b</c>: false hides cover thumbnails in both review dialogs.</summary>
    [ObservableProperty] private bool _showCovers = true;

    /// <summary>CE's <c>scrape_delay_n</c> in milliseconds - a proactive pause between books, distinct from the per-HTTP-request rate limiter.</summary>
    [ObservableProperty] private string _scrapeDelayMs = "1000";

    /// <summary>One <c>Publisher --&gt; Alias</c> per line, applied after imprint resolution - distinct from <see cref="ImprintOverrides"/>.</summary>
    [ObservableProperty] private string _publisherAliases = string.Empty;

    [ObservableProperty] private bool _hasComicVineKey;
    [ObservableProperty] private bool _hasMetronLogin;

    public static System.Collections.Generic.IReadOnlyList<string> ProviderNames => SeriesMissingIssuesViewModel.ProviderNames;

    /// <summary>The source scrapes start on ("ComicVine" or "Metron"); the match dialog can switch one run.</summary>
    [ObservableProperty] private string _defaultProviderText = "ComicVine";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus), nameof(HasInfoStatus))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus), nameof(HasInfoStatus))]
    private bool _statusIsError;

    public bool HasErrorStatus => !string.IsNullOrEmpty(StatusMessage) && StatusIsError;

    public bool HasInfoStatus => !string.IsNullOrEmpty(StatusMessage) && !StatusIsError;

    public void Load()
    {
        using var context = _createContext();
        var settings = ScrapeSettings.Load(context);

        AutoChooseTopMatch = settings.AutoChooseTopMatch;
        ConfirmIssueMatch = settings.ConfirmIssueMatch;
        OverwriteExisting = settings.OverwriteExisting;
        IgnoreBlankValues = settings.IgnoreBlankValues;
        MaxSearchResults = settings.MaxSearchResults?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        IgnoreBeforeYear = settings.IgnoreVolumesBeforeYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        IgnoreAfterYear = settings.IgnoreVolumesAfterYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        NeverIgnoreThreshold = settings.NeverIgnoreThreshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        IgnoredPublishers = string.Join(Environment.NewLine, settings.IgnoredPublishers.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        IgnoredSearchTerms = string.Join(Environment.NewLine, settings.IgnoredSearchTerms.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        ImprintOverrides = string.Join(Environment.NewLine, settings.ImprintOverrides.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{p.Key} --> {p.Value}"));
        ConvertImprints = settings.ConvertImprints;
        ForceSeriesArt = settings.ForceSeriesArt;
        ShowCovers = settings.ShowCovers;
        ScrapeDelayMs = settings.ScrapeDelayMs.ToString(CultureInfo.InvariantCulture);
        PublisherAliases = string.Join(Environment.NewLine, settings.PublisherAliases.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{p.Key} --> {p.Value}"));
        foreach (var toggle in FieldToggles)
        {
            toggle.IsEnabled = settings.EnabledScrapeFields.Contains(toggle.Field);
        }

        HasComicVineKey = !string.IsNullOrEmpty(CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey));
        HasMetronLogin = Paperbunkr.Data.ComicVine.ComicProviderFactory.IsAvailable(context, ComicProvider.Metron);
        DefaultProviderText = Paperbunkr.Data.ComicVine.ComicProviderFactory.DisplayName(settings.DefaultProvider);
        Profiles?.Reload();
    }

    [RelayCommand]
    private void Save()
    {
        if (!TryParseOptionalInt(MaxSearchResults, 1, 1000, "Maximum search results", out int? max, out var error)
            || !TryParseOptionalInt(IgnoreBeforeYear, 0, 9999, "Ignore volumes before year", out int? before, out error)
            || !TryParseOptionalInt(IgnoreAfterYear, 0, 9999, "Ignore volumes after year", out int? after, out error)
            || !TryParseOptionalInt(NeverIgnoreThreshold, 1, 100000, "Never-ignore threshold", out int? threshold, out error))
        {
            SetStatus(error!, isError: true);
            return;
        }

        if (!TryParseArrows(ImprintOverrides, "Imprint", "Vertigo --> DC Comics", out var overrides, out error)
            || !TryParseArrows(PublisherAliases, "Publisher alias", "Marvel UK --> Marvel", out var aliases, out error))
        {
            SetStatus(error!, isError: true);
            return;
        }

        if (!TryParseOptionalInt(ScrapeDelayMs, 0, 3_600_000, "Scrape delay", out int? delayMs, out error))
        {
            SetStatus(error!, isError: true);
            return;
        }

        var settings = new ScrapeSettings
        {
            AutoChooseTopMatch = AutoChooseTopMatch,
            ConfirmIssueMatch = ConfirmIssueMatch,
            OverwriteExisting = OverwriteExisting,
            IgnoreBlankValues = IgnoreBlankValues,
            DefaultProvider = Paperbunkr.Data.ComicVine.ComicProviderFactory.Parse(DefaultProviderText),
            MaxSearchResults = max,
            IgnoreVolumesBeforeYear = before,
            IgnoreVolumesAfterYear = after,
            NeverIgnoreThreshold = threshold,
            IgnoredPublishers = new(Lines(IgnoredPublishers), StringComparer.OrdinalIgnoreCase),
            IgnoredSearchTerms = new(Lines(IgnoredSearchTerms), StringComparer.OrdinalIgnoreCase),
            ImprintOverrides = overrides,
            ConvertImprints = ConvertImprints,
            ForceSeriesArt = ForceSeriesArt,
            ShowCovers = ShowCovers,
            ScrapeDelayMs = delayMs ?? 1000,
            PublisherAliases = aliases,
            EnabledScrapeFields = new(FieldToggles.Where(t => t.IsEnabled).Select(t => t.Field)),
        };

        using var context = _createContext();
        settings.Save(context);
        SetStatus("Saved.", isError: false);
    }

    [RelayCommand]
    private void OpenConnections() => _openConnections();

    [RelayCommand]
    private void EnableAllFields()
    {
        foreach (var toggle in FieldToggles)
        {
            toggle.IsEnabled = true;
        }
    }

    [RelayCommand]
    private void DisableAllFields()
    {
        foreach (var toggle in FieldToggles)
        {
            toggle.IsEnabled = false;
        }
    }

    /// <summary>Parses a newline-delimited <c>"X --> Y"</c> block (the shape shared by <see cref="ImprintOverrides"/> and <see cref="PublisherAliases"/>).</summary>
    private static bool TryParseArrows(string text, string label, string example, out System.Collections.Generic.Dictionary<string, string> parsed, out string? error)
    {
        parsed = new System.Collections.Generic.Dictionary<string, string>();
        error = null;
        foreach (var line in Lines(text))
        {
            var parts = line.Split("-->", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                error = $"{label} line \"{line}\" should look like: {example}";
                return false;
            }

            parsed[parts[0]] = parts[1];
        }

        return true;
    }

    private static System.Collections.Generic.IEnumerable<string> Lines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseOptionalInt(string text, int min, int max, string label, out int? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
        {
            error = $"{label} must be a whole number from {min} to {max}, or left blank.";
            return false;
        }

        value = parsed;
        return true;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }
}
