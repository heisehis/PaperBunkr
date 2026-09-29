using System.IO;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers the pure folder/file resolution split out of <see cref="RevealInExplorerHelper"/> (the
/// actual shell P/Invoke can't be tested without touching the real Explorer). Added alongside
/// <see cref="RevealInExplorerHelper.ResolveBookFilePath"/> for the Book Details screen
/// (docs/superpowers/specs/2026-08-27-book-details-screen-design.md).
/// </summary>
public class RevealInExplorerHelperTests
{
    [Fact]
    public void ResolveBookFilePath_WithFile_ReturnsThePathUnchanged()
    {
        var book = new Book { FilePath = @"C:\books\Dune.epub" };

        Assert.Equal(@"C:\books\Dune.epub", RevealInExplorerHelper.ResolveBookFilePath(book));
    }

    [Fact]
    public void ResolveBookFilePath_NoFile_ReturnsNull()
    {
        Assert.Null(RevealInExplorerHelper.ResolveBookFilePath(new Book { FilePath = string.Empty }));
    }

    // Regression: a moved/deleted file used to throw DirectoryNotFoundException out of
    // FileExplorer.SelectUsingAPI's File.GetAttributes probe and crash the menu click.
    [Fact]
    public void RevealIssue_FileNoLongerOnDisk_ReturnsFalseAndRaisesNotFoundAlert()
    {
        var activity = new ActivityService(dispatch: a => a(), recordRun: _ => { });
        string path = Path.Combine(Path.GetTempPath(), "pb-missing-" + System.Guid.NewGuid().ToString("N"), "Issue #5.cbz");

        bool revealed = RevealInExplorerHelper.RevealIssue(new Issue { FilePath = path }, activity);

        Assert.False(revealed);
        var alert = Assert.Single(activity.Alerts);
        Assert.Equal("File not found", alert.Title);
        Assert.Equal(new ActivityLink(ActivityLinkKind.Preferences, "LibraryHealth/Files"), alert.ActionLink);
    }

    [Fact]
    public void RevealIssue_SameMissingFileTwice_KeepsOneAlert()
    {
        var activity = new ActivityService(dispatch: a => a(), recordRun: _ => { });
        var issue = new Issue { FilePath = Path.Combine(Path.GetTempPath(), "pb-missing-" + System.Guid.NewGuid().ToString("N"), "a.cbz") };

        RevealInExplorerHelper.RevealIssue(issue, activity);
        RevealInExplorerHelper.RevealIssue(issue, activity);

        Assert.Single(activity.Alerts);
    }

    [Fact]
    public void RevealIssues_SeveralMissingFolders_RaisesOneCombinedAlert()
    {
        var activity = new ActivityService(dispatch: a => a(), recordRun: _ => { });
        string root = Path.Combine(Path.GetTempPath(), "pb-missing-" + System.Guid.NewGuid().ToString("N"));
        var issues = new[]
        {
            new Issue { FilePath = Path.Combine(root, "A", "1.cbz") },
            new Issue { FilePath = Path.Combine(root, "B", "2.cbz") },
        };

        RevealInExplorerHelper.RevealIssues(issues, activity);

        Assert.Equal("Folders not found", Assert.Single(activity.Alerts).Title);
    }
}
