using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Connections screen's service gallery (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md): tile groups,
/// per-tile status, the attention strip, Test connection, two-step Disconnect and the Metron tabs. The credential commands themselves stay in
/// <c>PreferencesScreenViewModel.cs</c> and are only referenced here through the rows.
/// </summary>
public partial class PreferencesScreenViewModel
{
    /// <summary>Tab keys of Metron's pane; same strings the Metron anchors name.</summary>
    public static class MetronTabKeys
    {
        public const string Account = "account";
        public const string Sync = "sync";
        public const string Activity = "activity";
    }

    private ConnectionStatusStore _connectionStatusStore = new(ConnectionStatusStore.DefaultPath());
    private readonly ConnectionProviderRow _gcdTile = ConnectionProviderRow.CreateGcdTile();
    private readonly ConnectionProviderRow _behaviorTile = ConnectionProviderRow.CreateBehaviorTile();
    private DateTimeOffset? _lastLocalServiceCheckUtc;

    /// <summary>The clock check results are stamped with (a seam so tests do not depend on wall time).</summary>
    internal Func<DateTimeOffset> ConnectionClock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Test seam: point the check cache at a temp file instead of the real one next to the database.</summary>
    internal void UseConnectionStatusStore(ConnectionStatusStore store) => _connectionStatusStore = store;

    /// <summary>ComicVine, Metron and the Grand Comics Database download.</summary>
    public ObservableCollection<ConnectionProviderRow> MetadataTiles { get; private set; } = new();

    /// <summary>The eight tracker accounts and the Behavior tile.</summary>
    public ObservableCollection<ConnectionProviderRow> TrackerTiles { get; private set; } = new();

    /// <summary>Prowlarr and qBittorrent (the same collection <see cref="DownloadProviderRows"/> already is).</summary>
    public ObservableCollection<ConnectionProviderRow> DownloadTiles => DownloadProviderRows;

    /// <summary>Metron's pane tabs: Account (login), Sync (what is sent), Activity.</summary>
    public SettingsTabs MetronTabs { get; } = new(new SettingsTabItem[]
    {
        new(MetronTabKeys.Account, "Account"),
        new(MetronTabKeys.Sync, "Sync"),
        new(MetronTabKeys.Activity, "Activity"),
    });

    private IEnumerable<ConnectionProviderRow> AccountTiles => SourceProviderRows.Concat(TrackerProviderRows).Concat(DownloadProviderRows);

    private IEnumerable<ConnectionProviderRow> AllTiles => MetadataTiles.Concat(TrackerTiles).Concat(DownloadTiles);

