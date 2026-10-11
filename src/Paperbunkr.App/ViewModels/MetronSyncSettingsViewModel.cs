using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences → Connections → "Metron account" (docs/superpowers/specs/2026-10-05-metron-account-sync-design.md): the master
/// switch, one switch per area, and the three things the user can ask for by hand. Every switch starts off, and the area
/// switches do nothing while the master is off - this is the only part of the app that sends reading data anywhere.
/// </summary>
public sealed partial class MetronSyncSettingsViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly IActivityService _activity;
    private readonly Func<Func<PaperbunkrDbContext>, MetronAccountSync?> _createSync;
    private readonly Func<Func<Task>, Task> _runInBackground;
    private bool _loading;

    /// <param name="createSync">Default <see cref="MetronAccountSyncRunner.Create"/>; null from it means no login is saved.</param>
    /// <param name="runInBackground">Tests pass <c>work => work()</c> so nothing hops threads.</param>
    public MetronSyncSettingsViewModel(
        Func<PaperbunkrDbContext> contextFactory,
        IActivityService activity,
        Func<Func<PaperbunkrDbContext>, MetronAccountSync?>? createSync = null,
        Func<Func<Task>, Task>? runInBackground = null)
    {
        _contextFactory = contextFactory;
        _activity = activity;
        _createSync = createSync ?? MetronAccountSyncRunner.Create;
        _runInBackground = runInBackground ?? (work => Task.Run(work));
        Refresh();
    }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _syncPullList;

    [ObservableProperty]
    private bool _syncReading;

    [ObservableProperty]
    private bool _syncCollection;

    [ObservableProperty]
    private bool _syncWishList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private bool _isBusy;

    /// <summary>What the last hand-started action did, or why it couldn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusText;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public bool CanRun => !IsBusy;

    /// <summary>Re-reads the switches (the section is opened, or settings were restored).</summary>
    public void Refresh()
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        _loading = true;
        try
        {
            IsEnabled = settings.MetronSyncEnabled;
            SyncPullList = settings.MetronSyncPullList;
            SyncReading = settings.MetronSyncReading;
            SyncCollection = settings.MetronSyncCollection;
            SyncWishList = settings.MetronSyncWishList;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Turns the hourly "Sync with Metron" task on and off with the master switch. The task ships disabled so a library that never uses
    /// account sync doesn't get an hourly "sync is off" line in its activity history. Set by <c>PreferencesScreenViewModel.AttachScheduler</c>.
    /// </summary>
    public Action<bool>? SetScheduledTaskEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Persist(s => s.MetronSyncEnabled = value);
        SetScheduledTaskEnabled?.Invoke(value);
    }

    partial void OnSyncPullListChanged(bool value) => Persist(s => s.MetronSyncPullList = value);

    partial void OnSyncCollectionChanged(bool value) => Persist(s => s.MetronSyncCollection = value);

    partial void OnSyncWishListChanged(bool value) => Persist(s => s.MetronSyncWishList = value);

    partial void OnSyncReadingChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        using var context = _contextFactory();
        context.GetOrCreateAppSettings().MetronSyncReading = value;
        context.SaveChanges();
        if (value)
        {
            // Reads from before the switch was turned on stay private until "Send my reading history" says otherwise.
            MetronAccountSync.StartReadingFromNow(context);
        }
    }

    private void Persist(Action<AppSettings> apply)
    {
        if (_loading)
        {
            return;
        }

        using var context = _contextFactory();
        apply(context.GetOrCreateAppSettings());
        context.SaveChanges();
    }

    [RelayCommand]
    private Task SyncNow() => RunAsync("Syncing with Metron", async (sync, ct) =>
    {
        if (!IsEnabled)
        {
            return (MetronAccountSyncRunner.SwitchedOffMessage, false);
        }

        var report = await sync.RunAsync(ct).ConfigureAwait(false);
        return (report.Summary, report.Failed);
    });

    /// <summary>Queues every finished read from the start; they go out over the following syncs, each within its request budget.</summary>
    [RelayCommand]
    private Task SendReadingHistory() => RunAsync("Sending reading history to Metron", async (sync, ct) =>
    {
        if (!IsEnabled || !SyncReading)
        {
            return ("Turn on Metron account sync and its Reading switch first.", false);
        }

        using (var context = _contextFactory())
        {
            MetronAccountSync.SendReadingHistory(context);
        }

        var report = await sync.RunAsync(ct).ConfigureAwait(false);
        return (report.Summary, report.Failed);
    });

    [RelayCommand]
    private Task ImportFromMetron() => RunAsync("Importing from Metron", async (sync, ct) =>
    {
        var report = await sync.ImportAsync(ct).ConfigureAwait(false);
        return (report.Summary, false);
    });

    private async Task RunAsync(string title, Func<MetronAccountSync, CancellationToken, Task<(string Summary, bool Failed)>> work)
    {
        if (IsBusy)
        {
            return;
        }

        if (_createSync(_contextFactory) is not { } sync)
        {
            StatusText = MetronAccountSyncRunner.NoLoginMessage;
            return;
        }

        IsBusy = true;
        StatusText = title + "…";
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, title, startQueued: HeavyJobLane.Shared.WouldWait);
        try
        {
            using var laneSlot = HeavyJobLane.Activate(await HeavyJobLane.Shared.EnterAsync(userStarted: true, job.CancellationToken));
            job.Begin();
            (string summary, bool failed) = (string.Empty, false);
            await _runInBackground(async () => (summary, failed) = await work(sync, job.CancellationToken).ConfigureAwait(false));
            StatusText = summary;
            if (failed)
            {
                job.Fail(summary);
            }
            else
            {
                job.Succeed(summary);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
            job.Fail("Cancelled.");
        }
        catch (Exception ex)
        {
            // The innermost message is the one that says what went wrong; EF's outer one only says to go and look for it.
            string message = ex.GetBaseException().Message;
            StatusText = message;
            job.Fail(message, ex: ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
