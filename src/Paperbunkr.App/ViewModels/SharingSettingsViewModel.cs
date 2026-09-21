using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Sharing;
using Paperbunkr.Sharing;
using Paperbunkr.Sharing.Client;
using Paperbunkr.Sharing.Discovery;

namespace Paperbunkr.App.ViewModels;

/// <summary>One shareable list in the host's "selected lists" picker.</summary>
public sealed partial class ShareListChoiceViewModel : ObservableObject
{
    public ShareListChoiceViewModel(string kind, int id, string name, bool isSelected)
    {
        Kind = kind;
        Id = id;
        Name = name;
        _isSelected = isSelected;
    }

    public string Kind { get; }

    public int Id { get; }

    public string Name { get; }

    public string KindLabel => Kind switch
    {
        DbShareCatalogSource.ReadingListKind => "Reading list",
        DbShareCatalogSource.CollectionKind => "Collection",
        _ => "Smart list",
    };

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One host found by "Find on this network".</summary>
public sealed partial class DiscoveredHostRowViewModel : ObservableObject
{
    private readonly Action<DiscoveredHost> _use;

    public DiscoveredHostRowViewModel(DiscoveredHost host, Action<DiscoveredHost> use)
    {
        Host = host;
        _use = use;
    }

    public DiscoveredHost Host { get; }

    public string Name => Host.DisplayName;

    public string Address => $"{Host.Address}:{Host.Port}";

    [RelayCommand]
    private void Use() => _use(Host);
}

/// <summary>
/// Preferences → Sharing (docs/superpowers/2026-09-19-remote-library-sharing-design.md §6/§9):
/// the host controls ("Share this library") and the saved remote libraries. Its own view model, like
/// Acquisition, so <see cref="PreferencesScreenViewModel"/> doesn't grow further. Everything is
/// explicit: sharing is off until turned on, needs a password, and shares nothing until lists or the
/// whole library are chosen.
/// </summary>
public sealed partial class SharingSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ShareHostService _host;
    private readonly RemoteLibraryService _remote;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan, Task<IReadOnlyList<DiscoveredHost>>> _browse;
    private readonly Func<Task<IReadOnlyList<string>>> _publicNetworks;
    private bool _loading;
    private ProbeResult? _pendingProbe;
    private string? _pendingPassword;

    /// <param name="post">Marshals work onto the UI thread; defaults to the Avalonia dispatcher. Tests pass their own so they don't need a pumped dispatcher.</param>
    /// <param name="browse">LAN discovery; defaults to mDNS. Tests pass a fake so they don't depend on multicast.</param>
    public SharingSettingsViewModel(ShareHostService host, RemoteLibraryService remote, Func<PaperbunkrDbContext> contextFactory, Action<Action>? post = null,
        Func<TimeSpan, Task<IReadOnlyList<DiscoveredHost>>>? browse = null,
        Func<Task<IReadOnlyList<string>>>? publicNetworks = null)
    {
        _publicNetworks = publicNetworks ?? (() => NetworkProfileProbe.GetPublicNetworksAsync());
        _browse = browse ?? (d => ShareDiscovery.BrowseAsync(d));
        _host = host;
        _remote = remote;
        _contextFactory = contextFactory;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _host.StateChanged += OnHostChanged;
        _remote.Changed += OnRemoteChanged;
        Load();
    }

    public void Dispose()
    {
        _host.StateChanged -= OnHostChanged;
        _remote.Changed -= OnRemoteChanged;
    }

    // ---------------------------------------------------------------- host

    [ObservableProperty]
    private bool _shareEnabled;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _portText = "7614";

    [ObservableProperty]
    private ShareMode _scopeMode = ShareMode.None;

    public bool ScopeNothing { get => ScopeMode == ShareMode.None; set { if (value) ScopeMode = ShareMode.None; } }

    public bool ScopeAll { get => ScopeMode == ShareMode.All; set { if (value) ScopeMode = ShareMode.All; } }

    public bool ScopeSelected { get => ScopeMode == ShareMode.Selected; set { if (value) ScopeMode = ShareMode.Selected; } }

    partial void OnScopeModeChanged(ShareMode value)
    {
        OnPropertyChanged(nameof(ScopeNothing));
        OnPropertyChanged(nameof(ScopeAll));
        OnPropertyChanged(nameof(ScopeSelected));
    }

