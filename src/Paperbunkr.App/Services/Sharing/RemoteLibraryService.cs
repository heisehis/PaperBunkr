using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Sharing;
using Paperbunkr.Sharing.Client;

namespace Paperbunkr.App.Services.Sharing;

/// <summary>Where a remote library currently stands, for the UI's status badge. In-memory: the next sync re-detects it.</summary>
public enum RemoteSourceState
{
    /// <summary>Not checked yet this session.</summary>
    Unknown,
    Syncing,
    Online,
    /// <summary>Unreachable; the mirror stays visible with an Offline badge and cached pages stay readable.</summary>
    Offline,
    /// <summary>The host presented a different certificate than the one trusted. Blocked until the user re-trusts it explicitly.</summary>
    CertificateChanged,
    AuthFailed,
    LockedOut,
    /// <summary>The address now belongs to a different instance (its library was rebuilt/reset). Needs Relink, Add-as-new, or Remove.</summary>
    HostChanged,
    ProtocolMismatch,
}

/// <param name="PresentedFingerprint">For <see cref="RemoteSourceState.CertificateChanged"/>: what the host showed this time, so the re-trust dialog can display old vs new.</param>
public sealed record RemoteSourceStatus(RemoteSourceState State, string? Detail = null, string? PresentedFingerprint = null);

/// <summary>
/// The client side of remote sharing for the running app (docs/superpowers/specs/2026-09-19-remote-
/// library-sharing-design.md §6/§7/§9): add a library (probe, explicit trust of the certificate
/// fingerprint, password), keep its mirror fresh, and handle every way that can go wrong - each with a
/// typed <see cref="RemoteSourceStatus"/> and an Activity Center job/alert rather than ad-hoc UI.
/// </summary>
/// <remarks>
/// Security stance: a changed certificate is never accepted silently. <see cref="SyncAsync"/> stops at
/// <see cref="RemoteSourceState.CertificateChanged"/>; only <see cref="RetrustAsync"/> - which the UI calls
/// after showing the old and new fingerprints and getting an explicit confirmation - updates the pin.
/// Nothing here deletes a mirror row except the user's explicit <see cref="Remove"/>.
/// </remarks>
public sealed class RemoteLibraryService : IDisposable
{
    private readonly Func<bool, PaperbunkrDbContext> _contextFactory;
    private readonly IActivityService _activity;
    private readonly Action<int, IReadOnlyList<int>>? _onSourceRemoved;
    private readonly Func<int, Task>? _afterSync;
    private readonly Action<int, IReadOnlyList<int>?>? _onContentInvalidated;
    private readonly Func<RemoteSource, string?> _passwordFor;

    private readonly ConcurrentDictionary<int, RemoteSourceStatus> _status = new();
    private readonly ConcurrentDictionary<int, string> _sessionPasswords = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _syncGates = new();
    private readonly ConcurrentDictionary<int, ShareClient> _clients = new();
    private readonly ConcurrentDictionary<string, Guid> _alerts = new();

    /// <param name="contextFactory">Creates a context; the bool is <c>includeRemote</c> (the mirror sync needs to see its own rows).</param>
    /// <param name="onSourceRemoved">Called after a source and its mirror are deleted, with the source id and the <em>local</em> ids of the issues that went, so caches (pages, covers) can be purged.</param>
    /// <param name="afterSync">Called after every successful sync/add/relink (the mirror is current) - the place to download covers. Failures are ignored: a missing cover is only a placeholder.</param>
    /// <param name="onContentInvalidated">Cached pages may now be wrong: (sourceId, null) means every id of the source was re-keyed (Relink); (sourceId, ids) lists host issue ids whose page count changed, i.e. the book behind them was replaced.</param>
    /// <param name="passwordFor">Test hook; defaults to the DPAPI-protected saved password, then the in-memory session password.</param>
    public RemoteLibraryService(
        Func<bool, PaperbunkrDbContext> contextFactory,
        IActivityService activity,
        Action<int, IReadOnlyList<int>>? onSourceRemoved = null,
        Func<RemoteSource, string?>? passwordFor = null,
        Func<int, Task>? afterSync = null,
        Action<int, IReadOnlyList<int>?>? onContentInvalidated = null)
    {
        _onContentInvalidated = onContentInvalidated;
        _contextFactory = contextFactory;
        _activity = activity;
        _onSourceRemoved = onSourceRemoved;
        _afterSync = afterSync;
        _passwordFor = passwordFor ?? DefaultPasswordFor;
    }

