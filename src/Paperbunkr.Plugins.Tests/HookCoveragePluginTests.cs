using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// End-to-end fixture test against the real "Hook Coverage" sample plugin - closes the "no live
/// sample plugin exercises this hook" gap docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-
/// hooks-plan.md flagged for BookOpened/ReaderResized/Editor/Books/NewBooks/ParseComicPath/
/// NetSearch/ConfigScript/ComicInfoHtml/ComicInfoUI/QuickOpenHtml/QuickOpenUI/DrawThumbnailOverlay -
/// every one of the 8 remaining wired hooks plus the 3 net-new UI-surface hooks. Discovers and
/// compiles it for real via <see cref="PluginEngine"/>, then invokes each hook against fixture data
/// to prove its typed payload actually arrives (not just that the script compiles), same convention
/// as <see cref="DuplicateFinderPluginTests"/>.
///
/// <see cref="PluginsRoot"/> is scoped to this plugin's own subfolder for the same reason
/// <see cref="DuplicateFinderPluginTests"/> already documents on its own copy of this property - a
/// sibling sample plugin's commands must never get counted alongside this one's fixed "13 entries"
/// assertion.
/// </summary>
public sealed class HookCoveragePluginTests
{
    private static string PluginsRoot => Path.Combine(AppContext.BaseDirectory, "SamplePlugins", "HookCoverage");

    private static PluginEngine DiscoverEngine(FakePluginEnvironment.FakeApplication? app = null)
    {
        var engine = new PluginEngine();
        engine.Discover(PluginsRoot, new FakePluginEnvironment(app));
        return engine;
    }

