using System;
using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Which of the three dialog shapes a <see cref="ConnectionProviderRow"/> opens (docs/superpowers/
/// specs/2026-09-06-connections-tracker-dialog-redesign-design.md). Driven by the provider's real
/// auth mechanism, not a generic field-visibility switch - three concrete dialog views, one per kind.
/// </summary>
public enum ConnectionDialogKind
{
    /// <summary>AniList, MyAnimeList, Shikimori - Client ID (+ Client Secret for Shikimori only), open-browser-then-paste-back.</summary>
    OAuth,

    /// <summary>Bangumi, MangaBaka, ComicVine - a single pasted secret, no browser step.</summary>
    Token,

    /// <summary>Metron, MangaUpdates, Kitsu - username + password.</summary>
    Credential,

    /// <summary>MangaDex - Client ID + Client Secret (its own registered "Personal Client", no
    /// shared public client like Kitsu's) *and* username + password, confirmed live 2026-09-18.
    /// The only provider needing both an app registration and account credentials at once.</summary>
    CredentialWithClient,

    /// <summary>Prowlarr - address + API key, with a Test button. Edited through the acquisition view-model (one source of truth for these fields).</summary>
    Prowlarr,

    /// <summary>qBittorrent - web UI address, username, password and category, with a Test button. Same view-model as <see cref="Prowlarr"/>.</summary>
    QBittorrent,

    /// <summary>The Grand Comics Database data download (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md) - a tile, not an account.</summary>
    Gcd,

    /// <summary>The tracker behavior toggles - the last tile of the Trackers group, not an account.</summary>
    TrackerBehavior,
}

/// <summary>
/// One row in the Connections screen's provider list (docs/superpowers/specs/2026-09-06-
/// connections-tracker-dialog-redesign-design.md) - the BrandMark/name/checkmark row that opens a
/// dialog on tap. <see cref="Id"/> matches the string this provider's credentials are stored under
/// in <c>CredentialStore</c> (either a <c>TrackingService</c> name or "ComicVine"/"Metron" - see
/// <c>ProviderCredential.Provider</c>'s own doc comment on why it's a free-form string, not an FK).
/// <see cref="IsConnected"/> is refreshed externally by <c>PreferencesScreenViewModel</c>'s existing
/// <c>RefreshTrackerConnectionState</c>/<c>RefreshSourceCredentials</c>, not computed here - this
/// class has no database access of its own.
/// </summary>
public partial class ConnectionProviderRow : ObservableObject
{
    public required string Id { get; init; }

    /// <summary>Also the <c>BrandMark</c> <c>Value</c> for this provider.</summary>
    public required string DisplayName { get; init; }

    public required ConnectionDialogKind Kind { get; init; }

    /// <summary>Only ever true for Shikimori - the one OAuth provider needing a Client Secret alongside its Client ID.</summary>
    public bool RequiresClientSecret { get; init; }

    /// <summary>"Save" for providers with no verification handshake (ComicVine, Metron); "Connect" for every other kind.</summary>
    public required string PrimaryActionLabel { get; init; }

    /// <summary>Help text shown at the top of the dialog - the same copy each provider's inline block already used.</summary>
    public required string HelpText { get; init; }

    /// <summary>Which captioned tile group this row sits in on the Connections screen.</summary>
    public ConnectionGroup Group { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusIsGood))]
    [NotifyPropertyChangedFor(nameof(StatusIsWarn))]
    [NotifyPropertyChangedFor(nameof(StatusIsError))]
    [NotifyPropertyChangedFor(nameof(NeedsAttention))]
    [NotifyPropertyChangedFor(nameof(CanDisconnect))]
    private bool _isConnected;

    // ===================== Tile status (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md) =====================

    /// <summary>The last explicit check of this provider, loaded from / saved to <c>ConnectionStatusStore</c>. Null = never checked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusIsGood))]
    [NotifyPropertyChangedFor(nameof(StatusIsWarn))]
    [NotifyPropertyChangedFor(nameof(StatusIsError))]
    [NotifyPropertyChangedFor(nameof(NeedsAttention))]
    [NotifyPropertyChangedFor(nameof(ActivityText))]
    private ConnectionCheck? _lastCheck;

    /// <summary>True while this account is actively syncing in the background (Metron's account sync switched on).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusIsGood))]
    private bool _isSyncing;

    /// <summary>Replaces the default status wording for a tile whose state is richer than "connected" (the GCD data version, "7 options").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _infoText = string.Empty;

    /// <summary>The tile whose pane is open.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Two-step inline Disconnect, assigned by the screen's view-model once <see cref="DisconnectCommand"/> exists.</summary>
    public Paperbunkr.App.ViewModels.TwoStepConfirm? DisconnectConfirm { get; set; }

    /// <summary>The Behavior tile is not an account, so it has no connection state of its own.</summary>
    public bool IsAccount => Kind is not (ConnectionDialogKind.TrackerBehavior or ConnectionDialogKind.Gcd);

    /// <summary>Metron has its own Account / Sync / Activity tabs.</summary>
    public bool IsMetron => Id == "Metron";

    public bool IsBrandTile => Kind != ConnectionDialogKind.TrackerBehavior;

    public ConnectionStatus Status => Kind == ConnectionDialogKind.TrackerBehavior
        ? ConnectionStatus.Info
        : ConnectionStatusResolver.Resolve(IsConnected, LastCheck, IsSyncing);

    public string StatusText
    {
        get
        {
            var status = Status;
            if (!string.IsNullOrEmpty(InfoText) && status is ConnectionStatus.Info or ConnectionStatus.NotSetUp or ConnectionStatus.Connected or ConnectionStatus.Syncing)
            {
                return InfoText;
            }

            return ConnectionStatusResolver.DefaultText(status);
        }
    }

    public bool StatusIsGood => Status is ConnectionStatus.Connected or ConnectionStatus.Syncing;

    public bool StatusIsWarn => Status == ConnectionStatus.Expired;

    public bool StatusIsError => Status == ConnectionStatus.Unreachable;

    public bool NeedsAttention => Status is ConnectionStatus.Expired or ConnectionStatus.Unreachable;

    public bool CanDisconnect => IsConnected && DisconnectCommand is not null;

    /// <summary>"Last checked: never" / "Last checked: 12 min ago - <detail>".</summary>
    public string ActivityText
    {
        get
        {
            string age = ConnectionStatusResolver.AgeText(LastCheck?.CheckedUtc, DateTimeOffset.UtcNow);
            return string.IsNullOrWhiteSpace(LastCheck?.Detail) ? $"Last checked: {age}" : $"Last checked: {age} · {LastCheck!.Detail}";
        }
    }

    /// <summary>Re-raises <see cref="ActivityText"/> (its wording depends on the clock, not on a property that changed).</summary>
    public void RefreshActivityText() => OnPropertyChanged(nameof(ActivityText));

    // ===================== Generic per-Kind bindable fields (docs/superpowers/specs/2026-09-07-
    // connections-redesign-design.md) - lets the dialog use one template per Kind instead of 9
    // DisplayName-gated copies. Only the fields for this row's own Kind are ever populated/read;
    // the rest sit unused at their default, which is harmless for 9 rows on one small model. =====================

    /// <summary>OAuth only.</summary>
    [ObservableProperty]
    private string _clientId = string.Empty;

    /// <summary>OAuth only - populated only when <see cref="RequiresClientSecret"/> (Shikimori).</summary>
    [ObservableProperty]
    private string _clientSecret = string.Empty;

    /// <summary>OAuth only - the pasted-back code/token from the provider's authorization page.</summary>
    [ObservableProperty]
    private string _pastedValue = string.Empty;

    /// <summary>OAuth only - "Paste token here" (AniList) vs "Paste code here" (MyAnimeList, Shikimori).</summary>
    public string PasteWatermark { get; init; } = string.Empty;

    /// <summary>Token only - the single pasted secret (PAT or API key).</summary>
    [ObservableProperty]
    private string _secretValue = string.Empty;

    /// <summary>Credential only.</summary>
    [ObservableProperty]
    private string _username = string.Empty;

    /// <summary>Credential only.</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>
    /// Metron only (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md): the API token is the main field
    /// (<see cref="SecretValue"/>) and the old username + password sit in a collapsed "Legacy login" section.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoginFields))]
    private bool _isLegacyLoginExpanded;

    /// <summary>Username/password fields: always for MangaUpdates/Kitsu, only when expanded for Metron.</summary>
    public bool ShowLoginFields => !IsMetron || IsLegacyLoginExpanded;

    /// <summary>Metron only: the result of the last Test connection.</summary>
    [ObservableProperty]
    private string _testStatus = string.Empty;

    [RelayCommand]
    private void ToggleLegacyLogin() => IsLegacyLoginExpanded = !IsLegacyLoginExpanded;

    /// <summary>Metron only - Test connection.</summary>
    public ICommand? TestCommand { get; set; }

    /// <summary>
    /// OAuth/Token/Credential command references, assigned once by
    /// <c>PreferencesScreenViewModel</c>'s constructor to the existing per-provider
    /// <c>[RelayCommand]</c>-generated command for this row - the dialog's generic per-Kind
    /// template binds to these instead of a hardcoded per-provider command name. Command bodies
    /// are unchanged; this only tells the view which one applies to which row.
    /// </summary>
    public ICommand? ConnectCommand { get; set; }

    /// <summary>OAuth only - completes the paste-back handshake.</summary>
    public ICommand? CompleteCommand { get; set; }

    /// <summary>Token only.</summary>
    public ICommand? SaveCommand { get; set; }

    /// <summary>Credential only - label ("Save" vs "Connect") comes from <see cref="PrimaryActionLabel"/>.</summary>
    public ICommand? PrimaryCommand { get; set; }

    /// <summary>All three shapes.</summary>
    public ICommand? DisconnectCommand
    {
        get => _disconnectCommand;
        set
        {
            _disconnectCommand = value;
            OnPropertyChanged(nameof(CanDisconnect));
        }
    }

    private ICommand? _disconnectCommand;

    /// <summary>
    /// Both provider lists, in the same fixed display order the current inline UI already uses.
    /// Reading List Sources first, then Trackers - matches <c>ConnectionsSection.axaml</c>'s two
    /// existing groupBox sections. Factory <b>methods</b>, not static properties returning shared
    /// instances - each row is a mutable <see cref="ObservableObject"/> (<see cref="IsConnected"/>),
    /// and <c>PreferencesScreenViewModel</c> is constructed fresh per test (and potentially per
    /// window) - sharing instances across callers would leak one instance's connection state into
    /// every other instance's list.
    /// </summary>
    public static List<ConnectionProviderRow> CreateSourceProviders() => new()
    {
        new()
        {
            Id = "ComicVine", DisplayName = "ComicVine", Group = ConnectionGroup.Metadata, Kind = ConnectionDialogKind.Token,
            PrimaryActionLabel = "Save", HelpText = "Get one free at comicvine.gamespot.com/api",
        },
        new()
        {
            Id = "Metron", DisplayName = "Metron", Group = ConnectionGroup.Metadata, Kind = ConnectionDialogKind.Credential,
            PrimaryActionLabel = "Save", HelpText = "Paste the API token from your account settings on metron.cloud.",
        },
    };

    /// <summary>The download-automation services acquisition talks to (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 7).</summary>
    public static List<ConnectionProviderRow> CreateDownloadProviders() => new()
    {
        new()
        {
            Id = "Prowlarr", DisplayName = "Prowlarr", Group = ConnectionGroup.Downloads, Kind = ConnectionDialogKind.Prowlarr,
            PrimaryActionLabel = "Save", HelpText = "Your own Prowlarr finds releases. Find the API key in Prowlarr under Settings → General; it is stored encrypted.",
        },
        new()
        {
            Id = "qBittorrent", DisplayName = "qBittorrent", Group = ConnectionGroup.Downloads, Kind = ConnectionDialogKind.QBittorrent,
            PrimaryActionLabel = "Save", HelpText = "Approved releases are sent here. Paperbunkr puts them in its own category and only ever touches torrents in it.",
        },
    };

    public static List<ConnectionProviderRow> CreateTrackerProviders() => new()
    {
        new()
        {
            Id = nameof(TrackingService.AniList), DisplayName = "AniList", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.OAuth,
            PrimaryActionLabel = "Connect", PasteWatermark = "Paste token here",
            HelpText = "Register your own app at anilist.co/settings/developer, then paste its Client ID.",
        },
        new()
        {
            Id = nameof(TrackingService.MyAnimeList), DisplayName = "MyAnimeList", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.OAuth,
            PrimaryActionLabel = "Connect", PasteWatermark = "Paste code here",
            HelpText = "Register your own app at myanimelist.net/apiconfig/create (needs manual approval), then paste its Client ID. The page after sign-in will fail to load - that's expected. Copy the \"code\" value from its address bar.",
        },
        new()
        {
            Id = nameof(TrackingService.Shikimori), DisplayName = "Shikimori", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.OAuth,
            RequiresClientSecret = true, PrimaryActionLabel = "Connect", PasteWatermark = "Paste code here",
            HelpText = "Register your own app at shikimori.one/oauth/applications, then paste its Client ID and Secret. Shikimori will show you a code to copy - paste it back here.",
        },
        new()
        {
            Id = nameof(TrackingService.Bangumi), DisplayName = "Bangumi", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.Token,
            PrimaryActionLabel = "Save",
            HelpText = "Generate a Personal Access Token at bgm.tv/dev/app, then paste it here - no browser sign-in needed.",
        },
        new()
        {
            Id = nameof(TrackingService.MangaBaka), DisplayName = "MangaBaka", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.Token,
            PrimaryActionLabel = "Save",
            HelpText = "Generate a Personal Access Token at mangabaka.org - My profile - Settings - API and Apps, then paste it here. The token has full access to your MangaBaka account - keep it private.",
        },
        new()
        {
            Id = nameof(TrackingService.MangaUpdates), DisplayName = "MangaUpdates", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.Credential,
            PrimaryActionLabel = "Connect",
            HelpText = "Sign in with your mangaupdates.com account. Your password is used once to connect and is never stored - only the session it returns is.",
        },
        new()
        {
            Id = nameof(TrackingService.Kitsu), DisplayName = "Kitsu", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.Credential,
            PrimaryActionLabel = "Connect",
            HelpText = "Sign in with your kitsu.app account. Your password is used once to connect and is never stored - only the session it returns is.",
        },
        new()
        {
            Id = nameof(TrackingService.MangaDex), DisplayName = "MangaDex", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.CredentialWithClient,
            PrimaryActionLabel = "Connect",
            HelpText = "Register a Personal Client at mangadex.org - Settings - API Clients, then paste its Client ID/Secret alongside your mangadex.org username and password. Your password is used once to connect and is never stored - only the session it returns is.",
        },
    };

    /// <summary>The two tiles that are not accounts: the Grand Comics Database data download (Metadata group) and the tracker behavior toggles (Trackers group).</summary>
    public static ConnectionProviderRow CreateGcdTile() => new()
    {
        Id = "Gcd", DisplayName = "Grand Comics DB", Group = ConnectionGroup.Metadata, Kind = ConnectionDialogKind.Gcd,
        PrimaryActionLabel = "Download",
        HelpText = "An optional download that links series which continue each other and orders story events by on-sale date. Nothing goes online until you press Download or Check for update.",
    };

    public static ConnectionProviderRow CreateBehaviorTile() => new()
    {
        Id = "TrackerBehavior", DisplayName = "Behavior", Group = ConnectionGroup.Trackers, Kind = ConnectionDialogKind.TrackerBehavior,
        PrimaryActionLabel = string.Empty,
        HelpText = "How tracker accounts are used. Each option is read fresh when it applies, so a change takes effect without a restart.",
    };
}
