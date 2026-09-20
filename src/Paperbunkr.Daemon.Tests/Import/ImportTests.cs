using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Import;
using Paperbunkr.Daemon.Tests.Services;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Tests.Import;

public class NameTemplateTests
{
    private static string? Values(string t) => t switch
    {
        "series" => "Spawn", "number" => "5", "year" => "2026", "volumeyear" => "1992", "publisher" => "Image", "title" => "Origins",
        _ => null,
    };

    [Theory]
    [InlineData("{series} #{number}", "Spawn #5")]
    [InlineData("{series} #{number:000}", "Spawn #005")]
    [InlineData("{series} #{number:00}", "Spawn #05")]
    [InlineData("[{title} - ]{series}", "Origins - Spawn")]
    [InlineData("{series}[ {volume}]", "Spawn")]                      // {volume} is empty: the whole group, space included, vanishes
    [InlineData("{series}[ ({year}/{month})]", "Spawn")]              // ...and one empty token drops a group even when another has a value
    [InlineData("{series} ({year})", "Spawn (2026)")]
    [InlineData(@"{series} \[draft\]", "Spawn [draft]")]              // backslash escapes
    [InlineData("{nonsense}{series}", "Spawn")]                       // unknown tokens are empty
    [InlineData("  {series}  ", "Spawn")]                             // the result is trimmed, as in CE
    public void Format_FollowsCeSemantics(string template, string expected) => Assert.Equal(expected, NameTemplate.Format(template, Values));

    [Fact]
    public void NumericFormat_OnlyAppliesToWholeNumbers_SoADecimalIssueIsNeverRounded()
    {
        string? Get(string t) => t == "number" ? "1.5" : null;
        Assert.Equal("#1.5", NameTemplate.Format("#{number:000}", Get));

        string? Annual(string t) => t == "number" ? "Annual 1" : null;
        Assert.Equal("#Annual 1", NameTemplate.Format("#{number:000}", Annual));
    }

    [Theory]
    [InlineData("{series} #{number:000}", null)]
    [InlineData("{publisher}/{series} ({volumeyear})/{series} #{number:000}", null)]
    [InlineData("[{title} - ]{series}", null)]
    [InlineData("", "empty")]
    [InlineData("{series", "never closed")]
    [InlineData("{series}}", "no matching {")]
    [InlineData("[{series}", "never closed")]
    [InlineData("{series}]", "no matching [")]
    [InlineData("{colour}", "Unknown token {colour}")]
    [InlineData("{{series}}", "nested")]
    public void Validate_ReportsProblemsBeforeAnythingIsImported(string template, string? errorFragment)
    {
        var error = NameTemplate.Validate(template);

        if (errorFragment is null) Assert.Null(error);
        else Assert.Contains(errorFragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatPath_MakesFolders_SanitizesEverySegment_AndAddsTheExtension()
    {
        string? Get(string t) => t switch { "publisher" => "DC: Comics?", "series" => "Batman.", "number" => "5", "volumeyear" => "2016", _ => null };

        var path = NameTemplate.FormatPath("{publisher}/{series} ({volumeyear})/{series} #{number:000}", Get, ".cbz");

        Assert.Equal("DC- Comics_/Batman. (2016)/Batman. #005.cbz", path);
    }

    [Fact]
    public void FormatPath_DropsEmptyFolders_AndNeverProducesAnEmptyName()
    {
        string? NoPublisher(string t) => t == "series" ? "Spawn" : null;
        Assert.Equal("Spawn/Spawn.cbz", NameTemplate.FormatPath("{publisher}/{series}/{series}", NoPublisher, "cbz"));
        Assert.Equal("Unnamed.cbz", NameTemplate.FormatPath("{publisher}", NoPublisher, ".cbz"));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("a\\b*c", "a_b_c")]
    [InlineData("..", "")]
    public void SanitizeSegment_KeepsNamesLegalOnWindows(string input, string expected) => Assert.Equal(expected, NameTemplate.SanitizeSegment(input));

    [Fact]
    public void SanitizeSegment_BoundsLength() => Assert.True(NameTemplate.SanitizeSegment(new string('x', 500)).Length <= 120);
}

public abstract class ArchiveTestBase : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), $"paperbunkr_import_test_{Guid.NewGuid():N}");

    protected ArchiveTestBase() => Directory.CreateDirectory(Root);

    public virtual void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch (IOException) { }
    }

    protected static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>Builds a zip with the given entries (name -> bytes) and returns its path.</summary>
    protected string Zip(string fileName, params (string Name, byte[] Data)[] entries)
    {
        var path = Path.Combine(Root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(data);
        }

        return path;
    }

    protected static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    protected static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);
}