    /// <summary>Called once from the constructor, after every row's command references are wired.</summary>
    private void InitConnectionTiles()
    {
        MetadataTiles = new ObservableCollection<ConnectionProviderRow>(SourceProviderRows.Append(_gcdTile));
        TrackerTiles = new ObservableCollection<ConnectionProviderRow>(TrackerProviderRows.Append(_behaviorTile));

        foreach (var row in AllTiles)
        {
            row.PropertyChanged += OnTilePropertyChanged;
            if (row.DisconnectCommand is { } disconnect)
            {
                row.DisconnectConfirm = new TwoStepConfirm(() => disconnect.Execute(null), "Disconnect", "Confirm disconnect?");
            }
        }

        _behaviorTile.InfoText = "7 options";
        GcdData.PropertyChanged += (_, _) => RefreshInfoTiles();
        MetronSync.PropertyChanged += (_, _) => RefreshInfoTiles();
        MetronTabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsTabs.SelectedKey))
            {
                RaiseConnectionPaneVisibility();
            }
        };

        RefreshInfoTiles();
    }

    /// <summary>Loads the cached check results onto the rows. Called after the connected state has been read, so a stored failure is only applied to an account that is really set up.</summary>
    private void LoadStoredChecks()
    {
        var stored = _connectionStatusStore.Load();
        foreach (var row in AccountTiles)
        {
            if (row.IsConnected && row.LastCheck is null && stored.TryGetValue(row.Id, out var check))
            {
                row.LastCheck = check;
            }
        }
    }

    /// <summary>Syncs the two non-account tiles (and Metron's "Syncing") with the view-models that own that state.</summary>
    private void RefreshInfoTiles()
    {
        if (GcdData is null || MetronSync is null)
        {
            return; // the constructor is still wiring things up
        }

        _gcdTile.IsConnected = GcdData.IsInstalled;
        _gcdTile.InfoText = GcdData.IsInstalled ? $"Data {GcdData.InstalledDumpDate}" : "Not downloaded";
        MetronRow.IsSyncing = MetronRow.IsConnected && MetronSync.IsEnabled;
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ConnectionProviderRow row && e.PropertyName == nameof(ConnectionProviderRow.IsConnected))
        {
            if (!row.IsConnected)
            {
                // A disconnect (or a removed key) supersedes whatever the last check said.
                row.DisconnectConfirm?.Cancel();
                if (row.LastCheck is not null)
                {
                    row.LastCheck = null;
                    _connectionStatusStore.Remove(row.Id);
                }
            }
            else if (row.LastCheck is { Ok: false })
            {
                // Connecting again is a fresh sign-in: the old failure no longer applies.
                row.LastCheck = null;
                _connectionStatusStore.Remove(row.Id);
            }

            if (row.IsMetron)
            {
                RefreshInfoTiles();
            }
        }

        UpdateConnectionSummary();
    }

    // ===================== Header summary and attention strip =====================

    /// <summary>"4 of 12 connected" - accounts only (not the GCD download or the Behavior tile).</summary>
    public string ConnectionSummaryText
    {
        get
        {
            int total = AccountTiles.Count();
            int connected = AccountTiles.Count(r => r.IsConnected);
            return $"{connected} of {total} connected";
        }
    }

    /// <summary>The first account whose last check failed, or null.</summary>
    public ConnectionProviderRow? AttentionRow => AccountTiles.FirstOrDefault(r => r.NeedsAttention);

    public bool HasAttention => AttentionRow is not null;

    public string AttentionTitle
    {
        get
        {
            int count = AccountTiles.Count(r => r.NeedsAttention);
            return count == 1 ? "1 needs attention" : $"{count} need attention";
        }
    }

    public string AttentionText
    {
        get
        {
            var row = AttentionRow;
            if (row is null)
            {
                return string.Empty;
            }

            string detail = string.IsNullOrWhiteSpace(row.LastCheck?.Detail) ? row.StatusText : row.LastCheck!.Detail!;
            return $"{row.DisplayName}: {detail}";
        }
    }

    private void UpdateConnectionSummary()
    {
        OnPropertyChanged(nameof(ConnectionSummaryText));
        OnPropertyChanged(nameof(AttentionRow));
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(AttentionTitle));
        OnPropertyChanged(nameof(AttentionText));
    }

    [RelayCommand]
    private void OpenAttentionRow()
    {
        if (AttentionRow is { } row)
        {
            OpenConnectionDialog(row);
        }
    }

    // ===================== Pane visibility =====================

    partial void OnSelectedConnectionProviderChanged(ConnectionProviderRow? oldValue, ConnectionProviderRow? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
            oldValue.DisconnectConfirm?.Cancel();
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
            newValue.RefreshActivityText();
            if (newValue.IsMetron)
            {
                MetronTabs.Select(MetronTabKeys.Account);
            }
        }

        RaiseConnectionPaneVisibility();
    }

    public bool IsMetronSelected => SelectedConnectionProvider?.IsMetron == true;

    /// <summary>The credential form shows for the credential accounts, and for Metron only under its Account tab.</summary>
    public bool ShowCredentialBody => SelectedConnectionProvider is { Kind: ConnectionDialogKind.Credential } row
        && (!row.IsMetron || MetronTabs.SelectedKey == MetronTabKeys.Account);

    public bool ShowMetronSyncBody => IsMetronSelected && MetronTabs.SelectedKey == MetronTabKeys.Sync;

    public bool ShowMetronActivityBody => IsMetronSelected && MetronTabs.SelectedKey == MetronTabKeys.Activity;

    public bool ShowGcdBody => SelectedConnectionProvider?.Kind == ConnectionDialogKind.Gcd;

    public bool ShowBehaviorBody => SelectedConnectionProvider?.Kind == ConnectionDialogKind.TrackerBehavior;

    /// <summary>Accounts other than Metron get a one-line activity footer; Metron has its Activity tab.</summary>
    public bool ShowAccountActivity => SelectedConnectionProvider is { IsAccount: true, IsMetron: false };

    public bool ShowTestButton => SelectedConnectionProvider?.IsAccount == true;

    private void RaiseConnectionPaneVisibility()
    {
        OnPropertyChanged(nameof(IsMetronSelected));
        OnPropertyChanged(nameof(ShowCredentialBody));
        OnPropertyChanged(nameof(ShowMetronSyncBody));
        OnPropertyChanged(nameof(ShowMetronActivityBody));
        OnPropertyChanged(nameof(ShowGcdBody));
        OnPropertyChanged(nameof(ShowBehaviorBody));
        OnPropertyChanged(nameof(ShowAccountActivity));
        OnPropertyChanged(nameof(ShowTestButton));
    }

    // ===================== Test connection =====================

    private static readonly Regex AuthFailureWords = new(
        @"401|403|unauthori[sz]ed|forbidden|api key|password|credential|log ?in|sign ?in|token",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A failed check is an auth failure when its message talks about credentials; otherwise the service was simply unreachable.</summary>
    internal static ConnectionFailure ClassifyFailure(string? message) =>
        !string.IsNullOrWhiteSpace(message) && AuthFailureWords.IsMatch(message) ? ConnectionFailure.Auth : ConnectionFailure.Unreachable;

    /// <summary>
    /// Prowlarr and qBittorrent run their real test and the result is remembered. Every other provider has no read-only call that can be made
    /// without changing data, so it reports that honestly instead of showing a pass it did not earn (see the design doc's "Test connection scope").
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync(ConnectionProviderRow? row)
    {
        row ??= SelectedConnectionProvider;
        if (row is null || !row.IsAccount)
        {
            return;
        }

        if (!row.IsConnected)
        {
            ConnectionDialogStatus = $"{row.DisplayName} is not set up yet, so there is nothing to test.";
            return;
        }

        switch (row.Kind)
        {
            case ConnectionDialogKind.Prowlarr:
                await RunLocalServiceTestAsync(row, Acquisition.TestProwlarrCommand);
                break;
            case ConnectionDialogKind.QBittorrent:
                await RunLocalServiceTestAsync(row, Acquisition.TestQBittorrentCommand);
                break;
            default:
                ConnectionDialogStatus = $"{row.DisplayName}'s sign-in is saved. This service has no read-only check, so it is verified the first time it is used.";
                break;
        }
    }

    private async Task RunLocalServiceTestAsync(ConnectionProviderRow row, IAsyncRelayCommand test)
    {
        await test.ExecuteAsync(null);

        bool ok = !Acquisition.HasErrorStatus;
        string detail = Acquisition.StatusMessage ?? string.Empty;
        var now = ConnectionClock();
        var check = ok ? ConnectionCheck.Success(now, detail) : ConnectionCheck.Failed(ClassifyFailure(detail), now, detail);

        row.LastCheck = check;
        _connectionStatusStore.Record(row.Id, check);
        ConnectionDialogStatus = detail;
    }

    /// <summary>
    /// Checks the saved download clients once when the Connections page is first shown (and at most every five minutes after). Local services
    /// only - nothing is sent to an account service, and nothing polls in the background.
    /// </summary>
    private async Task CheckLocalServicesAsync()
    {
        var now = ConnectionClock();
        if (_lastLocalServiceCheckUtc is { } last && now - last < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastLocalServiceCheckUtc = now;
        foreach (var row in DownloadProviderRows.Where(r => r.IsConnected).ToList())
        {
            try
            {
                await TestConnectionAsync(row);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.LastCheck = ConnectionCheck.Failed(ConnectionFailure.Unreachable, now, ex.Message);
            }
        }
    }

    // ===================== Search / deep-link reveal =====================

    /// <summary>
    /// Selects the tile that owns a pane-hosted anchor before the shell scrolls to it, so a search hit on "pull list" or "GCD" lands on
    /// something visible. Anchors that tag a whole tile group need no selection.
    /// </summary>
    private void RevealConnectionAnchor(string anchorKey)
    {
        switch (anchorKey)
        {
            case "connections.gcdData":
                OpenConnectionDialog(_gcdTile);
                break;
            case "connections.metronSync":
                OpenConnectionDialog(MetronRow);
                MetronTabs.Select(MetronTabKeys.Sync);
                break;
            case "connections.trackingBehavior":
                OpenConnectionDialog(_behaviorTile);
                break;
        }
    }
}
