using Paperbunkr.App.Plugins;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Hooks;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Installs the real, shipped <c>sample-plugins/DuplicateFinder.zip</c> (repo root, not test
/// fixture data - see that project's own doc comment) through the exact same
/// <see cref="PluginPackageService"/> path a user hits from the Plugins screen's "Install
/// Package…" button, then discovers and invokes it via a real <see cref="PluginEngine"/>. This is
/// deliberately end-to-end through the *packaging* layer -
/// <c>Paperbunkr.Plugins.Tests.DuplicateFinderPluginTests</c> already covers the hook logic itself
/// against the loose source files, but never exercises <see cref="PluginPackageService.Install"/>
/// or <c>PackageManager.UnzipFile</c> at all. Proves the thing a real user would actually download
/// and install is not broken, not just its source.
/// </summary>
public sealed class PluginPackageServiceTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _stagingDirectory;
    private readonly string _zipPath;

    public PluginPackageServiceTests()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_plugin_package_test_{Guid.NewGuid():N}");
        _rootDirectory = Path.Combine(testRoot, "plugins");
        _stagingDirectory = Path.Combine(testRoot, "plugin-staging");
        _zipPath = Path.Combine(AppContext.BaseDirectory, "SamplePlugins", "DuplicateFinder.zip");
    }

    public void Dispose()
    {
        try
        {
            string testRoot = Path.GetDirectoryName(_rootDirectory)!;
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_shipped_zip_exists_and_is_flat()
    {
        // Package.UnzipFile flattens every entry to the zip root's file name (PluginPackageService's
        // own doc comment) - a zip with a subfolder would silently produce a broken/nested install.
        Assert.True(File.Exists(_zipPath), $"Expected the built sample-plugins/DuplicateFinder.zip at {_zipPath} - did the csproj copy rule run?");

        using var archive = System.IO.Compression.ZipFile.OpenRead(_zipPath);
        Assert.All(archive.Entries, e => Assert.DoesNotContain('/', e.FullName));
        Assert.Contains(archive.Entries, e => e.Name == "plugin.xml");
    }

    [Fact]
    public void Install_unpacks_the_package_and_PluginEngine_discovers_all_three_commands_cleanly()
    {
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);

        bool installed = service.Install(_zipPath);

        Assert.True(installed);
        var package = Assert.Single(service.GetPackages());
        Assert.Equal("Duplicate Finder", package.Name);
        Assert.True(package.Installed);

        var engine = new PluginEngine();
        engine.Discover(_rootDirectory, new FakeEnvironment());

        Assert.Equal(3, engine.AllCommands.Count);
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken));
        Assert.Contains(engine.AllCommands, c => c.Hook == PluginHooks.Startup);
        Assert.Contains(engine.AllCommands, c => c.Hook == PluginHooks.Library);
        Assert.Contains(engine.AllCommands, c => c.Hook == PluginHooks.CreateBookList);
    }

    [Fact]
    public async Task The_installed_package_finds_a_real_duplicate_via_the_Library_hook()
    {
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(_zipPath);

        var app = new FakeEnvironment.FakeApplication
        {
            Library = new List<Issue>
            {
                new() { Id = 1, SeriesId = 10, Number = "1" },
                new() { Id = 2, SeriesId = 10, Number = "1" }, // duplicate of #1
            },
        };
        var engine = new PluginEngine();
        engine.Discover(_rootDirectory, new FakeEnvironment(app));

        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = new List<Issue> { app.Library[0] } });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var duplicates = Assert.IsAssignableFrom<List<string>>(result.ReturnValue);
        Assert.Single(duplicates);
    }

    [Fact]
    public void Uninstall_removes_the_package()
    {
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(_zipPath);
        var package = Assert.Single(service.GetPackages());

        service.Uninstall(package);

        Assert.Empty(service.GetPackages());
    }

    /// <summary>Minimal real <see cref="IPluginEnvironment"/> - only <see cref="IApplication"/> is exercised by Duplicate Finder's scripts, so every other sub-interface is a bare no-op stand-in.</summary>
    private sealed class FakeEnvironment : IPluginEnvironment
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

        public FakeEnvironment(IApplication? app = null) => App = app ?? new FakeApplication();

        public string? GetSetting(string key) => null;
        public void SetSetting(string key, string value) { }
        public string Localize(string resourceKey, string elementKey, string text) => text;
        public object Clone() => MemberwiseClone();

        public sealed class FakeApplication : IApplication
        {
            public string ProductVersion => "0.0.0-test";
            public void Restart() { }
            public void ScanFolders() { }
            public List<Issue> Library { get; set; } = new();
            public IEnumerable<Issue> GetLibraryBooks() => Library;
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
            public string CurrentSkinKey => "default";
        }
    }
}
