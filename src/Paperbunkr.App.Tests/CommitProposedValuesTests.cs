using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library's "Commit proposed values" action (docs/superpowers/specs/2026-10-11-proposed-values-design.md): accepted, filename-inferred values are written into the
/// books' own fields, as one undo step, and a book with nothing to commit is left alone. Same temp-SQLite harness as <see cref="LibraryBulkActionsTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CommitProposedValuesTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_commitproposed_{Guid.NewGuid():N}.db");
    private readonly List<(string Title, string Message)> _toasts = new();
    private readonly List<int> _writeBacks = new();

    public CommitProposedValuesTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private LibraryScreenViewModel NewVm()
    {
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (t, m) => _toasts.Add((t, m)),
            enqueueMetadataWriteBack: (id, _) => _writeBacks.Add(id));
        vm.History = new MetadataEditHistoryService();
        return vm;
    }

    /// <summary>One issue whose number and year only exist as accepted proposals (what a scan of "Alpha 005 (1999).cbz" leaves), and one that has everything stored.</summary>
    private (int Proposed, int Complete) Seed()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Alpha" };
        var proposed = new Issue { Series = series, FilePath = @"C:\lib\Alpha 005 (1999).cbz", PageCount = 10 };
        proposed.MetadataProposals.Add(new MetadataProposal { Field = MetadataProposalField.Number, ProposedValue = "5", Status = MetadataProposalStatus.Accepted });
        proposed.MetadataProposals.Add(new MetadataProposal { Field = MetadataProposalField.Year, ProposedValue = "1999", Status = MetadataProposalStatus.Accepted });
        proposed.MetadataProposals.Add(new MetadataProposal { Field = MetadataProposalField.Title, ProposedValue = "Held back", Status = MetadataProposalStatus.Pending });
        var complete = new Issue { Series = series, FilePath = @"C:\lib\Alpha 006.cbz", Number = "6", Year = 2000, PageCount = 10 };
        complete.MetadataProposals.Add(new MetadataProposal { Field = MetadataProposalField.Number, ProposedValue = "99", Status = MetadataProposalStatus.Accepted });
        context.Issues.AddRange(proposed, complete);
        context.SaveChanges();
        return (proposed.Id, complete.Id);
    }

    private static Issue Load(int id)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Single(i => i.Id == id);
    }

    [Fact]
    public void Commit_WritesTheAcceptedValuesIntoTheBooksOwnFields_AndLeavesPendingOnesOut()
    {
        var (proposed, _) = Seed();
        var vm = NewVm();

        vm.CommitProposedValuesCommand.Execute(LibraryTarget.Issues(new[] { proposed }));
        TestDispatcher.Drain();

        var issue = Load(proposed);
        Assert.Equal("5", issue.Number);
        Assert.Equal(1999, issue.Year);
        Assert.Null(issue.Title);
        Assert.Contains(_toasts, t => t.Title == "Committed proposed values" && t.Message.Contains("2 values on 1 book"));
        Assert.Contains(proposed, _writeBacks);
    }

    [Fact]
    public void Commit_OnABookWithEverythingStored_SaysThereIsNothingToCommit_AndRecordsNoUndoStep()
    {
        var (_, complete) = Seed();
        var vm = NewVm();

        vm.CommitProposedValuesCommand.Execute(LibraryTarget.Issues(new[] { complete }));
        TestDispatcher.Drain();

        Assert.Equal("6", Load(complete).Number);
        Assert.Contains(_toasts, t => t.Title == "Nothing to commit");
        Assert.False(vm.History.CanUndo);
        Assert.Empty(_writeBacks);
    }

    [Fact]
    public void Commit_OnAMixedSelection_TouchesOnlyTheBooksThatHaveSomething()
    {
        var (proposed, complete) = Seed();
        var vm = NewVm();

        vm.CommitProposedValuesCommand.Execute(LibraryTarget.Issues(new[] { proposed, complete }));
        TestDispatcher.Drain();

        Assert.Equal("5", Load(proposed).Number);
        Assert.Equal("6", Load(complete).Number);
        Assert.Contains(_toasts, t => t.Message.Contains("1 book") && !t.Message.Contains("2 books"));
    }

    [Fact]
    public void OneUndo_PutsTheBooksFieldsBack()
    {
        var (proposed, _) = Seed();
        var vm = NewVm();
        vm.CommitProposedValuesCommand.Execute(LibraryTarget.Issues(new[] { proposed }));
        TestDispatcher.Drain();

        Assert.NotNull(vm.History.Undo(PaperbunkrDb.CreateContext));

        var issue = Load(proposed);
        Assert.Null(issue.Number);
        Assert.Null(issue.Year);
    }

    [Fact]
    public void TheMenu_OffersTheAction_ForABook()
    {
        var (proposed, _) = Seed();
        var vm = NewVm();
        var row = vm.IssueList.Rows.First(r => r.Id == proposed);

        var entries = ((IContextMenuProvider)vm).BuildContextMenu(row) ?? Array.Empty<ContextMenuEntry>();

        Assert.Contains(entries.Single(e => e.Header == "Tidy up").Children!, e => e.Header == "Commit proposed values");
    }
}