    /// <summary>Raised whenever a source's status changes.</summary>
    public event Action? Changed;

    /// <summary>Raised (with the source id) whenever the mirrored books of a library changed - added, updated, relinked or removed - so a view showing them can reload.</summary>
    public event Action<int>? MirrorChanged;

    public IReadOnlyList<RemoteSource> List()
    {
        using PaperbunkrDbContext context = _contextFactory(true);
        return context.RemoteSources.AsNoTracking().OrderBy(s => s.DisplayName).ToList();
    }

    public RemoteSourceStatus StatusOf(int sourceId) =>
        _status.TryGetValue(sourceId, out RemoteSourceStatus? s) ? s : new RemoteSourceStatus(RemoteSourceState.Unknown);

    /// <summary>First contact: the host's identity and the certificate fingerprint to put in front of the user. Nothing secret is sent.</summary>
    public Task<ProbeResult> ProbeAsync(string host, int port, CancellationToken cancellationToken = default) =>
        ShareClient.ProbeAsync(host, port, cancellationToken: cancellationToken);

    /// <summary>
    /// Adds a library the user has just trusted. The password is verified by pulling the catalog before
    /// anything is kept: a wrong password leaves no source behind. Returns the saved source.
    /// </summary>
    public async Task<RemoteSource> AddAsync(string host, int port, string password, ProbeResult trusted, bool savePassword, CancellationToken cancellationToken = default)
    {
        using (PaperbunkrDbContext existing = _contextFactory(true))
        {
            if (existing.RemoteSources.Any(s => s.InstanceId == trusted.Hello.InstanceId))
            {
                throw new InvalidOperationException($"\"{trusted.Hello.DisplayName}\" is already in your remote libraries.");
            }
        }

        // Verify credentials + fetch the catalog before persisting anything.
        using var client = new ShareClient(host, port, trusted.Fingerprint, password);
        CatalogPull pull = await client.GetCatalogAsync(null, cancellationToken: cancellationToken).ConfigureAwait(false);

        var source = new RemoteSource
        {
            InstanceId = trusted.Hello.InstanceId,
            DisplayName = trusted.Hello.DisplayName,
            Host = host,
            Port = port,
            CertFingerprint = trusted.Fingerprint,
            ProtectedPassword = savePassword && CredentialProtector.IsProtectionAvailable ? CredentialProtector.Protect(password) : null,
            LastCatalogEtag = pull.ETag,
            LastSyncedAt = DateTime.UtcNow,
        };

        await Task.Run(() =>
        {
            using PaperbunkrDbContext context = _contextFactory(true);
            context.RemoteSources.Add(source);
            context.SaveChanges();
            RemoteMirrorSync.Apply(context, source.Id, pull.Series, pull.Issues);
        }, cancellationToken).ConfigureAwait(false);

        if (!savePassword || !CredentialProtector.IsProtectionAvailable)
        {
            _sessionPasswords[source.Id] = password;
        }

        SetStatus(source.Id, new RemoteSourceStatus(RemoteSourceState.Online));
        await AfterMirrorChangedAsync(source.Id).ConfigureAwait(false);
        return source;
    }

    public Task<MirrorSyncResult?> SyncAsync(int sourceId, ActivityTrigger trigger = ActivityTrigger.Manual, CancellationToken cancellationToken = default) =>
        SyncCoreAsync(sourceId, trigger, cancellationToken);

