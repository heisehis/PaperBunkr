using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.SmartLists;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Hooks;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Plugin API v3 (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §8):
/// <see cref="IMetadataGraph"/> read parity, the <c>GetLibraryBooks</c> include fix,
/// <see cref="IRulesEngine"/> equivalence with the real Smart List matcher, the audited
/// <see cref="IMetadataWriter"/> + its <c>confirmWrites</c> gate, per-plugin
/// <c>PluginSettingState</c> scoping, and an end-to-end Data-Manager-shaped fixture plugin.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class PluginApiV3Tests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public PluginApiV3Tests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginv3_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }

    private static int AddSeries(string name)
    {
        using var context = PaperbunkrDb.CreateContext();
        var s = new Series { Name = name };
        context.Series.Add(s);
        context.SaveChanges();
        return s.Id;
    }

    private static int AddIssue(int seriesId, string number, string? publisher = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var i = new Issue { SeriesId = seriesId, Number = number, Publisher = publisher };
        context.Issues.Add(i);
        context.SaveChanges();
        return i.Id;
    }

    // --- §2 IMetadataGraph ---

    [Fact]
    public void MetadataGraph_MirrorsWhatTheResolversReturn_ForPhase4Fixtures()
    {
        int a = AddSeries("Alpha");
        int b = AddSeries("Beta");
        int issueA = AddIssue(a, "1");

        using (var context = PaperbunkrDb.CreateContext())
        {
            MediaRelationResolver.TryCreate(context, a, b, RelationType.Sequel);
            var continuity = ContinuityResolver.GetOrCreate(context, "Prime");
            ContinuityResolver.AddSeriesToContinuity(context, a, continuity.Id);
            ContinuityResolver.AddSeriesToContinuity(context, b, continuity.Id);

            var storyEvent = new StoryEvent { Name = "The Big Event" };
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();
            EventMembershipResolver.AddMember(context, storyEvent.Id, issueA, EventMembershipRole.TieIn);
        }

        var graph = new PaperbunkrMetadataGraph();
        var seriesA = new Series { Id = a };

        Assert.Single(graph.GetRelations(seriesA));
        Assert.Contains(graph.GetRelatedSeries(seriesA), s => s.Id == b);
        Assert.Empty(graph.GetRelatedCollections(seriesA)); // no Collection-sided edge yet

        var continuities = graph.GetContinuities(seriesA);
        var prime = Assert.Single(continuities);
        Assert.Equal("Prime", prime.Name);
        Assert.Equal(2, graph.GetOtherSeriesInContinuity(prime).Count);

        var events = graph.GetEvents(new Issue { Id = issueA });
        var evt = Assert.Single(events);
        Assert.Equal("The Big Event", evt.Name);
        Assert.Single(graph.GetMemberships(evt));

        Assert.Equal(2, graph.GetSeriesFamily(seriesA).Count); // Alpha + Beta, connected by the relation/continuity
    }

    // --- Collection nodes (docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-
    // design.md) - the 3 new IMetadataGraph overloads ---

    [Fact]
    public void MetadataGraph_GetRelatedCollections_AndCollectionRootedOverloads_MirrorTheResolver()
    {
        int a = AddSeries("Alpha");
        int collectionId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var collection = new Collection { Name = "Omnibus" };
            context.Collections.Add(collection);
            context.SaveChanges();
            collectionId = collection.Id;

            MediaRelationResolver.TryCreate(context, MediaRelationEndpointKind.Series, a, MediaRelationEndpointKind.Collection, collectionId, RelationType.Crossover);
        }

        var graph = new PaperbunkrMetadataGraph();
        var seriesA = new Series { Id = a };
        var omnibus = new Collection { Id = collectionId };

        Assert.Contains(graph.GetRelatedCollections(seriesA), c => c.Id == collectionId);
        Assert.Single(graph.GetRelations(omnibus));
        Assert.Contains(graph.GetRelatedSeries(omnibus), s => s.Id == a);
    }

    // --- §3 GetLibraryBooks/GetBook includes ---

    [Fact]
    public void GetLibraryBooks_AndGetBook_PopulateTagsCustomValuesProposalsBookmarks()
    {
        int s = AddSeries("Tagged");
        int issueId = AddIssue(s, "1");

        using (var context = PaperbunkrDb.CreateContext())
        {
            var issue = context.Issues.Include(i => i.Tags).First(i => i.Id == issueId);
            issue.MergeFrom(IssueTagField.Tags, new[] { "keeper" });
            issue.CustomValues.Add(new IssueCustomValue { IssueId = issueId, Name = "Shelf", Value = "A3" });
            issue.Bookmarks.Add(new IssueBookmark { IssueId = issueId, PageNumber = 4, Label = "cliffhanger" });
            issue.MetadataProposals.Add(new MetadataProposal { IssueId = issueId, Field = MetadataProposalField.Title, ProposedValue = "Real Title", Source = MetadataProposalSource.FilenameParser, Status = MetadataProposalStatus.Pending });
            context.SaveChanges();
        }

        var app = new PaperbunkrApplication(new MainViewModel());

        var fromList = Assert.Single(app.GetLibraryBooks().Where(i => i.Id == issueId));
        Assert.NotEmpty(fromList.Tags);
        Assert.NotEmpty(fromList.CustomValues);
        Assert.NotEmpty(fromList.Bookmarks);
        Assert.NotEmpty(fromList.MetadataProposals);

        var fromGet = app.GetBook(issueId)!;
        Assert.NotEmpty(fromGet.Tags);
        Assert.NotEmpty(fromGet.CustomValues);
        Assert.NotEmpty(fromGet.Bookmarks);
        Assert.NotEmpty(fromGet.MetadataProposals);
    }

    // --- §4 IRulesEngine ---

    [Fact]
    public void RulesEngine_Evaluate_ReturnsTheSameIssueSetAsSmartListQueryBuilder()
    {
        int s = AddSeries("Rules");
        AddIssue(s, "1", "Acme");
        AddIssue(s, "2", "Zenith");
        AddIssue(s, "3", "Acme");

        var rule = PluginConditionGroup.And(new PluginCondition(SmartListField.Publisher, SmartListOperator.Is, "Acme"));

        var viaEngine = new PaperbunkrRulesEngine().Evaluate(rule).Select(i => i.Id).OrderBy(x => x).ToList();

        using var context = PaperbunkrDb.CreateContext();
        var transient = new SmartList
        {
            RootGroup = new SmartListConditionGroup
            {
                Conditions = { new SmartListCondition { Field = SmartListField.Publisher, Operator = SmartListOperator.Is, Value = "Acme" } },
            },
        };
        var viaBuilder = SmartListQueryBuilder.Build(context, transient).Select(i => i.Id).OrderBy(x => x).ToList();

        Assert.Equal(viaBuilder, viaEngine);
        Assert.Equal(2, viaEngine.Count);
    }

    [Fact]
    public void RulesEngine_EvaluateSmartList_MatchesTheSavedListsOwnBuilderResult()
    {
        int s = AddSeries("Saved");
        AddIssue(s, "1", "Acme");
        AddIssue(s, "2", "Other");

        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = new SmartList
            {
                Name = "Acme only",
                RootGroup = new SmartListConditionGroup
                {
                    Conditions = { new SmartListCondition { Field = SmartListField.Publisher, Operator = SmartListOperator.Is, Value = "Acme" } },
                },
            };
            context.SmartLists.Add(list);
            context.SaveChanges();
            listId = list.Id;
        }

        var viaEngine = new PaperbunkrRulesEngine().EvaluateSmartList(listId).Select(i => i.Id).OrderBy(x => x).ToList();

        using (var context = PaperbunkrDb.CreateContext())
        {
            var loaded = SmartListTreeLoader.LoadWithTree(context, listId)!;
            var viaBuilder = SmartListQueryBuilder.Build(context, loaded).Select(i => i.Id).OrderBy(x => x).ToList();
            Assert.Equal(viaBuilder, viaEngine);
        }

        Assert.Single(viaEngine);
        Assert.Empty(new PaperbunkrRulesEngine().EvaluateSmartList(999999)); // unknown id -> empty, not a throw
    }

    // --- §5 IMetadataWriter ---

    [Fact]
    public void MetadataWriter_HappyPath_PersistsAndIsVisibleToGetBook()
    {
        int s = AddSeries("Writable");
        int issueId = AddIssue(s, "1");
        var writer = new PaperbunkrMetadataWriter();
        var stale = new Issue { Id = issueId };

        Assert.True(writer.SetFormat(stale, "CBZ"));
        Assert.True(writer.SetBookAge(stale, "Modern"));
        Assert.True(writer.SetCustomValue(stale, "Shelf", "B7"));
        Assert.True(writer.AddTag(stale, "curated"));

        var book = new PaperbunkrApplication(new MainViewModel()).GetBook(issueId)!;
        Assert.Equal("CBZ", book.Format);
        Assert.Equal("Modern", book.BookAge);
        Assert.Contains(book.CustomValues, cv => cv is { Name: "Shelf", Value: "B7" });
        Assert.Contains(book.Tags, t => t.Field == IssueTagField.Tags && t.Value == "curated");

        Assert.True(writer.RemoveTag(stale, "curated"));
        Assert.DoesNotContain(new PaperbunkrApplication(new MainViewModel()).GetBook(issueId)!.Tags, t => t.Value == "curated");
    }

    [Fact]
    public void MetadataWriter_ReturnsFalse_ForAMissingIssue_NeverThrows()
    {
        var writer = new PaperbunkrMetadataWriter();
        Assert.False(writer.SetFormat(new Issue { Id = 987654 }, "CBZ"));
        Assert.False(writer.AddTag(new Issue { Id = 987654 }, "x"));
    }

    [Fact]
    public async Task ConfirmWrites_Gate_BlocksTheWrite_UntilAskQuestionIsAnsweredAffirmatively()
    {
        int s = AddSeries("Gated");
        int issueId = AddIssue(s, "1");

        var writer = new PaperbunkrMetadataWriter();

        // A confirmWrites command that never asks: writes fail closed.
        await RunInInvocation(pluginKey: "gate", confirmWrites: true, () =>
        {
            Assert.False(writer.AddTag(new Issue { Id = issueId }, "blocked"));
            return Task.CompletedTask;
        });
        Assert.DoesNotContain(new PaperbunkrApplication(new MainViewModel()).GetBook(issueId)!.Tags, t => t.Value == "blocked");

        // Same command after an affirmative AskQuestion (answer index 0): the write goes through.
        await RunInInvocation(pluginKey: "gate", confirmWrites: true, () =>
        {
            FakeConfirmingApp.Answer(0);
            new FakeConfirmingApp().AskQuestion("Apply changes?", "Yes", "No"); // flips the gate open
            Assert.True(writer.AddTag(new Issue { Id = issueId }, "allowed"));
            return Task.CompletedTask;
        });
        Assert.Contains(new PaperbunkrApplication(new MainViewModel()).GetBook(issueId)!.Tags, t => t.Value == "allowed");

        // A command that doesn't declare confirmWrites writes freely.
        await RunInInvocation(pluginKey: "free", confirmWrites: false, () =>
        {
            Assert.True(writer.AddTag(new Issue { Id = issueId }, "free"));
            return Task.CompletedTask;
        });
    }

    // --- §6 PluginSettingState ---

    [Fact]
    public void PluginSettings_AreScopedPerPluginKey_TwoPluginsSameKeyDontCollide()
    {
        var envA = MakeEnvironment(pluginKey: "plugin-a");
        var envB = MakeEnvironment(pluginKey: "plugin-b");

        envA.SetSetting("theme", "dark");
        envB.SetSetting("theme", "light");

        Assert.Equal("dark", envA.GetSetting("theme"));
        Assert.Equal("light", envB.GetSetting("theme"));
        Assert.Null(envA.GetSetting("missing"));

        // Overwrite is an upsert, not a duplicate row.
        envA.SetSetting("theme", "sepia");
        Assert.Equal("sepia", envA.GetSetting("theme"));
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(1, context.PluginSettingStates.Count(x => x.PluginKey == "plugin-a" && x.Key == "theme"));
    }

    // --- §8 end-to-end fixture plugin ---

    [Fact]
    public async Task DataManagerShapedPlugin_Startup_ReadsGraph_LibraryCommand_EvaluatesSmartListThenTagsMatches_GatedByConfirm()
    {
        int seriesId = AddSeries("Warhammer 40k");
        int m1 = AddIssue(seriesId, "1", "Black Library");
        int m2 = AddIssue(seriesId, "2", "Black Library");
        AddIssue(seriesId, "3", "Other Press");

        int smartListId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = new SmartList
            {
                Name = "Black Library",
                RootGroup = new SmartListConditionGroup
                {
                    Conditions = { new SmartListCondition { Field = SmartListField.Publisher, Operator = SmartListOperator.Is, Value = "Black Library" } },
                },
            };
            context.SmartLists.Add(list);
            context.SaveChanges();
            smartListId = list.Id;
        }

        string pluginDir = Path.Combine(Path.GetTempPath(), "pb-dm-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginDir, "plugin.xml"), $$"""
                <Plugin key="data-manager" name="Data Manager">
                  <Command hook="Startup" key="data-manager.startup" name="Warm up" script="startup.csx" />
                  <Command hook="Library" key="data-manager.tag-list" name="Tag Smart List" confirmWrites="true" script="tag.csx" />
                </Plugin>
                """);
            File.WriteAllText(Path.Combine(pluginDir, "startup.csx"),
                $"return Environment.Metadata.GetSeriesFamily(new Series {{ Id = {seriesId} }}).Count;");
            File.WriteAllText(Path.Combine(pluginDir, "tag.csx"), $$"""
                var matches = Environment.Rules.EvaluateSmartList({{smartListId}});
                Environment.App.AskQuestion("Tag " + matches.Count + " books?", "Yes", "No");
                int tagged = 0;
                foreach (var book in matches)
                {
                    if (Environment.Writer.AddTag(book, "data-manager")) tagged++;
                }
                return tagged;
                """);

            var engine = new PluginEngine();

            // 1. confirm gate blocks: AskQuestion answers "No" (index 1).
            FakeConfirmingApp.Answer(1);
            engine.Discover(pluginDir, MakeEnvironment(pluginKey: "data-manager"));
            Assert.All(engine.AllCommands, c => Assert.Null(c.CompileError));
            var blocked = await engine.InvokeAsync(PluginHooks.Library,
                env => new BooksHookGlobals { Environment = env, Books = new List<Issue> { new() { Id = m1, SeriesId = seriesId } } });
            Assert.Equal(0, Assert.Single(blocked).ReturnValue); // nothing tagged
            Assert.DoesNotContain(new PaperbunkrApplication(new MainViewModel()).GetBook(m1)!.Tags, t => t.Value == "data-manager");

            // 2. affirmative answer (index 0): both Black Library issues get tagged, the third doesn't.
            FakeConfirmingApp.Answer(0);
            var ran = await engine.InvokeAsync(PluginHooks.Library,
                env => new BooksHookGlobals { Environment = env, Books = new List<Issue> { new() { Id = m1, SeriesId = seriesId } } });
            Assert.Equal(2, Assert.Single(ran).ReturnValue);

            var app = new PaperbunkrApplication(new MainViewModel());
            Assert.Contains(app.GetBook(m1)!.Tags, t => t.Value == "data-manager");
            Assert.Contains(app.GetBook(m2)!.Tags, t => t.Value == "data-manager");

            // 3. Startup command reads the graph without error.
            var startup = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });
            Assert.True(Assert.Single(startup).Success);
        }
        finally
        {
            Directory.Delete(pluginDir, recursive: true);
        }
    }

    // --- helpers ---

    private static async Task RunInInvocation(string pluginKey, bool confirmWrites, Func<Task> body)
    {
        using var _ = PluginInvocationContext.Enter(pluginKey, confirmWrites);
        await body();
    }

    private static PaperbunkrPluginEnvironment MakeEnvironment(string pluginKey) => new()
    {
        MainWindow = new StubHostWindow(),
        App = new FakeConfirmingApp(),
        OpenBooks = new StubOpenBooks(),
        Browser = new StubBrowser(),
        ComicDisplay = new StubComicDisplay(),
        Metadata = new PaperbunkrMetadataGraph(),
        Rules = new PaperbunkrRulesEngine(),
        Writer = new PaperbunkrMetadataWriter(),
        ThemePlugin = new StubThemePlugin(),
        PluginKey = pluginKey,
    };

    /// <summary>Stand-in for <see cref="PaperbunkrApplication"/> that returns a scripted <see cref="AskQuestion"/> answer and applies the exact same confirm-gate side effect the real adapter does.</summary>
    private sealed class FakeConfirmingApp : IApplication
    {
        [ThreadStatic] private static int _answer;
        public static void Answer(int index) => _answer = index;

        public string ProductVersion => "test";
        public void Restart() { }
        public void ScanFolders() { }
        public IEnumerable<Issue> GetLibraryBooks() => new PaperbunkrApplication(new MainViewModel()).GetLibraryBooks();
        public Issue? GetBook(int issueId) => new PaperbunkrApplication(new MainViewModel()).GetBook(issueId);
        public bool RemoveBook(Issue issue) => false;
        public bool SetCustomBookThumbnail(Issue issue, byte[] imageBytes) => false;
        public byte[]? GetComicPage(Issue issue, int page) => null;
        public byte[]? GetComicThumbnail(Issue issue) => null;
        public Task<string?> ReadInternetAsync(string url) => Task.FromResult<string?>(null);
        public void ShowComicInfo(IEnumerable<Issue> books) { }
        public int GetOrCreateSeriesId(string seriesName) => 0;
        public Issue? AddNewBook(int seriesId, bool showDialog) => null;
        public byte[]? GetComicPublisherIcon(Issue issue) => null;
        public byte[]? GetComicImprintIcon(Issue issue) => null;
        public byte[]? GetComicAgeRatingIcon(Issue issue) => null;
        public byte[]? GetComicFormatIcon(Issue issue) => null;
        public IDictionary<string, string> GetComicFields() => new Dictionary<string, string>();

        public int AskQuestion(string question, string buttonText, string optionText)
        {
            int answer = _answer;
            if (answer == 0 && PluginInvocationContext.Current is { RequiresWriteConfirmation: true } ctx)
            {
                ctx.WritesConfirmed = true;
            }

            return answer;
        }
    }

    private sealed class StubHostWindow : IPluginHostWindow { public object Owner { get; } = new(); }
    private sealed class StubOpenBooks : IOpenBooksManager
    {
        public bool Open(Issue issue, int page) => true;
        public bool OpenFile(string file, int page) => true;
        public bool IsOpen(Issue issue) => false;
    }
    private sealed class StubBrowser : IBrowser
    {
        public bool OpenNextComic() => true;
        public bool OpenPrevComic() => true;
        public bool OpenRandomComic() => true;
        public void SelectComics(IEnumerable<Issue> books) { }
    }
    private sealed class StubComicDisplay : IComicDisplay
    {
        public Issue? CurrentBook => null;
        public int CurrentPageIndex => 0;
        public int PageCount => 0;
        public event Action<int>? CurrentPageIndexChanged { add { } remove { } }
        public void NextPage() { }
        public void PreviousPage() { }
        public void GoToPage(int index) { }
    }
    private sealed class StubThemePlugin : IThemePlugin { public string CurrentSkinKey => "default"; }
}
