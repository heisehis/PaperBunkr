using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Dictionary;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Offline and online dictionary (docs/superpowers/specs/2026-10-27-books-slice4b-dictionary-design.md), on hand-written WordNet-format samples.</summary>
public class DictionaryTests : IDisposable
{
    private const string NounData =
        "  1 This software and database is being provided to you, the LICENSEE, by Princeton University.\n" +
        "00001740 03 n 01 entity 0 000 | that which is perceived or known; \"a living entity\"\n" +
        "00002000 03 n 02 dog 0 domestic_dog 0 000 | a member of the genus Canis; \"the dog barked\"\n" +
        "00002100 03 n 01 dog 0 000 | informal term for a man; \"you lucky dog\"\n" +
        "00003000 03 n 01 mouse 0 000 | a small rodent; \"the cat caught a mouse\"\n";

    private const string NounIndex =
        "  1 license line\n" +
        "dog n 2 0 2 0 00002000 00002100\n" +
        "domestic_dog n 1 0 1 0 00002000\n" +
        "entity n 1 0 1 0 00001740\n" +
        "mouse n 1 0 1 0 00003000\n";

    private const string VerbData = "00004000 40 v 01 run 0 000 | move fast on foot; \"he runs every day\"\n";
    private const string VerbIndex = "run v 1 0 1 0 00004000\n";
    private const string AdjData = "00005000 00 a 01 happy 0 000 | enjoying or showing pleasure\n";
    private const string AdjIndex = "happy a 1 0 1 0 00005000\n";
    private const string AdvData = "00006000 00 r 01 quickly 0 000 | with speed\n";
    private const string AdvIndex = "quickly r 1 0 1 0 00006000\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_dict_{Guid.NewGuid():N}");

    public DictionaryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static DictionaryLookup SampleLookup()
    {
        var entries = WordNetParser.ParsePos('n', NounData, NounIndex)
            .Concat(WordNetParser.ParsePos('v', VerbData, VerbIndex))
            .Concat(WordNetParser.ParsePos('a', AdjData, AdjIndex))
            .ToLookup(e => e.Lemma);
        var exceptions = WordNetParser.ParseExceptions("mice mouse\nlice louse\n").Concat(WordNetParser.ParseExceptions("ran run\nwas be\n"))
            .ToLookup(e => e.Form, e => e.Lemma);
        return new DictionaryLookup(l => entries[l].ToList(), f => exceptions[f].ToList());
    }

    [Fact]
    public void Parser_ReadsSensesInIndexOrderWithExamples()
    {
        var dog = WordNetParser.ParsePos('n', NounData, NounIndex).Where(e => e.Lemma == "dog").ToList();

        Assert.Equal(2, dog.Count);
        Assert.Equal("a member of the genus Canis", dog[0].Definition);
        Assert.Equal("the dog barked", dog[0].Example);
        Assert.Equal("informal term for a man", dog[1].Definition);
        Assert.All(dog, e => Assert.Equal("noun", e.Pos));
    }

    [Fact]
    public void Parser_TurnsUnderscoresIntoSpacesAndSkipsTheLicenceHeader()
    {
        var all = WordNetParser.ParsePos('n', NounData, NounIndex).ToList();

        Assert.Contains(all, e => e.Lemma == "domestic dog");
        Assert.DoesNotContain(all, e => e.Definition.Contains("LICENSEE"));
    }

    [Fact]
    public void Parser_SplitsGlossIntoDefinitionAndFirstExample()
    {
        var (definition, example) = WordNetParser.SplitGloss("a; b; \"first\"; \"second\"");

        Assert.Equal("a; b", definition);
        Assert.Equal("first", example);
        Assert.Equal(("plain text", (string?)null), WordNetParser.SplitGloss("plain text"));
    }

    [Fact]
    public void Parser_ReadsIrregularForms()
    {
        var pairs = WordNetParser.ParseExceptions("mice mouse\nbeen be\n").ToList();

        Assert.Equal([("mice", "mouse"), ("been", "be")], pairs);
    }

    [Fact]
    public void Lookup_FindsExactWordsAndCapsSensesPerPartOfSpeech()
    {
        var result = SampleLookup().Lookup("Dog")!;

        Assert.Equal("dog", result.Lemma);
        var noun = Assert.Single(result.Groups);
        Assert.Equal("noun", noun.Pos);
        Assert.Equal(2, noun.Senses.Count);
    }

    [Theory]
    [InlineData("dogs", "dog")]
    [InlineData("mice", "mouse")]
    [InlineData("running", "run")]
    [InlineData("ran", "run")]
    public void Lookup_ReducesInflectedForms(string word, string lemma) =>
        Assert.Equal(lemma, SampleLookup().Lookup(word)!.Lemma);

    [Fact]
    public void Lookup_ReturnsNullForUnknownAndImplausibleInput()
    {
        var lookup = SampleLookup();

        Assert.Null(lookup.Lookup("zzzyx"));
        Assert.Null(lookup.Lookup("two words"));
        Assert.Null(lookup.Lookup("   "));
        Assert.Null(lookup.Lookup("1234"));
        Assert.Null(lookup.Lookup(new string('a', 41)));
    }

    [Theory]
    [InlineData("“Dog,”", "dog")]
    [InlineData("(dog).", "dog")]
    [InlineData("mother-in-law", "mother-in-law")]
    public void Normalize_StripsEdgePunctuation(string raw, string expected) =>
        Assert.Equal(expected, DictionaryLookup.Normalize(raw));

    [Fact]
    public void Database_RoundTripsEntriesAndExceptions()
    {
        string path = Path.Combine(_dir, "d.db");
        DictionaryDatabase.Build(path,
            WordNetParser.ParsePos('n', NounData, NounIndex),
            WordNetParser.ParseExceptions("mice mouse\n"));

        using var db = DictionaryDatabase.Open(path);
        var lookup = db.AsLookup();

        Assert.Equal("mouse", lookup.Lookup("mice")!.Lemma);
        Assert.Equal(2, db.Entries("dog").Count);
        Assert.Equal("a member of the genus Canis", db.Entries("dog")[0].Definition); // sense order kept
    }

    private static byte[] BuildArchive(bool includeAdverbs = true)
    {
        var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            void Add(string name, string text) =>
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "dict/" + name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });

            Add("data.noun", NounData);
            Add("index.noun", NounIndex);
            Add("data.verb", VerbData);
            Add("index.verb", VerbIndex);
            Add("data.adj", AdjData);
            Add("index.adj", AdjIndex);
            if (includeAdverbs)
            {
                Add("data.adv", AdvData);
                Add("index.adv", AdvIndex);
            }

            Add("noun.exc", "mice mouse\n");
            Add("verb.exc", "ran run\n");
        }

        return buffer.ToArray();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Fact]
    public async Task DataService_DownloadsBuildsAndReplacesTheFile()
    {
        string path = Path.Combine(_dir, "Dictionary", "dictionary-en.db");
        byte[] archive = BuildArchive();
        var service = new DictionaryDataService(path, () => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) })), "https://example.test/wn.tar.gz");
        Assert.False(service.IsInstalled);

        await service.DownloadAsync();

        Assert.True(service.IsInstalled);
        Assert.False(File.Exists(path + ".building"));
        using var db = DictionaryDatabase.Open(path);
        Assert.Equal("run", db.AsLookup().Lookup("ran")!.Lemma);
        Assert.Equal("quickly", db.AsLookup().Lookup("quickly")!.Lemma);
    }

    [Fact]
    public async Task DataService_RejectsAnArchiveMissingAPartOfSpeech()
    {
        string path = Path.Combine(_dir, "Dictionary", "dictionary-en.db");
        byte[] archive = BuildArchive(includeAdverbs: false);
        var service = new DictionaryDataService(path, () => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) })), "https://example.test/wn.tar.gz");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync());
        Assert.False(service.IsInstalled);
    }

    [Fact]
    public async Task DataService_ReportsAServerError()
    {
        var service = new DictionaryDataService(Path.Combine(_dir, "x.db"), () => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), "https://example.test/x");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAsync());
    }

    [Fact]
    public void Online_ParsesTheApiShapeAndCapsSenses()
    {
        const string json = """
            [{"word":"hello","meanings":[
              {"partOfSpeech":"noun","definitions":[{"definition":"a greeting","example":"she said hello"},{"definition":"b"},{"definition":"c"},{"definition":"d"}]},
              {"partOfSpeech":"verb","definitions":[{"definition":"to greet"}]}]}]
            """;

        var result = OnlineDictionaryClient.Parse("hello", json)!;

        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(3, result.Groups[0].Senses.Count);
        Assert.Equal("she said hello", result.Groups[0].Senses[0].Example);
        Assert.Null(OnlineDictionaryClient.Parse("x", "{\"title\":\"No Definitions Found\"}"));
    }

    [Fact]
    public async Task Online_ReturnsNullOnNotFoundAndSendsOnlyTheWord()
    {
        Uri? requested = null;
        var client = new OnlineDictionaryClient(() => new HttpClient(new FakeHandler(r =>
        {
            requested = r.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        })));

        Assert.Null(await client.LookupAsync("Zzyzx!"));
        Assert.Equal(OnlineDictionaryClient.BaseUrl + "zzyzx", requested!.ToString());
    }
}

