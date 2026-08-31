using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// End-to-end fixture test against the real "Duplicate Finder" sample plugin (docs/superpowers/
/// specs/2026-08-24-plugin-api-v2-design.md §7/§9) - the "one real test plugin" the Beta bar asks
/// for. Discovers and compiles it for real via <see cref="PluginEngine"/>, then exercises all
/// three of its hooks (Startup/Library/CreateBookList) against fixture data.
/// </summary>
public sealed class DuplicateFinderPluginTests
{
    // Scoped to this plugin's own subfolder (not the shared SamplePlugins root) so a sibling sample
    // plugin's commands never get counted alongside this one's fixed "3 commands" assertions.
    private static string PluginsRoot => Path.Combine(AppContext.BaseDirectory, "SamplePlugins", "DuplicateFinder");

    private static PluginEngine DiscoverEngine(FakePluginEnvironment.FakeApplication? app = null)
    {
        var engine = new PluginEngine();
        engine.Discover(PluginsRoot, new FakePluginEnvironment(app));
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
    public async Task Library_hook_flags_a_selected_issue_that_has_a_duplicate_elsewhere_in_the_library()
    {
        // Matches real usage (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5):
        // Paperbunkr's Library grid has no multi-select, so Books is always the single
        // right-clicked issue - the script compares it against the whole library, not Books itself.
        var app = new FakePluginEnvironment.FakeApplication
        {
            Library = new List<Issue>
            {
                new() { Id = 1, SeriesId = 10, Number = "1" },
                new() { Id = 2, SeriesId = 10, Number = "1" }, // duplicate of #1
                new() { Id = 3, SeriesId = 10, Number = "2" }, // unique
            },
        };
        var engine = DiscoverEngine(app);

        var rightClicked = new List<Issue> { app.Library[0] };
        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = rightClicked });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var duplicates = Assert.IsAssignableFrom<List<string>>(result.ReturnValue);
        Assert.Single(duplicates);
        Assert.Single(app.AskedQuestions);
    }

    [Fact]
    public async Task Library_hook_checks_every_book_in_a_multi_issue_selection()
    {
        // Now that Library has a real multi-selection model (docs/superpowers/specs/
        // 2026-08-24-library-multiselect-slice1-design.md), Books can carry more than one issue -
        // confirms each one is checked against the library independently, not just the first.
        var app = new FakePluginEnvironment.FakeApplication
        {
            Library = new List<Issue>
            {
                new() { Id = 1, SeriesId = 10, Number = "1" },
                new() { Id = 2, SeriesId = 10, Number = "1" }, // duplicate of #1
                new() { Id = 3, SeriesId = 20, Number = "5" },
                new() { Id = 4, SeriesId = 20, Number = "5" }, // duplicate of #3
                new() { Id = 5, SeriesId = 30, Number = "9" }, // unique, not selected
            },
        };
        var engine = DiscoverEngine(app);

        var selection = new List<Issue> { app.Library[0], app.Library[2] }; // issues #1 and #3, both have duplicates
        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = selection });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var duplicates = Assert.IsAssignableFrom<List<string>>(result.ReturnValue);
        Assert.Equal(2, duplicates.Count);
    }

    [Fact]
    public async Task Library_hook_asks_no_question_when_the_selected_issue_has_no_duplicate()
    {
        var app = new FakePluginEnvironment.FakeApplication
        {
            Library = new List<Issue>
            {
                new() { Id = 1, SeriesId = 10, Number = "1" },
                new() { Id = 2, SeriesId = 10, Number = "2" },
            },
        };
        var engine = DiscoverEngine(app);

        var rightClicked = new List<Issue> { app.Library[0] };
        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = rightClicked });

        Assert.True(results.Single().Success);
        Assert.Empty(app.AskedQuestions);
    }

    [Fact]
    public async Task CreateBookList_hook_returns_every_library_book_that_shares_series_and_number()
    {
        var app = new FakePluginEnvironment.FakeApplication
        {
            Library = new List<Issue>
            {
                new() { Id = 1, SeriesId = 10, Number = "1" },
                new() { Id = 2, SeriesId = 10, Number = "1" },
                new() { Id = 3, SeriesId = 20, Number = "5" },
            },
        };
        var engine = DiscoverEngine(app);

        var results = await engine.InvokeAsync(PluginHooks.CreateBookList, env => new CreateBookListHookGlobals { Environment = env });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var list = Assert.IsAssignableFrom<List<Issue>>(result.ReturnValue);
        Assert.Equal(new[] { 1, 2 }, list.Select(i => i.Id).OrderBy(id => id));
    }
}