    /// <summary>Syncs every source that isn't blocked (host changed / certificate changed). Used at launch and by "Refresh all".</summary>
    public async Task SyncAllAsync(ActivityTrigger trigger, CancellationToken cancellationToken = default)
    {
        foreach (RemoteSource source in List().Where(s => !s.HostChanged))
        {
            if (StatusOf(source.Id).State == RemoteSourceState.CertificateChanged)
            {
                continue; // stays blocked until the user re-trusts it
            }

            await SyncCoreAsync(source.Id, trigger, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Supplies the password for a source that has none saved (or whose saved one was rejected), optionally saving it protected.</summary>
    public void SetPassword(int sourceId, string password, bool save)
    {
        _sessionPasswords[sourceId] = password;
        DropClient(sourceId);

        if (save && CredentialProtector.IsProtectionAvailable)
        {
            using PaperbunkrDbContext context = _contextFactory(true);
            RemoteSource? source = context.RemoteSources.Find(sourceId);
            if (source is not null)
            {
                source.ProtectedPassword = CredentialProtector.Protect(password);
                context.SaveChanges();
            }
        }

        SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.Unknown));
    }

    /// <summary>
    /// The user reviewed the old and new fingerprints and explicitly accepted the new certificate.
    /// Overwrites the pin in place - the mirror and every bit of local reading data are untouched - then syncs.
    /// </summary>
    public async Task RetrustAsync(int sourceId, string newFingerprint, CancellationToken cancellationToken = default)
    {
        using (PaperbunkrDbContext context = _contextFactory(true))
        {
            RemoteSource source = context.RemoteSources.Find(sourceId) ?? throw new InvalidOperationException("That remote library no longer exists.");
            source.CertFingerprint = newFingerprint.ToUpperInvariant();
            context.SaveChanges();
        }

        DropClient(sourceId);
        DismissAlert(sourceId, "cert");
        SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.Unknown));
        await SyncCoreAsync(sourceId, ActivityTrigger.Manual, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Relink after a "Host changed" (spec §7.1): the user says the library at this address is the same
    /// logical one. Binds the new instance id to the existing source, re-keys the existing mirror rows by
    /// stable metadata so reading progress follows the books, then refreshes. Rows with no unambiguous
    /// counterpart are kept (orphaned, unreadable) and reported, never deleted.
    /// </summary>
    public async Task<RelinkResult> RelinkAsync(int sourceId, ProbeResult trusted, string password, CancellationToken cancellationToken = default)
    {
        using var client = new ShareClient(
            GetSource(sourceId).Host, GetSource(sourceId).Port, trusted.Fingerprint, password);
        CatalogPull pull = await client.GetCatalogAsync(null, cancellationToken: cancellationToken).ConfigureAwait(false);

        RelinkResult result = await Task.Run(() =>
        {
            using PaperbunkrDbContext context = _contextFactory(true);
            if (context.RemoteSources.Any(s => s.InstanceId == trusted.Hello.InstanceId && s.Id != sourceId))
            {
                throw new InvalidOperationException($"\"{trusted.Hello.DisplayName}\" is already another of your remote libraries.");
            }

            RemoteSource source = context.RemoteSources.Single(s => s.Id == sourceId);
            source.InstanceId = trusted.Hello.InstanceId;
            source.CertFingerprint = trusted.Fingerprint;
            source.HostChanged = false;
            source.LastCatalogEtag = pull.ETag;
            source.LastSyncedAt = DateTime.UtcNow;
            source.IsOffline = false;
            context.SaveChanges();

            RelinkResult relink = RemoteRelinkReconciler.Reconcile(context, sourceId, pull.Series, pull.Issues);
            RemoteMirrorSync.Apply(context, sourceId, pull.Series, pull.Issues);
            return relink;
        }, cancellationToken).ConfigureAwait(false);

        DropClient(sourceId);
        DismissAlert(sourceId, "host");
        _sessionPasswords[sourceId] = password;
        _onContentInvalidated?.Invoke(sourceId, null);   // every host id was re-keyed: nothing cached under the old ids can be trusted
        SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.Online));
        await AfterMirrorChangedAsync(sourceId).ConfigureAwait(false);
        return result;
    }

    /// <summary>Deletes the source and its whole mirror (rows cascade), then lets the caller purge caches. Local libraries are never touched.</summary>
    public void Remove(int sourceId)
    {
        List<int> goneIssueIds;
        using (PaperbunkrDbContext context = _contextFactory(true))
        {
            goneIssueIds = context.Issues.Where(i => i.RemoteSourceId == sourceId).Select(i => i.Id).ToList();

            // The FK cascade does the work; make sure it is actually enforced on this connection.
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
            // Series.CoverIssueId is Restrict, so release it before the issues go.
            context.Series.Where(s => s.RemoteSourceId == sourceId && s.CoverIssueId != null)
                .ExecuteUpdate(u => u.SetProperty(s => s.CoverIssueId, (int?)null));
            context.Issues.Where(i => i.RemoteSourceId == sourceId).ExecuteDelete();
            context.Series.Where(s => s.RemoteSourceId == sourceId).ExecuteDelete();
            context.RemoteSources.Where(s => s.Id == sourceId).ExecuteDelete();
        }

        DropClient(sourceId);
        _status.TryRemove(sourceId, out _);
        _sessionPasswords.TryRemove(sourceId, out _);
        foreach (string kind in new[] { "cert", "host", "auth", "offline" })
        {
            DismissAlert(sourceId, kind);
        }

        _onSourceRemoved?.Invoke(sourceId, goneIssueIds);
        MirrorChanged?.Invoke(sourceId);
        Changed?.Invoke();
    }

    /// <summary>
    /// A long-lived, pinned, authenticated client for reading pages/covers from <paramref name="sourceId"/>.
    /// Cached so the reader reuses one session; dropped on remove, re-trust, relink or a new password.
    /// </summary>
    public ShareClient GetClient(int sourceId)
    {
        return _clients.GetOrAdd(sourceId, id =>
        {
            RemoteSource source = GetSource(id);
            string password = _passwordFor(source) ?? throw new AuthFailedException();
            return new ShareClient(source.Host, source.Port, source.CertFingerprint, password);
        });
    }

    public void Dispose()
    {
        foreach (ShareClient client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
        foreach (SemaphoreSlim gate in _syncGates.Values)
        {
            gate.Dispose();
        }

        _syncGates.Clear();
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<MirrorSyncResult?> SyncCoreAsync(int sourceId, ActivityTrigger trigger, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _syncGates.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null; // a sync of this source is already running
        }

        RemoteSource source = GetSource(sourceId);
        if (source.HostChanged)
        {
            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.HostChanged));
            gate.Release();
            return null;
        }

        bool manual = trigger == ActivityTrigger.Manual;
        IActivityJobHandle job = _activity.StartJob(
            ActivityJobKind.RemoteSync, $"Syncing {source.DisplayName}", cancellable: true, trigger,
            manual ? ActivityToastPolicy.Always : ActivityToastPolicy.FailuresOnly);
        job.Begin();
        SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.Syncing));

        try
        {
            string password = _passwordFor(source) ?? throw new AuthFailedException();
            using var client = new ShareClient(source.Host, source.Port, source.CertFingerprint, password);

            job.Report("Contacting the host");
            var hello = await client.HelloAsync(job.CancellationToken).ConfigureAwait(false);
            if (hello.InstanceId != source.InstanceId)
            {
                MarkHostChanged(source);
                job.Fail($"{source.DisplayName} looks like a different library now");
                return null;
            }

            var progress = new Progress<int>(n => job.Report($"{n} books"));
            CatalogPull pull = await client.GetCatalogAsync(source.LastCatalogEtag, progress: progress, cancellationToken: job.CancellationToken).ConfigureAwait(false);

            MirrorSyncResult? result = null;
            if (!pull.NotModified)
            {
                job.Report("Updating your copy");
                result = await Task.Run(() =>
                {
                    using PaperbunkrDbContext context = _contextFactory(true);
                    return RemoteMirrorSync.Apply(context, sourceId, pull.Series, pull.Issues);
                }, job.CancellationToken).ConfigureAwait(false);
            }

            await Task.Run(() =>
            {
                using PaperbunkrDbContext context = _contextFactory(true);
                RemoteSource row = context.RemoteSources.Single(s => s.Id == sourceId);
                row.LastCatalogEtag = pull.ETag ?? row.LastCatalogEtag;
                row.LastSyncedAt = DateTime.UtcNow;
                row.IsOffline = false;
                context.SaveChanges();
            }).ConfigureAwait(false);

            foreach (string kind in new[] { "cert", "auth", "offline" })
            {
                DismissAlert(sourceId, kind);
            }

            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.Online));
            if (result is not null)
            {
                if (result.ChangedContent.Count > 0)
                {
                    _onContentInvalidated?.Invoke(sourceId, result.ChangedContent);
                }

                MirrorChanged?.Invoke(sourceId);
            }

            // Covers are fetched even when the catalog was unchanged: an earlier fetch may have been cut short.
            await AfterMirrorChangedAsync(sourceId, raise: false).ConfigureAwait(false);
            job.Succeed(result is null ? $"{source.DisplayName} is up to date" : $"{source.DisplayName}: {result.IssuesAdded} new, {result.IssuesUpdated} refreshed, {result.IssuesRemoved} removed",
                itemsProcessed: result is null ? 0 : result.IssuesAdded + result.IssuesUpdated);
            return result;
        }
        catch (CertificateMismatchException ex)
        {
            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.CertificateChanged, ex.Message, ex.Actual));
            MarkOffline(sourceId);
            Alert(sourceId, "cert", ActivityAlertSeverity.Error, $"{source.DisplayName}'s certificate changed",
                "It no longer matches the one you trusted. Nothing was downloaded. Only continue if its owner reinstalled or regenerated it.");
            job.Fail("Certificate changed - not connected");
            return null;
        }
        catch (AuthFailedException)
        {
            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.AuthFailed, "The password was rejected."));
            Alert(sourceId, "auth", ActivityAlertSeverity.Warning, $"{source.DisplayName} rejected your password", "Enter the current sharing password to reconnect.");
            job.Fail("Password rejected");
            return null;
        }
        catch (LockedOutException ex)
        {
            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.LockedOut, $"Too many attempts. Try again in about {Math.Ceiling(ex.RetryAfter.TotalMinutes)} min."));
            job.Fail("Locked out by the host");
            return null;
        }
        catch (ProtocolMismatchException ex)
        {
            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.ProtocolMismatch, ex.Message));
            job.Fail(ex.Message);
            return null;
        }
        catch (HostUnreachableException ex)
        {
            MarkOffline(sourceId);
            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.Offline, ex.Message));
            if (manual)
            {
                Alert(sourceId, "offline", ActivityAlertSeverity.Warning, $"{source.DisplayName} is offline", "Your saved copy is still there, and pages you already opened still work.");
            }

            job.Fail($"{source.DisplayName} is offline");
            return null;
        }
        catch (OperationCanceledException)
        {
            SetStatus(sourceId, new RemoteSourceStatus(RemoteSourceState.Unknown));
            job.Fail("Cancelled");
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task AfterMirrorChangedAsync(int sourceId, bool raise = true)
    {
        if (raise)
        {
            MirrorChanged?.Invoke(sourceId);
        }

        if (_afterSync is null)
        {
            return;
        }

        try
        {
            await _afterSync(sourceId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: a cover that couldn't be fetched is a placeholder until the next sync.
        }
    }

    private RemoteSource GetSource(int sourceId)
    {
        using PaperbunkrDbContext context = _contextFactory(true);
        return context.RemoteSources.AsNoTracking().Single(s => s.Id == sourceId);
    }

    private void MarkHostChanged(RemoteSource source)
    {
        using (PaperbunkrDbContext context = _contextFactory(true))
        {
            RemoteSource row = context.RemoteSources.Single(s => s.Id == source.Id);
            row.HostChanged = true;
            context.SaveChanges();
        }

        SetStatus(source.Id, new RemoteSourceStatus(RemoteSourceState.HostChanged, "This address now belongs to a different library."));
        Alert(source.Id, "host", ActivityAlertSeverity.Warning, $"{source.DisplayName} looks like a different library",
            "Its library may have been rebuilt. Relink to keep your reading progress, add it as a new library, or remove it.");
    }

    private void MarkOffline(int sourceId)
    {
        using PaperbunkrDbContext context = _contextFactory(true);
        RemoteSource? row = context.RemoteSources.Find(sourceId);
        if (row is not null && !row.IsOffline)
        {
            row.IsOffline = true;
            context.SaveChanges();
        }
    }

    private string? DefaultPasswordFor(RemoteSource source) =>
        _sessionPasswords.TryGetValue(source.Id, out string? session)
            ? session
            : CredentialProtector.Unprotect(source.ProtectedPassword);

    private void DropClient(int sourceId)
    {
        if (_clients.TryRemove(sourceId, out ShareClient? client))
        {
            client.Dispose();
        }
    }

    private void SetStatus(int sourceId, RemoteSourceStatus status)
    {
        _status[sourceId] = status;
        Changed?.Invoke();
    }

    private void Alert(int sourceId, string kind, ActivityAlertSeverity severity, string title, string detail)
    {
        var alert = new ActivityAlert
        {
            Severity = severity,
            Title = title,
            Detail = detail,
            ActionLabel = "Open Sharing",
            ActionLink = new ActivityLink(ActivityLinkKind.Preferences, "Sharing"),
            DedupeKey = $"remote-{sourceId}-{kind}",
        };
        _alerts[alert.DedupeKey] = alert.Id;
        _activity.RaiseAlert(alert);
    }

    private void DismissAlert(int sourceId, string kind)
    {
        if (_alerts.TryRemove($"remote-{sourceId}-{kind}", out Guid id))
        {
            _activity.DismissAlert(id);
        }
    }
}
