using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.Plugins.Tests;

internal sealed class FakePluginEnvironment : IPluginEnvironment
{
    public IPluginHostWindow MainWindow { get; } = new FakeHostWindow();
    public IApplication App { get; }
    public IOpenBooksManager OpenBooks { get; } = new FakeOpenBooksManager();
    public IBrowser Browser { get; } = new FakeBrowser();
    public IComicDisplay ComicDisplay { get; } = new FakeComicDisplay();
    public IMetadataGraph Metadata { get; } = new FakeMetadataGraph();
    public IRulesEngine Rules { get; } = new FakeRulesEngine();
    public IMetadataWriter Writer { get; } = new FakeMetadataWriter();
    public IThemePlugin ThemePlugin { get; } = new FakeThemePlugin();
    public string CommandPath { get; set; } = string.Empty;
    public string PluginKey { get; set; } = string.Empty;
    public IEnumerable<string> LibraryPaths { get; } = Array.Empty<string>();

    private readonly Dictionary<string, string> _settings = new();

    public FakePluginEnvironment(IApplication? app = null)
    {
        App = app ?? new FakeApplication();
    }

    public string? GetSetting(string key) => _settings.TryGetValue(key, out var v) ? v : null;

    public void SetSetting(string key, string value) => _settings[key] = value;

    public string Localize(string resourceKey, string elementKey, string text) => text;

    public object Clone() => MemberwiseClone();

    private sealed class FakeMetadataGraph : IMetadataGraph
    {
        public IReadOnlyList<Data.Entities.MediaRelation> GetRelations(Data.Entities.Series series) => Array.Empty<Data.Entities.MediaRelation>();
        public IReadOnlyList<Data.Entities.Series> GetRelatedSeries(Data.Entities.Series series) => Array.Empty<Data.Entities.Series>();
        public IReadOnlyList<Data.Entities.Continuity> GetContinuities(Data.Entities.Series series) => Array.Empty<Data.Entities.Continuity>();
        public IReadOnlyList<Data.Entities.Series> GetOtherSeriesInContinuity(Data.Entities.Continuity continuity) => Array.Empty<Data.Entities.Series>();
        public IReadOnlyList<Data.Entities.StoryEvent> GetEvents(Data.Entities.Issue issue) => Array.Empty<Data.Entities.StoryEvent>();
        public IReadOnlyList<Data.Entities.EventMembership> GetMemberships(Data.Entities.StoryEvent storyEvent) => Array.Empty<Data.Entities.EventMembership>();
        public IReadOnlyList<Data.Entities.EventRelation> GetEventRelations(Data.Entities.StoryEvent storyEvent) => Array.Empty<Data.Entities.EventRelation>();
        public (Data.Metadata.ComicAge? Age, decimal Confidence, string? Reason) GetAge(Data.Entities.Issue issue) => (null, 0m, null);
        public IReadOnlyList<Data.Entities.Series> GetSeriesFamily(Data.Entities.Series series) => Array.Empty<Data.Entities.Series>();
    }

    private sealed class FakeRulesEngine : IRulesEngine
    {
        public IReadOnlyList<Data.Entities.Issue> Evaluate(PluginConditionGroup rule) => Array.Empty<Data.Entities.Issue>();
        public IReadOnlyList<Data.Entities.Issue> EvaluateSmartList(int smartListId) => Array.Empty<Data.Entities.Issue>();
    }

    public sealed class FakeMetadataWriter : IMetadataWriter
    {
        public List<string> Writes { get; } = new();
        public bool SetFormat(Data.Entities.Issue issue, string? value) { Writes.Add($"Format={value}"); return true; }
        public bool SetBookAge(Data.Entities.Issue issue, string? value) { Writes.Add($"BookAge={value}"); return true; }
        public bool SetCustomValue(Data.Entities.Issue issue, string name, string? value) { Writes.Add($"{name}={value}"); return true; }
        public bool AddTag(Data.Entities.Issue issue, string tag) { Writes.Add($"+{tag}"); return true; }
        public bool RemoveTag(Data.Entities.Issue issue, string tag) { Writes.Add($"-{tag}"); return true; }
    }

    private sealed class FakeHostWindow : IPluginHostWindow
    {
        public object Owner { get; } = new object();
    }

    private sealed class FakeOpenBooksManager : IOpenBooksManager
    {
        public bool Open(Data.Entities.Issue issue, int page) => true;
        public bool OpenFile(string file, int page) => true;
        public bool IsOpen(Data.Entities.Issue issue) => false;
    }

    private sealed class FakeBrowser : IBrowser
    {
        public bool OpenNextComic() => true;
        public bool OpenPrevComic() => true;
        public bool OpenRandomComic() => true;
        public void SelectComics(IEnumerable<Data.Entities.Issue> books) { }
    }

    private sealed class FakeComicDisplay : IComicDisplay
    {
        public Data.Entities.Issue? CurrentBook => null;
        public int CurrentPageIndex => 0;
        public int PageCount => 0;
        public event Action<int>? CurrentPageIndexChanged;
        public void NextPage() { }
        public void PreviousPage() { }
        public void GoToPage(int index) { }
    }

    private sealed class FakeThemePlugin : IThemePlugin
    {
        public string CurrentSkinKey => "default";
    }

    public sealed class FakeApplication : IApplication
    {
        public string ProductVersion => "0.0.0-test";
        public void Restart() { }
        public void ScanFolders() { }
        public List<Data.Entities.Issue> Library { get; set; } = new();
        public IEnumerable<Data.Entities.Issue> GetLibraryBooks() => Library;
        public Data.Entities.Issue? GetBook(int issueId) => null;
        public bool RemoveBook(Data.Entities.Issue issue) => false;
        public bool SetCustomBookThumbnail(Data.Entities.Issue issue, byte[] imageBytes) => false;
        public byte[]? GetComicPage(Data.Entities.Issue issue, int page) => null;
        public byte[]? GetComicThumbnail(Data.Entities.Issue issue) => null;
        public Task<string?> ReadInternetAsync(string url) => Task.FromResult<string?>(null);
        public List<(string Question, string ButtonText, string OptionText)> AskedQuestions { get; } = new();
        public int AskQuestion(string question, string buttonText, string optionText)
        {
            AskedQuestions.Add((question, buttonText, optionText));
            return 0;
        }
        public void ShowComicInfo(IEnumerable<Data.Entities.Issue> books) { }
    }
}
