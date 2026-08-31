using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// End-to-end fixture test against the "Franchise Tools" sample plugin - the Plugin API v3
/// (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md) counterpart to
/// <see cref="DuplicateFinderPluginTests"/>'s v2-era Duplicate Finder. Where Duplicate Finder only
/// ever touches <see cref="IApplication"/>, this one exercises all three v3 additions to
/// <see cref="IPluginEnvironment"/> - <see cref="IMetadataGraph"/> (read), <see cref="IMetadataWriter"/>
/// + its <c>confirmWrites</c> gate (write), and <see cref="IRulesEngine"/> (query) - via real,
/// discovered-and-compiled .csx scripts rather than the inline string-literal fixture
/// Paperbunkr.App.Tests' PluginApiV3Tests builds for its own end-to-end case.
/// </summary>
public sealed class FranchiseToolsPluginTests
{
    private static string PluginsRoot => Path.Combine(AppContext.BaseDirectory, "SamplePlugins", "FranchiseTools");

    private static PluginEngine DiscoverEngine(FakePluginEnvironment.FakeApplication? app = null, IMetadataGraph? metadata = null, IRulesEngine? rules = null)
    {
        var engine = new PluginEngine();
        engine.Discover(PluginsRoot, new FakePluginEnvironment(app, metadata, rules));
        return engine;
    }

    [Fact]
    public void All_three_commands_compile_cleanly()
    {
        var engine = DiscoverEngine();

        Assert.Equal(3, engine.AllCommands.Count);
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken));
    }

    [Fact]
    public async Task Startup_hook_runs_and_returns_its_activation_message()
    {
        var engine = DiscoverEngine();

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Contains("active", (string)result.ReturnValue!);
    }

    [Fact]
    public async Task Library_hook_tags_every_issue_across_the_whole_series_family_and_asks_first()
    {
        var app = new FakePluginEnvironment.FakeApplication
        {
            Library = new List<Issue>
            {
                new() { Id = 1, SeriesId = 10, Number = "1" }, // right-clicked book's own series
                new() { Id = 2, SeriesId = 10, Number = "2" },
                new() { Id = 3, SeriesId = 20, Number = "1" }, // in the family (sequel series)
                new() { Id = 4, SeriesId = 30, Number = "1" }, // unrelated series, not in the family
            },
        };
        var graph = new StubMetadataGraph { FamilyBySeriesId = { [10] = new List<Series> { new() { Id = 20 } } } };
        var engine = DiscoverEngine(app, metadata: graph);

        var rightClicked = new List<Issue> { app.Library[0] };
        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = rightClicked });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(3, result.ReturnValue); // issues 1, 2 (series 10) and 3 (series 20) - not 4
        Assert.Single(app.AskedQuestions);
        Assert.Contains("3 issue(s) across 2 connected series", app.AskedQuestions[0].Question);
    }

    [Fact]
    public async Task CreateBookList_hook_builds_the_expected_rule_and_returns_whatever_the_rules_engine_gives_back()
    {
        var canned = new List<Issue> { new() { Id = 7, SeriesId = 1, Number = "1" } };
        var rules = new StubRulesEngine { Result = canned };
        var engine = DiscoverEngine(rules: rules);

        var results = await engine.InvokeAsync(PluginHooks.CreateBookList, env => new CreateBookListHookGlobals { Environment = env });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var list = Assert.IsAssignableFrom<List<Issue>>(result.ReturnValue);
        Assert.Same(canned[0], Assert.Single(list));

        var rule = Assert.Single(rules.CapturedRules);
        Assert.Equal(SmartListGroupMode.And, rule.Mode);
        Assert.Equal(2, rule.Conditions.Count);
        Assert.Contains(rule.Conditions, c => c.Field == SmartListField.Rating && c.Op == SmartListOperator.GreaterThan && c.Value == "0");
        Assert.Contains(rule.Conditions, c => c.Field == SmartListField.Checked && c.Op == SmartListOperator.Is && c.Value == "false");
    }

    private sealed class StubMetadataGraph : IMetadataGraph
    {
        public Dictionary<int, IReadOnlyList<Series>> FamilyBySeriesId { get; } = new();

        public IReadOnlyList<Series> GetSeriesFamily(Series series) =>
            FamilyBySeriesId.TryGetValue(series.Id, out var family) ? family : Array.Empty<Series>();

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
        public (ComicAge? Age, decimal Confidence, string? Reason) GetAge(Issue issue) => (null, 0m, null);
    }

    private sealed class StubRulesEngine : IRulesEngine
    {
        public List<Issue> Result { get; set; } = new();
        public List<PluginConditionGroup> CapturedRules { get; } = new();

        public IReadOnlyList<Issue> Evaluate(PluginConditionGroup rule)
        {
            CapturedRules.Add(rule);
            return Result;
        }

        public IReadOnlyList<Issue> EvaluateSmartList(int smartListId) => Array.Empty<Issue>();
    }
}
