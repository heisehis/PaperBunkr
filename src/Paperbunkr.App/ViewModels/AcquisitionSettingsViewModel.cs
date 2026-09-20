using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Import;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences → Acquisition (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §7). Slice 1 only shows what slice 1
/// uses - the Prowlarr connection, how often to look, and how to filter and rank results. The qBittorrent and destination-folder
/// settings exist in the database but get their controls in the slices that use them, rather than shipping controls that do nothing.
/// <para>
/// Edits are held in the form and written by <see cref="SaveCommand"/> (a per-keystroke save would rewrite the row and the encrypted
/// key on every character). The Prowlarr API key is write-only: it is stored through <see cref="CredentialStore"/> (DPAPI-encrypted)
/// and never read back into the UI - <see cref="HasSavedApiKey"/> just says one is stored, and a blank key field means "keep it".
/// </para>
/// </summary>
public sealed partial class AcquisitionSettingsViewModel : ViewModelBase
{
    private readonly Func<PaperbunkrDbContext> _createContext;
    private readonly Action _openConnections;
    private readonly Func<string, string, IIndexerClient> _createIndexer;
    private readonly Func<string, string, string, string, IDownloadClient> _createDownloadClient;

    public AcquisitionSettingsViewModel(
        Func<PaperbunkrDbContext> createContext,
        Action openConnections,
        Func<string, string, IIndexerClient>? createIndexer = null,
        Func<string, string, string, string, IDownloadClient>? createDownloadClient = null)
    {
        _createContext = createContext;
        _openConnections = openConnections;
        _createIndexer = createIndexer ?? ((url, key) => new ProwlarrSearchClient(url, key));
        _createDownloadClient = createDownloadClient ?? ((url, user, password, category) => new QBittorrentClient(url, user, password, category));
        Load();
    }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _prowlarrUrl = string.Empty;

    /// <summary>What the user typed this session; blank means "leave the stored key alone". Cleared after a successful save.</summary>
    [ObservableProperty] private string _prowlarrApiKey = string.Empty;

    [ObservableProperty] private int _pollIntervalMinutes = 60;
    [ObservableProperty] private int _minSizeMb;
    [ObservableProperty] private int _maxSizeMb = 500;
    [ObservableProperty] private string _preferredReleaseGroups = string.Empty;
    [ObservableProperty] private string _ignoredWords = string.Empty;
    [ObservableProperty] private bool _preferCbz = true;

    // qBittorrent (slice 2)
    [ObservableProperty] private string _qBittorrentUrl = string.Empty;
    [ObservableProperty] private string _qBittorrentUsername = string.Empty;

    /// <summary>Write-only, like the Prowlarr key: blank means "keep the stored password".</summary>
    [ObservableProperty] private string _qBittorrentPassword = string.Empty;

    [ObservableProperty] private string _qBittorrentCategory = "paperbunkr-comics";
    [ObservableProperty] private bool _hasSavedQBittorrentPassword;

