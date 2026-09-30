using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Gcd;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences → Connections → "Grand Comics Database data" (docs/superpowers/specs/2026-09-27-gcd-data-design.md §2): download the
/// optional extract, update it when a newer one is published, or remove it; with GCD's credit and licence always shown.
/// </summary>
public sealed partial class GcdDataSettingsViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly IActivityService _activity;
    private readonly GcdDataInstaller _installer;
    private readonly Func<Func<Task>, Task> _runInBackground;
    private readonly Func<Func<PaperbunkrDbContext>, IActivityJobHandle?, CancellationToken, string, Task<string>> _match;

    /// <param name="bundled">The manifest built into the app; default <see cref="GcdDataInstaller.BundledManifest"/>. Nothing goes online
    /// until you click Download or Check for update.</param>
    /// <param name="runInBackground">Tests pass <c>work => work()</c> so nothing hops threads.</param>
    /// <param name="match">What runs after installing; default <see cref="GcdMatching.RunAsync"/>.</param>
    public GcdDataSettingsViewModel(
        Func<PaperbunkrDbContext> contextFactory,
        IActivityService activity,
        GcdDataInstaller? installer = null,
        GcdManifest? bundled = null,
        Func<Func<Task>, Task>? runInBackground = null,
        Func<Func<PaperbunkrDbContext>, IActivityJobHandle?, CancellationToken, string, Task<string>>? match = null)
    {
        _contextFactory = contextFactory;
        _activity = activity;
        _installer = installer ?? new GcdDataInstaller();
        _runInBackground = runInBackground ?? (work => Task.Run(work));
        _match = match ?? ((factory, handle, ct, path) => GcdMatching.RunAsync(factory, handle, ct, path));
        _available = bundled ?? GcdDataInstaller.BundledManifest();
        Refresh();
    }

    public string Credit => "Contains data from the Grand Comics Database™ (comics.org), licensed CC BY-SA 4.0.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(StateText), nameof(CanDownload), nameof(CanUpdate), nameof(CanRemove), nameof(CanCheck), nameof(ShowDownload), nameof(DownloadLabel))]
    private string? _installedDumpDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanDownload), nameof(CanUpdate), nameof(CanCheck), nameof(ShowDownload), nameof(DownloadLabel))]
    private GcdManifest? _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanDownload), nameof(CanUpdate), nameof(CanRemove), nameof(CanCheck), nameof(ShowDownload))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanCheck))]
    private bool _isChecking;

    /// <summary>The last outcome (a failure or a finished install), shown under the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusText;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public bool IsInstalled => InstalledDumpDate is not null;

    public bool CanDownload => !IsBusy && !IsInstalled && Available is not null;

    public bool CanUpdate => !IsBusy && IsInstalled && Available is not null && GcdDataInstaller.IsNewer(Available, InstalledDumpDate);

    public bool CanRemove => !IsBusy && IsInstalled;

    public bool CanCheck => !IsBusy && !IsChecking && IsInstalled && !CanUpdate;

    public bool ShowDownload => CanDownload || CanUpdate;

    public string DownloadLabel => Available is null
        ? "Download"
        : $"{(IsInstalled ? "Update" : "Download")} ({Math.Max(1, (int)Math.Round(Available.SizeBytes / 1048576.0)).ToString(CultureInfo.CurrentCulture)} MB)";

    public string StateText
    {
        get
        {
            if (IsBusy)
            {
                return IsInstalled ? "Working…" : "Downloading…";
            }

            if (IsChecking)
            {
                return "Checking for newer data…";
            }

            if (IsInstalled)
            {
                return CanUpdate ? $"Data from {InstalledDumpDate} · {Available!.DumpDate} is available" : $"Data from {InstalledDumpDate}";
            }

            return Available is null ? "Not available in this version yet" : $"Not installed · data from {Available.DumpDate}";
        }
    }

    /// <summary>Re-reads what's installed (the section was shown again, or the data changed underneath).</summary>
    public void Refresh()
    {
        InstalledDumpDate = _installer.InstalledDumpDate();
        OnPropertyChanged(nameof(CanCheck));
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsChecking || IsBusy)
        {
            return;
        }

        IsChecking = true;
        StatusText = null;
        try
        {
            var live = await _installer.FetchManifestAsync(CancellationToken.None);
            if (live is null)
            {
                StatusText = "Couldn't check for newer data. Check your connection and try again.";
                return;
            }

            if (Available is null || GcdDataInstaller.IsNewer(live, Available.DumpDate))
            {
                Available = live;
            }

            if (!CanUpdate)
            {
                StatusText = "You have the latest Grand Comics Database data.";
            }
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(nameof(CanCheck));
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsBusy || (!CanDownload && !CanUpdate))
        {
            return;
        }

        IsBusy = true;
        StatusText = null;

        // The live manifest wins when it's reachable and newer; the one built into the app still points at a real release asset.
        var live = await _installer.FetchManifestAsync(CancellationToken.None);
        if (live is not null && (Available is null || GcdDataInstaller.IsNewer(live, Available.DumpDate)))
        {
            Available = live;
        }

        var manifest = Available!;
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Downloading Grand Comics Database data");
        try
        {
            string summary = string.Empty;
            var progress = new Progress<(long Done, long Total)>(p =>
                job.Report((int)(p.Done / 1024), (int)(p.Total / 1024), $"{p.Done / 1048576.0:F1} of {p.Total / 1048576.0:F1} MB"));
            await _runInBackground(async () =>
            {
                await _installer.InstallAsync(manifest, progress, job.CancellationToken).ConfigureAwait(false);
                summary = await _match(_contextFactory, job, job.CancellationToken, _installer.ExtractPath).ConfigureAwait(false);
            });

            string done = $"Grand Comics Database data from {manifest.DumpDate} installed. {summary}";
            job.Succeed(done);
            StatusText = done;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled.";
        }
        catch (GcdInstallException ex)
        {
            job.Fail(ex.Message, ex: ex);
            StatusText = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            const string message = "Couldn't download the Grand Comics Database data. Check your connection and try again.";
            job.Fail(message, ex: ex);
            StatusText = message;
        }
        finally
        {
            Refresh();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (!IsInstalled || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _runInBackground(() =>
            {
                _installer.Remove(_contextFactory);
                return Task.CompletedTask;
            });
            StatusText = "Grand Comics Database data removed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Couldn't remove the data - it may be in use. Try again in a moment.";
        }
        finally
        {
            Refresh();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void OpenGcd() => ExternalLinks.TryOpenWeb("https://www.comics.org");

    [RelayCommand]
    private static void OpenLicence() => ExternalLinks.TryOpenWeb(GcdLinks.LicenceUrl);

    [RelayCommand]
    private static void OpenDataRepository() => ExternalLinks.TryOpenWeb(ProjectLinks.GcdDataRepository);
}
