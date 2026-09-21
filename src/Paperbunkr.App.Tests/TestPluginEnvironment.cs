using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Minimal real <see cref="IPluginEnvironment"/> shared across App.Tests plugin-related fixtures
/// (originally written for <c>PluginPackageServiceTests</c>, extracted when <c>SmartScreenViewModelTests</c>
/// needed the same shape rather than a second copy). Only <see cref="IApplication"/> has real
/// behavior (<see cref="TestPluginApplication.Library"/> is settable) - every other sub-interface
/// is a bare no-op stand-in, since no test using this exercises them.
/// </summary>
internal sealed class TestPluginEnvironment : IPluginEnvironment
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
    /// <summary>What every command clone reported through <see cref="Activity"/> - shared by reference across <see cref="Clone"/>.</summary>
    public RecordingPluginActivity RecordedActivity { get; } = new();

    public IPluginActivity Activity => RecordedActivity;

    public IPluginLogger Log { get; } = new NullPluginLogger();
    public string CommandPath { get; set; } = string.Empty;
    public string PluginKey { get; set; } = string.Empty;
    public IEnumerable<string> LibraryPaths { get; } = Array.Empty<string>();

    public TestPluginEnvironment(IApplication? app = null) => App = app ?? new TestPluginApplication();

    public string? GetSetting(string key) => null;
    public void SetSetting(string key, string value) { }
    public string Localize(string resourceKey, string elementKey, string text) => text;
    public object Clone() => MemberwiseClone();

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
        public bool SetFormat(Issue issue, string? value) => true;
        public bool SetBookAge(Issue issue, string? value) => true;
        public bool SetCustomValue(Issue issue, string name, string? value) => true;
        public bool AddTag(Issue issue, string tag) => true;
        public bool RemoveTag(Issue issue, string tag) => true;
    }

    private sealed class FakeHostWindow : IPluginHostWindow
    {
        public object Owner { get; } = new object();
    }

    private sealed class FakeOpenBooksManager : IOpenBooksManager
    {
        public bool Open(Issue issue, int page) => true;
        public bool OpenFile(string file, int page) => true;
        public bool IsOpen(Issue issue) => false;
    }

    private sealed class FakeBrowser : IBrowser
    {
        public bool OpenNextComic() => true;
        public bool OpenPrevComic() => true;
        public bool OpenRandomComic() => true;
        public void SelectComics(IEnumerable<Issue> books) { }
    }

    private sealed class FakeComicDisplay : IComicDisplay
    {
        public Issue? CurrentBook => null;
        public int CurrentPageIndex => 0;
        public int PageCount => 0;
        public event Action<int>? CurrentPageIndexChanged;
        public void NextPage() { }
        public void PreviousPage() { }
        public void GoToPage(int index) { }
    }

    private sealed class FakeThemePlugin : IThemePlugin
    {
        public string CurrentThemeKey => "default";
    }

}

internal sealed class NullPluginLogger : IPluginLogger
{
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>Records every alert a plugin raised and every job title it started; job handles are inert. Thread-safe - domain hooks run in the background.</summary>
internal sealed class RecordingPluginActivity : IPluginActivity
{
    private readonly object _gate = new();
    private readonly List<(PluginAlertSeverity Severity, string Title, string? Detail, string? DedupeKey)> _alerts = new();
    private readonly List<string> _jobs = new();

    public IReadOnlyList<(PluginAlertSeverity Severity, string Title, string? Detail, string? DedupeKey)> Alerts
    {
        get
        {
            lock (_gate)
            {
                return _alerts.ToList();
            }
        }
    }

    public IReadOnlyList<string> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.ToList();
            }
        }
    }

    public IPluginActivityHandle StartJob(string title, bool cancellable = true)
    {
        lock (_gate)
        {
            _jobs.Add(title);
        }

        return new InertHandle();
    }

    public void RaiseAlert(PluginAlertSeverity severity, string title, string? detail = null, string? dedupeKey = null)
    {
        lock (_gate)
        {
            _alerts.Add((severity, title, detail, dedupeKey));
        }
    }

    private sealed class InertHandle : IPluginActivityHandle
    {
        public System.Threading.CancellationToken CancellationToken => default;
        public void Report(int done, int total, string? detail = null) { }
        public void Report(string detail) { }
        public void Succeed(string summary) { }
        public void Fail(string summary) { }
        public void Dispose() { }
    }
}

/// <summary>The one sub-interface fake with real, settable behavior - a plain in-memory library list.</summary>
internal sealed class TestPluginApplication : IApplication
{
    public string ProductVersion => "0.0.0-test";
    public void Restart() { }
    public void ScanFolders() { }
    public List<Issue> Library { get; set; } = new();
    public IEnumerable<Issue> GetLibraryBooks() => Library;
    public Issue? GetBook(int issueId) => Library.FirstOrDefault(i => i.Id == issueId);
    public bool RemoveBook(Issue issue) => Library.Remove(issue);
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
