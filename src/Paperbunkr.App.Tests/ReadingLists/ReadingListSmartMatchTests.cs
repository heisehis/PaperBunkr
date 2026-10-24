using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The list page's side of the matching core (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §7): "Recheck library",
/// the "needs a choice" row, and the suggested matches Find &amp; link shows. Same isolation as
/// <see cref="ReadingListPageRequestMissingTests"/>: <see cref="PaperbunkrDbContext.DatabasePathOverride"/> points at a temp database.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListSmartMatchTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ReadingListSmartMatchTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_match_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private static ReadingListsScreenViewModel Create() =>
        new(new FakeFilePicker(), (_, _) => { }, activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }), loadOnConstruction: false);

    private static int ImportCsv(string rows)
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_match_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "Series,Number,Year\n" + rows);
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            return CsvReadingListIO.Import(context, path).List.Id;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static int AddComic(string series, string number, int year)
    {
        using var context = PaperbunkrDb.CreateContext();
        var row = context.Series.FirstOrDefault(s => s.Name == series) ?? new Series { Name = series, SortName = series };
        var issue = new Issue { Series = row, Number = number, Year = year, FilePath = $@"C:\comics\{series} {number} ({year}).cbz" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void RecheckLibrary_LinksTheEntryWhoseComicArrived_AndSaysSo()
    {
        int listId = ImportCsv("Herc,7,2011\nHerc,8,2011\n");
        int arrived = AddComic("Herc", "7", 2011);
        var vm = Create();
        vm.LoadReadingList(listId);
        Assert.All(vm.List.Rows, r => Assert.True(r.IsMissing));

        vm.List.RecheckLibraryCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.Equal(arrived, vm.List.Rows[0].Item.IssueId);
        Assert.False(vm.List.Rows[0].IsMissing);
        Assert.True(vm.List.Rows[1].IsMissing);
        Assert.Equal("1 entry linked, 1 still missing.", vm.List.StatusMessage);
    }

    [Fact]
    public void AmbiguousEntry_ReadsNeedsAChoice_AndFindAndLinkOffersTheCandidatesWithReasons()
    {
        // The source gave a year no owned volume has: two candidates, neither confirmed.
        int listId = ImportCsv("Avengers,1,\n");
        int first = AddComic("Avengers", "1", 1963);
        int second = AddComic("Avengers", "1", 1996);
        var vm = Create();
        vm.LoadReadingList(listId);
        vm.List.RecheckLibraryCommand.Execute(null);
        TestDispatcher.Drain();

        var row = Assert.Single(vm.List.Rows);
        Assert.True(row.NeedsChoice);
        Assert.Equal("needs a choice ·", row.MissingLabel);
        Assert.Equal("1 needs a choice.", vm.List.StatusMessage!.Replace("0 entries linked, ", string.Empty));

        row.LinkCommand.Execute(null);
        Assert.True(vm.List.HasSuggestedMatches);
        Assert.Equal(new[] { first, second }, vm.List.SuggestedMatches.Select(s => s.IssueId).OrderBy(id => id));
        Assert.All(vm.List.SuggestedMatches, s => Assert.Contains("same number", s.Reason));

        vm.List.LinkSuggestionCommand.Execute(vm.List.SuggestedMatches.First(s => s.IssueId == second));
        TestDispatcher.Drain();

        Assert.Equal(second, vm.List.Rows[0].Item.IssueId);
        Assert.False(vm.List.IsLinking);
        using var context = PaperbunkrDb.CreateContext();
        var source = context.ReadingListItemSources.Single();
        Assert.Equal(MatchMethod.User, source.Method);       // the user's pick: no automatic pass revisits it
        Assert.Equal("Avengers", source.Series);
    }

    [Fact]
    public void LinkSuggestion_RefusesAComicTheListAlreadyHolds()
    {
        int listId = ImportCsv("Avengers,1,1996\nAvengers,1,2030\n");     // the second matches nothing by year
        int owned = AddComic("Avengers", "1", 1996);
        var vm = Create();
        vm.LoadReadingList(listId);
        vm.List.RecheckLibraryCommand.Execute(null);
        TestDispatcher.Drain();
        Assert.Equal(owned, vm.List.Rows[0].Item.IssueId);

        vm.List.Rows[1].LinkCommand.Execute(null);
        var suggestion = Assert.Single(vm.List.SuggestedMatches);
        Assert.Contains("different year", suggestion.Reason);

        vm.List.LinkSuggestionCommand.Execute(suggestion);
        TestDispatcher.Drain();

        Assert.True(vm.List.Rows[1].IsMissing);
        Assert.Contains("already in this list", vm.List.StatusMessage);
    }
}
