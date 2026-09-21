using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.Data.Entities;
using Paperbunkr.Sharing.Client;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One saved remote library in Preferences → Sharing (docs/superpowers/specs/2026-09-19-remote-library-
/// sharing-design.md §6/§7.1/§9). Everything the user has to decide about it - remove, re-trust a changed
/// certificate (old vs new fingerprint shown), supply a new password, relink after a host reset - is an
/// inline panel on the row, never a modal dialog, and nothing destructive happens without a second click.
/// </summary>
public sealed partial class RemoteSourceRowViewModel : ObservableObject
{
    private readonly RemoteLibraryService _service;
    private readonly Action _requestReload;
    private readonly Action<RemoteSource> _addAsNew;
    private readonly Action<Action> _post;
    private ProbeResult? _relinkProbe;

    public RemoteSourceRowViewModel(RemoteLibraryService service, RemoteSource source, Action requestReload, Action<RemoteSource> addAsNew, Action<Action>? post = null)
    {
        _service = service;
        _requestReload = requestReload;
        _addAsNew = addAsNew;
        _post = post ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        Id = source.Id;
        Name = source.DisplayName;
        Address = $"{source.Host}:{source.Port}";
        TrustedFingerprint = FormatFingerprint(source.CertFingerprint);
        HasSavedPassword = !string.IsNullOrEmpty(source.ProtectedPassword);
        Refresh(source);
    }

    public int Id { get; }

    public string Name { get; }

    public string Address { get; }

    /// <summary>The fingerprint the user trusted, grouped for reading.</summary>
    public string TrustedFingerprint { get; }

    public bool HasSavedPassword { get; }

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>ok | busy | warn | error | info - drives the badge style; the text always carries the meaning too (never colour alone).</summary>
    [ObservableProperty]
    private string _statusKind = "info";

    public bool IsStatusOk => StatusKind == "ok";
    public bool IsStatusWarn => StatusKind == "warn";
    public bool IsStatusError => StatusKind == "error";
    public bool IsStatusBusy => StatusKind == "busy";

    partial void OnStatusKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsStatusOk));
        OnPropertyChanged(nameof(IsStatusWarn));
        OnPropertyChanged(nameof(IsStatusError));
        OnPropertyChanged(nameof(IsStatusBusy));
    }

    [ObservableProperty]
    private string _lastSyncedText = "";

    [ObservableProperty]
    private string? _detail;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    partial void OnDetailChanged(string? value) => OnPropertyChanged(nameof(HasDetail));

    [ObservableProperty]
    private bool _isCertificateChanged;

    [ObservableProperty]
    private string _newFingerprint = "";

    [ObservableProperty]
    private bool _needsPassword;

    [ObservableProperty]
    private bool _isHostChanged;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConfirmingRemove;

    [ObservableProperty]
    private string _rowPassword = "";

    [ObservableProperty]
    private bool _saveRowPassword = true;

    /// <summary>Relink is a two-step inline flow: probe, show what the host is now, then confirm.</summary>
    [ObservableProperty]
    private bool _isRelinking;

    [ObservableProperty]
    private string _relinkHostName = "";

    [ObservableProperty]
    private string _relinkFingerprint = "";

    [ObservableProperty]
    private string? _relinkError;

    public bool ShowNormalActions => !IsConfirmingRemove;

    partial void OnIsConfirmingRemoveChanged(bool value) => OnPropertyChanged(nameof(ShowNormalActions));

    /// <summary>Re-reads state from the service and the saved row. Called whenever the service reports a change.</summary>
    public void Refresh(RemoteSource source)
    {
        RemoteSourceStatus status = _service.StatusOf(source.Id);
        IsHostChanged = source.HostChanged || status.State == RemoteSourceState.HostChanged;
        IsCertificateChanged = status.State == RemoteSourceState.CertificateChanged;
        NeedsPassword = status.State == RemoteSourceState.AuthFailed;
        NewFingerprint = IsCertificateChanged && status.PresentedFingerprint is not null ? FormatFingerprint(status.PresentedFingerprint) : "";
        IsBusy = status.State == RemoteSourceState.Syncing;

        (StatusText, StatusKind) = status.State switch
        {
            RemoteSourceState.Syncing => ("Syncing…", "busy"),
            RemoteSourceState.Online => ("Online", "ok"),
            RemoteSourceState.Offline => ("Offline", "warn"),
            RemoteSourceState.CertificateChanged => ("Certificate changed", "error"),
            RemoteSourceState.AuthFailed => ("Password needed", "warn"),
            RemoteSourceState.LockedOut => ("Locked out", "warn"),
            RemoteSourceState.HostChanged => ("Host changed", "warn"),
            RemoteSourceState.ProtocolMismatch => ("Incompatible version", "error"),
            _ => source.HostChanged ? ("Host changed", "warn") : source.IsOffline ? ("Offline", "warn") : ("Not checked yet", "info"),
        };

        if (StatusText == "Host changed")
        {
            IsHostChanged = true;
        }

        Detail = status.Detail;
        LastSyncedText = source.LastSyncedAt is { } t ? $"Last synced {t.ToLocalTime():g}" : "Never synced";
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await _service.SyncAsync(Id).ConfigureAwait(false);
    }

    [RelayCommand]
    private void AskRemove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand]
    private void ConfirmRemove()
    {
        // The click that got us here is still routing through this row's button; removing the row (or
        // reloading the list) synchronously would detach the very control raising the event. Defer one tick.
        _post(() =>
        {
            _service.Remove(Id);
            _requestReload();
        });
    }

    /// <summary>The user compared the trusted and new fingerprints and accepted the new certificate.</summary>
    [RelayCommand]
    private async Task TrustNewCertificateAsync()
    {
        RemoteSourceStatus status = _service.StatusOf(Id);
        if (status.PresentedFingerprint is null)
        {
            return;
        }

        await _service.RetrustAsync(Id, status.PresentedFingerprint).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task SubmitPasswordAsync()
    {
        if (string.IsNullOrEmpty(RowPassword))
        {
            return;
        }

        _service.SetPassword(Id, RowPassword, SaveRowPassword);
        RowPassword = "";
        await _service.SyncAsync(Id).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task StartRelinkAsync()
    {
        RelinkError = null;
        try
        {
            RemoteSource? source = FindSource();
            if (source is null)
            {
                return;
            }

            _relinkProbe = await _service.ProbeAsync(source.Host, source.Port).ConfigureAwait(true);
            RelinkHostName = _relinkProbe.Hello.DisplayName;
            RelinkFingerprint = FormatFingerprint(_relinkProbe.Fingerprint);
            IsRelinking = true;
        }
        catch (ShareClientException ex)
        {
            RelinkError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ConfirmRelinkAsync()
    {
        if (_relinkProbe is null)
        {
            return;
        }

        string? password = string.IsNullOrEmpty(RowPassword) ? null : RowPassword;
        RelinkError = null;   // a fresh attempt starts clean - never leave the previous attempt's error behind
        try
        {
            if (password is null)
            {
                RelinkError = "Enter the host's sharing password to relink.";
                return;
            }

            var result = await _service.RelinkAsync(Id, _relinkProbe, password).ConfigureAwait(true);
            if (SaveRowPassword)
            {
                _service.SetPassword(Id, password, save: true);
            }

            RowPassword = "";
            IsRelinking = false;
            Detail = result.IssuesOrphaned > 0
                ? $"Relinked. {result.IssuesMatched} books kept their progress; {result.IssuesOrphaned} had no match and were kept offline."
                : $"Relinked. All {result.IssuesMatched} books kept their progress.";
            _requestReload();
        }
        catch (ShareClientException ex)
        {
            RelinkError = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            RelinkError = ex.Message;
        }
    }

    [RelayCommand]
    private void CancelRelink()
    {
        IsRelinking = false;
        RelinkError = null;
        RowPassword = "";
    }

    [RelayCommand]
    private void AddAsNew()
    {
        RemoteSource? source = FindSource();
        if (source is not null)
        {
            _addAsNew(source);
        }
    }

    private RemoteSource? FindSource() => _service.List().FirstOrDefault(s => s.Id == Id);

    /// <summary>Uppercase hex in groups of four so two fingerprints can actually be compared by eye.</summary>
    public static string FormatFingerprint(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return "";
        }

        var groups = new System.Collections.Generic.List<string>();
        for (int i = 0; i < hex.Length; i += 4)
        {
            groups.Add(hex.Substring(i, Math.Min(4, hex.Length - i)).ToUpperInvariant());
        }

        return string.Join(" ", groups);
    }
}
