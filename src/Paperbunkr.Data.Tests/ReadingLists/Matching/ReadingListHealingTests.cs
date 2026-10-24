using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Migrations;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Matching;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.ReadingLists.Matching;

/// <summary>
/// The database half of the matching core (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.6-§6): imports through the
/// resolver, source records, the healer, the repair preview and the migration's backfill - against a real temp SQLite database.
/// </summary>
public class ReadingListHealingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public ReadingListHealingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_healing_test_{Guid.NewGuid():N}.db");
        _context = NewContext();
        _context.Database.EnsureCreated();
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private Issue AddComic(string series, string number, int? year = null, string? comicVineId = null)
    {
        var row = _context.Series.FirstOrDefault(s => s.Name == series) ?? new Series { Name = series, SortName = series };
        var issue = new Issue { Series = row, Number = number, Year = year, FilePath = $@"C:\comics\{series} {number}.cbz" };
        _context.Issues.Add(issue);
        _context.SaveChanges();
        if (comicVineId is not null)
        {
            _context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
            {
                EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = ComicProvider.ComicVine, ExternalId = comicVineId,
            });
            _context.SaveChanges();
        }

        return issue;
    }

    private string WriteFile(string extension, string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_healing_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    private const string CblWithIds = """
        <?xml version="1.0" encoding="utf-8"?>
        <ReadingList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <Name>RoboCop</Name>
        <NumIssues>3</NumIssues>
        <Books>
        <Book Series="Frank Miller's RoboCop" Number="1" Volume="2003" Year="2003">
        <Database Name="cv" Series="20346" Issue="121607" />
        </Book>
        <Book Series="Frank Miller's RoboCop" Number="2" Volume="2003" Year="2003">
        <Database Name="cv" Series="20346" Issue="121617" />
        </Book>
        </Books>
        <Matchers />
        </ReadingList>
        """;

    [Fact]
    public void Cbl_WithProviderIds_LinksByIdAndKeepsWhatWasAskedFor()
    {
        // Filed under another name entirely: only the id can find it.
        var owned = AddComic("RoboCop (Avatar)", "1", 2003, comicVineId: "121607");
        string path = WriteFile(".cbl", CblWithIds);

        var (list, report) = CblReadingListIO.ImportWithReport(_context, path);

        Assert.Equal(2, report.Parsed);
        Assert.Equal(1, report.MatchedById);
        Assert.Equal(1, report.Missing);
        Assert.True(report.Incomplete);        // the file says 3, holds 2

        using var fresh = NewContext();
        var items = fresh.ReadingListItems.Include(i => i.Source).Include(i => i.Issue).Where(i => i.ReadingListId == list.Id).OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(owned.Id, items[0].IssueId);
        Assert.Equal(MatchMethod.ProviderId, items[0].Source!.Method);
        Assert.Equal("Frank Miller's RoboCop", items[0].Source!.Series);
        Assert.Equal("121607", items[0].Source!.ComicVineIssueId);
        Assert.Equal("20346", items[0].Source!.ComicVineSeriesId);
        Assert.True(items[1].Issue!.IsPlaceholder);
        Assert.Equal(MatchState.Unresolved, items[1].Source!.State);
        Assert.Equal(CblReadingListIO.SourceKey, items[1].Source!.SourceKey);
    }

    [Fact]
    public void Resolve_AloneWritesNothing()
    {
        int series = _context.Series.Count(), issues = _context.Issues.Count();
        ReadingListResolver.Resolve(_context, [new ComicReference("Brand New", "1"), new ComicReference("Another", "2")]);

        using var fresh = NewContext();
        Assert.Equal(series, fresh.Series.Count());
        Assert.Equal(issues, fresh.Issues.Count());
    }

    [Fact]
    public void Placeholder_GoesIntoTheSeriesRowThatOwnsIssues_NotANewRow()
    {
        AddComic("Herc", "1");
        string path = WriteFile(".csv", "Series,Number\nHerc,7\n");
        CsvReadingListIO.Import(_context, path);

        using var fresh = NewContext();
        Assert.Single(fresh.Series.Where(s => s.Name == "Herc"));
    }

    [Fact]
    public void ScanArrival_RelinksTheWaitingEntry_InPlace()
    {
        string path = WriteFile(".csv", "Series,Number,Year\nHerc,7,2011\nHerc,8,2011\n");
        var list = CsvReadingListIO.Import(_context, path).List;
        var first = _context.ReadingListItems.Where(i => i.ReadingListId == list.Id).OrderBy(i => i.SortOrder).First();
        first.Notes = "keep me";
        first.Role = EventMembershipRole.TieIn;
        _context.SaveChanges();
        int placeholderId = first.IssueId;

        var arrived = AddComic("Herc", "007", 2011);        // as a scan would add it, number padded
        var result = ReadingListHealer.RecheckFor(_context, [arrived.Id]);

        Assert.Equal(1, result.Relinked);
        using var fresh = NewContext();
        var item = fresh.ReadingListItems.Include(i => i.Source).First(i => i.Id == first.Id);
        Assert.Equal(arrived.Id, item.IssueId);
        Assert.Equal("keep me", item.Notes);
        Assert.Equal(EventMembershipRole.TieIn, item.Role);
        Assert.Equal(0, item.SortOrder);
        Assert.Equal(MatchMethod.Metadata, item.Source!.Method);
        Assert.Equal(MatchState.Linked, item.Source.State);
        Assert.Equal("7", item.Source.Number);                  // what was asked for is untouched
        Assert.Null(fresh.Issues.Find(placeholderId));          // nothing else used the placeholder
        Assert.Equal(2, fresh.ReadingListItems.Count(i => i.ReadingListId == list.Id));
    }

    [Fact]
    public void Healer_LeavesUserLinksAndRealLinksAlone_AndSharedPlaceholdersAlive()
    {
        var other = AddComic("Other", "1");
        string path = WriteFile(".csv", "Series,Number\nHerc,7\nHerc,8\n");
        var listA = CsvReadingListIO.Import(_context, path).List;
        var listB = CsvReadingListIO.Import(_context, path).List;      // shares both placeholders with A

        var itemsA = _context.ReadingListItems.Where(i => i.ReadingListId == listA.Id).OrderBy(i => i.SortOrder).ToList();
        int sharedPlaceholder = itemsA[1].IssueId;
        ReadingListItemLinker.Relink(_context, itemsA[0].Id, other.Id);         // the user's own (odd) choice for Herc #7

        var seven = AddComic("Herc", "7");
        var result = ReadingListHealer.RecheckAll(_context);

        Assert.Equal(1, result.Relinked);                       // only list B's Herc #7
        using var fresh = NewContext();
        Assert.Equal(other.Id, fresh.ReadingListItems.Find(itemsA[0].Id)!.IssueId);
        Assert.Equal(MatchMethod.User, fresh.ReadingListItemSources.First(s => s.ReadingListItemId == itemsA[0].Id).Method);
        Assert.Contains(fresh.ReadingListItems.Where(i => i.ReadingListId == listB.Id), i => i.IssueId == seven.Id);
        Assert.NotNull(fresh.Issues.Find(sharedPlaceholder));   // Herc #8 is still awaited by both lists
    }

    [Fact]
    public void Healer_DoesNotLinkAcrossYears_OrPickBetweenVolumes()
    {
        string path = WriteFile(".csv", "Series,Number,Year\nThunderbolts,1,2021\nAvengers,1,\n");
        CsvReadingListIO.Import(_context, path);
        AddComic("Thunderbolts", "1", 1997);
        AddComic("Avengers", "1", 1963);
        AddComic("Avengers", "1", 1996);

        var result = ReadingListHealer.RecheckAll(_context);

        Assert.Equal(0, result.Relinked);
        Assert.Equal(2, result.NeedChoice);
        using var fresh = NewContext();
        Assert.All(fresh.ReadingListItemSources, s => Assert.Equal(MatchState.NeedsChoice, s.State));
    }

    [Fact]
    public void RepairPreview_ChangesNothing_ApplyDoesWhatItShowed()
    {
        string path = WriteFile(".csv", "Series,Number\nHerc,7\nGone,1\n");
        var list = CsvReadingListIO.Import(_context, path).List;
        AddComic("Herc", "7");
        AddComic("The Ultimates", "1");
        AddComic("Ultimates", "2");

        var preview = ReadingListRepair.Preview(_context);

        Assert.Single(preview.Relinks);
        Assert.Equal("Herc #7", preview.Relinks[0].Asked);
        Assert.Equal(1, preview.StillMissing);
        Assert.Contains("The Ultimates · Ultimates", preview.SplitSeries);
        using (var fresh = NewContext())
        {
            Assert.Equal(2, fresh.ReadingListItems.Count(i => i.ReadingListId == list.Id && i.Issue!.IsPlaceholder));
        }

        Assert.Equal(1, ReadingListRepair.Apply(_context).Relinked);
        Assert.False(ReadingListRepair.Preview(_context).HasWork);
    }

    [Fact]
    public async Task ArcRefresh_KeepsNotesOfAPlaceholderWhoseComicArrived_AndReportsDuplicates()
    {
        var source = new FakeSource(
        [
            new ArcIssue("Herc", "7", 2011, null, ComicVineIssueId: "70"),
            new ArcIssue("Herc", "8", 2011, null, ComicVineIssueId: "80"),
            new ArcIssue("Herc", "08", 2011, null),
        ]);
        var created = await ArcReadingListBuilder.CreateFromArcWithReportAsync(_context, source, new ArcSearchResult("1", "Arc", null, null, 3), CancellationToken.None);
        Assert.Equal(2, created.Report.Missing);
        Assert.Single(created.Report.Duplicates);
        Assert.Equal(2, created.List.Items.Count);

        var waiting = _context.ReadingListItems.Where(i => i.ReadingListId == created.List.Id).OrderBy(i => i.SortOrder).First();
        waiting.Notes = "note";
        _context.SaveChanges();

        // Arrives under a different name; the ComicVine id the source gave is what finds it.
        var arrived = AddComic("Hercules", "7", 2011, comicVineId: "70");
        var refreshed = await ArcReadingListBuilder.RefreshAsync(_context, created.List.Id, CancellationToken.None, source);

        Assert.Equal(1, refreshed.ReplacedPlaceholderCount);
        Assert.Equal(1, refreshed.StillMissingCount);
        using var fresh = NewContext();
        var item = fresh.ReadingListItems.First(i => i.Id == waiting.Id);
        Assert.Equal(arrived.Id, item.IssueId);
        Assert.Equal("note", item.Notes);
    }

    [Fact]
    public void Backfill_GivesWaitingEntriesARecord_AndLinkedOnesNone()
    {
        var owned = AddComic("Herc", "1");
        var series = _context.Series.First(s => s.Name == "Herc");
        var placeholder = new Issue { SeriesId = series.Id, Number = "7", Year = 2011, IsPlaceholder = true, FileIsMissing = true };
        _context.Issues.Add(placeholder);
        var list = new ReadingList { Name = "Old list", Source = "Metron", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        list.Items.Add(new ReadingListItem { Issue = owned, SortOrder = 0 });
        list.Items.Add(new ReadingListItem { Issue = placeholder, SortOrder = 1 });
        _context.ReadingLists.Add(list);
        _context.SaveChanges();

        foreach (string sql in ReadingListItemSourceBackfill.Statements.Concat(ReadingListItemSourceBackfill.Statements))     // twice: idempotent
        {
            _context.Database.ExecuteSqlRaw(sql);
        }

        using var fresh = NewContext();
        var record = Assert.Single(fresh.ReadingListItemSources);
        Assert.Equal(("Herc", "7", 2011, "Metron", MatchMethod.Backfilled), (record.Series, record.Number, record.Year, record.SourceKey, record.Method));
        Assert.Null(record.ComicVineIssueId);
        Assert.Equal(string.Empty, record.SeriesKey);

        Assert.Equal(1, ReadingListSourceKeys.FillMissing(fresh));
        Assert.Equal(SeriesKeys.Of("Herc"), fresh.ReadingListItemSources.Single().SeriesKey);

        // And the healer picks it up once the comic is there.
        var arrived = AddComic("Herc", "7", 2011);
        Assert.Equal(1, ReadingListHealer.RecheckFor(_context, [arrived.Id]).Relinked);
    }

    [Fact]
    public void LargeImport_ReadsTheLibraryOnce_AndAccountsForEveryEntry()
    {
        // 6,000 library comics, a 300-entry list: half owned, half not.
        var rows = Enumerable.Range(0, 60).Select(s => new Series { Name = $"Series {s}", SortName = $"Series {s}" }).ToList();
        _context.Series.AddRange(rows);
        foreach (var row in rows)
        {
            _context.Issues.AddRange(Enumerable.Range(1, 100).Select(n => new Issue { Series = row, Number = n.ToString(), FilePath = "x" }));
        }

        _context.SaveChanges();
        var lines = new List<string> { "Series,Number" };
        lines.AddRange(Enumerable.Range(0, 150).Select(i => $"Series {i % 60},{1 + i % 100}"));
        lines.AddRange(Enumerable.Range(0, 150).Select(i => $"Series {i % 60},{500 + i}"));
        string path = WriteFile(".csv", string.Join('\n', lines));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = CsvReadingListIO.Import(_context, path).Report!;
        watch.Stop();

        Assert.Equal(300, report.Parsed);
        Assert.Equal(150, report.MatchedByMetadata);
        Assert.Equal(150, report.Missing);
        Assert.Equal(report.Parsed, report.Matched + report.NeedsChoice + report.Missing + report.Duplicates.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"import took {watch.Elapsed}");
    }

    private sealed class FakeSource(IReadOnlyList<ArcIssue> issues) : IReadingListSource
    {
        public string SourceKey => "ComicVine";
        public string DisplayName => "Fake";
        public bool RequiresCredentials => false;
        public bool HasBrowsableCatalog => false;

        public Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArcSearchResult>>(Array.Empty<ArcSearchResult>());

        public Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken) => Task.FromResult(issues);

        public Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken) => Task.FromResult<ArcOverviewInfo?>(null);
    }
}
