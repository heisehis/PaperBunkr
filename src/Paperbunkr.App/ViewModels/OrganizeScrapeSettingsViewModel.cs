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

/// <summary>One row of "Which source wins": a field and the source it is taken from when both know it.</summary>
public sealed partial class FieldSourceChoice(ScrapeField field, string name) : ObservableObject
{
    public const string MatchedSource = "Matched source";

    public static System.Collections.Generic.IReadOnlyList<string> Choices { get; } = new[] { MatchedSource, "Metron", "ComicVine" };

    public ScrapeField Field { get; } = field;

    public string Name { get; } = name;

    [ObservableProperty]
    private string _choiceText = MatchedSource;

    /// <summary>The preferred source, or null for "whichever source the match came from".</summary>
    public ComicProvider? Preferred => ChoiceText switch
    {
        "Metron" => ComicProvider.Metron,
        "ComicVine" => ComicProvider.ComicVine,
        _ => null,
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

        foreach (var field in ScrapeSettings.SourcePreferableFields)
        {
            FieldSources.Add(new FieldSourceChoice(field, FieldToggles.First(t => t.Field == field).Name));
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

    /// <summary>"Which source wins" (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 4).</summary>
    public ObservableCollection<FieldSourceChoice> FieldSources { get; } = new();

    [RelayCommand]
    private void ResetFieldSources()
    {
        foreach (var row in FieldSources)
        {
            row.ChoiceText = FieldSourceChoice.MatchedSource;
        }
    }

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

    /// <summary>The user's own scan-group names, one per line, added to the built-in ones for "Find scanner from file name" (docs/superpowers/specs/2026-10-11-scan-credits-design.md). Kept in <c>scan-credits.json</c>, not the database.</summary>
    [ObservableProperty] private string _scannerNames = string.Empty;

    /// <summary>The user's own words that mean "this bracket is not a scanner", one per line.</summary>
    [ObservableProperty] private string _scanIgnoredWords = string.Empty;

    [ObservableProperty] private bool _hasComicVineKey;
    [ObservableProperty] private bool _hasMetronLogin;

    public static System.Collections.Generic.IReadOnlyList<string> ProviderNames => SeriesMissingIssuesViewModel.ProviderNames;

    /// <summary>The source scrapes start on ("ComicVine" or "Metron"); the match dialog can switch one run.</summary>
    [ObservableProperty] private string _defaultProviderText = "ComicVine";

    public static System.Collections.Generic.IReadOnlyList<string> SpeedNames { get; } = new[] { "Careful", "Balanced", "Fast" };

    public static System.Collections.Generic.IReadOnlyList<string> StrictnessNames { get; } = new[] { "Lenient", "Balanced", "Strict" };

    /// <summary>docs/superpowers/specs/2026-10-21-scraper-organizer-b-speed-core-design.md section 5.</summary>
    [ObservableProperty] private string _speedText = "Balanced";

    /// <summary>docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrictnessDescription))]
    private string _strictnessText = "Balanced";

    public string StrictnessDescription
    {
        get
        {
            string describe = ConfidencePolicy.Describe(ParseStrictness(StrictnessText));
            var checks = ScrapeDecisionHistory.Shared?.Checks() ?? Array.Empty<CoverCheck>();
            if (checks.Count < MinimumChecksForPreview)
            {
                return describe;
            }

            // What each level would have done with the user's own recent automatic matches (spec C section 1).
            var would = ScrapeDecisionHistory.WouldApply(checks);
            return $"{describe} On your last {checks.Count} automatic cover checks: Lenient would apply {would[ScrapeStrictness.Lenient]}, " +
                   $"Balanced {would[ScrapeStrictness.Balanced]}, Strict {would[ScrapeStrictness.Strict]}; the rest wait for review.";
        }
    }

    /// <summary>Below this the counts say more about chance than about the setting.</summary>
    public const int MinimumChecksForPreview = 20;

    /// <summary>Metron first, ComicVine last (spec B section 1).</summary>
    [ObservableProperty] private bool _fallBackToComicVine = true;

    /// <summary>Scrape on import (spec C section 9).</summary>
    [ObservableProperty] private bool _scrapeNewFiles = true;

    /// <summary>Show the changes before applying a multi-comic scrape (spec C section 3).</summary>
    [ObservableProperty] private bool _previewChanges = true;

    /// <summary>Fill Metron's blank fields from ComicVine (spec C section 4).</summary>
    [ObservableProperty] private bool _fillBlanksFromComicVine;

    /// <summary>"N saved answers, X MB" for the provider cache.</summary>
    [ObservableProperty] private string _providerCacheSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnfinishedScrape))]
    private string _unfinishedScrapeText = string.Empty;

    public bool HasUnfinishedScrape => UnfinishedScrapeText.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReviewItems))]
    private int _reviewQueueCount;

    public bool HasReviewItems => ReviewQueueCount > 0;

    /// <summary>Wired by the shell: runs "Scrape all unmatched" as a background job and returns its summary.</summary>
    public Func<System.Threading.Tasks.Task<string>>? RunScrapeAllUnmatched { get; set; }

    /// <summary>Wired by the shell: resumes an unfinished batch.</summary>
    public Func<ScrapeJobRecord, System.Threading.Tasks.Task<string>>? ResumeScrape { get; set; }

    /// <summary>Wired by the shell: opens the review queue.</summary>
    public Action? OpenReviewQueue { get; set; }

    /// <summary>Wired by the shell: undoes a scrape batch and returns what it did.</summary>
    public Func<ScrapeUndoBatch, string>? UndoScrapeBatch { get; set; }

    private ScrapeUndoBatch? _lastBatch;

    /// <summary>"The last scrape (12 comics, 21/10 14:02)" when one can be undone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndoLastScrape))]
    private string _lastScrapeText = string.Empty;

    public bool CanUndoLastScrape => LastScrapeText.Length > 0;

    private ScrapeJobRecord? _unfinished;

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

        foreach (var row in FieldSources)
        {
            row.ChoiceText = settings.FieldSourcePreference.TryGetValue(row.Field, out var preferred)
                ? Paperbunkr.Data.ComicVine.ComicProviderFactory.DisplayName(preferred)
                : FieldSourceChoice.MatchedSource;
        }

        HasComicVineKey = !string.IsNullOrEmpty(CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey));
        HasMetronLogin = Paperbunkr.Data.ComicVine.ComicProviderFactory.IsAvailable(context, ComicProvider.Metron);
        DefaultProviderText = Paperbunkr.Data.ComicVine.ComicProviderFactory.DisplayName(settings.DefaultProvider);
        SpeedText = settings.Speed.ToString();
        StrictnessText = settings.Strictness.ToString();
        FallBackToComicVine = settings.FallBackToComicVine;
        ScrapeNewFiles = settings.ScrapeNewFiles;
        PreviewChanges = settings.PreviewChanges;
        FillBlanksFromComicVine = settings.FillBlanksFromComicVine;
        RefreshAutomationState();
        var scanCredits = Paperbunkr.Data.Metadata.ScanCredits.ScanCreditSettings.Load();
        ScannerNames = string.Join(Environment.NewLine, scanCredits.Scanners);
        ScanIgnoredWords = string.Join(Environment.NewLine, scanCredits.Ignored);
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
            Speed = Enum.TryParse<ScrapeSpeed>(SpeedText, ignoreCase: true, out var speed) ? speed : ScrapeSpeed.Balanced,
            Strictness = ParseStrictness(StrictnessText),
            FallBackToComicVine = FallBackToComicVine,
            ScrapeNewFiles = ScrapeNewFiles,
            PreviewChanges = PreviewChanges,
            FillBlanksFromComicVine = FillBlanksFromComicVine,
            FieldSourcePreference = FieldSources.Where(r => r.Preferred is not null).ToDictionary(r => r.Field, r => r.Preferred!.Value),
        };

        using var context = _createContext();
        settings.Save(context);
        try
        {
            Paperbunkr.Data.Metadata.ScanCredits.ScanCreditSettings.Save(new(Lines(ScannerNames).ToList(), Lines(ScanIgnoredWords).ToList()));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            SetStatus($"Saved, but the scanner lists could not be written: {ex.Message}", isError: true);
            return;
        }

        SetStatus("Saved.", isError: false);
    }

    [RelayCommand]
    private void OpenConnections() => _openConnections();

    private static ScrapeStrictness ParseStrictness(string text) =>
        Enum.TryParse<ScrapeStrictness>(text, ignoreCase: true, out var value) ? value : ScrapeStrictness.Balanced;

    /// <summary>Re-reads the provider cache size, an unfinished batch and the review queue (also after each of the actions below).</summary>
    public void RefreshAutomationState()
    {
        var cache = Paperbunkr.Data.ComicVine.Caching.ProviderCache.Shared;
        if (cache is null)
        {
            ProviderCacheSummary = "Not in use.";
        }
        else
        {
            var (count, bytes) = cache.Size();
            ProviderCacheSummary = count == 0 ? "Nothing saved yet." : $"{count:N0} saved answers, {bytes / (1024.0 * 1024.0):0.0} MB.";
        }

        _unfinished = ScrapeJobStore.Shared?.LoadUnfinished().FirstOrDefault();
        UnfinishedScrapeText = _unfinished is null ? string.Empty : $"{_unfinished.Pending.Count:N0} of {_unfinished.Total:N0} comics left, started {_unfinished.CreatedUtc.ToLocalTime():g}.";
        ReviewQueueCount = ScrapeReviewQueue.Shared?.Load().Count ?? 0;
        OnPropertyChanged(nameof(StrictnessDescription)); // picks up checks recorded since the page was last shown
        _lastBatch = ScrapeUndoStore.Shared?.List().FirstOrDefault();
        LastScrapeText = _lastBatch is null ? string.Empty
            : $"{_lastBatch.Label}, {_lastBatch.CreatedUtc.ToLocalTime():g} - {_lastBatch.Before.Count} changed. Kept for 30 days.";
    }

    [RelayCommand]
    private void UndoLastScrape()
    {
        if (_lastBatch is null || UndoScrapeBatch is null)
        {
            return;
        }

        SetStatus(UndoScrapeBatch(_lastBatch), isError: false);
        RefreshAutomationState();
    }

    /// <summary>"Refresh provider data": the next scrape asks the sources again (spec B section 3).</summary>
    [RelayCommand]
    private void RefreshProviderData()
    {
        int removed = Paperbunkr.Data.ComicVine.Caching.ProviderCache.Shared?.Clear() ?? 0;
        SetStatus($"Forgot {removed:N0} saved answers; the next scrape asks the sources again.", isError: false);
        RefreshAutomationState();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ScrapeAllUnmatchedAsync()
    {
        if (RunScrapeAllUnmatched is null)
        {
            return;
        }

        SetStatus("Scraping every unmatched comic in the background - follow it in the Activity Center.", isError: false);
        string summary = await RunScrapeAllUnmatched();
        SetStatus(summary, isError: false);
        RefreshAutomationState();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ResumeUnfinishedScrapeAsync()
    {
        if (_unfinished is null || ResumeScrape is null)
        {
            return;
        }

        var job = _unfinished;
        UnfinishedScrapeText = string.Empty;
        SetStatus("Resuming in the background - follow it in the Activity Center.", isError: false);
        string summary = await ResumeScrape(job);
        SetStatus(summary, isError: false);
        RefreshAutomationState();
    }

    [RelayCommand]
    private void DiscardUnfinishedScrape()
    {
        if (_unfinished is not null)
        {
            ScrapeJobStore.Shared?.Delete(_unfinished.Id);
        }

        RefreshAutomationState();
    }

    [RelayCommand]
    private void ShowReviewQueue() => OpenReviewQueue?.Invoke();

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
