using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Services.Speech;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

public class BookReadAloudPlanTests
{
    private const string Html =
        "<h1 id=\"pb-p1\">Chapter One</h1>" +
        "<p id=\"pb-p2\">It was a <em>dark</em> night.</p>" +
        "<blockquote id=\"pb-p3\"><p id=\"pb-p4\">Quoted &amp; wise.</p></blockquote>" +
        "<p id=\"pb-p5\">   </p>" +
        "<p id=\"pb-p6\"><img src=\"x.png\"/></p>" +
        "<p id=\"pb-p7\">The end.</p>";

    [Fact]
    public void Build_ListsLeafBlocksInOrderWithPlainText()
    {
        var plan = BookReadAloudPlan.Build(Html, null);

        Assert.Equal(["pb-p1", "pb-p2", "pb-p4", "pb-p7"], plan.Select(b => b.BlockId));
        Assert.Equal("It was a dark night.", plan[1].Text);
        Assert.Equal("Quoted & wise.", plan[2].Text);
    }

    [Fact]
    public void Build_StartsAtTheGivenBlockOrTheChapterStart()
    {
        Assert.Equal("pb-p4", BookReadAloudPlan.Build(Html, "pb-p4")[0].BlockId);
        Assert.Equal("pb-p1", BookReadAloudPlan.Build(Html, null)[0].BlockId);
        Assert.Equal("pb-p1", BookReadAloudPlan.Build(Html, "not-a-block")[0].BlockId);
    }

    [Fact]
    public void Build_AStartOnABlockThatIsNotReadLandsOnTheNextOneThatIs()
    {
        Assert.Equal("pb-p4", BookReadAloudPlan.Build(Html, "pb-p3")[0].BlockId); // the blockquote around a paragraph
        Assert.Equal("pb-p7", BookReadAloudPlan.Build(Html, "pb-p5")[0].BlockId); // an empty paragraph
        Assert.Empty(BookReadAloudPlan.Build(Html, "pb-p99")); // past the last block there is nothing left to read
    }

    [Fact]
    public void Build_HandlesEmptyInput()
    {
        Assert.Empty(BookReadAloudPlan.Build(null, null));
        Assert.Empty(BookReadAloudPlan.Build("<div>no blocks</div>", null));
    }
}

[Collection(nameof(AvaloniaTestCollection))]
public class BookReaderReadAloudViewModelTests : IDisposable
{
    private sealed class FakeEngine : ISpeechEngine
    {
        public IReadOnlyList<string> Voices { get; } = ["Anna", "Ben"];

        public string? VoiceName { get; set; }

        public int Rate { get; set; }

        public event Action<bool>? Completed;

        public List<string> Spoken { get; } = new();

        public int Stops { get; private set; }

        public bool Paused { get; private set; }

        public void Speak(string text) => Spoken.Add(text);

        public void Pause() => Paused = true;

        public void Resume() => Paused = false;

        public void Stop() => Stops++;

        public void Finish(bool cancelled = false) => Completed?.Invoke(cancelled);

        public void Dispose()
        {
        }
    }

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_speech_{Guid.NewGuid():N}.db");
    private readonly string _epub = Path.Combine(Path.GetTempPath(), $"paperbunkr_speech_{Guid.NewGuid():N}.epub");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public BookReaderReadAloudViewModelTests()
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

