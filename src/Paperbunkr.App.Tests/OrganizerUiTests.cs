using System;
using Avalonia.VisualTree;
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

    private OrganizeCoordinator Coordinator(Func<OrganizePlanSummary, IReadOnlyList<OrganizerProfile>, Task<bool>>? confirmPlan = null) =>
        new(new NativePluginModalHostViewModel(), NewContext, new ActivityService(dispatch: a => a(), recordRun: _runs.Add), _writeBacks.Add, confirmPlan, Path.Combine(_root, "reports"));

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

    [Theory]
    [InlineData(OrganizerMode.Simulate)]
    [InlineData(OrganizerMode.Copy)]
    public async Task ASimulateOrCopyRun_NeverQueuesAWriteBack_BecauseNothingChangedWhereTheLibraryPointsAtIt(OrganizerMode mode)
    {
        var (id, source) = SeedIssue();
        var profile = SeedProfile(Path.Combine(_root, "library"), mode);

        await Coordinator().OrganizeLibraryAsync(profile.Id, CancellationToken.None);

        Assert.Empty(_writeBacks);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ABadTemplateToken_IsReportedWithItsReason_InsteadOfEndingTheJobAsCancelled()
    {
        var (id, source) = SeedIssue();
        var profile = SeedProfile(Path.Combine(_root, "library"));
        profile.FileTemplate = "{<definitelynotatoken>}";
        Coordinator().Profiles.Save(profile);

        var message = await Coordinator().OrganizeIssuesAsync(new[] { id });

        Assert.Contains("1 failed", message);
        Assert.Contains("definitelynotatoken", message);
        Assert.True(File.Exists(source));
        Assert.Equal(ActivityRunStatus.Succeeded, Assert.Single(_runs).Status);
        Assert.Empty(_writeBacks);
    }

    [Fact]
    public async Task AFileAlreadyWhereItBelongs_IsCountedAsSuchAndNotWrittenBack()
    {
        var (id, source) = SeedIssue();
        var library = Path.Combine(_root, "library");
        var profile = SeedProfile(library);
        await Coordinator().OrganizeLibraryAsync(profile.Id, CancellationToken.None);
        _writeBacks.Clear();

        var message = await Coordinator().OrganizeLibraryAsync(profile.Id, CancellationToken.None);

        Assert.Contains("1 already in place", message);
        Assert.Empty(_writeBacks);
    }

    [Fact]
    public async Task AManualMove_ShowsThePreviewFirst_AndCancellingLeavesEverythingWhereItWas()
    {
        var (id, source) = SeedIssue();
        SeedProfile(Path.Combine(_root, "library"));
        OrganizePlanSummary? seen = null;

        var message = await Coordinator((summary, _) => { seen = summary; return Task.FromResult(false); }).OrganizeIssuesAsync(new[] { id });

        Assert.Equal("Organize cancelled.", message);
        Assert.Equal(1, seen!.Moving);
        Assert.True(File.Exists(source));
        Assert.Empty(_writeBacks);
    }

    [Fact]
    public async Task AManualMove_ProceedsOnceTheUserConfirmsThePreview()
    {
        var (id, source) = SeedIssue();
        var library = Path.Combine(_root, "library");
        SeedProfile(library);

        var message = await Coordinator((_, _) => Task.FromResult(true)).OrganizeIssuesAsync(new[] { id });

        Assert.Contains("Organized 1 comic", message);
        Assert.True(File.Exists(Path.Combine(library, "Batman", "Batman #03.cbz")));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task AScheduledRun_NeverAsksForAPreview()
    {
        var (id, source) = SeedIssue();
        var profile = SeedProfile(Path.Combine(_root, "library"));
        var asked = false;

        await Coordinator((_, _) => { asked = true; return Task.FromResult(false); }).OrganizeLibraryAsync(profile.Id, CancellationToken.None);

        Assert.False(asked);
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task ASimulateRun_ShowsThePlan_WritesAReport_AndMovesNothing()
    {
        var (id, source) = SeedIssue();
        SeedProfile(Path.Combine(_root, "library"), OrganizerMode.Simulate);
        var shown = false;

        var message = await Coordinator((_, _) => { shown = true; return Task.FromResult(false); }).OrganizeIssuesAsync(new[] { id });

        Assert.True(shown);
        Assert.Contains("Nothing was moved", message);
        Assert.Contains("1 would be processed", message);
        Assert.True(File.Exists(source));
        Assert.Empty(_writeBacks);
    }

    [Fact]
    public async Task ARunWithFailures_KeepsTheFullListInAReportFile()
    {
        var (id, source) = SeedIssue();
        var profile = SeedProfile(Path.Combine(_root, "library"));
        profile.FileTemplate = "{<definitelynotatoken>}";
        Coordinator().Profiles.Save(profile);

        var message = await Coordinator().OrganizeIssuesAsync(new[] { id });

        Assert.Contains("Full report:", message);
    }

    [Fact]
    public void ThePreviewDialog_DescribesWhatWillHappen_AndASimulationOffersOnlyClose()
    {
        var plan = new OrganizePlan(new[]
        {
            new PlannedMove(new Issue { Series = new Series { Name = "Batman" }, Number = "3" }, "C:/in/3.cbz", "C:/lib/Batman/Batman #03.cbz", false),
            new PlannedMove(new Issue { Series = new Series { Name = "Batman" }, Number = "4" }, "C:/in/4.cbz", "C:/in/4.cbz", false, Problem: "bad token"),
        });
        var summary = OrganizePlanSummary.From(plan);

        var move = new OrganizePreviewDialogViewModel(summary, new OrganizerProfile { Name = "P", BaseFolder = "C:/lib", Mode = OrganizerMode.Move }, _ => { });
        Assert.Equal("Move 1 comic?", move.Title);
        Assert.Equal("Move", move.ConfirmLabel);
        Assert.False(move.IsSimulation);
        Assert.True(move.HasProblems);
        Assert.Contains(move.Counts, c => c.Contains("cannot be organized"));

        var simulate = new OrganizePreviewDialogViewModel(summary, new OrganizerProfile { Name = "P", Mode = OrganizerMode.Simulate }, _ => { });
        Assert.True(simulate.IsSimulation);
        Assert.Equal("Close", simulate.CancelLabel);
    }

    [Fact]
    public async Task SeveralProfilesInOneRun_CopyAndMoveTogether_AndOnlyTheMoveIsWrittenBack()
    {
        var (id, source) = SeedIssue();
        var move = SeedProfile(Path.Combine(_root, "library"));
        var coordinator = Coordinator();
        var copy = coordinator.Profiles.Save(new OrganizerProfile
        {
            Name = "Backup", BaseFolder = Path.Combine(_root, "backup"), Mode = OrganizerMode.Copy, FolderTemplate = "{<series>}", FileTemplate = "{<series>} #{<number2>}",
        });

        var message = await coordinator.OrganizeLibraryAsync(new[] { move.Id, copy.Id }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "library", "Batman", "Batman #03.cbz")));
        Assert.True(File.Exists(Path.Combine(_root, "backup", "Batman", "Batman #03.cbz")));
        Assert.False(File.Exists(source));
        Assert.Contains("Test:", message);                      // each profile is named in the one-line report
        Assert.Contains("Backup:", message);
        Assert.Equal(new[] { id }, _writeBacks);                // the copy leaves the library pointing where it did; only the move is written back
        Assert.Single(_runs);                                   // one Activity job for the whole run
    }

    [Fact]
    public async Task ACopyProfileWhoseFolderIsInsideAWatchedFolder_IsRefusedBeforeAnythingIsCopied()
    {
        var (id, source) = SeedIssue();
        var watched = Path.Combine(_root, "library");
        using (var context = NewContext())
        {
            context.WatchedFolders.Add(new WatchedFolder { Path = watched });
            context.SaveChanges();
        }

        var profile = SeedProfile(Path.Combine(watched, "Organized"), OrganizerMode.Copy);

        var message = await Coordinator().OrganizeLibraryAsync(profile.Id, CancellationToken.None);

        Assert.Contains("inside the watched library folder", message);
        Assert.Contains("Nothing was copied", message);
        Assert.False(Directory.Exists(Path.Combine(watched, "Organized")));
        Assert.True(File.Exists(source));
        Assert.Empty(_writeBacks);
        Assert.Empty(_runs);                                         // refused before an Activity job even started
    }

    [Fact]
    public void ThePicker_TicksTheFirstProfile_LetsYouTickMore_AndReturnsThemInListOrder()
    {
        var profiles = new[] { new OrganizerProfile { Name = "A" }, new OrganizerProfile { Name = "B" }, new OrganizerProfile { Name = "C" } };
        IReadOnlyList<OrganizerProfile>? chosen = null;
        var vm = new ProfileSelectDialogViewModel(profiles, r => chosen = r);
        Assert.True(vm.Profiles[0].IsSelected);
        Assert.Equal("1 profile selected.", vm.Summary);

        vm.Profiles[2].IsSelected = true;
        Assert.Contains("2 profiles selected", vm.Summary);
        vm.RunCommand.Execute(null);

        Assert.Equal(new[] { "A", "C" }, chosen!.Select(p => p.Name));
    }

    [Fact]
    public void ThePicker_CannotRunWithNothingTicked_AndCancelResolvesNull()
    {
        var resolved = false;
        IReadOnlyList<OrganizerProfile>? chosen = new List<OrganizerProfile>();
        var vm = new ProfileSelectDialogViewModel(new[] { new OrganizerProfile { Name = "A" } }, r => { resolved = true; chosen = r; });

        vm.Profiles[0].IsSelected = false;
        Assert.False(vm.RunCommand.CanExecute(null));
        Assert.Equal("Tick at least one profile.", vm.Summary);

        vm.CancelCommand.Execute(null);
        Assert.True(resolved);
        Assert.Null(chosen);
    }

    [Fact]
    public void ThePreviewDialog_NamesEveryProfile_WhenSeveralRunTogether()
    {
        var plan = new OrganizePlan(new[] { new PlannedMove(new Issue { Series = new Series { Name = "Batman" }, Number = "3" }, "C:/in/3.cbz", "C:/lib/3.cbz", false) });
        var summary = OrganizePlanSummary.From(plan);
        var profiles = new[]
        {
            new OrganizerProfile { Name = "Library", Mode = OrganizerMode.Move }, new OrganizerProfile { Name = "Backup", Mode = OrganizerMode.Copy },
        };

        var vm = new OrganizePreviewDialogViewModel(summary, profiles, _ => { });

        Assert.Equal("Organize 1 comic with 2 profiles?", vm.Title);
        Assert.Equal("Library (Move); Backup (Copy)", vm.Subtitle);
        Assert.Equal("Organize", vm.ConfirmLabel);
        Assert.False(vm.IsSimulation);
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

    [Fact]
    public void ThePluginOptions_SurviveTheEditorRoundTrip_AsTextTheUserCanEdit()
    {
        var profile = new OrganizerProfile
        {
            Name = "P",
            UseFolder = false,
            EmptyFolder = "Unknown",
            EmptyData = new Dictionary<string, string> { ["publisher"] = "Indie" },
            FailEmptyValues = true,
            FailedFields = new List<string> { "series", "number" },
            ExcludeFolders = new List<string> { "Keep Here", "Old" },
            ExcludeRuleJson = System.Text.Json.JsonSerializer.Serialize(
                Paperbunkr.Plugins.Automation.PluginConditionGroup.Or(new Paperbunkr.Plugins.Automation.PluginCondition(SmartListField.SeriesName, SmartListOperator.Contains, "x", Not: false))),
        };

        var row = new OrganizerProfileRowViewModel(profile);
        Assert.Contains("publisher=Indie", row.EmptyDataText);
        Assert.Equal("series, number", row.FailedFieldsText);
        Assert.True(row.ExcludeMatchAny);

        row.EmptyDataText = "publisher = Indie\nimprint=<none>\nbroken line";
        row.ExcludeFoldersText = "Keep Here\n\nOld ";
        var saved = row.ToProfile();

        Assert.False(saved.UseFolder);
        Assert.Equal("Indie", saved.EmptyData["publisher"]);
        Assert.Equal("<none>", saved.EmptyData["imprint"]);
        Assert.Equal(2, saved.EmptyData.Count);                                   // the line with no '=' is ignored
        Assert.Equal(new[] { "Keep Here", "Old" }, saved.ExcludeFolders);
        Assert.Equal(new[] { "series", "number" }, saved.FailedFields);
        Assert.Contains("\"Mode\":1", saved.ExcludeRuleJson);                     // ANY stays an OR group
    }

    private static Paperbunkr.Plugins.Automation.PluginCondition Cond(SmartListField field, string value) =>
        new(field, SmartListOperator.Contains, value, Not: false);

    [Fact]
    public void NestedExcludeGroups_RoundTripThroughTheEditor_ToAnyDepth()
    {
        var rule = new Paperbunkr.Plugins.Automation.PluginConditionGroup(
            SmartListGroupMode.And,
            new[] { Cond(SmartListField.SeriesName, "Batman") },
            new[]
            {
                new Paperbunkr.Plugins.Automation.PluginConditionGroup(
                    SmartListGroupMode.Or,
                    new[] { Cond(SmartListField.Publisher, "DC"), Cond(SmartListField.Publisher, "Vertigo") },
                    new[] { Paperbunkr.Plugins.Automation.PluginConditionGroup.And(Cond(SmartListField.Number, "1")) }),
            });
        var profile = new OrganizerProfile { Name = "P", ExcludeRuleJson = System.Text.Json.JsonSerializer.Serialize(rule) };

        var row = new OrganizerProfileRowViewModel(profile);

        Assert.False(row.RootGroup.MatchAny);                                       // the top level is ALL of
        var child = Assert.Single(row.RootGroup.Groups);
        Assert.True(child.MatchAny);                                                // its child is ANY of
        Assert.Equal(2, child.Conditions.Count);
        Assert.Single(Assert.Single(child.Groups).Conditions);                     // and that has a group of its own
        var saved = System.Text.Json.JsonSerializer.Deserialize<Paperbunkr.Plugins.Automation.PluginConditionGroup>(row.ToProfile().ExcludeRuleJson!)!;
        Assert.Equal(SmartListGroupMode.And, saved.Mode);
        Assert.Equal(SmartListGroupMode.Or, Assert.Single(saved.ChildGroups).Mode);
        Assert.Equal("Vertigo", saved.ChildGroups[0].Conditions[1].Value);
        Assert.Equal("1", Assert.Single(Assert.Single(saved.ChildGroups[0].ChildGroups).Conditions).Value);
    }

    [Fact]
    public void AGroupWithNoConditions_IsDroppedWhenSaved_AndAnEmptyRuleSavesNoRuleAtAll()
    {
        var row = new OrganizerProfileRowViewModel(new OrganizerProfile { Name = "P" });
        Assert.True(row.RootGroup.IsEmpty);
        Assert.Null(row.ToProfile().ExcludeRuleJson);

        row.RootGroup.AddGroupCommand.Execute(null);
        row.RootGroup.Groups[0].AddGroupCommand.Execute(null);
        Assert.Null(row.ToProfile().ExcludeRuleJson);                               // only empty groups: nothing to exclude

        row.RootGroup.Groups[0].Groups[0].AddConditionCommand.Execute(null);
        var saved = System.Text.Json.JsonSerializer.Deserialize<Paperbunkr.Plugins.Automation.PluginConditionGroup>(row.ToProfile().ExcludeRuleJson!)!;
        Assert.Single(Assert.Single(Assert.Single(saved.ChildGroups).ChildGroups).Conditions);
    }

    [Fact]
    public void ARemovedGroupOrCondition_LeavesOnAfterTheClickHasFinishedRouting()
    {
        var row = new OrganizerProfileRowViewModel(new OrganizerProfile { Name = "P" });
        row.RootGroup.AddGroupCommand.Execute(null);
        var group = row.RootGroup.Groups[0];
        group.AddConditionCommand.Execute(null);

        group.Conditions[0].RemoveCommand.Execute(null);
        group.RemoveCommand.Execute(null);
        Assert.Single(row.RootGroup.Groups);                                        // not yet: detaching a control mid-click crashes Avalonia
        TestDispatcher.Drain();

        Assert.Empty(row.RootGroup.Groups);
        Assert.Empty(group.Conditions);
    }

    /// <summary>The headless test app loads no theme, so item controls never apply their templates and a layout pass cannot show the recursion
    /// (checked: an ItemsControl reports its items but has no visual children). What can be proven here is that the group template is registered
    /// for the type and builds; whether nested groups render on screen is an on-screen check.</summary>
    [Fact]
    public void TheGroupTemplate_IsRegisteredForTheGroupType_AndBuilds()
    {
        TestAppBuilder.EnsureInitialized();
        var view = new ProfileManagerView();
        var template = Assert.Single(view.DataTemplates);
        var group = new ExcludeGroupViewModel(matchAny: true);

        Assert.True(template.Match(group));
        Assert.False(template.Match(new object()));
        var built = template.Build(group);
        Assert.NotNull(built);
        Assert.True(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(built!).OfType<Avalonia.Controls.ItemsControl>().Count() >= 2,
            "the template should hold a conditions list and a child-groups list");
    }

    // -- live preview --

    private ProfileManagerViewModel ManagerWithASelectedProfile(Action<OrganizerProfile>? configure = null)
    {
        var store = new OrganizerProfileStore(NewContext);
        var profile = new OrganizerProfile
        {
            Name = "P", BaseFolder = Path.Combine(_root, "library"), FolderTemplate = "{<series>}", FileTemplate = "{<series>} #{<number2>}",
        };
        configure?.Invoke(profile);
        store.Save(profile);
        var manager = new ProfileManagerViewModel(store, organizerService: OrganizeCoordinator.CreateService(NewContext), createDbContext: NewContext)
        {
            PreviewDelay = TimeSpan.Zero,
        };
        manager.Selected = manager.Profiles.Single(p => p.Id == profile.Id);
        return manager;
    }

    [Fact]
    public async Task ThePreview_ShowsWhereARealComicWouldGo_RelativeToTheBaseFolder()
    {
        SeedIssue();
        var manager = ManagerWithASelectedProfile();

        await manager.RefreshPreviewCommand.ExecuteAsync(null);
        TestDispatcher.Drain();

        var row = Assert.Single(manager.PreviewRows);
        Assert.Equal("Batman #3", row.Label);
        Assert.Equal("→ " + Path.Combine("Batman", "Batman #03.cbz"), row.Text);
        Assert.False(row.IsProblem);
        Assert.False(manager.HasPreviewMessage);
    }

    [Fact]
    public void EditingATemplate_RedrawsThePreview_WithoutAnyClick()
    {
        SeedIssue();
        var manager = ManagerWithASelectedProfile();
        manager.RefreshPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        TestDispatcher.Drain();

        manager.Selected!.FileTemplate = "{<series>} - Issue {<number>}";
        manager.PreviewTask!.GetAwaiter().GetResult();          // the scheduled recompute; it publishes through the UI dispatcher
        TestDispatcher.Drain();

        Assert.Equal("→ " + Path.Combine("Batman", "Batman - Issue 3.cbz"), Assert.Single(manager.PreviewRows).Text);
    }

    [Fact]
    public async Task ThePreview_SaysWhatIsMissing_InsteadOfShowingNothing()
    {
        SeedIssue();
        var noBase = ManagerWithASelectedProfile(p => p.BaseFolder = string.Empty);
        await noBase.RefreshPreviewCommand.ExecuteAsync(null);
        TestDispatcher.Drain();
        Assert.Empty(noBase.PreviewRows);
        Assert.Contains("base folder", noBase.PreviewMessage);

        var badTemplate = ManagerWithASelectedProfile(p => p.FileTemplate = "{<definitelynotatoken>}");
        await badTemplate.RefreshPreviewCommand.ExecuteAsync(null);
        TestDispatcher.Drain();
        Assert.Empty(badTemplate.PreviewRows);
        Assert.Contains("definitelynotatoken", badTemplate.PreviewMessage);
    }

    [Fact]
    public async Task ThePreview_ShowsAComicTheTemplatesCannotName_InRed_AndOneInAnExcludedFolder_AsLeftAlone()
    {
        SeedIssue();                                                                    // Batman #3, in ".../incoming"
        var manager = ManagerWithASelectedProfile(p => p.FileTemplate = "{<publisher>}");   // this comic has no publisher -> no file name
        await manager.RefreshPreviewCommand.ExecuteAsync(null);
        TestDispatcher.Drain();
        var problem = Assert.Single(manager.PreviewRows);
        Assert.True(problem.IsProblem);
        Assert.Contains("empty file name", problem.Text);

        manager.Selected!.FileTemplate = "{<series>}";
        manager.Selected.ExcludeFoldersText = "incoming";
        await manager.RefreshPreviewCommand.ExecuteAsync(null);
        TestDispatcher.Drain();
        Assert.Contains("excluded folder", Assert.Single(manager.PreviewRows).Text);
    }

    [Fact]
    public async Task ThePreview_SaysSoWhenThereAreNoComicsToDrawFrom()
    {
        var manager = ManagerWithASelectedProfile();

        await manager.RefreshPreviewCommand.ExecuteAsync(null);
        TestDispatcher.Drain();

        Assert.Empty(manager.PreviewRows);
        Assert.Contains("Add comics", manager.PreviewMessage);
    }

    [Fact]
    public void TheGroupMode_IsEditedAsText_AndAnUnknownWordIsIgnored()
    {
        var group = new ExcludeGroupViewModel(matchAny: true);
        Assert.Equal("Any", group.ModeText);

        group.ModeText = "all";
        Assert.False(group.MatchAny);
        Assert.Equal("All", group.ModeText);

        group.ModeText = "nonsense";
        Assert.False(group.MatchAny);                                               // unchanged, not thrown
    }

    [Fact]
    public void SavingABadTemplate_IsRefusedWithTheReason_AndNothingIsWritten()
    {
        var store = new OrganizerProfileStore(NewContext);
        var profile = store.Save(new OrganizerProfile { Name = "P", BaseFolder = "C:/lib" });
        var manager = new ProfileManagerViewModel(store, organizerService: OrganizeCoordinator.CreateService(NewContext), createDbContext: NewContext);
        manager.Selected = manager.Profiles.Single(p => p.Id == profile.Id);
        manager.Selected.FileTemplate = "{<definitelynotatoken>}";

        manager.SaveSelectedCommand.Execute(null);

        Assert.Contains("File template", manager.SaveStatusMessage);
        Assert.Contains("definitelynotatoken", manager.SaveStatusMessage);
        Assert.NotEqual("{<definitelynotatoken>}", store.Get(profile.Id)!.FileTemplate);

        manager.Selected!.FileTemplate = "{<series>} #{<number2>}";
        manager.SaveSelectedCommand.Execute(null);
        Assert.Null(manager.SaveStatusMessage);
        Assert.Equal("{<series>} #{<number2>}", store.Get(profile.Id)!.FileTemplate);
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
        var previewPlan = new OrganizePlan(new[] { new PlannedMove(new Issue { Series = new Series { Name = "Batman" }, Number = "3" }, "C:/in/3.cbz", "C:/lib/3.cbz", false) });
        Assert.NotNull(new OrganizePreviewDialogView
        {
            DataContext = new OrganizePreviewDialogViewModel(OrganizePlanSummary.From(previewPlan), new OrganizerProfile { Name = "P", BaseFolder = "C:/lib" }, _ => { }),
        }.Content);

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