    public ObservableCollection<ShareListChoiceViewModel> ListChoices { get; } = new();

    public bool HasListChoices => ListChoices.Count > 0;

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private string _hostMessage = "";

    public bool HasHostMessage => !string.IsNullOrEmpty(HostMessage);

    partial void OnHostMessageChanged(string value) => OnPropertyChanged(nameof(HasHostMessage));

    public bool HasPassword => _host.HasPassword;

    public string PasswordStatus => _host.HasPassword ? "A password is set. Type a new one to change it." : "No password set yet. Sharing can't start without one.";

    public string HostStatus => _host.IsRunning
        ? $"Sharing on port {_host.Port}"
        : _host.LastError is { } error ? error : "Sharing is off";

    public string ClientsText => _host.IsRunning
        ? _host.ConnectedClients switch { 0 => "No one connected right now", 1 => "1 connection", var n => $"{n} connections" }
        : "";

    public bool IsHostRunning => _host.IsRunning;

    /// <summary>Set when Windows classes an active network as Public. A warning only - sharing is never blocked by it.</summary>
    [ObservableProperty]
    private string? _networkWarning;

    public bool HasNetworkWarning => !string.IsNullOrEmpty(NetworkWarning);

    partial void OnNetworkWarningChanged(string? value) => OnPropertyChanged(nameof(HasNetworkWarning));

    /// <summary>Re-checks the network classification (called when the section is shown and when sharing is switched on).</summary>
    public async Task RefreshNetworkWarningAsync()
    {
        IReadOnlyList<string> networks = await _publicNetworks().ConfigureAwait(true);
        NetworkWarning = NetworkProfileProbe.WarningFor(networks);
    }

    public string CertificateFingerprint => RemoteSourceRowViewModel.FormatFingerprint(SafeFingerprint());

    [ObservableProperty]
    private bool _isConfirmingRegenerate;

    [RelayCommand]
    private void SetPassword()
    {
        if (NewPassword.Length < 8)
        {
            HostMessage = "Use at least 8 characters.";
            return;
        }

        _host.SetPassword(NewPassword);
        NewPassword = "";
        HostMessage = _host.IsRunning ? "Password changed. Apply changes to make it take effect (clients will be asked again)." : "Password saved.";
        RaiseHostProperties();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!int.TryParse(PortText, out int port) || port is < 1024 or > 65535)
        {
            HostMessage = "Choose a port between 1024 and 65535.";
            return;
        }

        _host.Settings.Enabled = ShareEnabled;
        _host.Settings.DisplayName = DisplayName.Trim();
        _host.Settings.Port = port;
        _host.Settings.Scope = new ShareScope
        {
            Mode = ScopeMode,
            ReadingListIds = SelectedIds(DbShareCatalogSource.ReadingListKind),
            CollectionIds = SelectedIds(DbShareCatalogSource.CollectionKind),
            SmartListIds = SelectedIds(DbShareCatalogSource.SmartListKind),
        };

