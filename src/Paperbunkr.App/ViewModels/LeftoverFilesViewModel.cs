using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Library;

namespace Paperbunkr.App.ViewModels;

/// <summary>One leftover copy whose comic is missing or damaged: shown so the user can put it back or delete it.</summary>
public sealed class LeftoverFileRow
{
    public LeftoverFileRow(SwapRestoreCandidate candidate)
    {
        Candidate = candidate;
    }

    public SwapRestoreCandidate Candidate { get; }

    public string FileName => Path.GetFileName(Candidate.LeftoverPath);

    public string Folder => Path.GetDirectoryName(Candidate.LeftoverPath) ?? string.Empty;

    public string Detail =>
        $"{FormatSize(Candidate.Size)} · {Candidate.ModifiedUtc.ToLocalTime():g} · " +
        (Candidate.OriginalExists ? "the comic beside it does not open" : "the comic it belongs to is missing") +
        (Candidate.LeftoverIsValidArchive ? "" : " · this copy does not open either");

    public bool CanRestore => !Candidate.OriginalExists && Candidate.LeftoverIsValidArchive;

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}

/// <summary>
/// Library Health &gt; Files &gt; "Leftover files" (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md Part 2):
/// sweeps the library folders for swap copies a metadata write or an organize left behind (<c>.crswap</c>, <c>.pbwrite-*.tmp</c>,
/// <c>~RF*.TMP</c>), deletes the ones beside an intact comic, and lists the ones that may be the only good copy.
/// </summary>
public partial class LeftoverFilesViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly IActivityService? _activity;

    public LeftoverFilesViewModel(Func<PaperbunkrDbContext> contextFactory, IActivityService? activity = null)
    {
        _contextFactory = contextFactory;
        _activity = activity;
    }

    /// <summary>Test seam: the sweep itself (runs off the UI thread).</summary>
    internal Func<IReadOnlyList<string>, CancellationToken, SwapSweepResult> Sweep { get; set; } = (roots, ct) => SwapFileSweeper.Sweep(roots, ct);

    public ObservableCollection<LeftoverFileRow> RestoreCandidates { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanUpCommand))]
    private bool _isBusy;

    public bool HasStatus => Status.Length > 0;

    public bool HasRestoreCandidates => RestoreCandidates.Count > 0;

    /// <summary>The library's own folders: the only places swept.</summary>
    public IReadOnlyList<string> LibraryRoots()
    {
        using var context = _contextFactory();
        return context.WatchedFolders.AsNoTracking().Select(f => f.Path).ToList();
    }

    [RelayCommand(CanExecute = nameof(CanCleanUp))]
    private async Task CleanUpAsync()
    {
        IsBusy = true;
        Status = "Looking for leftover files…";
        try
        {
            var roots = LibraryRoots();
            var result = await Task.Run(() => Sweep(roots, CancellationToken.None));
            Apply(result);
            Status = Describe(result);
        }
        catch (Exception ex)
        {
            Status = $"Could not finish the clean-up: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCleanUp() => !IsBusy;

    /// <summary>Shows a sweep's restore candidates (also used by the startup sweep).</summary>
    public void Apply(SwapSweepResult result)
    {
        RestoreCandidates.Clear();
        foreach (var candidate in result.RestoreCandidates)
        {
            RestoreCandidates.Add(new LeftoverFileRow(candidate));
        }

        OnPropertyChanged(nameof(HasRestoreCandidates));
    }

    [RelayCommand]
    private void Restore(LeftoverFileRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            string restored = SwapFileSweeper.Restore(row.Candidate);
            Status = $"Restored {Path.GetFileName(restored)}. Scan the library to pick it up.";
            RemoveRowDeferred(row);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void Delete(LeftoverFileRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            RecycleBinHelper.SendToRecycleBin(row.Candidate.LeftoverPath);
            Status = $"Moved {row.FileName} to the Recycle Bin.";
            RemoveRowDeferred(row);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    // The clicked button lives in the row: removing it while its Click is still routing breaks Avalonia's child list (CLAUDE.md).
    private void RemoveRowDeferred(LeftoverFileRow row) => Dispatcher.UIThread.Post(() =>
    {
        RestoreCandidates.Remove(row);
        OnPropertyChanged(nameof(HasRestoreCandidates));
    });

    public static string Describe(SwapSweepResult result)
    {
        var parts = new List<string>();
        parts.Add(result.Deleted == 0 ? "No leftover files to remove." : $"Removed {result.Deleted} leftover file{(result.Deleted == 1 ? "" : "s")}.");
        if (result.RestoreCandidates.Count > 0)
        {
            parts.Add($"{result.RestoreCandidates.Count} kept because the comic beside {(result.RestoreCandidates.Count == 1 ? "it is" : "them is")} missing or damaged.");
        }

        if (result.Skipped > 0)
        {
            parts.Add($"{result.Skipped} in use or too recent, left for next time.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// The quiet sweep at startup: says something in the Activity Center only when it removed files or needs the user.
    /// </summary>
    public async Task SweepInBackgroundAsync(CancellationToken cancellationToken = default)
    {
        var roots = LibraryRoots();
        if (roots.Count == 0)
        {
            return;
        }

        var result = await Task.Run(() => Sweep(roots, cancellationToken), cancellationToken).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() => Apply(result));

        if (_activity is null || (result.Deleted == 0 && result.RestoreCandidates.Count == 0))
        {
            return;
        }

        _activity.RaiseAlert(new ActivityAlert
        {
            Severity = result.RestoreCandidates.Count > 0 ? ActivityAlertSeverity.Warning : ActivityAlertSeverity.Info,
            Title = result.RestoreCandidates.Count > 0 ? "Leftover copies need a look" : "Cleaned up leftover files",
            Detail = Describe(result),
            ActionLabel = "Library Health",
            ActionLink = new ActivityLink(ActivityLinkKind.Preferences, "LibraryHealth/Files/LeftoverFiles"),
            DedupeKey = "leftover-files",
        });
    }
}
