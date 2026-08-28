using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The New Reading List dialog (docs/superpowers/specs/2026-08-28-reading-lists-screen-redesign-
/// design.md → v2). Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp
/// SQLite file, same pattern as <see cref="ReadingScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class NewReadingListViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly FakeFilePickerService _filePicker = new();

    public NewReadingListViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pbnrl-{Guid.NewGuid():N}.db");
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private int _created;

    private NewReadingListViewModel Create()
    {
        _created = 0;
        var vm = new NewReadingListViewModel(_filePicker, id => _created = id, () => { });
        vm.Reset();
        return vm;
    }

    [Fact]
    public void Blank_CreatesListWithGivenName_AndFiresCallback()
    {
        var vm = Create();
        vm.Name = "My Order";
        vm.SelectMethodCommand.Execute("Blank");

        vm.CreateCommand.Execute(null);

        int id = _created;
        Assert.NotEqual(0, id);
        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Single(l => l.Id == id);
        Assert.Equal("My Order", list.Name);
        Assert.Equal(ReadingListType.User, list.Type);
    }

    [Fact]
    public void Event_SeedsItemsFromMembers_WithRolesAndLink()
    {
        int eventId, i1, i2;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Crisis" };
            context.Series.Add(series);
            context.SaveChanges();
            var issue1 = new Issue { SeriesId = series.Id, Number = "1" };
            var issue2 = new Issue { SeriesId = series.Id, Number = "2" };
            context.Issues.AddRange(issue1, issue2);
            context.SaveChanges();
            i1 = issue1.Id;
            i2 = issue2.Id;

            var storyEvent = new StoryEvent { Name = "The Big Crisis", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            storyEvent.Members.Add(new EventMembership { IssueId = i1, Position = 0, Role = EventMembershipRole.TieIn });
            storyEvent.Members.Add(new EventMembership { IssueId = i2, Position = 1, Role = EventMembershipRole.Core });
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();
            eventId = storyEvent.Id;
        }

        var vm = Create();
        vm.SelectMethodCommand.Execute("Event");
        vm.SelectedStoryEvent = vm.StoryEventOptions.Single(o => o.Id == eventId);
        Assert.True(vm.CanCreate);

        vm.CreateCommand.Execute(null);

        int id = _created;
        using var verify = PaperbunkrDb.CreateContext();
        var list = verify.ReadingLists.Include(l => l.Items).Single(l => l.Id == id);
        Assert.Equal(ReadingListType.Event, list.Type);
        Assert.Equal(eventId, list.StoryEventId);
        Assert.Equal(new[] { i1, i2 }, list.Items.OrderBy(x => x.SortOrder).Select(x => x.IssueId));
        Assert.Equal(EventMembershipRole.TieIn, list.Items.OrderBy(x => x.SortOrder).First().Role);
    }

    [Fact]
    public void CanCreate_FalseUntilAMethodIsChosen()
    {
        var vm = Create();
        Assert.False(vm.CanCreate);

        vm.SelectMethodCommand.Execute("Blank");
        Assert.True(vm.CanCreate);

        vm.SelectMethodCommand.Execute("Event");
        Assert.False(vm.CanCreate); // Event needs a selection first
    }
}
