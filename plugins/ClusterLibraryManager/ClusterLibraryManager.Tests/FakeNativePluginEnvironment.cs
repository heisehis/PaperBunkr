using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Abstractions.Ui;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace ClusterLibraryManager.Tests;

/// <summary>Minimal test double for <see cref="INativePluginUiEnvironment"/> - this plugin's own test
/// suite has no access to <c>Paperbunkr.Plugins.Tests</c>' internal fake, so it needs its own. Every
/// member throws/returns a trivial default unless a specific test overrides it via the settable
/// properties below.</summary>
internal sealed class FakeNativePluginEnvironment : INativePluginUiEnvironment
{
    public IPluginHostWindow MainWindow { get; set; } = new FakeHostWindow();
    public IApplication App { get; set; } = new FakeApplication();
    public IOpenBooksManager OpenBooks { get; set; } = new FakeOpenBooksManager();
    public IBrowser Browser { get; set; } = new FakeBrowser();
    public IComicDisplay ComicDisplay { get; set; } = new FakeComicDisplay();
    public IMetadataGraph Metadata { get; set; } = new FakeMetadataGraph();
    public IRulesEngine Rules { get; set; } = new FakeRulesEngine();
    public IMetadataWriter Writer { get; set; } = new FakeMetadataWriter();
    public IThemePlugin ThemePlugin { get; set; } = new FakeThemePlugin();
    public string CommandPath { get; set; } = string.Empty;
    public string PluginKey { get; set; } = string.Empty;
    public IEnumerable<string> LibraryPaths { get; set; } = Array.Empty<string>();

    public Func<PaperbunkrDbContext> CreateDbContext { get; set; } =
        () => throw new NotSupportedException("No test in this pass needs a real PaperbunkrDbContext.");

    public Func<ActivityJobKind, string, bool, IPluginActivityHandle> StartActivityJob { get; set; } =
        (_, _, _) => throw new NotSupportedException("No test in this pass needs a real activity job.");

    private readonly Dictionary<string, string> _settings = new();

    public string? GetSetting(string key) => _settings.TryGetValue(key, out var v) ? v : null;

    public void SetSetting(string key, string value) => _settings[key] = value;

    public string Localize(string resourceKey, string elementKey, string text) => text;

    public Task<TResult> ShowModalAsync<TResult>(Func<Action<TResult>, Avalonia.Controls.Control> contentFactory) =>
        throw new NotSupportedException("No test in this pass shows a real modal.");

    public object Clone() => new FakeNativePluginEnvironment
    {
        MainWindow = MainWindow,
        App = App,
        OpenBooks = OpenBooks,
        Browser = Browser,
        ComicDisplay = ComicDisplay,
        Metadata = Metadata,
        Rules = Rules,
        Writer = Writer,
        ThemePlugin = ThemePlugin,
        CommandPath = CommandPath,
        PluginKey = PluginKey,
        LibraryPaths = LibraryPaths,
        CreateDbContext = CreateDbContext,
        StartActivityJob = StartActivityJob,
    };

    private sealed class FakeHostWindow : IPluginHostWindow
    {
        public object Owner { get; } = new();
    }

    private sealed class FakeApplication : IApplication
    {
        public string ProductVersion => "test";
        public void Restart() { }
        public void ScanFolders() { }
        public IEnumerable<Issue> GetLibraryBooks() => Array.Empty<Issue>();
        public Issue? GetBook(int issueId) => null;
        public bool RemoveBook(Issue issue) => false;
        public bool SetCustomBookThumbnail(Issue issue, byte[] imageBytes) => false;
        public byte[]? GetComicPage(Issue issue, int page) => null;
        public byte[]? GetComicThumbnail(Issue issue) => null;
        public Task<string?> ReadInternetAsync(string url) => Task.FromResult<string?>(null);
        public int AskQuestion(string question, string buttonText, string optionText) => 0;
        public void ShowComicInfo(IEnumerable<Issue> books) { }
        public int GetOrCreateSeriesId(string seriesName) => 0;
        public Issue? AddNewBook(int seriesId, bool showDialog) => null;
        public byte[]? GetComicPublisherIcon(Issue issue) => null;
        public byte[]? GetComicImprintIcon(Issue issue) => null;
        public byte[]? GetComicAgeRatingIcon(Issue issue) => null;
        public byte[]? GetComicFormatIcon(Issue issue) => null;
        public IDictionary<string, string> GetComicFields() => new Dictionary<string, string>();
    }

    private sealed class FakeOpenBooksManager : IOpenBooksManager
    {
        public bool Open(Issue issue, int page) => false;
        public bool OpenFile(string file, int page) => false;
        public bool IsOpen(Issue issue) => false;
    }

    private sealed class FakeBrowser : IBrowser
    {
        public bool OpenNextComic() => false;
        public bool OpenPrevComic() => false;
        public bool OpenRandomComic() => false;
        public void SelectComics(IEnumerable<Issue> books) { }
    }

    private sealed class FakeComicDisplay : IComicDisplay
    {
        public Issue? CurrentBook => null;
        public int CurrentPageIndex => 0;
        public int PageCount => 0;
        public event Action<int>? CurrentPageIndexChanged { add { } remove { } }
        public void NextPage() { }
        public void PreviousPage() { }
        public void GoToPage(int index) { }
    }

    private sealed class FakeMetadataGraph : IMetadataGraph
    {
        public IReadOnlyList<MediaRelation> GetRelations(Series series) => Array.Empty<MediaRelation>();
        public IReadOnlyList<Series> GetRelatedSeries(Series series) => Array.Empty<Series>();
        public IReadOnlyList<Collection> GetRelatedCollections(Series series) => Array.Empty<Collection>();
        public IReadOnlyList<MediaRelation> GetRelations(Collection collection) => Array.Empty<MediaRelation>();
        public IReadOnlyList<Series> GetRelatedSeries(Collection collection) => Array.Empty<Series>();
        public IReadOnlyList<Continuity> GetContinuities(Series series) => Array.Empty<Continuity>();
        public IReadOnlyList<Series> GetOtherSeriesInContinuity(Continuity continuity) => Array.Empty<Series>();
        public IReadOnlyList<StoryEvent> GetEvents(Issue issue) => Array.Empty<StoryEvent>();
        public IReadOnlyList<EventMembership> GetMemberships(StoryEvent storyEvent) => Array.Empty<EventMembership>();
        public IReadOnlyList<EventRelation> GetEventRelations(StoryEvent storyEvent) => Array.Empty<EventRelation>();
        public (Paperbunkr.Data.Metadata.ComicAge? Age, decimal Confidence, string? Reason) GetAge(Issue issue) => (null, 0m, null);
        public IReadOnlyList<Series> GetSeriesFamily(Series series) => Array.Empty<Series>();
    }

    private sealed class FakeRulesEngine : IRulesEngine
    {
        public IReadOnlyList<Issue> Evaluate(PluginConditionGroup rule) => Array.Empty<Issue>();
        public IReadOnlyList<Issue> EvaluateSmartList(int smartListId) => Array.Empty<Issue>();
    }

    private sealed class FakeMetadataWriter : IMetadataWriter
    {
        public bool SetFormat(Issue issue, string? value) => false;
        public bool SetBookAge(Issue issue, string? value) => false;
        public bool SetCustomValue(Issue issue, string name, string? value) => false;
        public bool AddTag(Issue issue, string tag) => false;
        public bool RemoveTag(Issue issue, string tag) => false;
    }

    private sealed class FakeThemePlugin : IThemePlugin
    {
        public string CurrentSkinKey => "default";
    }
}
