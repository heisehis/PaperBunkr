using ClusterLibraryManager.ComicVine;
using ClusterLibraryManager.Dialogs;
using ClusterLibraryManager.Organizing;

namespace ClusterLibraryManager.Tests;

/// <summary>Implementation plan Phase 3 Step 3.5 verification - the dialog ViewModels' resolve
/// callback wiring, independent of any real Avalonia rendering.</summary>
public sealed class DialogViewModelTests
{
    [Fact]
    public void FileConflictDialog_replace_resolves_with_Replace_and_the_checkbox_state()
    {
        (CollisionResolution Resolution, bool ApplyToAllRemaining)? captured = null;
        var vm = new FileConflictDialogViewModel("Incoming", "Existing", "C:\\dest.cbz", result => captured = result);
        vm.ApplyToAllRemaining = true;

        vm.ReplaceCommand.Execute(null);

        Assert.Equal((CollisionResolution.Replace, true), captured);
    }

    [Fact]
    public void FileConflictDialog_rename_resolves_with_Rename()
    {
        (CollisionResolution Resolution, bool ApplyToAllRemaining)? captured = null;
        var vm = new FileConflictDialogViewModel("Incoming", "Existing", "C:\\dest.cbz", result => captured = result);

        vm.RenameCommand.Execute(null);

        Assert.Equal(CollisionResolution.Rename, captured!.Value.Resolution);
        Assert.False(captured.Value.ApplyToAllRemaining);
    }

    [Fact]
    public void FileConflictDialog_skip_resolves_with_Skip()
    {
        (CollisionResolution Resolution, bool ApplyToAllRemaining)? captured = null;
        var vm = new FileConflictDialogViewModel("Incoming", "Existing", "C:\\dest.cbz", result => captured = result);

        vm.SkipCommand.Execute(null);

        Assert.Equal(CollisionResolution.Skip, captured!.Value.Resolution);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_orders_candidates_by_descending_score()
    {
        var low = new ComicVineVolumeSearchResult(1, "Low", null, null, null, null);
        var high = new ComicVineVolumeSearchResult(2, "High", null, null, null, null);
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1",
            new[] { (low, 10.0), (high, 90.0) },
            _ => { });

        Assert.Equal("High", vm.Candidates[0].Volume.Name);
        Assert.Equal("Low", vm.Candidates[1].Volume.Name);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_choosing_a_candidate_resolves_with_that_volume()
    {
        ComicVineVolumeSearchResult? resolved = null;
        var volume = new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null);
        var vm = new ComicVineMatchReviewDialogViewModel("Batman #1", new[] { (volume, 100.0) }, v => resolved = v);

        vm.Candidates[0].ChooseCommand.Execute(null);

        Assert.Same(volume, resolved);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_skip_resolves_with_null()
    {
        ComicVineVolumeSearchResult? resolved = new(1, "Placeholder", null, null, null, null);
        var vm = new ComicVineMatchReviewDialogViewModel("Batman #1", Array.Empty<(ComicVineVolumeSearchResult, double)>(), v => resolved = v);

        vm.SkipCommand.Execute(null);

        Assert.Null(resolved);
    }
}
