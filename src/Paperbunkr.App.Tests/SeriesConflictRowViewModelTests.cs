using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="SeriesConflictRowViewModel.ApplyBulkAction"/> (docs/superpowers/specs/
/// 2026-08-06-migration-ux-polish-design.md §3) against plain in-memory rows - the same object
/// both <see cref="Paperbunkr.App.ViewModels.MigrationViewModel"/> and
/// <see cref="Paperbunkr.App.ViewModels.NeedsReviewViewModel"/> wire their own Merge/KeepSeparate
/// callbacks into, so this test only needs to verify which rows get touched and how.
/// </summary>
public class SeriesConflictRowViewModelTests
{
    [Fact]
    public void KeepAllSeparate_ResolvesEveryUnresolvedRow()
    {
        bool mergedA = false, keptA = false, mergedB = false, keptB = false;
        var rowA = new SeriesConflictRowViewModel("A", "A2", 0.95, _ => mergedA = true, _ => keptA = true);
        var rowB = new SeriesConflictRowViewModel("B", "B2", 0.60, _ => mergedB = true, _ => keptB = true);

        SeriesConflictRowViewModel.ApplyBulkAction(new[] { rowA, rowB }, ConflictBulkAction.KeepAllSeparate);

        Assert.True(keptA);
        Assert.True(keptB);
        Assert.False(mergedA);
        Assert.False(mergedB);
        Assert.True(rowA.IsResolved);
        Assert.True(rowB.IsResolved);
    }

    [Fact]
    public void MergeAllAboveNinety_OnlyTouchesQualifyingRows()
    {
        bool mergedHigh = false, mergedLow = false, keptLow = false;
        var rowHigh = new SeriesConflictRowViewModel("High", "High2", 0.93, _ => mergedHigh = true, _ => { });
        var rowLow = new SeriesConflictRowViewModel("Low", "Low2", 0.85, _ => mergedLow = true, _ => keptLow = true);

        SeriesConflictRowViewModel.ApplyBulkAction(new[] { rowHigh, rowLow }, ConflictBulkAction.MergeAboveNinetyPercent);

        Assert.True(mergedHigh);
        Assert.True(rowHigh.IsResolved);

        Assert.False(mergedLow);
        Assert.False(keptLow);
        Assert.False(rowLow.IsResolved);
    }

    [Fact]
    public void ApplyBulkAction_SkipsAlreadyResolvedRows()
    {
        int mergeCallCount = 0;
        var row = new SeriesConflictRowViewModel("A", "A2", 0.95, _ => mergeCallCount++, _ => { });
        row.MergeCommand.Execute(null);
        Assert.Equal(1, mergeCallCount);

        SeriesConflictRowViewModel.ApplyBulkAction(new[] { row }, ConflictBulkAction.MergeAboveNinetyPercent);

        Assert.Equal(1, mergeCallCount);
    }
}