public class ComicArchiveTests : ArchiveTestBase
{
    private static readonly ComicInfoFields Info = new("Spawn & Co", "263", "Origins <1>", 2026, 9, 16, "Image");

    private static List<string> Names(string cbz)
    {
        using var zip = ZipFile.OpenRead(cbz);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    [Fact]
    public void Repack_KeepsOnlyPages_InNaturalOrder_WithSequentialNames_AndAddsComicInfo()
    {
        var source = Zip("in.cbr", ("Scans/page10.jpg", Png), ("Scans/page2.jpg", Png), ("Scans/page1.png", Png), ("Thumbs.db", Text("x")), ("readme.nfo", Text("x")), ("Scans/", Array.Empty<byte>()));

        var (cbz, pages) = ComicArchive.PrepareCbz(source, Path.Combine(Root, "t1"), Info);

        Assert.Equal(3, pages);
        Assert.Equal(new[] { "00001.png", "00002.jpg", "00003.jpg", "ComicInfo.xml" }, Names(cbz));   // page1, page2, page10 - not lexical order
        using var zip = ZipFile.OpenRead(cbz);
        var xml = new StreamReader(zip.GetEntry("ComicInfo.xml")!.Open()).ReadToEnd();
        Assert.Contains("<Series>Spawn &amp; Co</Series>", xml);                                       // escaped, not corrupt XML
        Assert.Contains("<Title>Origins &lt;1&gt;</Title>", xml);
        Assert.Contains("<Number>263</Number>", xml);
        Assert.Contains("<Year>2026</Year>", xml);
        Assert.Contains("<Publisher>Image</Publisher>", xml);
    }

    [Fact]
    public void Repack_DoesNotTouchTheSourceFile()
    {
        var source = Zip("keep.cbz", ("1.jpg", Png));
        var before = Sha(source);

        ComicArchive.PrepareCbz(source, Path.Combine(Root, "t2"), Info);

        Assert.Equal(before, Sha(source));
    }

    [Fact]
    public void AnExistingComicInfo_ThatIdentifiesTheIssue_IsKept_ButAnUnusableOneIsReplaced()
    {
        var good = Zip("good.cbz", ("1.jpg", Png), ("comicinfo.xml", Text("<ComicInfo><Series>Theirs</Series><Number>7</Number></ComicInfo>")));
        var (keptCbz, _) = ComicArchive.PrepareCbz(good, Path.Combine(Root, "t3"), Info);
        using (var zip = ZipFile.OpenRead(keptCbz))
        {
            var xml = new StreamReader(zip.GetEntry("ComicInfo.xml")!.Open()).ReadToEnd();
            Assert.Contains("<Series>Theirs</Series>", xml);
            Assert.DoesNotContain("Spawn", xml);
        }

        var poor = Zip("poor.cbz", ("1.jpg", Png), ("ComicInfo.xml", Text("<ComicInfo><Summary>only a summary</Summary></ComicInfo>")));
        var (replaced, _) = ComicArchive.PrepareCbz(poor, Path.Combine(Root, "t4"), Info);
        using var zip2 = ZipFile.OpenRead(replaced);
        Assert.Contains("<Series>Spawn &amp; Co</Series>", new StreamReader(zip2.GetEntry("ComicInfo.xml")!.Open()).ReadToEnd());
    }

    [Fact]
    public void WithoutInfo_NoComicInfoIsWritten()
    {
        var source = Zip("plain.cbz", ("1.jpg", Png));

        var (cbz, _) = ComicArchive.PrepareCbz(source, Path.Combine(Root, "t5"), info: null);

        Assert.Equal(new[] { "00001.jpg" }, Names(cbz));
    }

    [Fact]
    public void AnArchiveWithNoPages_IsUnreadable()
    {
        var source = Zip("empty.cbz", ("readme.txt", Text("hi")));

        var ex = Assert.Throws<ImportFailureException>(() => ComicArchive.PrepareCbz(source, Path.Combine(Root, "t6"), Info));

        Assert.Equal(BlocklistReason.Unreadable, ex.Reason);
    }

    [Fact]
    public void AGarbageFile_IsReportedAsCorrupt_NotACrash()
    {
        var path = Path.Combine(Root, "garbage.cbz");
        File.WriteAllBytes(path, Text("this is definitely not an archive"));

        var ex = Assert.Throws<ImportFailureException>(() => ComicArchive.PrepareCbz(path, Path.Combine(Root, "t7"), Info));

        Assert.Equal(BlocklistReason.Corrupt, ex.Reason);
    }

    [Fact]
    public void TryReadIdentity_ReadsSeriesAndNumber_OrNothing()
    {
        var tagged = Zip("tagged.cbz", ("1.jpg", Png), ("ComicInfo.xml", Text("<ComicInfo><Series>Saga</Series><Number>4</Number></ComicInfo>")));
        Assert.Equal(("Saga", "4"), ComicArchive.TryReadIdentity(tagged));

        Assert.Null(ComicArchive.TryReadIdentity(Zip("untagged.cbz", ("1.jpg", Png))));
        Assert.Null(ComicArchive.TryReadIdentity(Zip("half.cbz", ("ComicInfo.xml", Text("<ComicInfo><Series>Saga</Series></ComicInfo>")))));
        Assert.Null(ComicArchive.TryReadIdentity(Zip("bad.cbz", ("ComicInfo.xml", Text("<not xml")))));
        Assert.Null(ComicArchive.TryReadIdentity(Path.Combine(Root, "missing.cbz")));
    }
}

public class ImportProcessorTests : CycleTestBase
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_importproc_{Guid.NewGuid():N}");
    private readonly List<string> _ingested = new();
    private string SavePath => Path.Combine(_root, "downloads");
    private string Library => Path.Combine(_root, "library");
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public ImportProcessorTests()
    {
        Directory.CreateDirectory(SavePath);
        Directory.CreateDirectory(Library);
    }