    private (BookReaderScreenViewModel Vm, FakeEngine Engine) Open(bool withEngine = true)
    {
        EpubFixture.Create(_epub, firstChapterEmpty: false);
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Books.Add(new Book { Title = "T", FilePath = _epub, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
            context.SaveChanges();
        }

        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var engine = new FakeEngine();
        var vm = new BookReaderScreenViewModel(() => { }) { SpeechFactory = () => withEngine ? engine : null };
        vm.LoadBook(1);
        vm.UpdateViewportSize(new Avalonia.Size(700, 800));
        return (vm, engine);
    }

    [Fact]
    public void ChapterHtml_CarriesTheBlockIdsEverythingAnchorsTo()
    {
        var (vm, _) = Open();

        Assert.Contains("id=\"pb-p1\"", vm.CurrentChapterHtml);
    }

    [Fact]
    public void Start_SpeaksTheFirstBlockWithTheChosenVoiceAndRate()
    {
        var (vm, engine) = Open();
        vm.SpeechVoice = "Ben";
        vm.SpeechRate = 2;
        var blocks = new List<string?>();
        vm.SpeakingBlockChanged += blocks.Add;

        vm.ToggleSpeakCommand.Execute(null);

        Assert.True(vm.IsSpeaking);
        Assert.Single(engine.Spoken);
        Assert.Equal("Ben", engine.VoiceName);
        Assert.Equal(4, engine.Rate); // SAPI scale is twice the reader's -5..5
        Assert.StartsWith("pb-p", blocks[0]);
    }

    [Fact]
    public void Completion_AdvancesToTheNextBlockAndStopsAtTheChapterEnd()
    {
        var (vm, engine) = Open();
        vm.ToggleSpeakCommand.Execute(null);
        int total = BookReadAloudPlan.Build(vm.CurrentChapterHtml, null).Count;
        Assert.True(total >= 1);

        for (int i = 1; i < total; i++)
        {
            vm.OnBlockSpoken(cancelled: false);
            Assert.True(vm.IsSpeaking);
        }

        Assert.Equal(total, engine.Spoken.Count);
        vm.OnBlockSpoken(cancelled: false);

        Assert.False(vm.IsSpeaking); // never goes on into the next chapter
    }

    [Fact]
    public void ACancelledUtterance_IsIgnored()
    {
        var (vm, engine) = Open();
        vm.ToggleSpeakCommand.Execute(null);
        int spoken = engine.Spoken.Count;

        vm.OnBlockSpoken(cancelled: true);

        Assert.Equal(spoken, engine.Spoken.Count);
        Assert.True(vm.IsSpeaking);
    }

    [Fact]
    public void Stop_StopsTheEngineAndClearsTheHighlight()
    {
        var (vm, engine) = Open();
        string? last = "x";
        vm.SpeakingBlockChanged += b => last = b;
        vm.ToggleSpeakCommand.Execute(null);

        vm.ToggleSpeakCommand.Execute(null);

        Assert.False(vm.IsSpeaking);
        Assert.Equal(1, engine.Stops);
        Assert.Null(last);
    }

    [Fact]
    public void PauseAndResume_DriveTheEngine()
    {
        var (vm, engine) = Open();
        vm.ToggleSpeakCommand.Execute(null);

        vm.ToggleSpeakPauseCommand.Execute(null);
        Assert.True(vm.IsSpeakingPaused);
        Assert.True(engine.Paused);

        vm.ToggleSpeakPauseCommand.Execute(null);
        Assert.False(vm.IsSpeakingPaused);
        Assert.False(engine.Paused);
    }

    [Fact]
    public void Speed_StaysBetweenMinusFiveAndFiveAndIsRemembered()
    {
        var (vm, _) = Open();

        for (int i = 0; i < 20; i++) vm.SpeakFasterCommand.Execute(null);
        Assert.Equal(5, vm.SpeechRate);
        for (int i = 0; i < 20; i++) vm.SpeakSlowerCommand.Execute(null);
        Assert.Equal(-5, vm.SpeechRate);

        using var context = new PaperbunkrDbContext(_options);
        Assert.Equal(-5, context.GetOrCreateAppSettings().BookReaderSpeechRate);
    }

    [Fact]
    public void Voice_IsRemembered()
    {
        var (vm, _) = Open();

        vm.SpeechVoice = "Anna";

        using var context = new PaperbunkrDbContext(_options);
        Assert.Equal("Anna", context.GetOrCreateAppSettings().BookReaderSpeechVoice);
    }

    [Fact]
    public void ChapterChangeJumpAndClose_AllStopSpeaking()
    {
        var (vm, engine) = Open();
        vm.ToggleSpeakCommand.Execute(null);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);
        Assert.False(vm.IsSpeaking);

        vm.ToggleSpeakCommand.Execute(null);
        Assert.True(vm.IsSpeaking);
        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsSpeaking);
        Assert.True(engine.Stops >= 2);
    }

    [Fact]
    public void WithoutVoices_NothingStartsAndTheCardIsHidden()
    {
        var (vm, _) = Open(withEngine: false);

        vm.OpenFontSheetCommand.Execute(null);
        vm.ToggleSpeakCommand.Execute(null);

        Assert.False(vm.HasSpeech);
        Assert.False(vm.IsSpeaking);
        Assert.False(vm.HasSpeechEngine());
    }

    [Fact]
    public void FontSheet_ShowsTheCardAndListsVoicesWhenThereAreSome()
    {
        var (vm, _) = Open();

        vm.OpenFontSheetCommand.Execute(null);

        Assert.True(vm.HasSpeech);
        Assert.Equal(["Anna", "Ben"], vm.SpeechVoices);
    }

    [Fact]
    public void SheetSwitch_StartsAndStopsLikeTheKey()
    {
        var (vm, engine) = Open();

        vm.IsSpeaking = true;
        Assert.Single(engine.Spoken);

        vm.IsSpeaking = false;
        Assert.Equal(1, engine.Stops);
    }

    [Fact]
    public void SpeakingNotesActivityButAutoScrollingDoesNotDoubleCount()
    {
        var (vm, _) = Open();
        vm.ToggleSpeakCommand.Execute(null);

        Assert.True(vm.IsSpeaking); // each spoken block calls NoteInput; the activity tracker's own tests cover the clock
    }

    [Fact]
    public void ReadAloudActions_AreInTheBookReaderGroup()
    {
        string[] ids = [InputActionIds.BookSpeakToggle, InputActionIds.BookSpeakPause, InputActionIds.BookSpeakFaster, InputActionIds.BookSpeakSlower, InputActionIds.BookLookUp];
        foreach (string id in ids)
        {
            Assert.Equal(InputActions.BookReaderGroup, InputActions.Core.Single(a => a.Id == id).Group);
        }

        var all = InputActions.Core.Where(a => a.Scope == InputScope.BookReader).SelectMany(a => a.Defaults).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }
}
