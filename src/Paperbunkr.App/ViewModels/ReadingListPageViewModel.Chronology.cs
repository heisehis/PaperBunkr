using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Checks strip's order-vs-chronology line (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.2): where the list's order
/// contradicts the chronology of the story events its issues belong to, with a one-click reorder, and a loop in the chronology
/// reported instead of silently resolved. Advisory - "Order is intended" dismisses it for this list for good.
/// </summary>
public partial class ReadingListPageViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChronologyCheck), nameof(ChronologySummary), nameof(ChronologyLines), nameof(CanReorderToChronology), nameof(IsChronologyFirstCheck))]
    private ReadingListChronologyResult? _chronology;

    public bool HasChronologyCheck => Chronology is { HasFindings: true } && IsListOpen;

    public bool CanReorderToChronology => Chronology is { CanReorder: true };

    /// <summary>The strip's first visible line has no divider above it.</summary>
    public bool IsChronologyFirstCheck => !HasOverlap && !HasCanonicalPanel && !HasRebuildNote;

    /// <summary>"3 of 24 entries are out of story order." / "The story order has a loop, so it cannot be checked: A → B → A."</summary>
    public string ChronologySummary
    {
        get
        {
            if (Chronology is not { } result)
            {
                return string.Empty;
            }

            if (result.OutOfOrderCount > 0)
            {
                string entries = result.OutOfOrderCount == 1 ? "1 entry is" : $"{result.OutOfOrderCount} entries are";
                return $"{entries} out of story order (of {result.RankedCount} that belong to linked story events).";
            }

            return "The story order has a loop, so part of this list cannot be checked.";
        }
    }

    /// <summary>The first few pairs that are the wrong way round, then any loop, one per line.</summary>
    public IReadOnlyList<string> ChronologyLines
    {
        get
        {
            if (Chronology is not { } result)
            {
                return Array.Empty<string>();
            }

            return result.Inversions
                .Select(i => $"{i.First} ({i.FirstEvent}) comes before {i.Second} ({i.SecondEvent}), which happens earlier")
                .Concat(result.Loops.Select(loop => $"Loop in the story order: {loop}"))
                .ToList();
        }
    }

    /// <summary>Called with the rest of the list's checks whenever the open list is loaded.</summary>
    private void RefreshChronology(PaperbunkrDbContext context)
    {
        ReadingListChronologyResult? result = null;
        if (_activeReadingListId is int listId)
        {
            try
            {
                result = ReadingListChronologyCheck.Check(context, listId);
            }
            catch (Exception ex)
            {
                // An advisory check must never stop a list from opening.
                DiagnosticsService.LogMilestone($"Reading list: chronology check failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        Chronology = result;
        RaiseChecks();
    }

    /// <summary>Moves only the entries that belong to linked story events, within the slots they already hold. Deferred reload: the button is in the strip being rebuilt.</summary>
    [RelayCommand]
    private void ReorderToChronology()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ReadingListChronologyCheck.ReorderToChronology(context, listId);
        }

        Dispatcher.UIThread.Post(Reload);
    }

    /// <summary>"Order is intended": this list is not checked against chronology again.</summary>
    [RelayCommand]
    private void DismissChronology()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            HealthDismissals.Dismiss(context, HealthDismissals.ListOrder, ReadingListChronologyCheck.DismissalKey(listId), ListName);
        }

        Dispatcher.UIThread.Post(() =>
        {
            Chronology = null;
            RaiseChecks();
        });
    }
}