    public new void Dispose()
    {
        base.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class Ingester(Func<string, int?> respond, List<string> log) : ILibraryIngester
    {
        public Task<int?> IngestAsync(string filePath, CancellationToken cancellationToken)
        {
            log.Add(filePath);
            return Task.FromResult(respond(filePath));
        }
    }

    private ImportProcessor Processor(Func<string, int?>? ingest = null) =>
        new(NewContext, new Ingester(ingest ?? (_ => null), _ingested), Events, () => Now, Path.Combine(_root, "tmp"));

    private void Settings(Action<AcquisitionSettings> change)
    {
        using var context = NewContext();
        var s = context.GetOrCreateAcquisitionSettings();
        s.DestinationFolderPath = Library;
        change(s);
        context.SaveChanges();
    }

    private string MakeCbz(string relative, params string[] names)
    {
        var path = Path.Combine(SavePath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in names.Length == 0 ? new[] { "1.jpg" } : names)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(Png);
        }

        return path;
    }

    /// <summary>A real library issue for the fake scanner to "add" (the wanted row's IssueId is a foreign key).</summary>
    private int SeedIssue(string number = "261")
    {
        using var context = NewContext();
        var series = new Series { Name = "Spawn" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = number, FilePath = "scanned.cbz" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private DownloadStatus Download(string name) => new(FakeDownloadClient.Hash('a'), name, 1.0, DownloadState.Completed, 1000, 0, null, SavePath, Path.Combine(SavePath, name));

    private static DownloadFile F(string path) => new(path, 1000);

    [Fact]
    public async Task ASingleCbz_IsCopiedTaggedNamedAndIngested_LeavingTheDownloadUntouched()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)new DateTime(2026, 9, 16));
        var source = MakeCbz("Spawn 261 (1992).cbz");
        var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));

        int scannedId = SeedIssue();
        var outcome = await Processor(_ => scannedId).ImportAsync(ids[0], Download("Spawn 261 (1992)"), new[] { F("Spawn 261 (1992).cbz") }, CancellationToken.None);

        Assert.True(outcome.Success);
        var expected = Path.Combine(Library, "Image", "Spawn (1992)", "Spawn #261.cbz");        // {publisher}/{series} ({volumeyear})/{series} #{number:000}
        Assert.True(File.Exists(expected));
        Assert.Equal(new[] { expected }, _ingested);
        using (var zip = ZipFile.OpenRead(expected))
        {
            var xml = new StreamReader(zip.GetEntry("ComicInfo.xml")!.Open()).ReadToEnd();
            Assert.Contains("<Series>Spawn</Series>", xml);
            Assert.Contains("<Number>261</Number>", xml);
            Assert.Contains("<Month>9</Month>", xml);
        }

        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));   // the seeded file is byte-for-byte unchanged
        using var context = NewContext();
        var wanted = context.WantedIssues.Single();
        Assert.Equal(WantedIssueStatus.Imported, wanted.Status);
        Assert.Equal(scannedId, wanted.IssueId);
        Assert.NotNull(wanted.ImportedAt);
        Assert.Null(wanted.DownloadProgress);
        Assert.Equal(expected, Assert.IsType<IssueImportedEvent>(Drain().Single(e => e is IssueImportedEvent)).Path);
        Assert.False(outcome.RemoveTorrent);
        Assert.False(Directory.Exists(Path.Combine(_root, "tmp")) && Directory.GetDirectories(Path.Combine(_root, "tmp")).Length > 0);   // temp work was cleaned up
    }

    [Fact]
    public async Task TheImportedIssue_ReplacesItsReadingListPlaceholder_InPlace()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null);
        int placeholderId, itemId, newIssueId;
        using (var context = NewContext())
        {
            var series = new Series { Name = "Spawn" };
            context.Series.Add(series);
            context.SaveChanges();
            context.WatchedSeries.Single().SeriesId = series.Id;
            var placeholder = new Issue { SeriesId = series.Id, Number = "261", IsPlaceholder = true, FileIsMissing = true };
            var real = new Issue { SeriesId = series.Id, Number = "261", FilePath = "x.cbz" };
            var list = new ReadingList { Name = "Arc", CreatedAt = Now, UpdatedAt = Now };
            context.Issues.AddRange(placeholder, real);
            context.ReadingLists.Add(list);
            context.SaveChanges();
            var item = new ReadingListItem { ReadingListId = list.Id, IssueId = placeholder.Id, SortOrder = 3 };
            context.ReadingListItems.Add(item);
            context.SaveChanges();
            placeholderId = placeholder.Id; itemId = item.Id; newIssueId = real.Id;
        }

        MakeCbz("Spawn 261 (1992).cbz");
        await Processor(_ => newIssueId).ImportAsync(ids[0], Download("Spawn 261 (1992)"), new[] { F("Spawn 261 (1992).cbz") }, CancellationToken.None);

        using var check = NewContext();
        var relinked = check.ReadingListItems.Single(i => i.Id == itemId);
        Assert.Equal(newIssueId, relinked.IssueId);
        Assert.Equal(3, relinked.SortOrder);                                       // same position in the list
        Assert.DoesNotContain(check.Issues, i => i.Id == placeholderId);           // the placeholder is gone
    }

    [Fact]
    public async Task WithoutADestinationFolderOrWithABadTemplate_TheImportIsDeferred_NotFailed()
    {
        var ids = AddWanted((DateTime?)null);
        MakeCbz("a.cbz");

        var noFolder = await Processor().ImportAsync(ids[0], Download("a"), new[] { F("a.cbz") }, CancellationToken.None);
        Assert.True(noFolder.IsDeferred);
        Assert.Contains("destination library folder", noFolder.Failure);

        Settings(s => s.RenameTemplate = "{oops}");
        var badTemplate = await Processor().ImportAsync(ids[0], Download("a"), new[] { F("a.cbz") }, CancellationToken.None);
        Assert.True(badTemplate.IsDeferred);
        Assert.Contains("naming template", badTemplate.Failure);

        Settings(s => s.DestinationFolderPath = Path.Combine(_root, "does-not-exist"));
        Assert.True((await Processor().ImportAsync(ids[0], Download("a"), new[] { F("a.cbz") }, CancellationToken.None)).IsDeferred);
        Assert.Empty(_ingested);
    }

    [Fact]
    public async Task ADownloadWithNoComicFiles_FailsAsUnreadable()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null);
        File.WriteAllText(Path.Combine(SavePath, "readme.nfo"), "x");

        var outcome = await Processor().ImportAsync(ids[0], Download("x"), new[] { F("readme.nfo"), F("cover.jpg") }, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(BlocklistReason.Unreadable, outcome.FailureReason);
        Assert.Contains("no comic archives", outcome.Failure);
    }

    [Fact]
    public async Task ACorruptArchive_FailsWithACorruptReason()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null);
        File.WriteAllText(Path.Combine(SavePath, "bad.cbz"), "not an archive");

        var outcome = await Processor().ImportAsync(ids[0], Download("bad"), new[] { F("bad.cbz") }, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(BlocklistReason.Corrupt, outcome.FailureReason);
        Assert.Empty(_ingested);
        Assert.Empty(Directory.GetFiles(Library, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AFileNeverOverwritesAnExistingOne()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null);
        var existing = Path.Combine(Library, "Image", "Spawn (1992)", "Spawn #261.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "precious");
        MakeCbz("Spawn 261 (1992).cbz");

        await Processor().ImportAsync(ids[0], Download("Spawn 261 (1992)"), new[] { F("Spawn 261 (1992).cbz") }, CancellationToken.None);

        Assert.Equal("precious", File.ReadAllText(existing));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(existing)!, "Spawn #261 (2).cbz")));
    }

    [Fact]
    public async Task APack_ImportsEachFileToItsOwnIssue_ReportsTheRest_AndNeverMapsTheFolderToOneIssue()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null, (DateTime?)null);            // wants #261 and #262
        MakeCbz("Spawn Pack/Spawn 262 (1992).cbz");
        MakeCbz("Spawn Pack/Spawn 261 (1992).cbz");
        MakeCbz("Spawn Pack/Spawn 300 (1992).cbz");                       // not wanted
        var files = new[] { F("Spawn Pack/Spawn 262 (1992).cbz"), F("Spawn Pack/Spawn 261 (1992).cbz"), F("Spawn Pack/Spawn 300 (1992).cbz") };

        var outcome = await Processor(_ => null).ImportAsync(ids[0], Download("Spawn v1-2 Complete"), files, CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(new[] { ids[0], ids[1] }.OrderBy(i => i), outcome.ImportedWantedIssueIds.OrderBy(i => i));
        Assert.Equal(new[] { "Spawn Pack/Spawn 300 (1992).cbz" }, outcome.Unmatched);
        Assert.True(File.Exists(Path.Combine(Library, "Image", "Spawn (1992)", "Spawn #261.cbz")));
        Assert.True(File.Exists(Path.Combine(Library, "Image", "Spawn (1992)", "Spawn #262.cbz")));
        using var context = NewContext();
        Assert.All(context.WantedIssues, w => Assert.Equal(WantedIssueStatus.Imported, w.Status));
    }

    [Fact]
    public async Task APackThatLacksTheGrabbedIssue_SettlesThatIssue_SoItIsNotReimportedForever()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null, (DateTime?)null);            // grabbed for #261, but the pack only holds #262
        MakeCbz("p/Spawn 262 (1992).cbz");
        MakeCbz("p/Spawn 999 (1992).cbz");

        var outcome = await Processor().ImportAsync(ids[0], Download("Spawn Complete Collection"), new[] { F("p/Spawn 262 (1992).cbz"), F("p/Spawn 999 (1992).cbz") }, CancellationToken.None);

        Assert.Equal(new[] { ids[1] }, outcome.ImportedWantedIssueIds);
        using var context = NewContext();
        var grabbedFor = context.WantedIssues.Single(w => w.Id == ids[0]);
        Assert.Equal(WantedIssueStatus.Failed, grabbedFor.Status);
        Assert.Contains("didn't contain", grabbedFor.FailureReason);
        Assert.Single(context.ReleaseBlocklist);
    }

    [Fact]
    public async Task APackFileIsMatchedByItsOwnComicInfo_WhenItsNameGivesNothingAway()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null, (DateTime?)null);
        var path = Path.Combine(SavePath, "p", "scan_0007.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("1.jpg").Open()) s.Write(Png);
            using var info = new StreamWriter(zip.CreateEntry("ComicInfo.xml").Open());
            info.Write("<ComicInfo><Series>Spawn</Series><Number>262</Number></ComicInfo>");
        }

        var outcome = await Processor().ImportAsync(ids[0], Download("Spawn weekly pack"), new[] { F("p/scan_0007.cbz") }, CancellationToken.None);

        Assert.Equal(new[] { ids[1] }, outcome.ImportedWantedIssueIds);
    }

    [Fact]
    public async Task APathThatEscapesTheDownloadFolder_IsNeverReadOrCopied()
    {
        Settings(_ => { });
        var ids = AddWanted((DateTime?)null);
        var outside = Path.Combine(_root, "secret.cbz");
        using (var zip = ZipFile.Open(outside, ZipArchiveMode.Create)) { using var s = zip.CreateEntry("1.jpg").Open(); s.Write(Png); }

        var outcome = await Processor().ImportAsync(ids[0], Download("evil"), new[] { F("../secret.cbz") }, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Empty(_ingested);
        Assert.Empty(Directory.GetFiles(Library, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MoveOriginal_AsksTheHostToRemoveTheTorrent_OnlyWhenEverythingWasImported()
    {
        Settings(s => s.MoveOriginalOnImport = true);
        var ids = AddWanted((DateTime?)null);
        MakeCbz("Spawn 261 (1992).cbz");

        var outcome = await Processor().ImportAsync(ids[0], Download("Spawn 261 (1992)"), new[] { F("Spawn 261 (1992).cbz") }, CancellationToken.None);

        Assert.True(outcome.RemoveTorrent);
    }

    [Fact]
    public async Task WriteComicInfoOff_LeavesTheArchiveUntagged()
    {
        Settings(s => s.WriteComicInfo = false);
        var ids = AddWanted((DateTime?)null);
        MakeCbz("Spawn 261 (1992).cbz");

        await Processor().ImportAsync(ids[0], Download("Spawn 261 (1992)"), new[] { F("Spawn 261 (1992).cbz") }, CancellationToken.None);

        using var zip = ZipFile.OpenRead(Path.Combine(Library, "Image", "Spawn (1992)", "Spawn #261.cbz"));
        Assert.Null(zip.GetEntry("ComicInfo.xml"));
    }
}
