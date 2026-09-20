using Paperbunkr.Data.Organizing;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.Organizing;

/// <summary>
/// Implementation plan Phase 3 Step 3.4/3.7 verification: PlanAsync as a pure function (collision
/// detection, path generation), ExecuteAsync's Move/Copy/Simulate modes and each collision-resolution
/// branch via a stub resolver (no real dialog in tests), and the undo log's write-ordering guarantee.
/// </summary>
public sealed class LibraryOrganizerServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;

    public LibraryOrganizerServiceTests()
    {
        _testRoot = Directory.CreateTempSubdirectory("clm-organizer-test-").FullName;
        _dbPath = Path.Combine(_testRoot, "test.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>Persists <paramref name="issue"/> (and its Series, if not already persisted). Reuses
    /// an already-persisted Series row rather than the issue's own (different-object, same-key)
    /// Series instance when one already exists - two issues built via separate <see cref="MakeIssue"/>
    /// calls each carry their own fresh Series object with the same Id, and EF Core's change tracker
    /// doesn't deduplicate by key value alone for an unattached entity reachable via navigation, so
    /// seeding both naively would attempt a duplicate-primary-key insert on the second call.</summary>
    private void SeedIssue(Issue issue)
    {
        using PaperbunkrDbContext context = CreateDbContext();
        Series? existingSeries = context.Series.Find(issue.SeriesId);
        if (existingSeries is null)
        {
            context.Series.Add(issue.Series ?? new Series { Id = issue.SeriesId, Name = "Batman" });
        }
        else
        {
            issue.Series = existingSeries;
        }

        context.Issues.Add(issue);
        context.SaveChanges();
    }

    private static Issue MakeIssue(int id, string sourcePath) => new()
    {
        Id = id,
        SeriesId = 1,
        Series = new Series { Id = 1, Name = "Batman" },
        Number = id.ToString(),
        FilePath = sourcePath,
    };

    private static OrganizerProfile MakeProfile(string baseFolder, OrganizerMode mode = OrganizerMode.Move) => new()
    {
        Name = "Test",
        BaseFolder = baseFolder,
        FolderTemplate = "{<series>}",
        FileTemplate = "{<series>} #{<number>}",
        Mode = mode,
    };

    private string CreateSourceFile(string name, string content = "cbz")
    {
        string path = Path.Combine(_testRoot, "incoming", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // -- PlanAsync --

    [Fact]
    public async Task PlanAsync_computes_the_destination_path_from_the_templates_and_does_no_io()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(Path.Combine(_testRoot, "library", "Batman", "Batman #1.cbz"), move.DestinationPath);
        Assert.False(move.IsCollision);
        Assert.False(File.Exists(move.DestinationPath));
    }

    [Fact]
    public async Task PlanAsync_flags_a_collision_when_the_destination_already_exists()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));

        OrganizePlan preview = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        Directory.CreateDirectory(Path.GetDirectoryName(preview.Moves[0].DestinationPath)!);
        File.WriteAllText(preview.Moves[0].DestinationPath, "already there");

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.True(Assert.Single(plan.Moves).IsCollision);
    }

    [Fact]
    public async Task PlanAsync_skips_a_book_with_no_file_path()
    {
        var issue = MakeIssue(1, string.Empty);
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, MakeProfile(_testRoot), CreateDbContext);

        Assert.Empty(plan.Moves);
    }

    // -- ExecuteAsync: modes --

    [Fact]
    public async Task ExecuteAsync_move_mode_relocates_the_file_and_updates_the_issues_file_path()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.Single(result.Succeeded);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(plan.Moves[0].DestinationPath));

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal(plan.Moves[0].DestinationPath, context.Issues.Single(i => i.Id == 1).FilePath);
    }

    [Fact]
    public async Task ExecuteAsync_copy_mode_duplicates_the_file_and_leaves_the_original_and_its_db_row_untouched()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"), OrganizerMode.Copy);
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(plan.Moves[0].DestinationPath));

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal(source, context.Issues.Single(i => i.Id == 1).FilePath);
    }

    [Fact]
    public async Task ExecuteAsync_simulate_mode_performs_no_real_io_and_no_db_write()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"), OrganizerMode.Simulate);
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.Single(result.Succeeded);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(plan.Moves[0].DestinationPath));

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal(source, context.Issues.Single(i => i.Id == 1).FilePath);
    }

    // -- ExecuteAsync: collision resolution branches --

    private async Task<(OrganizePlan Plan, string ExistingPath)> PlanWithACollision(OrganizerProfile profile, Issue issue)
    {
        var service = new LibraryOrganizerService();
        OrganizePlan preview = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        string existingPath = preview.Moves[0].DestinationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllText(existingPath, "already there");
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        return (plan, existingPath);
    }

    [Fact]
    public async Task Interactive_skip_leaves_both_files_in_place()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        (OrganizePlan plan, string existingPath) = await PlanWithACollision(profile, issue);
        var service = new LibraryOrganizerService();

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true,
            (_, _, _) => Task.FromResult((CollisionResolution.Skip, false)), CreateDbContext);

        Assert.Single(result.Skipped);
        Assert.True(File.Exists(source));
        Assert.Equal("already there", File.ReadAllText(existingPath));
    }

    [Fact]
    public async Task Interactive_rename_uses_CEs_numeric_suffix_algorithm()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        (OrganizePlan plan, string existingPath) = await PlanWithACollision(profile, issue);
        var service = new LibraryOrganizerService();

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true,
            (_, _, _) => Task.FromResult((CollisionResolution.Rename, false)), CreateDbContext);

        var succeeded = Assert.Single(result.Succeeded);
        string expectedRenamed = Path.Combine(
            Path.GetDirectoryName(existingPath)!,
            Path.GetFileNameWithoutExtension(existingPath) + " (1)" + Path.GetExtension(existingPath));
        Assert.Equal(expectedRenamed, succeeded.DestinationPath);
        Assert.True(File.Exists(expectedRenamed));
        Assert.True(File.Exists(existingPath)); // the original colliding file is untouched
    }

    [Fact]
    public async Task Interactive_replace_deletes_the_existing_file_before_moving_in_the_new_one()
    {
        string source = CreateSourceFile("book.cbz", "new content");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        (OrganizePlan plan, string existingPath) = await PlanWithACollision(profile, issue);
        var service = new LibraryOrganizerService();

        await service.ExecuteAsync(plan, profile, isInteractive: true,
            (_, _, _) => Task.FromResult((CollisionResolution.Replace, false)), CreateDbContext);

        Assert.Equal("new content", File.ReadAllText(existingPath));
    }

    [Fact]
    public async Task Apply_to_all_remaining_skips_asking_again_for_later_collisions()
    {
        string sourceA = CreateSourceFile("a.cbz");
        string sourceB = CreateSourceFile("b.cbz");
        var issueA = MakeIssue(1, sourceA);
        var issueB = MakeIssue(2, sourceB);
        SeedIssue(issueA);
        SeedIssue(issueB);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        // Force both to collide with pre-existing files at their exact computed destinations.
        var service = new LibraryOrganizerService();
        OrganizePlan preview = await service.PlanAsync(new[] { issueA, issueB }, profile, CreateDbContext);
        foreach (var move in preview.Moves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(move.DestinationPath)!);
            File.WriteAllText(move.DestinationPath, "already there");
        }
        OrganizePlan plan = await service.PlanAsync(new[] { issueA, issueB }, profile, CreateDbContext);

        int askCount = 0;
        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true,
            (_, _, _) =>
            {
                askCount++;
                return Task.FromResult((CollisionResolution.Skip, true)); // "apply to all remaining"
            },
            CreateDbContext);

        Assert.Equal(1, askCount);
        Assert.Equal(2, result.Skipped.Count);
    }

    [Fact]
    public async Task Non_interactive_run_never_calls_the_resolver_and_uses_the_automation_policy_instead()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.AutomationCollisionPolicy = AutomationCollisionPolicy.Skip;
        (OrganizePlan plan, _) = await PlanWithACollision(profile, issue);
        var service = new LibraryOrganizerService();
        bool resolverCalled = false;

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: false,
            (_, _, _) => { resolverCalled = true; return Task.FromResult((CollisionResolution.Replace, false)); },
            CreateDbContext);

        Assert.False(resolverCalled);
        Assert.Single(result.Skipped);
    }

    // -- Failure isolation --

    [Fact]
    public async Task A_failed_item_does_not_abort_the_rest_of_the_batch()
    {
        string sourceA = CreateSourceFile("a.cbz");
        // sourceB deliberately does not exist on disk - its move will throw.
        string sourceB = Path.Combine(_testRoot, "incoming", "missing.cbz");
        var issueA = MakeIssue(1, sourceA);
        var issueB = MakeIssue(2, sourceB);
        SeedIssue(issueA);
        SeedIssue(issueB);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        var service = new LibraryOrganizerService();
        OrganizePlan plan = await service.PlanAsync(new[] { issueA, issueB }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.Single(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.Equal(1, result.Succeeded[0].Issue.Id);
        Assert.Equal(2, result.Failed[0].Move.Issue.Id);
    }

    // -- Undo log --

    [Fact]
    public async Task A_successful_move_batch_is_recorded_in_the_undo_log()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        var lastBatch = undoLog.GetLastBatch();
        var entry = Assert.Single(lastBatch);
        Assert.Equal(source, entry.OldPath);
        Assert.Equal(plan.Moves[0].DestinationPath, entry.NewPath);
    }

    [Fact]
    public async Task Copy_mode_does_not_write_to_the_undo_log_since_nothing_moved()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"), OrganizerMode.Copy);
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.Empty(undoLog.GetLastBatch());
    }

    // -- SeriesAggregate wiring (startyear/EndYear/firstissuenumber/lastissuenumber tokens) --

    [Fact]
    public async Task PlanAsync_computes_a_real_full_library_series_aggregate_for_naming_templates()
    {
        Issue MakeYearIssue(int id, int number, int year) => new()
        {
            Id = id,
            SeriesId = 1,
            Series = new Series { Id = 1, Name = "Batman" },
            Number = number.ToString(),
            Year = year,
            FilePath = CreateSourceFile($"book{id}.cbz"),
        };

        var issues = new[] { MakeYearIssue(1, 1, 1940), MakeYearIssue(2, 897, 2011) };
        foreach (Issue issue in issues)
        {
            SeedIssue(issue);
        }

        var service = new LibraryOrganizerService();
        var profile = new OrganizerProfile
        {
            Name = "Test",
            BaseFolder = Path.Combine(_testRoot, "library"),
            FolderTemplate = "{<series>}",
            FileTemplate = "{<series>} #{<number>} ({<firstissuenumber>}-{<lastissuenumber>}, {<startyear>}-{<EndYear>})",
        };

        OrganizePlan plan = await service.PlanAsync(issues, profile, CreateDbContext);

        Assert.Equal("Batman #1 (1-897, 1940-2011).cbz", Path.GetFileName(plan.Moves[0].DestinationPath));
        Assert.Equal("Batman #897 (1-897, 1940-2011).cbz", Path.GetFileName(plan.Moves[1].DestinationPath));
    }

    // -- RemoveEmptyFolders --

    [Fact]
    public async Task RemoveEmptyFolders_deletes_the_now_empty_source_folder_but_stops_at_the_base_folder()
    {
        // Nest the source two folders deep under BaseFolder itself, so the cleanup walk has to climb
        // more than one level and must stop exactly at BaseFolder without deleting it too.
        string baseFolder = Path.Combine(_testRoot, "library");
        string nestedSourceDir = Path.Combine(baseFolder, "OldPublisher", "OldSeries");
        Directory.CreateDirectory(nestedSourceDir);
        string source = Path.Combine(nestedSourceDir, "book.cbz");
        File.WriteAllText(source, "cbz");

        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(baseFolder);
        profile.RemoveEmptyFolders = true;
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.False(Directory.Exists(nestedSourceDir));
        Assert.False(Directory.Exists(Path.Combine(baseFolder, "OldPublisher")));
        Assert.True(Directory.Exists(baseFolder)); // the base folder itself is never deleted
    }

    [Fact]
    public async Task RemoveEmptyFolders_never_deletes_a_folder_that_still_has_other_files_in_it()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        string sourceDir = Path.Combine(baseFolder, "OldSeries");
        Directory.CreateDirectory(sourceDir);
        string source = Path.Combine(sourceDir, "book.cbz");
        File.WriteAllText(source, "cbz");
        File.WriteAllText(Path.Combine(sourceDir, "cover.jpg"), "not part of this organize run");

        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(baseFolder);
        profile.RemoveEmptyFolders = true;
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.True(Directory.Exists(sourceDir));
    }

    [Fact]
    public async Task RemoveEmptyFolders_false_leaves_the_now_empty_folder_in_place()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        string sourceDir = Path.Combine(baseFolder, "OldSeries");
        Directory.CreateDirectory(sourceDir);
        string source = Path.Combine(sourceDir, "book.cbz");
        File.WriteAllText(source, "cbz");

        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(baseFolder);
        profile.RemoveEmptyFolders = false;
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.True(Directory.Exists(sourceDir));
    }

    // -- UndoLastOrganizeAsync --

    [Fact]
    public async Task UndoLastOrganizeAsync_moves_the_file_back_and_restores_the_issues_file_path()
    {
        string source = CreateSourceFile("book.cbz", "original content");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);
        string organizedPath = plan.Moves[0].DestinationPath;
        Assert.True(File.Exists(organizedPath));

        UndoResult result = await service.UndoLastOrganizeAsync(CreateDbContext);

        Assert.Equal(1, result.Reversed);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(source));
        Assert.Equal("original content", File.ReadAllText(source));
        Assert.False(File.Exists(organizedPath));

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal(source, context.Issues.Single(i => i.Id == 1).FilePath);
    }

    [Fact]
    public async Task UndoLastOrganizeAsync_removes_the_batch_so_it_cannot_be_undone_twice()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        await service.UndoLastOrganizeAsync(CreateDbContext);
        UndoResult secondAttempt = await service.UndoLastOrganizeAsync(CreateDbContext);

        Assert.Equal(0, secondAttempt.Reversed);
        Assert.Equal(0, secondAttempt.Failed);
        Assert.Empty(undoLog.GetLastBatch());
    }

    [Fact]
    public async Task UndoLastOrganizeAsync_with_nothing_recorded_reverses_nothing()
    {
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);

        UndoResult result = await service.UndoLastOrganizeAsync(CreateDbContext);

        Assert.Equal(0, result.Reversed);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task UndoLastOrganizeAsync_skips_an_entry_whose_original_location_is_occupied_again()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        // Something else now occupies the book's original path - undo must not overwrite it.
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "a different file now lives here");

        UndoResult result = await service.UndoLastOrganizeAsync(CreateDbContext);

        Assert.Equal(0, result.Reversed);
        Assert.Equal(1, result.Failed);
        Assert.Equal("a different file now lives here", File.ReadAllText(source));
        Assert.True(File.Exists(plan.Moves[0].DestinationPath)); // untouched, still at its organized spot
    }
}