    [Fact]
    public void Every_command_compiles_cleanly_and_the_config_script_pairs_with_its_owner()
    {
        var engine = DiscoverEngine();

        // 14 manifest entries minus the ConfigScript one, which is diverted into the Editor
        // Probe command's own Configure property (PluginEngine.Discover), not added to AllCommands.
        Assert.Equal(13, engine.AllCommands.Count);
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken));

        var editorProbe = engine.AllCommands.Single(c => c.Key == "hook-coverage.editor");
        Assert.NotNull(editorProbe.Configure);
        Assert.Equal(PluginHooks.ConfigScript, editorProbe.Configure!.Hook);
    }

    [Fact]
    public async Task BookOpened_hook_receives_the_real_book()
    {
        var engine = DiscoverEngine();
        var book = new Issue { Id = 77 };

        var results = await engine.InvokeAsync(PluginHooks.BookOpened, env => new BookOpenedHookGlobals { Environment = env, Book = book });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(77, result.ReturnValue);
    }

    [Fact]
    public async Task ReaderResized_hook_receives_width_and_height()
    {
        var engine = DiscoverEngine();

        var results = await engine.InvokeAsync(PluginHooks.ReaderResized, env => new ReaderResizedHookGlobals { Environment = env, Width = 800, Height = 600 });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(1400, result.ReturnValue);
    }

    [Fact]
    public async Task Editor_hook_receives_the_full_books_payload()
    {
        var engine = DiscoverEngine();
        var books = new List<Issue> { new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 } };

        var results = await engine.InvokeAsync(PluginHooks.Editor, env => new BooksHookGlobals { Environment = env, Books = books });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(3, result.ReturnValue);
    }

    [Fact]
    public async Task ConfigScript_hook_runs_independently_and_can_ask_a_question()
    {
        var app = new FakePluginEnvironment.FakeApplication();
        var engine = DiscoverEngine(app);
        var editorProbe = engine.AllCommands.Single(c => c.Key == "hook-coverage.editor");

        var result = await editorProbe.Configure!.InvokeAsync(new ConfigScriptHookGlobals { Environment = editorProbe.Configure.Environment! });

        Assert.Null(result);
        Assert.Single(app.AskedQuestions);
    }

    [Fact]
    public async Task Books_hook_receives_novel_book_entities_not_issues()
    {
        var engine = DiscoverEngine();
        var books = new List<Book> { new() { Id = 1, Title = "A" }, new() { Id = 2, Title = "B" } };

        var results = await engine.InvokeAsync(PluginHooks.Books, env => new NovelBooksHookGlobals { Environment = env, Books = books });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(2, result.ReturnValue);
    }

    [Fact]
    public async Task NewBooks_hook_returns_a_draft_issue()
    {
        var engine = DiscoverEngine();

        var results = await engine.InvokeAsync(PluginHooks.NewBooks, env => new NewBooksHookGlobals { Environment = env });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var draft = Assert.IsType<Issue>(result.ReturnValue);
        Assert.Equal(42, draft.SeriesId);
    }

    [Fact]
    public async Task ParseComicPath_hook_receives_the_path_and_returns_an_override()
    {
        var engine = DiscoverEngine();

        var results = await engine.InvokeAsync(PluginHooks.ParseComicPath, env => new ParseComicPathHookGlobals { Environment = env, Path = @"C:\comics\file.cbz" });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var parsed = Assert.IsType<ParsedComicPath>(result.ReturnValue);
        Assert.Contains(@"C:\comics\file.cbz", parsed.Series);
        Assert.Equal("7", parsed.Number);
    }

    [Fact]
    public async Task NetSearch_hook_receives_the_query_and_returns_results()
    {
        var engine = DiscoverEngine();

        var results = await engine.InvokeAsync(PluginHooks.NetSearch, env => new NetSearchHookGlobals { Environment = env, Query = "batman" });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var matches = Assert.IsAssignableFrom<NetSearchResult[]>(result.ReturnValue);
        var match = Assert.Single(matches);
        Assert.Contains("batman", match.Title);
        Assert.Equal(0.99, match.Confidence);
    }

    [Fact]
    public async Task ComicInfoHtml_hook_receives_the_book_and_returns_html_text()
    {
        var engine = DiscoverEngine();
        var book = new Issue { Id = 5 };

        var results = await engine.InvokeAsync(PluginHooks.ComicInfoHtml, env => new ComicInfoHookGlobals { Environment = env, Book = book });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal("<b>HTML</b> info for issue 5", result.ReturnValue);
    }

    [Fact]
    public async Task ComicInfoUI_hook_receives_the_book_and_returns_plain_text()
    {
        var engine = DiscoverEngine();
        var book = new Issue { Id = 6 };

        var results = await engine.InvokeAsync(PluginHooks.ComicInfoUI, env => new ComicInfoHookGlobals { Environment = env, Book = book });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal("UI info for issue 6", result.ReturnValue);
    }

    [Fact]
    public async Task QuickOpenHtml_hook_receives_the_query()
    {
        var engine = DiscoverEngine();

        var results = await engine.InvokeAsync(PluginHooks.QuickOpenHtml, env => new QuickOpenHookGlobals { Environment = env, Query = "spider-man" });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal("<i>Found</i>: spider-man", result.ReturnValue);
    }

    [Fact]
    public async Task QuickOpenUI_hook_receives_the_query()
    {
        var engine = DiscoverEngine();

        var results = await engine.InvokeAsync(PluginHooks.QuickOpenUI, env => new QuickOpenHookGlobals { Environment = env, Query = "spider-man" });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal("Found: spider-man", result.ReturnValue);
    }

    [Fact]
    public async Task DrawThumbnailOverlay_hook_receives_the_book_and_returns_bytes()
    {
        var engine = DiscoverEngine();
        var book = new Issue { Id = 9 };

        var results = await engine.InvokeAsync(PluginHooks.DrawThumbnailOverlay, env => new DrawThumbnailOverlayHookGlobals { Environment = env, Book = book });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var bytes = Assert.IsType<byte[]>(result.ReturnValue);
        Assert.Equal(new byte[] { 1, 2, 3, 9 }, bytes);
    }

    [Fact]
    public async Task CreateBookList_hook_grouped_shape_round_trips()
    {
        var app = new FakePluginEnvironment.FakeApplication { Library = new List<Issue> { new() { Id = 1 }, new() { Id = 2 } } };
        var engine = DiscoverEngine(app);

        var results = await engine.InvokeAsync(PluginHooks.CreateBookList, env => new CreateBookListHookGlobals { Environment = env });

        var result = Assert.Single(results.Where(r => r.Command.Key == "hook-coverage.create-book-list-grouped"));
        Assert.True(result.Success);
        var groups = Assert.IsAssignableFrom<PluginBookGroup[]>(result.ReturnValue);
        var group = Assert.Single(groups);
        Assert.Equal("all books", group.Label);
        Assert.Equal(new[] { 1, 2 }, group.Books.Select(b => b.Id));
        Assert.Equal(1, group.SuggestedKeepIssueId);
    }
}
