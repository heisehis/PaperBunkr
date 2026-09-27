using Paperbunkr.Data.Organizing;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

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

    // -- Audit defects (docs/superpowers/specs/2026-09-25-library-organizer-audit-fixes-design.md, Phase 1) --

    private static OrganizerProfile MakePublisherProfile(string baseFolder) => new()
    {
        Name = "Test",
        BaseFolder = baseFolder,
        FolderTemplate = @"{<publisher>}\{<imprint>}\{<series>}",
        FileTemplate = "{<series>} #{<number>}",
    };

    [Fact]
    public async Task PlanAsync_a_missing_publisher_and_imprint_keep_the_file_under_the_base_folder()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, MakePublisherProfile(baseFolder), CreateDbContext);

        var move = Assert.Single(plan.Moves);
        Assert.Null(move.Problem);
        Assert.Equal(Path.Combine(baseFolder, "Batman", "Batman #1.cbz"), move.DestinationPath);
    }

    [Fact]
    public async Task PlanAsync_an_empty_middle_segment_does_not_leave_a_doubled_separator()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        issue.Publisher = "DC";
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, MakePublisherProfile(baseFolder), CreateDbContext);

        Assert.Equal(Path.Combine(baseFolder, "DC", "Batman", "Batman #1.cbz"), plan.Moves[0].DestinationPath);
    }

    [Fact]
    public async Task PlanAsync_a_slash_inside_a_series_name_does_not_nest_a_folder()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        issue.Series!.Name = "Fate/Zero";
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, MakeProfile(baseFolder), CreateDbContext);

        Assert.Equal(Path.Combine(baseFolder, "FateZero", "FateZero #1.cbz"), plan.Moves[0].DestinationPath);
    }

    [Fact]
    public async Task PlanAsync_a_blank_base_folder_is_refused_instead_of_planning_relative_paths()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        var service = new LibraryOrganizerService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PlanAsync(new[] { issue }, MakeProfile(string.Empty), CreateDbContext));
    }

    [Fact]
    public async Task An_empty_file_name_is_a_problem_for_that_book_only()
    {
        string sourceA = CreateSourceFile("a.cbz");
        string sourceB = CreateSourceFile("b.cbz");
        var issueA = MakeIssue(1, sourceA);
        issueA.Publisher = "DC";
        var issueB = MakeIssue(2, sourceB);                 // no publisher -> the file template is empty
        SeedIssue(issueA);
        SeedIssue(issueB);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.FileTemplate = "{<publisher>}";
        var service = new LibraryOrganizerService();
        OrganizePlan plan = await service.PlanAsync(new[] { issueA, issueB }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.Single(result.Succeeded);
        var failed = Assert.Single(result.Failed);
        Assert.Equal(2, failed.Move.Issue.Id);
        Assert.Contains("empty file name", failed.Error);
        Assert.True(File.Exists(sourceB));
    }

    [Fact]
    public async Task An_unsupported_token_fails_the_book_with_a_reason_and_does_not_throw_out_of_the_plan()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        SeedIssue(issue);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.FileTemplate = "{<definitelynotatoken>}";
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        var failed = Assert.Single(result.Failed);
        Assert.Contains("definitelynotatoken", failed.Error);
        Assert.True(File.Exists(issue.FilePath));
    }

    [Fact]
    public async Task A_book_already_at_its_calculated_path_is_left_alone_and_not_logged_for_undo()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        string inPlace = Path.Combine(baseFolder, "Batman", "Batman #1.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(inPlace)!);
        File.WriteAllText(inPlace, "cbz");
        var issue = MakeIssue(1, inPlace);
        SeedIssue(issue);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(baseFolder);
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.True(Assert.Single(plan.Moves).IsAlreadyInPlace);
        Assert.False(plan.Moves[0].IsCollision);
        Assert.Single(result.AlreadyInPlace);
        Assert.Empty(result.Succeeded);
        Assert.Empty(result.Failed);
        Assert.Empty(undoLog.GetLastBatch());
    }

    [Fact]
    public async Task A_path_that_differs_only_in_case_is_renamed_not_treated_as_a_collision()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;     // on a case-sensitive filesystem these are two different files
        }

        string baseFolder = Path.Combine(_testRoot, "library");
        string wrongCase = Path.Combine(baseFolder, "batman", "batman #1.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(wrongCase)!);
        File.WriteAllText(wrongCase, "cbz");
        var issue = MakeIssue(1, wrongCase);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(baseFolder);
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.False(plan.Moves[0].IsCollision);
        Assert.False(plan.Moves[0].IsAlreadyInPlace);
        Assert.Single(result.Succeeded);
        Assert.Equal("Batman #1.cbz", Directory.GetFiles(Path.Combine(baseFolder, "Batman")).Select(Path.GetFileName).Single());
    }

    [Fact]
    public async Task Two_books_headed_for_the_same_path_reach_the_collision_resolver()
    {
        string sourceA = CreateSourceFile("a.cbz", "first");
        string sourceB = CreateSourceFile("b.cbz", "second");
        var issueA = MakeIssue(1, sourceA);
        var issueB = MakeIssue(2, sourceB);
        issueA.Number = "1";
        issueB.Number = "1";                                  // same series + number -> same destination
        SeedIssue(issueA);
        SeedIssue(issueB);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        OrganizePlan plan = await service.PlanAsync(new[] { issueA, issueB }, profile, CreateDbContext);
        int asked = 0;

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true,
            (_, _, _) => { asked++; return Task.FromResult((CollisionResolution.Rename, false)); }, CreateDbContext);

        Assert.True(plan.Moves[1].IsCollision);
        Assert.Equal(1, asked);
        Assert.Empty(result.Failed);
        Assert.Equal(2, result.Succeeded.Count);
        Assert.EndsWith("Batman #1 (1).cbz", result.Succeeded[1].DestinationPath);
    }

    [Fact]
    public async Task Simulate_also_detects_books_headed_for_the_same_path()
    {
        var issueA = MakeIssue(1, CreateSourceFile("a.cbz"));
        var issueB = MakeIssue(2, CreateSourceFile("b.cbz"));
        issueA.Number = "1";
        issueB.Number = "1";
        SeedIssue(issueA);
        SeedIssue(issueB);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"), OrganizerMode.Simulate);
        OrganizePlan plan = await service.PlanAsync(new[] { issueA, issueB }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: false, null, CreateDbContext);

        // Non-interactive default policy is Rename: the second book gets " (1)" even though nothing is created.
        Assert.EndsWith("Batman #1 (1).cbz", result.Succeeded[1].DestinationPath);
    }

    [Fact]
    public async Task Replace_sends_the_old_file_to_the_recycler_and_leaves_its_library_entry_without_a_file()
    {
        string source = CreateSourceFile("book.cbz", "new content");
        var incoming = MakeIssue(1, source);
        SeedIssue(incoming);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        var planner = new LibraryOrganizerService();
        string existingPath = (await planner.PlanAsync(new[] { incoming }, profile, CreateDbContext)).Moves[0].DestinationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllText(existingPath, "old content");
        var existingEntry = MakeIssue(2, existingPath);
        existingEntry.Number = "99";
        SeedIssue(existingEntry);
        var recycled = new List<string>();
        var service = new LibraryOrganizerService(sendToRecycleBin: path => { recycled.Add(path); File.Delete(path); });
        OrganizePlan plan = await service.PlanAsync(new[] { incoming }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true,
            (_, _, _) => Task.FromResult((CollisionResolution.Replace, false)), CreateDbContext);

        Assert.Equal(new[] { existingPath }, recycled);
        Assert.Equal("new content", File.ReadAllText(existingPath));
        Assert.Equal(new[] { 2 }, result.ReplacedIssueIds);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Null(context.Issues.Single(i => i.Id == 2).FilePath);
        Assert.Equal(existingPath, context.Issues.Single(i => i.Id == 1).FilePath);
    }

    [Fact]
    public async Task A_failed_library_update_puts_the_file_back_so_disk_and_database_agree()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null,
            () => throw new InvalidOperationException("database is down"));

        var failed = Assert.Single(result.Failed);
        Assert.Contains("moved back", failed.Error);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(plan.Moves[0].DestinationPath));
    }

    [Fact]
    public async Task Undo_only_ever_reverses_the_most_recent_organize_run()
    {
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        string sourceA = CreateSourceFile("a.cbz");
        var issueA = MakeIssue(1, sourceA);
        SeedIssue(issueA);
        OrganizePlan planA = await service.PlanAsync(new[] { issueA }, profile, CreateDbContext);
        await service.ExecuteAsync(planA, profile, isInteractive: true, null, CreateDbContext);

        string sourceB = CreateSourceFile("b.cbz");
        var issueB = MakeIssue(2, sourceB);
        issueB.Number = "2";
        SeedIssue(issueB);
        OrganizePlan planB = await service.PlanAsync(new[] { issueB }, profile, CreateDbContext);
        await service.ExecuteAsync(planB, profile, isInteractive: true, null, CreateDbContext);

        UndoResult first = await service.UndoLastOrganizeAsync(CreateDbContext);
        UndoResult second = await service.UndoLastOrganizeAsync(CreateDbContext);

        Assert.Equal(1, first.Reversed);
        Assert.True(File.Exists(sourceB));
        Assert.Equal(0, second.Reversed);
        Assert.False(File.Exists(sourceA));                   // run A stays organized
        Assert.True(File.Exists(planA.Moves[0].DestinationPath));
    }

    [Fact]
    public async Task Simulate_and_copy_runs_do_not_start_an_undo_batch()
    {
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var issue = MakeIssue(1, CreateSourceFile("a.cbz"));
        SeedIssue(issue);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"), OrganizerMode.Copy);
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Empty(context.OrganizeBatches);
    }

    // -- Plugin options (Phase 2): UseFolder/UseFileName, EmptyFolder/EmptyData, FailEmptyValues, ExcludeFolders, safety --

    [Fact]
    public async Task UseFolder_off_renames_in_place_and_keeps_the_files_folder()
    {
        string source = CreateSourceFile("old name.cbz");
        var issue = MakeIssue(1, source);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.UseFolder = false;
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Equal(Path.Combine(Path.GetDirectoryName(source)!, "Batman #1.cbz"), plan.Moves[0].DestinationPath);
    }

    [Fact]
    public async Task UseFileName_off_moves_the_file_into_the_new_folder_under_its_own_name()
    {
        string source = CreateSourceFile("keep me.cbz");
        var issue = MakeIssue(1, source);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.UseFileName = false;
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Equal(Path.Combine(_testRoot, "library", "Batman", "keep me.cbz"), plan.Moves[0].DestinationPath);
    }

    [Fact]
    public async Task With_both_halves_off_every_book_is_already_in_place()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.UseFolder = false;
        profile.UseFileName = false;
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.True(plan.Moves[0].IsAlreadyInPlace);
    }

    [Fact]
    public async Task EmptyFolder_names_a_folder_segment_that_comes_out_empty()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        var profile = MakePublisherProfile(baseFolder);
        profile.EmptyFolder = "Unknown";
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Equal(Path.Combine(baseFolder, "Unknown", "Unknown", "Batman", "Batman #1.cbz"), plan.Moves[0].DestinationPath);
    }

    [Fact]
    public async Task EmptyData_fills_a_token_that_is_empty()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        var profile = MakePublisherProfile(baseFolder);
        profile.EmptyData = new Dictionary<string, string> { ["publisher"] = "Indie", ["imprint"] = "None" };
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Equal(Path.Combine(baseFolder, "Indie", "None", "Batman", "Batman #1.cbz"), plan.Moves[0].DestinationPath);
    }

    [Fact]
    public async Task FailEmptyValues_skips_a_book_whose_required_token_is_empty_and_says_which()
    {
        string sourceA = CreateSourceFile("a.cbz");
        string sourceB = CreateSourceFile("b.cbz");
        var withPublisher = MakeIssue(1, sourceA);
        withPublisher.Publisher = "DC";
        var withoutPublisher = MakeIssue(2, sourceB);
        SeedIssue(withPublisher);
        SeedIssue(withoutPublisher);
        var profile = MakePublisherProfile(Path.Combine(_testRoot, "library"));
        profile.FailEmptyValues = true;
        profile.FailedFields = new List<string> { "publisher" };
        var service = new LibraryOrganizerService();
        OrganizePlan plan = await service.PlanAsync(new[] { withPublisher, withoutPublisher }, profile, CreateDbContext);

        OrganizeResult result = await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);

        Assert.Single(result.Succeeded);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(2, skipped.Issue.Id);
        Assert.Contains("publisher", skipped.SkipReason);
        Assert.True(File.Exists(sourceB));
        Assert.Empty(result.Failed);
    }

    [Fact]
    public async Task A_book_inside_an_excluded_folder_is_left_out_of_the_plan()
    {
        string protectedFolder = Path.Combine(_testRoot, "incoming", "Keep Here");
        Directory.CreateDirectory(protectedFolder);
        string protectedPath = Path.Combine(protectedFolder, "a.cbz");
        File.WriteAllText(protectedPath, "cbz");
        var protectedIssue = MakeIssue(1, protectedPath);
        var normalIssue = MakeIssue(2, CreateSourceFile("b.cbz"));
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.ExcludeFolders = new List<string> { "Keep Here" };
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { protectedIssue, normalIssue }, profile, CreateDbContext);

        Assert.Equal(2, Assert.Single(plan.Moves).Issue.Id);
    }

    [Fact]
    public async Task A_reserved_windows_device_name_is_a_problem_for_that_book()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.FileTemplate = "CON";
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Contains("reserved", plan.Moves[0].Problem);
    }

    [Fact]
    public async Task A_path_over_the_windows_limit_is_a_problem_for_that_book()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.FileTemplate = new string('x', 250);
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Contains("259", plan.Moves[0].Problem);
    }

    [Fact]
    public async Task A_series_wide_multi_value_token_puts_every_issue_of_the_series_in_one_folder()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        var first = MakeIssue(1, CreateSourceFile("a.cbz"));
        first.Writer = "Alan Moore";
        var second = MakeIssue(2, CreateSourceFile("b.cbz"));
        second.Writer = "Dave Gibbons";
        SeedIssue(first);
        SeedIssue(second);
        var profile = MakeProfile(baseFolder);
        profile.FolderTemplate = "{<series>} - {<writer( & )(series)>}";
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { first, second }, profile, CreateDbContext);

        string folder = Path.Combine(baseFolder, "Batman - Alan Moore & Dave Gibbons");
        Assert.All(plan.Moves, m => Assert.Equal(folder, Path.GetDirectoryName(m.DestinationPath)));
    }

    [Fact]
    public async Task Undo_removes_the_folders_the_run_created_but_never_the_base_folder()
    {
        string baseFolder = Path.Combine(_testRoot, "library");
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(undoLog: undoLog);
        var profile = MakeProfile(baseFolder);
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);
        await service.ExecuteAsync(plan, profile, isInteractive: true, null, CreateDbContext);
        string createdFolder = Path.GetDirectoryName(plan.Moves[0].DestinationPath)!;
        Assert.True(Directory.Exists(createdFolder));

        await service.UndoLastOrganizeAsync(CreateDbContext);

        Assert.False(Directory.Exists(createdFolder));
        Assert.True(Directory.Exists(baseFolder));
    }

    // -- Several profiles in one run (plugin `create_book_paths`, lobookmover.py:172-236) --

    private static OrganizerProfile NamedProfile(string name, string baseFolder, OrganizerMode mode) =>
        new() { Name = name, BaseFolder = baseFolder, FolderTemplate = "{<series>}", FileTemplate = "{<series>} #{<number>}", Mode = mode };

    [Fact]
    public async Task ACopyProfileAndAMoveProfile_BothPlaceTheBook_TheCopyFirstSoItReadsTheFileBeforeItMoves()
    {
        string source = CreateSourceFile("book.cbz", "content");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var move = NamedProfile("Move", Path.Combine(_testRoot, "library"), OrganizerMode.Move);
        var copy = NamedProfile("Backup", Path.Combine(_testRoot, "backup"), OrganizerMode.Copy);
        var service = new LibraryOrganizerService();
        // The move profile is listed FIRST on purpose: the copy must still run before it.
        var plans = await service.PlanManyAsync(new[] { issue }, new[] { move, copy }, CreateDbContext);

        var results = await service.ExecuteManyAsync(plans, isInteractive: true, null, CreateDbContext);

        Assert.True(File.Exists(Path.Combine(_testRoot, "library", "Batman", "Batman #1.cbz")));
        Assert.True(File.Exists(Path.Combine(_testRoot, "backup", "Batman", "Batman #1.cbz")));
        Assert.False(File.Exists(source));
        Assert.All(results, r => Assert.Empty(r.Result.Failed));
    }

    [Fact]
    public async Task OfSeveralMoveProfiles_OnlyTheLastOnePlacesTheBook_AndTheEarlierOnesSayWhy()
    {
        string source = CreateSourceFile("book.cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var first = NamedProfile("First", Path.Combine(_testRoot, "one"), OrganizerMode.Move);
        var second = NamedProfile("Second", Path.Combine(_testRoot, "two"), OrganizerMode.Move);
        var service = new LibraryOrganizerService();
        var plans = await service.PlanManyAsync(new[] { issue }, new[] { first, second }, CreateDbContext);

        var results = await service.ExecuteManyAsync(plans, isInteractive: true, null, CreateDbContext);

        Assert.True(File.Exists(Path.Combine(_testRoot, "two", "Batman", "Batman #1.cbz")));
        Assert.False(Directory.Exists(Path.Combine(_testRoot, "one")));
        var firstResult = results.Single(r => r.Profile.Name == "First").Result;
        var skipped = Assert.Single(firstResult.Skipped);
        Assert.Contains("later profile (Second)", skipped.SkipReason);
        Assert.Single(results.Single(r => r.Profile.Name == "Second").Result.Succeeded);
    }

    [Fact]
    public async Task AnEarlierMoveProfileStillPlacesTheBook_WhenTheLaterOneExcludesIt()
    {
        string protectedFolder = Path.Combine(_testRoot, "incoming", "Keep Here");
        Directory.CreateDirectory(protectedFolder);
        string source = Path.Combine(protectedFolder, "a.cbz");
        File.WriteAllText(source, "cbz");
        var issue = MakeIssue(1, source);
        SeedIssue(issue);
        var first = NamedProfile("First", Path.Combine(_testRoot, "one"), OrganizerMode.Move);
        var second = NamedProfile("Second", Path.Combine(_testRoot, "two"), OrganizerMode.Move);
        second.ExcludeFolders = new List<string> { "Keep Here" };
        var service = new LibraryOrganizerService();
        var plans = await service.PlanManyAsync(new[] { issue }, new[] { first, second }, CreateDbContext);

        await service.ExecuteManyAsync(plans, isInteractive: true, null, CreateDbContext);

        Assert.True(File.Exists(Path.Combine(_testRoot, "one", "Batman", "Batman #1.cbz")));
    }

    [Fact]
    public async Task OneProfile_ThroughThePlanManyPath_BehavesExactlyLikeAPlainRun()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        SeedIssue(issue);
        var profile = NamedProfile("Only", Path.Combine(_testRoot, "library"), OrganizerMode.Move);
        var service = new LibraryOrganizerService();

        var plans = await service.PlanManyAsync(new[] { issue }, new[] { profile }, CreateDbContext);
        var results = await service.ExecuteManyAsync(plans, isInteractive: true, null, CreateDbContext);

        Assert.Single(plans);
        Assert.Single(Assert.Single(results).Result.Succeeded);
    }

    [Fact]
    public async Task ASeveralProfileRun_IsOneUndoBatch()
    {
        string sourceA = CreateSourceFile("a.cbz");
        string sourceB = CreateSourceFile("b.cbz");
        var issueA = MakeIssue(1, sourceA);
        var issueB = MakeIssue(2, sourceB);
        issueB.Number = "2";
        SeedIssue(issueA);
        SeedIssue(issueB);
        // Each profile excludes the other's book, so both profiles really move something.
        var one = NamedProfile("One", Path.Combine(_testRoot, "one"), OrganizerMode.Move);
        var two = NamedProfile("Two", Path.Combine(_testRoot, "two"), OrganizerMode.Move);
        var undoLog = new OrganizeUndoLog(CreateDbContext);
        var service = new LibraryOrganizerService(resolveExcluded: json => json == "one" ? new[] { 2 } : new[] { 1 }, undoLog: undoLog);
        one.ExcludeRuleJson = "one";
        two.ExcludeRuleJson = "two";
        var plans = await service.PlanManyAsync(new[] { issueA, issueB }, new[] { one, two }, CreateDbContext);
        await service.ExecuteManyAsync(plans, isInteractive: true, null, CreateDbContext);

        UndoResult undone = await service.UndoLastOrganizeAsync(CreateDbContext);

        Assert.Equal(2, undone.Reversed);
        Assert.True(File.Exists(sourceA));
        Assert.True(File.Exists(sourceB));
    }

    [Fact]
    public async Task ProgressAcrossSeveralProfiles_CountsUpToTheGrandTotal()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        SeedIssue(issue);
        var service = new LibraryOrganizerService();
        var plans = await service.PlanManyAsync(
            new[] { issue },
            new[] { NamedProfile("A", Path.Combine(_testRoot, "a"), OrganizerMode.Copy), NamedProfile("B", Path.Combine(_testRoot, "b"), OrganizerMode.Copy) },
            CreateDbContext);
        var seen = new List<(int Done, int Total)>();

        await service.ExecuteManyAsync(plans, isInteractive: true, null, CreateDbContext, (done, total, _) => seen.Add((done, total)));

        Assert.All(seen, s => Assert.Equal(2, s.Total));
        Assert.Contains((2, 2), seen);
    }

    // -- Values that live only in filename proposals / tags (the callers' issues do not carry them) --

    [Fact]
    public async Task AComicWhoseNumberAndYearWereOnlyParsedFromItsFileName_IsStillNamedFromThem()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        issue.Number = null;                                    // nothing in the raw fields: the number only exists as an accepted proposal
        SeedIssue(issue);
        using (PaperbunkrDbContext seed = CreateDbContext())
        {
            foreach ((MetadataProposalField field, string value) in new[] { (MetadataProposalField.Number, "7"), (MetadataProposalField.Year, "1999") })
            {
                seed.MetadataProposals.Add(new MetadataProposal { IssueId = 1, Field = field, ProposedValue = value, Status = MetadataProposalStatus.Accepted });
            }

            seed.SaveChanges();
        }

        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.FileTemplate = "{<series>} #{<number2>}{ (<year>)}";
        var service = new LibraryOrganizerService();

        // The caller's issue is deliberately the bare one - no proposals loaded, exactly as a real run passes it.
        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Equal("Batman #07 (1999).cbz", Path.GetFileName(Assert.Single(plan.Moves).DestinationPath));
        Assert.Equal("7", plan.Moves[0].Issue.EffectiveNumber());            // and the plan carries the loaded copy, so labels/reports show it too
    }

    [Fact]
    public async Task GenreAndTagTokens_AreFilled_EvenThoughTheCallersIssueHasNoTagsLoaded()
    {
        var issue = MakeIssue(1, CreateSourceFile("book.cbz"));
        SeedIssue(issue);
        using (PaperbunkrDbContext seed = CreateDbContext())
        {
            seed.Set<IssueTag>().Add(new IssueTag { IssueId = 1, Field = IssueTagField.Genre, Value = "Crime" });
            seed.SaveChanges();
        }

        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.FileTemplate = "{<series>}{ [<genre>]}";
        var service = new LibraryOrganizerService();

        OrganizePlan plan = await service.PlanAsync(new[] { issue }, profile, CreateDbContext);

        Assert.Equal("Batman [Crime].cbz", Path.GetFileName(Assert.Single(plan.Moves).DestinationPath));
    }

    // -- Live preview (the editor draws a few real comics through the unsaved profile) --

    [Fact]
    public async Task ThePreview_SeesTheWholeSeries_EvenThoughOnlyOneComicIsInTheSample()
    {
        Issue MakeYearIssue(int id, int number, int year) => new()
        {
            Id = id, SeriesId = 1, Series = new Series { Id = 1, Name = "Batman" }, Number = number.ToString(), Year = year,
            FilePath = CreateSourceFile($"book{id}.cbz"),
        };

        var first = MakeYearIssue(1, 1, 1940);
        var last = MakeYearIssue(2, 897, 2011);
        SeedIssue(first);
        SeedIssue(last);
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.FileTemplate = "{<series>} #{<number>} ({<startyear>}-{<EndYear>})";
        var service = new LibraryOrganizerService();

        OrganizePlan preview = await service.PreviewAsync(new[] { first }, profile, CreateDbContext);   // only the first comic is in the sample

        Assert.Equal("Batman #1 (1940-2011).cbz", Path.GetFileName(Assert.Single(preview.Moves).DestinationPath));
    }

    [Fact]
    public async Task ThePreview_DoesNotEvaluateTheExcludeRule_ButDoesHonourExcludedFolders()
    {
        string protectedFolder = Path.Combine(_testRoot, "incoming", "Keep Here");
        Directory.CreateDirectory(protectedFolder);
        string protectedPath = Path.Combine(protectedFolder, "a.cbz");
        File.WriteAllText(protectedPath, "cbz");
        var inFolder = MakeIssue(1, protectedPath);
        var ruleVictim = MakeIssue(2, CreateSourceFile("b.cbz"));
        var profile = MakeProfile(Path.Combine(_testRoot, "library"));
        profile.ExcludeFolders = new List<string> { "Keep Here" };
        profile.ExcludeRuleJson = "anything";
        var service = new LibraryOrganizerService(resolveExcluded: _ => new[] { 2 });      // a rule that would exclude book 2

        OrganizePlan preview = await service.PreviewAsync(new[] { inFolder, ruleVictim }, profile, CreateDbContext);
        OrganizePlan real = await service.PlanAsync(new[] { inFolder, ruleVictim }, profile, CreateDbContext);

        Assert.Equal(2, Assert.Single(preview.Moves).Issue.Id);       // the folder exclusion applies; the (whole-library) rule is skipped
        Assert.Empty(real.Moves);                                       // a real run applies both
    }

    [Fact]
    public void TheSample_TakesOneComicPerSeries_AndSkipsPlaceholdersAndFilelessEntries()
    {
        using (PaperbunkrDbContext seed = CreateDbContext())
        {
            var batman = seed.Series.Add(new Series { Name = "Batman" }).Entity;
            var thor = seed.Series.Add(new Series { Name = "Thor" }).Entity;
            var ghost = seed.Series.Add(new Series { Name = "Ghost" }).Entity;
            seed.SaveChanges();
            seed.Issues.AddRange(
                new Issue { SeriesId = batman.Id, Number = "1", FilePath = "C:/a/1.cbz" },
                new Issue { SeriesId = batman.Id, Number = "2", FilePath = "C:/a/2.cbz" },
                new Issue { SeriesId = thor.Id, Number = "1", FilePath = "C:/a/t1.cbz" },
                new Issue { SeriesId = thor.Id, Number = "0", FilePath = null },
                new Issue { SeriesId = ghost.Id, Number = "1", FilePath = "C:/a/g.cbz", IsPlaceholder = true });
            seed.SaveChanges();
        }

        using PaperbunkrDbContext context = CreateDbContext();
        var sample = OrganizerPreviewSample.Pick(context, count: 6);

        Assert.Equal(new[] { "Batman", "Thor" }, sample.Select(i => i.Series!.Name).OrderBy(n => n));
    }
}