/// <summary>The popup's look-up state on <see cref="BookReaderScreenViewModel"/>.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookReaderDictionaryViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_dictvm_{Guid.NewGuid():N}.db");
    private readonly string _epub = Path.Combine(Path.GetTempPath(), $"paperbunkr_dictvm_{Guid.NewGuid():N}.epub");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public BookReaderDictionaryViewModelTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        PaperbunkrDbContext.DatabasePathOverride = null;
        try
        {
            File.Delete(_dbPath);
            File.Delete(_epub);
        }
        catch (IOException)
        {
        }
    }

    private BookReaderScreenViewModel Open(string? language = "en")
    {
        EpubFixture.Create(_epub, firstChapterEmpty: false);
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Books.Add(new Book { Title = "T", FilePath = _epub, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow, LanguageISO = language });
            context.SaveChanges();
        }

        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { })
        {
            // Never the machine's real dictionary (the user's own download made "not downloaded yet" fail here).
            DictionaryData = new DictionaryDataService(Path.Combine(Path.GetTempPath(), $"paperbunkr_nodict_{Guid.NewGuid():N}.db")),
        };
        vm.LoadBook(1);
        return vm;
    }

    private static DictionaryResult Result(string word) =>
        new(word, word, [new DictionaryPosGroup("noun", [new DictionarySense("a thing", "an example")])]);

    private static void Select(BookReaderScreenViewModel vm, string text) =>
        vm.OnWebViewSelectionCompleted("pb-p1", 0, text.Length, text, new Avalonia.Rect(0, 0, 10, 10));

    [Fact]
    public void SingleWordSelection_CanBeLookedUpButAPhraseCannot()
    {
        var vm = Open();

        Select(vm, "serendipity");
        Assert.True(vm.CanLookUp);

        Select(vm, "two words");
        Assert.False(vm.CanLookUp);
    }

    [Fact]
    public async Task LookUp_ShowsTheDefinitionCardAndBackReturnsToColours()
    {
        var vm = Open();
        vm.OfflineLookup = Result;
        Select(vm, "Serendipity,");

        await vm.LookUpCommand.ExecuteAsync(null);

        Assert.True(vm.IsLookingUp);
        Assert.Equal("serendipity", vm.LookUpWord);
        Assert.Single(vm.LookUpGroups);

        vm.BackFromLookUpCommand.Execute(null);
        Assert.False(vm.IsLookingUp);
        Assert.True(vm.IsNotLookingUp);
    }

    [Fact]
    public async Task LookUp_WithoutTheDictionaryOffersTheDownload()
    {
        var vm = Open();
        Select(vm, "word");

        await vm.LookUpCommand.ExecuteAsync(null);

        Assert.True(vm.OfferDictionaryDownload);
        Assert.Contains("not downloaded", vm.LookUpStatus);
    }

    [Fact]
    public async Task Download_ThenLooksTheWordUpAutomatically()
    {
        var vm = Open();
        bool downloaded = false;
        vm.DownloadDictionary = _ =>
        {
            downloaded = true;
            vm.OfflineLookup = Result;
            return Task.CompletedTask;
        };
        Select(vm, "word");
        await vm.LookUpCommand.ExecuteAsync(null);

        await vm.DownloadDictionaryCommand.ExecuteAsync(null);

        Assert.True(downloaded);
        Assert.Single(vm.LookUpGroups);
        Assert.False(vm.IsDownloadingDictionary);
    }

    [Fact]
    public async Task Download_FailureShowsTheErrorAndOffersRetry()
    {
        var vm = Open();
        vm.DownloadDictionary = _ => throw new HttpRequestException("offline");
        Select(vm, "word");
        await vm.LookUpCommand.ExecuteAsync(null);

        await vm.DownloadDictionaryCommand.ExecuteAsync(null);

        Assert.Contains("offline", vm.LookUpStatus);
        Assert.True(vm.OfferDictionaryDownload);
    }

    [Fact]
    public async Task UnknownWord_OffersOnlineOnlyWhenTheSettingIsOn()
    {
        var vm = Open();
        vm.OfflineLookup = _ => null;
        Select(vm, "zzyzx");
        await vm.LookUpCommand.ExecuteAsync(null);
        Assert.Equal("No definition found.", vm.LookUpStatus);
        Assert.False(vm.OfferOnlineLookUp);

        vm.LookUpOnline = true;
        await vm.LookUpCommand.ExecuteAsync(null);
        Assert.True(vm.OfferOnlineLookUp);

        vm.OnlineLookup = word => Task.FromResult<DictionaryResult?>(Result(word));
        await vm.LookUpOnlineNowCommand.ExecuteAsync(null);
        Assert.Single(vm.LookUpGroups);
    }

    [Fact]
    public async Task OnlineLookUp_DoesNothingWhileTheSettingIsOff()
    {
        var vm = Open();
        bool called = false;
        vm.OnlineLookup = _ =>
        {
            called = true;
            return Task.FromResult<DictionaryResult?>(null);
        };
        vm.OfflineLookup = _ => null;
        Select(vm, "word");
        await vm.LookUpCommand.ExecuteAsync(null);

        await vm.LookUpOnlineNowCommand.ExecuteAsync(null);

        Assert.False(called);
    }

    [Fact]
    public async Task NonEnglishBook_SaysThereIsNoOfflineDictionary()
    {
        var vm = Open("fr");
        Select(vm, "bonjour");

        await vm.LookUpCommand.ExecuteAsync(null);

        Assert.Contains("this language", vm.LookUpStatus);
    }

    [Fact]
    public void LookUpOnlineSetting_IsOffByDefaultAndRemembered()
    {
        var vm = Open();
        Assert.False(vm.LookUpOnline);

        vm.LookUpOnline = true;

        using var context = new PaperbunkrDbContext(_options);
        Assert.True(context.GetOrCreateAppSettings().BookReaderLookUpOnline);
    }

    [Fact]
    public void LookUpShortcut_DeclinesWithoutASelectionAndActsOnOne()
    {
        var vm = Open();
        vm.OfflineLookup = Result;
        Assert.False(vm.TryLookUpSelection());

        Select(vm, "word");
        vm.IsHighlightPopupOpen = true;

        Assert.True(vm.TryLookUpSelection());
    }

    [Fact]
    public void ClosingThePopup_ResetsTheCard()
    {
        var vm = Open();
        Select(vm, "word");
        vm.IsHighlightPopupOpen = true;
        vm.IsLookingUp = true;

        vm.IsHighlightPopupOpen = false;

        Assert.False(vm.IsLookingUp);
    }
}
