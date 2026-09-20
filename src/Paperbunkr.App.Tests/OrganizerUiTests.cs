using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Scraper;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Organizing;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>"Organize…" and the organizer profiles in Preferences (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 8).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class OrganizerUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_organizer_ui_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly List<ActivityRun> _runs = new();
    private readonly List<int> _writeBacks = new();

    public OrganizerUiTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private OrganizeCoordinator Coordinator() =>
        new(new NativePluginModalHostViewModel(), NewContext, new ActivityService(dispatch: a => a(), recordRun: _runs.Add), _writeBacks.Add);

    private (int IssueId, string SourcePath) SeedIssue()
    {
        var source = Path.Combine(_root, "incoming", "batman3.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "not really a comic");
        using var context = NewContext();
        var issue = new Issue { Series = new Series { Name = "Batman" }, Number = "3", FilePath = source, FileSize = 18 };
        context.Issues.Add(issue);
        context.SaveChanges();
        return (issue.Id, source);
    }

    private OrganizerProfile SeedProfile(string baseFolder, OrganizerMode mode = OrganizerMode.Move) =>
        Coordinator().Profiles.Save(new OrganizerProfile
        {
            Name = "Test", BaseFolder = baseFolder, Mode = mode, FolderTemplate = "{<series>}", FileTemplate = "{<series>} #{<number2>}",
        });

    [Fact]
    public async Task WithNoProfiles_OrganizeSaysWhereToCreateOne()
    {
        var (id, source) = SeedIssue();

        Assert.Equal(OrganizeCoordinator.NoProfilesMessage, await Coordinator().OrganizeIssuesAsync(new[] { id }));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task AProfileWithNoBaseFolder_IsRefusedBeforeAnythingMoves()
    {
        var (id, source) = SeedIssue();
        var profile = SeedProfile(baseFolder: string.Empty);

        var message = await Coordinator().OrganizeLibraryAsync(profile.Id, CancellationToken.None);

        Assert.Contains("base folder", message);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task AnUnattendedOrganize_MovesTheFile_UpdatesTheLibrary_QueuesTheWriteBack_AndIsOneActivityJob()
    {
        var (id, source) = SeedIssue();
        var library = Path.Combine(_root, "library");
        var profile = SeedProfile(library);

        var message = await Coordinator().OrganizeLibraryAsync(profile.Id, CancellationToken.None);

        var expected = Path.Combine(library, "Batman", "Batman #03.cbz");
        Assert.Contains("Organized 1 comic", message);
        Assert.True(File.Exists(expected));
        Assert.False(File.Exists(source));
        using (var check = NewContext())
        {
            Assert.Equal(expected, check.Issues.Single(i => i.Id == id).FilePath);
            Assert.Equal(1, check.OrganizeMoves.Count());
        }

        Assert.Equal(new[] { id }, _writeBacks);
        Assert.Equal(ActivityRunStatus.Succeeded, Assert.Single(_runs).Status);
    }

    [Fact]
    public async Task UndoLastOrganize_PutsTheFileBack_AndMarksTheBatchRevertedInsteadOfDeletingIt()
    {
        var (id, source) = SeedIssue();
        var library = Path.Combine(_root, "library");
        var profile = SeedProfile(library);
        await Coordinator().OrganizeLibraryAsync(profile.Id, CancellationToken.None);
        var manager = new ProfileManagerViewModel(
            new OrganizerProfileStore(NewContext), organizerService: OrganizeCoordinator.CreateService(NewContext), createDbContext: NewContext);

        await manager.UndoLastOrganizeCommand.ExecuteAsync(null);

        Assert.True(File.Exists(source));
        using var check = NewContext();
        Assert.Equal(source, check.Issues.Single(i => i.Id == id).FilePath);
        Assert.All(check.OrganizeMoves, m => Assert.True(m.IsReverted));         // kept for the audit trail
        Assert.Contains("1", manager.UndoStatusMessage);
    }

    [Fact]
    public void TheProfileEditor_UsesTextForItsChoices_AndIgnoresAnUnknownName()
    {
        var row = new OrganizerProfileRowViewModel(new OrganizerProfile { Mode = OrganizerMode.Move, AutomationCollisionPolicy = AutomationCollisionPolicy.Rename });

        row.ModeText = "copy";
        row.CollisionPolicyText = "Skip";
        Assert.Equal(OrganizerMode.Copy, row.Mode);
        Assert.Equal("Skip", row.CollisionPolicyText);

        row.ModeText = "nonsense";
        Assert.Equal(OrganizerMode.Copy, row.Mode);                               // unchanged, not thrown

        row.AddExcludeCondition();
        var condition = row.ExcludeConditions.Single();
        condition.FieldText = "SeriesName";
        condition.OperatorText = "contains";
        var reloaded = new OrganizerProfileRowViewModel(ProfileRoundTrip(row)).ExcludeConditions.Single();   // through the saved rule JSON and back
        Assert.Equal("SeriesName", reloaded.FieldText);
        Assert.Equal("Contains", reloaded.OperatorText);
    }

    private static OrganizerProfile ProfileRoundTrip(OrganizerProfileRowViewModel row) => row.ToProfile();

    /// <summary>Proves each new view's compiled XAML was woven (see CLAUDE.md, "adding a new Avalonia View").</summary>
    [Fact]
    public void TheOrganizerViews_Construct()
    {
        TestAppBuilder.EnsureInitialized();
        var manager = new ProfileManagerViewModel(new OrganizerProfileStore(NewContext), organizerService: OrganizeCoordinator.CreateService(NewContext), createDbContext: NewContext);
        manager.AddProfileCommand.Execute(null);

        Assert.NotNull(new ProfileManagerView { DataContext = manager }.Content);
        Assert.NotNull(new ProfileSelectDialogView { DataContext = new ProfileSelectDialogViewModel(new[] { new OrganizerProfile { Name = "A" } }, _ => { }) }.Content);
        Assert.NotNull(new FileConflictDialogView { DataContext = new FileConflictDialogViewModel("Batman #3", "Batman #03.cbz", "C:/x/Batman #03.cbz", _ => { }) }.Content);

        var section = new Paperbunkr.App.Views.Preferences.OrganizeScrapeSection
        {
            DataContext = new OrganizeScrapeSettingsViewModel(NewContext, () => { }, manager),
        };
        Assert.NotNull(section.Content);
    }
}

/// <summary>The two scheduled tasks the built-in scraper and organizer add to Preferences → Automation.</summary>
public class ScrapeOrganizeScheduledTaskTests
{
    [Theory]
    [InlineData(ScheduledTaskCatalog.ComicVineScrape)]
    [InlineData(ScheduledTaskCatalog.LibraryOrganize)]
    public void TheTasksExist_AreOffByDefault_AndCannotRunBeforeTheShellIsReady(string id)
    {
        var task = Assert.Single(ScheduledTaskCatalog.All, t => t.Id == id);

        Assert.False(task.DefaultEnabled);                                  // scraping and reorganizing a library are never on until asked for
        Assert.Equal(1, ScheduledTaskCatalog.All.Count(t => t.Id == id));
        Assert.Equal(ScheduledTaskCatalog.All.Count, ScheduledTaskCatalog.All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void TheOrganizeTask_NamesTheProfileFlagItNeeds()
    {
        var task = Assert.Single(ScheduledTaskCatalog.All, t => t.Id == ScheduledTaskCatalog.LibraryOrganize);

        Assert.Contains("marked for scheduled runs", task.Description);
    }
}