        HostMessage = "";
        await _host.SaveAndApplyAsync().ConfigureAwait(true);
        RaiseHostProperties();
        if (_host.IsRunning)
        {
            await RefreshNetworkWarningAsync().ConfigureAwait(true);
        }
    }

    partial void OnShareEnabledChanged(bool value)
    {
        // The switch takes effect immediately, like every other on/off in Preferences.
        if (!_loading)
        {
            _ = ApplyAsync();
        }
    }

    [RelayCommand]
    private void AskRegenerate() => IsConfirmingRegenerate = true;

    [RelayCommand]
    private void CancelRegenerate() => IsConfirmingRegenerate = false;

    [RelayCommand]
    private async Task ConfirmRegenerateAsync()
    {
        IsConfirmingRegenerate = false;
        await _host.RegenerateCertificateAsync().ConfigureAwait(true);
        HostMessage = "New certificate created. Everyone who connected before will be asked to trust it again.";
        RaiseHostProperties();
    }

    private List<int> SelectedIds(string kind) =>
        ListChoices.Where(c => c.Kind == kind && c.IsSelected).Select(c => c.Id).ToList();

    private string SafeFingerprint()
    {
        try
        {
            return _host.CertificateFingerprint;
        }
        catch (Exception)
        {
            return "";
        }
    }

    // ---------------------------------------------------------------- remote libraries

    public ObservableCollection<RemoteSourceRowViewModel> Sources { get; } = new();

    public bool HasSources => Sources.Count > 0;

    public bool HasNoSources => Sources.Count == 0;

    [ObservableProperty]
    private string _addHost = "";

    [ObservableProperty]
    private string _addPortText = "7614";

    [ObservableProperty]
    private string _addPassword = "";

    [ObservableProperty]
    private bool _addSavePassword = true;

    [ObservableProperty]
    private string? _addError;

    public bool HasAddError => !string.IsNullOrEmpty(AddError);

    partial void OnAddErrorChanged(string? value) => OnPropertyChanged(nameof(HasAddError));

    [ObservableProperty]
    private bool _isAddBusy;

    [ObservableProperty]
    private bool _isAddPanelOpen;

    /// <summary>Second step of adding: the host has identified itself and the user must trust its fingerprint.</summary>
    [ObservableProperty]
    private bool _hasPendingProbe;

    [ObservableProperty]
    private string _pendingName = "";

    [ObservableProperty]
    private string _pendingFingerprint = "";

    /// <summary>Hosts found on the local network by the last "Find on this network" - a convenience that only pre-fills the address.</summary>
    public ObservableCollection<DiscoveredHostRowViewModel> DiscoveredHosts { get; } = new();

    public bool HasDiscoveredHosts => DiscoveredHosts.Count > 0;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _discoveryMessage = "";

    public bool HasDiscoveryMessage => !string.IsNullOrEmpty(DiscoveryMessage);

    partial void OnDiscoveryMessageChanged(string value) => OnPropertyChanged(nameof(HasDiscoveryMessage));

    [RelayCommand]
    private async Task FindOnNetworkAsync()
    {
        IsSearching = true;
        DiscoveryMessage = "";
        DiscoveredHosts.Clear();
        try
        {
            var hosts = await _browse(TimeSpan.FromSeconds(4)).ConfigureAwait(true);
            var known = _remote.List().Select(s => s.InstanceId).ToHashSet();
            known.Add(_host.Settings.InstanceId);                       // never offer this computer to itself
            foreach (var host in hosts.Where(h => !known.Contains(h.InstanceId)))
            {
                DiscoveredHosts.Add(new DiscoveredHostRowViewModel(host, UseDiscovered));
            }

            DiscoveryMessage = DiscoveredHosts.Count == 0
                ? "No other Paperbunkr sharing was found on this network. You can still type an address."
                : "";
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(HasDiscoveredHosts));
        }
    }

    private void UseDiscovered(DiscoveredHost host)
    {
        AddHost = host.Address;
        AddPortText = host.Port.ToString();
        AddError = null;
    }

    [RelayCommand]
    private void OpenAddPanel()
    {
        IsAddPanelOpen = true;
        AddError = null;
    }

    [RelayCommand]
    private void CancelAdd()
    {
        IsAddPanelOpen = false;
        HasPendingProbe = false;
        _pendingProbe = null;
        _pendingPassword = null;
        AddPassword = "";
        AddError = null;
    }

    /// <summary>Step 1: contact the host over an unverified connection purely to learn who it is and what certificate it presents. The password is not sent.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        AddError = null;
        if (string.IsNullOrWhiteSpace(AddHost))
        {
            AddError = "Enter the host's address.";
            return;
        }

        if (!int.TryParse(AddPortText, out int port) || port is < 1 or > 65535)
        {
            AddError = "Enter a valid port.";
            return;
        }

        if (string.IsNullOrEmpty(AddPassword))
        {
            AddError = "Enter the host's sharing password.";
            return;
        }

        IsAddBusy = true;
        try
        {
            _pendingProbe = await _remote.ProbeAsync(AddHost.Trim(), port).ConfigureAwait(true);
            _pendingPassword = AddPassword;
            PendingName = _pendingProbe.Hello.DisplayName;
            PendingFingerprint = RemoteSourceRowViewModel.FormatFingerprint(_pendingProbe.Fingerprint);
            HasPendingProbe = true;
        }
        catch (ShareClientException ex)
        {
            AddError = ex.Message;
        }
        finally
        {
            IsAddBusy = false;
        }
    }

    /// <summary>Step 2: the user confirmed the fingerprint is the one they expect. Only now is the password sent, over a connection pinned to it.</summary>
    [RelayCommand]
    private async Task TrustAndAddAsync()
    {
        if (_pendingProbe is null || _pendingPassword is null || !int.TryParse(AddPortText, out int port))
        {
            return;
        }

        IsAddBusy = true;
        AddError = null;
        try
        {
            await _remote.AddAsync(AddHost.Trim(), port, _pendingPassword, _pendingProbe, AddSavePassword).ConfigureAwait(true);
            CancelAdd();
            Reload();
        }
        catch (AuthFailedException)
        {
            AddError = "The host rejected that password.";
            HasPendingProbe = false;
        }
        catch (LockedOutException ex)
        {
            AddError = $"Too many attempts. Try again in about {Math.Ceiling(ex.RetryAfter.TotalMinutes)} minute(s).";
            HasPendingProbe = false;
        }
        catch (ShareClientException ex)
        {
            AddError = ex.Message;
            HasPendingProbe = false;
        }
        catch (InvalidOperationException ex)
        {
            AddError = ex.Message;
            HasPendingProbe = false;
        }
        finally
        {
            IsAddBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        await _remote.SyncAllAsync(Paperbunkr.Data.Entities.ActivityTrigger.Manual).ConfigureAwait(false);
    }

    /// <summary>"Add as new library" from a host-changed row: pre-fill the address so only the password is needed.</summary>
    private void PrepareAddFrom(RemoteSource source)
    {
        AddHost = source.Host;
        AddPortText = source.Port.ToString();
        AddPassword = "";
        HasPendingProbe = false;
        IsAddPanelOpen = true;
        AddError = "Enter the password to add this address as a new library.";
    }

    // ---------------------------------------------------------------- loading

    /// <summary>Re-reads everything from settings, the database and the services.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            ShareEnabled = _host.Settings.Enabled;
            DisplayName = _host.Settings.DisplayName;
            PortText = _host.Settings.Port.ToString();
            ScopeMode = _host.Settings.Scope.Mode;
            LoadListChoices();
        }
        finally
        {
            _loading = false;
        }

        LoadSources();
        RaiseHostProperties();
    }

    /// <summary>Called from row commands. Deferred one tick: a row's button is still routing its click when it asks for a reload, and rebuilding the list synchronously would detach it mid-event.</summary>
    public void Reload() => _post(LoadSources);

    private void LoadListChoices()
    {
        ListChoices.Clear();
        ShareScope scope = _host.Settings.Scope;
        using PaperbunkrDbContext context = _contextFactory();
        foreach (var l in context.ReadingLists.OrderBy(l => l.Name).Select(l => new { l.Id, l.Name }).ToList())
        {
            ListChoices.Add(new ShareListChoiceViewModel(DbShareCatalogSource.ReadingListKind, l.Id, l.Name, scope.ReadingListIds.Contains(l.Id)));
        }

        foreach (var c in context.Collections.OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToList())
        {
            ListChoices.Add(new ShareListChoiceViewModel(DbShareCatalogSource.CollectionKind, c.Id, c.Name, scope.CollectionIds.Contains(c.Id)));
        }

        // Only issue-target smart lists can be shared in v1.
        foreach (var s in context.SmartLists.Where(s => s.TargetKind == SmartListTargetKind.Issue && !s.IsSystem).OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToList())
        {
            ListChoices.Add(new ShareListChoiceViewModel(DbShareCatalogSource.SmartListKind, s.Id, s.Name, scope.SmartListIds.Contains(s.Id)));
        }

        OnPropertyChanged(nameof(HasListChoices));
    }

    private void LoadSources()
    {
        Sources.Clear();
        foreach (RemoteSource source in _remote.List())
        {
            Sources.Add(new RemoteSourceRowViewModel(_remote, source, Reload, PrepareAddFrom, _post));
        }

        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasNoSources));
    }

    private void OnHostChanged() => _post(RaiseHostProperties);

    private void OnRemoteChanged() => _post(RefreshRows);

    private void RefreshRows()
    {
        var byId = _remote.List().ToDictionary(s => s.Id);
        foreach (RemoteSourceRowViewModel row in Sources)
        {
            if (byId.TryGetValue(row.Id, out RemoteSource? source))
            {
                row.Refresh(source);
            }
        }
    }

    private void RaiseHostProperties()
    {
        OnPropertyChanged(nameof(HostStatus));
        OnPropertyChanged(nameof(ClientsText));
        OnPropertyChanged(nameof(IsHostRunning));
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(PasswordStatus));
        OnPropertyChanged(nameof(CertificateFingerprint));
    }
}
