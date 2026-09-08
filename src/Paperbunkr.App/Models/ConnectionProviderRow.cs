using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty]
    private bool _isConnected;

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
    public ICommand? DisconnectCommand { get; set; }

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
            Id = "ComicVine", DisplayName = "ComicVine", Kind = ConnectionDialogKind.Token,
            PrimaryActionLabel = "Save", HelpText = "Get one free at comicvine.gamespot.com/api",
        },
        new()
        {
            Id = "Metron", DisplayName = "Metron", Kind = ConnectionDialogKind.Credential,
            PrimaryActionLabel = "Save", HelpText = "Sign in with your metron.cloud account.",
        },
    };

    public static List<ConnectionProviderRow> CreateTrackerProviders() => new()
    {
        new()
        {
            Id = nameof(TrackingService.AniList), DisplayName = "AniList", Kind = ConnectionDialogKind.OAuth,
            PrimaryActionLabel = "Connect", PasteWatermark = "Paste token here",
            HelpText = "Register your own app at anilist.co/settings/developer, then paste its Client ID.",
        },
        new()
        {
            Id = nameof(TrackingService.MyAnimeList), DisplayName = "MyAnimeList", Kind = ConnectionDialogKind.OAuth,
            PrimaryActionLabel = "Connect", PasteWatermark = "Paste code here",
            HelpText = "Register your own app at myanimelist.net/apiconfig/create (needs manual approval), then paste its Client ID. The page after sign-in will fail to load - that's expected. Copy the \"code\" value from its address bar.",
        },
        new()
        {
            Id = nameof(TrackingService.Shikimori), DisplayName = "Shikimori", Kind = ConnectionDialogKind.OAuth,
            RequiresClientSecret = true, PrimaryActionLabel = "Connect", PasteWatermark = "Paste code here",
            HelpText = "Register your own app at shikimori.one/oauth/applications, then paste its Client ID and Secret. Shikimori will show you a code to copy - paste it back here.",
        },
        new()
        {
            Id = nameof(TrackingService.Bangumi), DisplayName = "Bangumi", Kind = ConnectionDialogKind.Token,
            PrimaryActionLabel = "Save",
            HelpText = "Generate a Personal Access Token at bgm.tv/dev/app, then paste it here - no browser sign-in needed.",
        },
        new()
        {
            Id = nameof(TrackingService.MangaBaka), DisplayName = "MangaBaka", Kind = ConnectionDialogKind.Token,
            PrimaryActionLabel = "Save",
            HelpText = "Generate a Personal Access Token at mangabaka.org - My profile - Settings - API and Apps, then paste it here. The token has full access to your MangaBaka account - keep it private.",
        },
        new()
        {
            Id = nameof(TrackingService.MangaUpdates), DisplayName = "MangaUpdates", Kind = ConnectionDialogKind.Credential,
            PrimaryActionLabel = "Connect",
            HelpText = "Sign in with your mangaupdates.com account. Your password is used once to connect and is never stored - only the session it returns is.",
        },
        new()
        {
            Id = nameof(TrackingService.Kitsu), DisplayName = "Kitsu", Kind = ConnectionDialogKind.Credential,
            PrimaryActionLabel = "Connect",
            HelpText = "Sign in with your kitsu.app account. Your password is used once to connect and is never stored - only the session it returns is.",
        },
    };
}
