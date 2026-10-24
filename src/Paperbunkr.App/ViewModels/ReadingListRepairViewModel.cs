using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Library Health's "Reading list repairs" section (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §6, decision Q14):
/// entries that read "missing" although the comic is in the library, shown first and linked only on Apply. On demand like Publisher
/// logos - nothing is checked until the user asks. What it found but does not touch (titles split over several series rows, placeholders
/// no list uses) is listed for information.
/// </summary>
public sealed partial class ReadingListRepairViewModel : ObservableObject
{
    private const int MaxRows = 500;

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public ReadingListRepairViewModel(Func<PaperbunkrDbContext>? contextFactory = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
    }

    /// <summary>"List · what was asked for → the comic found", one per entry Apply would link.</summary>
    public ObservableCollection<RepairRelink> Relinks { get; } = new();

    public ObservableCollection<string> SplitSeries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDetail), nameof(HasRelinks), nameof(ApplyLabel), nameof(HasSplitSeries), nameof(HasNotes))]
    private RepairPreview? _preview;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasRelinks => Preview is { Relinks.Count: > 0 };

    public bool HasSplitSeries => Preview is { SplitSeries.Count: > 0 };

    public string ApplyLabel => Preview is { Relinks.Count: var n and > 0 } ? (n == 1 ? "Link 1 entry" : $"Link {n} entries") : "Link";

    public string HeaderDetail => Preview switch
    {
        null => "· Not checked yet",
        { Relinks.Count: 0 } => "· Nothing to link",
        { Relinks.Count: 1 } => "· 1 entry can be linked",
        var p => $"· {p.Relinks.Count} entries can be linked",
    };

    /// <summary>What the check saw and leaves alone, as sentences; empty when there is nothing to say.</summary>
    public string Notes
    {
        get
        {
            if (Preview is not { } p)
            {
                return string.Empty;
            }

            var lines = new System.Collections.Generic.List<string>();
            if (p.NeedChoice > 0)
            {
                lines.Add($"{p.NeedChoice} {(p.NeedChoice == 1 ? "entry needs" : "entries need")} a choice: the library has more than one comic that could be it. Open the list and use Find & link.");
            }

            if (p.StillMissing > 0)
            {
                lines.Add($"{p.StillMissing} {(p.StillMissing == 1 ? "entry is" : "entries are")} not in the library.");
            }

            if (p.AlreadyInList.Count > 0)
            {
                lines.Add($"{p.AlreadyInList.Count} {(p.AlreadyInList.Count == 1 ? "entry's comic is" : "entries' comics are")} already in the same list under another entry, for example {p.AlreadyInList[0]}.");
            }

            if (p.UnusedPlaceholders > 0)
            {
                lines.Add($"{p.UnusedPlaceholders} placeholder {(p.UnusedPlaceholders == 1 ? "issue is" : "issues are")} not used by any list. They are left alone, because \"add a physical book\" makes placeholders the same way.");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool HasNotes => Notes.Length > 0;

    /// <summary>Looks, changes nothing.</summary>
    [RelayCommand]
    private async Task Check()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            Show(await Task.Run(() =>
            {
                using var context = _contextFactory();
                return ReadingListRepair.Preview(context);
            }));
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            StatusMessage = $"Couldn't check the reading lists: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Links what the last check showed (re-evaluated against the library as it is now), in one transaction, then checks again.</summary>
    [RelayCommand]
    private async Task Apply()
    {
        if (IsBusy || !HasRelinks)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var (healed, preview) = await Task.Run(() =>
            {
                using var context = _contextFactory();
                var result = ReadingListRepair.Apply(context);
                return (result, ReadingListRepair.Preview(context));
            });
            Show(preview);
            StatusMessage = healed.Summary;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            StatusMessage = $"Nothing was changed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(RepairPreview preview)
    {
        Relinks.Clear();
        foreach (var relink in preview.Relinks.Take(MaxRows))
        {
            Relinks.Add(relink);
        }

        SplitSeries.Clear();
        foreach (var title in preview.SplitSeries.Take(MaxRows))
        {
            SplitSeries.Add(title);
        }

        Preview = preview;
        OnPropertyChanged(nameof(Notes));
    }
}
