using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests.CeSmartLists;

/// <summary>
/// <see cref="CblFile.Detect"/> and <see cref="CblImport.ImportAny"/> (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md): a ComicRack smart list
/// becomes a native Smart List that matches the same books, and a reading list still goes through the existing import.
/// </summary>
public class CblImportTests : IDisposable
{
    private const string Header = "<?xml version=\"1.0\"?><ReadingList xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_cbl_import_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public CblImportTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "test.db");
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "CeSmartLists", name + ".cbl");

    private static string Matcher(string type, string value, int op = 0, bool not = false) =>
        $"<ComicBookMatcher xsi:type=\"{type}\"{(op == 0 ? "" : $" MatchOperator=\"{op}\"")}{(not ? " Not=\"true\"" : "")}><MatchValue>{value}</MatchValue></ComicBookMatcher>";

    private string SmartListFile(string name, params string[] matchers) =>
        Write(name + ".cbl", $"{Header}<Name>{name}</Name><Books /><Matchers>{string.Concat(matchers)}</Matchers></ReadingList>");

    // --- Detect ---

    [Fact]
    public void Detect_ARealSmartListFile_IsASmartList()
    {
        Assert.Equal(CblFileKind.SmartList, CblFile.Detect(Fixture("8.6 Alternate Universes now has a Main")));
    }

    [Fact]
    public void Detect_AFileWithBooks_IsAReadingList()
    {
        string path = Write("rl.cbl", $"{Header}<Name>x</Name><Books><Book Series=\"A\" Number=\"1\" Volume=\"2000\" Year=\"2000\"><Id>{Guid.NewGuid()}</Id></Book></Books><Matchers /></ReadingList>");

        Assert.Equal(CblFileKind.ReadingList, CblFile.Detect(path));
    }

    [Fact]
    public void Detect_BooksAndMatchers_IsAReadingList_SoNoBookIsLost()
    {
        string path = Write("both.cbl", $"{Header}<Name>x</Name><Books><Book Series=\"A\" Number=\"1\" Volume=\"2000\" Year=\"2000\"><Id>{Guid.NewGuid()}</Id></Book></Books><Matchers>{Matcher("ComicBookFormatMatcher", "X")}</Matchers></ReadingList>");

        Assert.Equal(CblFileKind.ReadingList, CblFile.Detect(path));
    }

    [Fact]
    public void Detect_AnEmptyList_StaysAReadingList_AsItAlwaysImportedBefore()
    {
        Assert.Equal(CblFileKind.ReadingList, CblFile.Detect(Write("empty.cbl", $"{Header}<Name>x</Name><Books /><Matchers /></ReadingList>")));
    }

    [Fact]
    public void Detect_NotXml_NotAList_OrMissing_IsInvalid()
    {
        Assert.Equal(CblFileKind.Invalid, CblFile.Detect(Write("junk.cbl", "this is not xml")));
        Assert.Equal(CblFileKind.Invalid, CblFile.Detect(Write("other.cbl", "<Other />")));
        Assert.Equal(CblFileKind.Invalid, CblFile.Detect(Path.Combine(_dir, "missing.cbl")));
    }

    // --- Import ---

    [Fact]
    public void ImportAny_ASmartList_IsSavedAndMatchesTheSameBooksAsTheRules()
    {
        var series = new Series { Name = "S" };
        _context.Series.Add(series);
        _context.Issues.AddRange(
            new Issue { Series = series, Number = "1", Publisher = "DC Comics", Format = "Annual" },
            new Issue { Series = series, Number = "2", Publisher = "DC Comics", Format = "TPB" },
            new Issue { Series = series, Number = "3", Publisher = "Marvel", Format = "Annual" });
        _context.SaveChanges();
        string path = SmartListFile("DC but not TPB", Matcher("ComicBookPublisherMatcher", "DC Comics"), Matcher("ComicBookFormatMatcher", "TPB", not: true));

        CblImportResult result = CblImport.ImportAny(_context, path);

        Assert.Equal(CblFileKind.SmartList, result.Kind);
        Assert.Null(result.ReadingList);
        Assert.NotNull(result.SmartList);
        Assert.Empty(result.Warnings);

        // Read it back through a fresh context, the way the Smart screen does.
        using var fresh = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        SmartList saved = SmartListTreeLoader.LoadWithTree(fresh, result.SmartList!.Id)!;
        Assert.Equal("DC but not TPB", saved.Name);
        Assert.False(saved.IsSystem);
        Assert.Equal(SmartListTargetKind.Issue, saved.TargetKind);
        Assert.Equal(["1"], SmartListQueryBuilder.Build(fresh, saved).Select(i => i.Number));
    }

    [Fact]
    public void ImportAny_AReadingList_StillGoesThroughTheExistingImport()
    {
        string path = Write("rl.cbl", $"{Header}<Name>My Reading List</Name><Books><Book Series=\"Alpha\" Number=\"1\" Volume=\"2000\" Year=\"2000\"><Id>{Guid.NewGuid()}</Id></Book></Books><Matchers /></ReadingList>");

        CblImportResult result = CblImport.ImportAny(_context, path);

        Assert.Equal(CblFileKind.ReadingList, result.Kind);
        Assert.Null(result.SmartList);
        Assert.Equal("My Reading List", result.ReadingList!.Name);
        Assert.Single(result.ReadingList.Items);
        Assert.Empty(_context.SmartLists.Where(s => !s.IsSystem));
    }

    [Fact]
    public void ImportAny_ANameAlreadyInUse_GetsASuffix()
    {
        string path = SmartListFile("Dupes", Matcher("ComicBookFormatMatcher", "X"));

        string first = CblImport.ImportAny(_context, path).SmartList!.Name;
        string second = CblImport.ImportAny(_context, path).SmartList!.Name;
        string third = CblImport.ImportAny(_context, path).SmartList!.Name;

        Assert.Equal(["Dupes", "Dupes (2)", "Dupes (3)"], [first, second, third]);
    }

    [Fact]
    public void ImportAny_AppendsAfterTheExistingLists()
    {
        int before = _context.SmartLists.Count();

        var list = CblImport.ImportAny(_context, SmartListFile("Later", Matcher("ComicBookFormatMatcher", "X"))).SmartList!;

        Assert.Equal(before, list.SortOrder);
    }

    [Fact]
    public void ImportAny_ADroppedConditionIsReportedAndTheRestIsKept()
    {
        string path = SmartListFile(
            "Half",
            Matcher("ComicBookFormatMatcher", "X"),
            "<ComicBookMatcher xsi:type=\"ComicBookPluginMatcher\" MatchOperator=\"1\" PluginKey=\"someUnknownScript\"><MatchValue /></ComicBookMatcher>");

        CblImportResult result = CblImport.ImportAny(_context, path);

        Assert.NotNull(result.SmartList);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("script 'someUnknownScript'", warning.ConditionType);
    }

    [Fact]
    public void ImportAny_ComicRacksSameXDifferentYScript_BecomesTheCondition_WithNoWarning()
    {
        CblImportResult result = CblImport.ImportAny(_context, Fixture("7.1 Dupe with Comicvine_issue"));

        Assert.Empty(result.Warnings);
        var conditions = SmartListQueryBuilder.Flatten(result.SmartList!.RootGroup).ToList();
        var sameX = conditions.Single(c => c.Field == SmartListField.SameXDifferentY);
        Assert.Equal("comicvine_issue|Format", sameX.Value);
        Assert.Equal(string.Empty, sameX.Value2);
        Assert.Contains(conditions, c => c.Field == SmartListField.CustomValue);
    }

    [Fact]
    public void ImportAny_AListWithNoUsableCondition_IsRefusedAndNothingIsSaved()
    {
        int before = _context.SmartLists.Count();
        string path = SmartListFile("Scripty", "<ComicBookMatcher xsi:type=\"ComicBookPluginMatcher\" MatchOperator=\"1\" PluginKey=\"someUnknownScript\"><MatchValue /></ComicBookMatcher>");

        CblImportResult result = CblImport.ImportAny(_context, path);

        Assert.True(result.Refused);
        Assert.Null(result.SmartList);
        Assert.Contains("Scripty", result.RefusedReason);
        Assert.Single(result.Warnings);
        Assert.Equal(before, _context.SmartLists.Count());
    }

    [Fact]
    public void ImportAny_ComicRacksProposedValuesScript_BecomesTheHasProposedValueCondition()
    {
        CblImportResult result = CblImport.ImportAny(_context, Fixture("1.1 Commit Proposed Values"));

        Assert.False(result.Refused);
        Assert.Empty(result.Warnings);
        var condition = Assert.Single(SmartListQueryBuilder.Flatten(result.SmartList!.RootGroup));
        Assert.Equal(SmartListField.HasProposedValue, condition.Field);
        Assert.Equal(SmartListOperator.Is, condition.Operator);
    }

    [Fact]
    public void ImportAny_NotAList_Throws()
    {
        Assert.Throws<InvalidDataException>(() => CblImport.ImportAny(_context, Write("junk.cbl", "nope")));
    }

    [Fact]
    public void ImportAny_ARealWorldNestedList_SavesItsWholeTree()
    {
        CblImportResult result = CblImport.ImportAny(_context, Fixture("6.8 Set Format Limited Series"));

        using var fresh = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        SmartList saved = SmartListTreeLoader.LoadWithTree(fresh, result.SmartList!.Id)!;
        var all = SmartListQueryBuilder.Flatten(saved.RootGroup).ToList();
        Assert.Contains(all, c => c.Field == SmartListField.SeriesLastNumber && c.Operator == SmartListOperator.InRange && c.Value == "2" && c.Value2 == "25");
        Assert.Equal(8, all.Count(c => c.Field is SmartListField.Format or SmartListField.SeriesName or SmartListField.CustomValue));
    }
}
