using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// "Merge series…" for two or more selected series (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §2/§3) - surfaces the
/// existing <see cref="SeriesMergeHelper"/>, until now only reachable from Library Health's Needs Review and Find Similar Series.
/// </summary>
public partial class LibraryScreenViewModel
{
    private Action<IReadOnlyList<int>, IReadOnlyDictionary<int, IBrush>> _openMergeSeries = (_, _) => { };

    /// <summary>Opens the Merge series dialog - set by the shell.</summary>
    internal Action<IReadOnlyList<int>, IReadOnlyDictionary<int, IBrush>> OpenMergeSeries
    {
        get => _openMergeSeries;
        set => _openMergeSeries = value;
    }

    [RelayCommand]
    private void MergeSeries(LibraryTarget target)
    {
        var local = LocalSeriesOnly(target.Ids);
        if (local.Count < 2)
        {
            return;
        }

        var covers = VisibleSeriesCards.Where(c => local.Contains(c.SeriesId))
            .GroupBy(c => c.SeriesId)
            .ToDictionary(g => g.Key, g => g.First().CoverBrush);
        _openMergeSeries(local, covers);
    }

    /// <summary>The dialog's confirm: every source merged into <paramref name="targetId"/> in one fresh context and one save.</summary>
    internal void ApplySeriesMerge(int targetId, IReadOnlyList<int> sourceIds)
    {
        string targetName;
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var target = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == targetId);
            if (target is null)
            {
                return;
            }

            targetName = target.Name;
            foreach (var source in context.Series.Include(s => s.Issues).Where(s => sourceIds.Contains(s.Id)).ToList())
            {
                SeriesMergeHelper.MergeInto(context, source, target);
            }

            context.SaveChanges();
        }
        catch (DbUpdateException ex)
        {
            // A duplicate row still referenced elsewhere (a reading list, an event) can't be removed; nothing was saved.
            _showToast("Merge failed", $"The series were not merged: {ex.InnerException?.Message ?? ex.Message}");
            return;
        }

        ClearSeriesSelection();
        LoadFromDatabase();
        _showToast("Series merged", sourceIds.Count == 1 ? $"Merged 1 series into {targetName}." : $"Merged {sourceIds.Count} series into {targetName}.");
    }
}
