using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Sharing;
using Paperbunkr.Sharing;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.App.Services.Sharing;

/// <summary>
/// Owns the host side of remote sharing for the running app (docs/superpowers/specs/2026-09-19-remote-
/// library-sharing-design.md §4/§6/§9): loads the persisted <see cref="ShareSettings"/>, builds the
/// <see cref="ShareServer"/> over the EF-backed catalog and the archive page source, and reports
/// everything through the Activity Center - a long-lived "Sharing library" job while it runs (cancel
/// it and the server stops), and alerts for a failed start or a client being locked out. The server
/// only ever runs while the app is open; there is no service or background mode.
/// </summary>
public sealed class ShareHostService : IAsyncDisposable
{
    private readonly ShareSettingsStore _store;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly IActivityService _activity;
    private readonly CertificateManager _certificates;
    private readonly Action<ShareServerOptions>? _configure;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ShareServer? _server;
    private DbShareCatalogSource? _catalog;
    private ArchivePageSource? _pages;
    private IActivityJobHandle? _job;
    private Guid? _startFailureAlertId;
    private Paperbunkr.Sharing.Discovery.ShareDiscovery.Advertiser? _advertiser;

    /// <param name="configure">Test hook: adjust the server options (e.g. a lower failed-attempt limit) after they are built from the settings.</param>
    public ShareHostService(
        ShareSettingsStore store,
        Func<PaperbunkrDbContext> contextFactory,
        IActivityService activity,
        string certificateDirectory,
        Action<ShareServerOptions>? configure = null)
    {
        _store = store;
        _contextFactory = contextFactory;
        _activity = activity;
        _certificates = new CertificateManager(certificateDirectory);
        _configure = configure;
        Settings = store.Load();
    }

    /// <summary>The current settings. Mutate then call <see cref="SaveAndApplyAsync"/>; do not save them directly.</summary>
    public ShareSettings Settings { get; private set; }

    public bool IsRunning => _server is not null;

    /// <summary>The port actually bound while running; otherwise the configured one.</summary>
    public int Port => _server?.Port ?? Settings.Port;

    /// <summary>Set when a start attempt failed (no password, port in use, ...); cleared by the next successful start or a stop.</summary>
    public string? LastError { get; private set; }

    public int ConnectedClients => _server?.Sessions.ActiveCount ?? 0;

    public bool HasPassword => !string.IsNullOrEmpty(Settings.PasswordHash);

    /// <summary>True while this host is announcing itself on the local network (mDNS). Off when sharing is off; failure to advertise never stops sharing.</summary>
    public bool IsAdvertising => _advertiser?.IsAdvertising == true;

    /// <summary>The certificate fingerprint clients will be asked to trust. Creates the certificate on first use.</summary>
    public string CertificateFingerprint => _certificates.Fingerprint;

    public event Action? StateChanged;

    /// <summary>Starts serving if sharing is enabled in the settings (used at app launch). A failure is recorded in <see cref="LastError"/> and alerted, never thrown.</summary>
    public async Task StartIfEnabledAsync()
    {
        if (Settings.Enabled)
        {
            await StartAsync().ConfigureAwait(false);
        }
    }

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StartCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync("Stopped sharing").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Persists the (already mutated) <see cref="Settings"/> and makes them take effect: starts, stops or
    /// restarts the server to match <see cref="ShareSettings.Enabled"/>, and drops the catalog snapshot so a
    /// narrowed scope applies immediately rather than after the snapshot's TTL.
    /// </summary>
    public async Task SaveAndApplyAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _store.Save(Settings);
            _catalog?.Invalidate();

            if (_server is not null)
            {
                await StopCoreAsync("Restarting sharing", quiet: true).ConfigureAwait(false);
            }

            if (Settings.Enabled)
            {
                await StartCoreAsync().ConfigureAwait(false);
            }
            else
            {
                LastError = null;
                Notify();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Hashes and stores a new sharing password. The plaintext is never kept. Takes effect on the next start/apply.</summary>
    public void SetPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        Settings.PasswordHash = PasswordHasher.Hash(password);
        _store.Save(Settings);
        Notify();
    }