    // Import (slice 3)
    [ObservableProperty] private string _destinationFolderPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenameTemplatePreview), nameof(RenameTemplateIsValid))]
    private string _renameTemplate = AcquisitionSettings.DefaultRenameTemplate;

    /// <summary>The template as it was before the one-time upgrade to the shared grammar, shown only when it could not be converted.</summary>
    [ObservableProperty] private string? _renameTemplateOriginal;

    [ObservableProperty] private bool _renameTemplateUpgradeFailed;

    [ObservableProperty] private bool _writeComicInfo = true;
    [ObservableProperty] private bool _moveOriginalOnImport;

    // Auto-grab (slice 4)
    [ObservableProperty] private bool _autoGrab;
    [ObservableProperty] private int _autoGrabMinScore = 20;

    /// <summary>The user's library folders, offered as destinations.</summary>
    public ObservableCollection<string> DestinationChoices { get; } = new();

    public bool RenameTemplateIsValid => ImportNaming.Validate(RenameTemplate) is null;

    /// <summary>Shown when the upgrade could not convert the user's old template exactly, so they know why the default is in use and what they had.</summary>
    public bool HasTemplateUpgradeNotice => RenameTemplateUpgradeFailed && !string.IsNullOrWhiteSpace(RenameTemplateOriginal);

    public string TemplateUpgradeNotice => $"Your previous template couldn't be converted exactly, so the default is in use. It was: {RenameTemplateOriginal}";

    partial void OnRenameTemplateUpgradeFailedChanged(bool value) => NotifyTemplateNotice();

    partial void OnRenameTemplateOriginalChanged(string? value) => NotifyTemplateNotice();

    private void NotifyTemplateNotice()
    {
        OnPropertyChanged(nameof(HasTemplateUpgradeNotice));
        OnPropertyChanged(nameof(TemplateUpgradeNotice));
    }

    /// <summary>A live example of what the template produces (or why it is invalid), so mistakes show before anything is imported.</summary>
    public string RenameTemplatePreview
    {
        get
        {
            var error = ImportNaming.Validate(RenameTemplate);
            if (error is not null)
            {
                return error;
            }

            return "e.g. " + ImportNaming.Preview(RenameTemplate);
        }
    }

    public string QBittorrentPasswordWatermark => HasSavedQBittorrentPassword ? "Saved — leave blank to keep it" : "qBittorrent password (blank if none)";

    partial void OnHasSavedQBittorrentPasswordChanged(bool value) => OnPropertyChanged(nameof(QBittorrentPasswordWatermark));

    [ObservableProperty] private bool _hasSavedApiKey;
    [ObservableProperty] private bool _hasComicVineKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus))]
    [NotifyPropertyChangedFor(nameof(HasInfoStatus))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus))]
    [NotifyPropertyChangedFor(nameof(HasInfoStatus))]
    private bool _statusIsError;

    /// <summary>Two flags rather than one, because a plain binding can't combine "has a message" with "is / isn't an error".</summary>
    public bool HasErrorStatus => !string.IsNullOrEmpty(StatusMessage) && StatusIsError;

    public bool HasInfoStatus => !string.IsNullOrEmpty(StatusMessage) && !StatusIsError;

    /// <summary>The API-key field's placeholder: tells the user whether one is already stored.</summary>
    public string ApiKeyWatermark => HasSavedApiKey ? "Saved — leave blank to keep it" : "Prowlarr API key";

    partial void OnHasSavedApiKeyChanged(bool value) => OnPropertyChanged(nameof(ApiKeyWatermark));

    public void Load()
    {
        using var context = _createContext();
        var settings = context.GetOrCreateAcquisitionSettings();

        Enabled = settings.Enabled;
        ProwlarrUrl = settings.ProwlarrUrl;
        PollIntervalMinutes = settings.PollIntervalMinutes;
        MinSizeMb = settings.MinSizeMb;
        MaxSizeMb = settings.MaxSizeMb;
        PreferredReleaseGroups = settings.PreferredReleaseGroups;
        IgnoredWords = settings.IgnoredWords;
        PreferCbz = settings.PreferCbz;
        QBittorrentUrl = settings.QBittorrentUrl;
        QBittorrentCategory = settings.QBittorrentCategory;
        DestinationFolderPath = settings.DestinationFolderPath;
        RenameTemplate = settings.RenameTemplate;
        RenameTemplateOriginal = settings.RenameTemplateOriginal;
        RenameTemplateUpgradeFailed = settings.RenameTemplateUpgradeFailed;
        WriteComicInfo = settings.WriteComicInfo;
        MoveOriginalOnImport = settings.MoveOriginalOnImport;
        AutoGrab = settings.AutoGrab;
        AutoGrabMinScore = settings.AutoGrabMinScore;

        QBittorrentUsername = CredentialStore.Get(context, DownloadClientFactory.CredentialProvider, CredentialKind.Username) ?? string.Empty;
        HasSavedQBittorrentPassword = !string.IsNullOrEmpty(CredentialStore.Get(context, DownloadClientFactory.CredentialProvider, CredentialKind.Password));
        QBittorrentPassword = string.Empty;

        DestinationChoices.Clear();
        foreach (var path in context.WatchedFolders.Select(f => f.Path).OrderBy(p => p))
        {
            DestinationChoices.Add(path);
        }

        HasSavedApiKey = !string.IsNullOrEmpty(CredentialStore.Get(context, "Prowlarr", CredentialKind.ApiKey));
        HasComicVineKey = !string.IsNullOrEmpty(CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey));
        ProwlarrApiKey = string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        if (MaxSizeMb > 0 && MinSizeMb > MaxSizeMb)
        {
            SetStatus("The minimum size can't be larger than the maximum.", isError: true);
            return;
        }

        var templateError = ImportNaming.Validate(RenameTemplate);
        if (templateError is not null)
        {
            SetStatus($"The naming template is invalid: {templateError}", isError: true);
            return;
        }

        var destination = DestinationFolderPath.Trim();
        if (destination.Length > 0 && !Directory.Exists(destination))
        {
            SetStatus("The destination folder doesn't exist. Pick one of your library folders.", isError: true);
            return;
        }

        using var context = _createContext();
        var settings = context.GetOrCreateAcquisitionSettings();

        settings.Enabled = Enabled;
        settings.ProwlarrUrl = ProwlarrUrl.Trim();
        settings.PollIntervalMinutes = Math.Max(15, PollIntervalMinutes);
        settings.MinSizeMb = Math.Max(0, MinSizeMb);
        settings.MaxSizeMb = Math.Max(0, MaxSizeMb);
        settings.PreferredReleaseGroups = PreferredReleaseGroups.Trim();
        settings.IgnoredWords = IgnoredWords.Trim();
        settings.PreferCbz = PreferCbz;
        settings.QBittorrentUrl = QBittorrentUrl.Trim();
        // Paperbunkr only ever touches torrents in its own category, so an empty one is never allowed once qBittorrent is set up.
        settings.QBittorrentCategory = string.IsNullOrWhiteSpace(QBittorrentCategory) ? "paperbunkr-comics" : QBittorrentCategory.Trim();
        settings.DestinationFolderPath = destination;
        settings.RenameTemplate = RenameTemplate.Trim();
        // Saving a template is the explicit act that retires the pre-upgrade original (see TemplateUpgrade): from here it is the user's own.
        settings.RenameTemplateGrammar = TemplateGrammar.Organizer;
        settings.RenameTemplateOriginal = null;
        settings.RenameTemplateUpgradeFailed = false;
        settings.WriteComicInfo = WriteComicInfo;
        settings.MoveOriginalOnImport = MoveOriginalOnImport;
        settings.AutoGrab = AutoGrab;
        settings.AutoGrabMinScore = Math.Clamp(AutoGrabMinScore, 0, 500);
        context.SaveChanges();

        QBittorrentCategory = settings.QBittorrentCategory;
        AutoGrabMinScore = settings.AutoGrabMinScore;
        RenameTemplateOriginal = null;
        RenameTemplateUpgradeFailed = false;
        CredentialStore.Set(context, DownloadClientFactory.CredentialProvider, CredentialKind.Username, QBittorrentUsername.Trim());
        if (!string.IsNullOrWhiteSpace(QBittorrentPassword))
        {
            CredentialStore.Set(context, DownloadClientFactory.CredentialProvider, CredentialKind.Password, QBittorrentPassword);
            HasSavedQBittorrentPassword = true;
            QBittorrentPassword = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(ProwlarrApiKey))
        {
            CredentialStore.Set(context, "Prowlarr", CredentialKind.ApiKey, ProwlarrApiKey.Trim());
            HasSavedApiKey = true;
            ProwlarrApiKey = string.Empty;
        }

        PollIntervalMinutes = settings.PollIntervalMinutes;
        SetStatus(Enabled && (string.IsNullOrWhiteSpace(settings.ProwlarrUrl) || !HasSavedApiKey)
            ? "Saved. Add a Prowlarr address and API key to start searching."
            : "Saved.", isError: false);
    }

    /// <summary>Tests the address and key as currently typed (falling back to the stored key), without saving them.</summary>
    [RelayCommand]
    private async Task TestProwlarrAsync(CancellationToken cancellationToken)
    {
        var url = ProwlarrUrl.Trim();
        var key = ProwlarrApiKey.Trim();
        if (key.Length == 0)
        {
            using var context = _createContext();
            key = CredentialStore.Get(context, "Prowlarr", CredentialKind.ApiKey) ?? string.Empty;
        }

        if (url.Length == 0 || key.Length == 0)
        {
            SetStatus("Enter the Prowlarr address and API key first.", isError: true);
            return;
        }

        try
        {
            var result = await _createIndexer(url, key).TestConnectionAsync(cancellationToken);
            SetStatus(result.Message, isError: !result.Success);
        }
        catch (IndexerException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Couldn't test the connection: {ex.Message}", isError: true);
        }
    }

    /// <summary>Tests qBittorrent as currently typed (falling back to the stored password), without saving anything.</summary>
    [RelayCommand]
    private async Task TestQBittorrentAsync(CancellationToken cancellationToken)
    {
        var url = QBittorrentUrl.Trim();
        var password = QBittorrentPassword;
        if (password.Length == 0)
        {
            using var context = _createContext();
            password = CredentialStore.Get(context, DownloadClientFactory.CredentialProvider, CredentialKind.Password) ?? string.Empty;
        }

        if (url.Length == 0)
        {
            SetStatus("Enter the qBittorrent address first.", isError: true);
            return;
        }

        var category = string.IsNullOrWhiteSpace(QBittorrentCategory) ? "paperbunkr-comics" : QBittorrentCategory.Trim();
        try
        {
            var result = await _createDownloadClient(url, QBittorrentUsername.Trim(), password, category).TestConnectionAsync(cancellationToken);
            SetStatus(result.Message, isError: !result.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Couldn't test the connection: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void OpenConnections() => _openConnections();

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }
}