    /// <summary>Replaces the host certificate. Every client that trusted the old one must explicitly re-trust. Restarts the server if it is running.</summary>
    public async Task RegenerateCertificateAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            bool wasRunning = _server is not null;
            if (wasRunning)
            {
                await StopCoreAsync("Restarting sharing", quiet: true).ConfigureAwait(false);
            }

            _certificates.Regenerate();

            if (wasRunning)
            {
                await StartCoreAsync().ConfigureAwait(false);
            }
            else
            {
                Notify();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Called when the library changed in a way clients should see sooner than the snapshot TTL.</summary>
    public void InvalidateCatalog() => _catalog?.Invalidate();

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task StartCoreAsync()
    {
        if (_server is not null)
        {
            return;
        }

        if (!HasPassword)
        {
            Fail("Set a sharing password before turning sharing on.");
            return;
        }

        var options = new ShareServerOptions
        {
            Port = Settings.Port,
            BindAddress = IPAddress.Any,
            DisplayName = string.IsNullOrWhiteSpace(Settings.DisplayName) ? Environment.MachineName : Settings.DisplayName,
            InstanceId = Settings.InstanceId,
            PasswordHash = Settings.PasswordHash,
        };
        _configure?.Invoke(options);

        _catalog = new DbShareCatalogSource(_contextFactory, () => Settings.Scope);
        _pages = new ArchivePageSource(_contextFactory);
        var server = new ShareServer(options, _catalog, _pages, _certificates.GetOrCreate());
        server.Limiter.LockedOut += OnLockedOut;
        server.SessionStarted += _ => Notify();

        try
        {
            await server.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            server.Limiter.LockedOut -= OnLockedOut;
            await server.DisposeAsync().ConfigureAwait(false);
            _pages.Dispose();
            _pages = null;
            _catalog = null;
            Fail($"Couldn't start sharing on port {Settings.Port}: {ex.Message}");
            return;
        }

        _server = server;
        LastError = null;
        // Announce on the LAN so other installs can find this one without typing an address. Best effort and secret-free.
        _advertiser = new Paperbunkr.Sharing.Discovery.ShareDiscovery.Advertiser(options.InstanceId, options.DisplayName, server.Port);
        if (_startFailureAlertId is Guid stale)
        {
            _activity.DismissAlert(stale); // it started after all - the earlier failure no longer applies
            _startFailureAlertId = null;
        }

        _job = _activity.StartJob(ActivityJobKind.Sharing, "Sharing library", cancellable: true, ActivityTrigger.Manual, ActivityToastPolicy.Never);
        _job.Begin();
        _job.Report($"Serving on port {server.Port}");
        _job.CancellationToken.Register(() => _ = StopAsync());
        Notify();
    }

    private async Task StopCoreAsync(string summary, bool quiet = false)
    {
        ShareServer? server = _server;
        if (server is null)
        {
            return;
        }

        _server = null;
        _advertiser?.Dispose();
        _advertiser = null;
        server.Limiter.LockedOut -= OnLockedOut;
        await server.DisposeAsync().ConfigureAwait(false);
        _pages?.Dispose();
        _pages = null;
        _catalog = null;

        IActivityJobHandle? job = _job;
        _job = null;
        job?.Succeed(summary);
        Notify();
    }

    private void OnLockedOut(string client)
    {
        _activity.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Warning,
            Title = "Repeated failed sign-ins to your shared library",
            Detail = $"{client} entered the wrong password several times and is locked out for a while. If that isn't someone you expect, change the sharing password.",
            ActionLabel = "Open Sharing",
            ActionLink = new ActivityLink(ActivityLinkKind.Preferences, "Sharing"),
            DedupeKey = $"sharing-lockout-{client}",
        });
    }

    private void Fail(string message)
    {
        LastError = message;
        var alert = new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Error,
            Title = "Sharing didn't start",
            Detail = message,
            ActionLabel = "Open Sharing",
            ActionLink = new ActivityLink(ActivityLinkKind.Preferences, "Sharing"),
            DedupeKey = SharingAlertKey,
        };
        _startFailureAlertId = alert.Id;
        _activity.RaiseAlert(alert);
        Notify();
    }

    private const string SharingAlertKey = "sharing-start-failed";

    private void Notify() => StateChanged?.Invoke();
}
