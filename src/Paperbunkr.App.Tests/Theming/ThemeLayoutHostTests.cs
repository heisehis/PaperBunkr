using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Layout;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.App.Views.Home;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The layout engine on a real screen (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phases 4, 5 and 7): Home shown
/// through a theme's layout, through two different layouts, and through its built-in layout whenever a theme's is missing, switched off,
/// refused or broken; plus motion policy and the extension attachment point.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeLayoutHostTests : IDisposable
{
    private const string SidebarLayout = """
        { "root": { "id": "root", "type": "Dock", "children": [
            { "id": "side", "type": "Panel", "dock": "left", "width": 360, "background": "surface.raised", "children": [
                { "id": "sideStack", "type": "Stack", "spacing": 12, "padding": 16, "children": [
                    { "id": "hello", "type": "Text", "style": "heading", "text": "{home.greeting} - {app.name}" },
                    { "id": "search", "type": "Home.Search" },
                    { "id": "customize", "type": "Button", "style": "ghost", "text": "Customize", "icon": "navigation.preferences", "command": "home.customize" },
                    { "id": "empty", "type": "Text", "text": "Nothing to show", "visible": { "not": "home.hasSections" } } ] } ] },
            { "id": "main", "type": "Scroll", "dock": "fill", "children": [ { "id": "sections", "type": "Home.Sections", "margin": 24, "motion": "fadeUp" } ] } ] },
          "breakpoints": { "compact": { "maxWidth": 700, "overrides": { "side": { "width": 220 } } } } }
        """;

    private const string TabbedLayout = """
        { "root": { "id": "root", "type": "Stack", "children": [
            { "id": "masthead", "type": "Home.Masthead" },
            { "id": "tabs", "type": "Tabs", "children": [
                { "id": "reading", "type": "Stack", "title": "Reading", "children": [ { "id": "continue", "type": "Home.Section", "section": "continueReading" } ] },
                { "id": "discover", "type": "Stack", "title": "Discover ({home.sectionCount})", "children": [ { "id": "recent", "type": "Home.Section", "section": "recentlyAdded" }, { "id": "hidden", "type": "Home.Section", "section": "collections" } ] } ] } ] } }
        """;

    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_layout_host_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly List<string> _anchors = new();

    public ThemeLayoutHostTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "home.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();

        // Something for Home to show: a series part-way through, so the Continue reading and Recently added sections exist.
        var series = new Series { Name = "Kilo Station" };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", LastPageRead = 30, PageCount = 100, AddedTime = DateTime.UtcNow, OpenedTime = DateTime.UtcNow });
        context.SaveChanges();

        ThemeLayoutSource.Clear();
        ThemeMotionRuntime.Configure(new Dictionary<string, JsonObject>(), ThemeEffectQuality.High, reducedMotion: false);
        ThemeMotionRuntime.IsBatteryLow = () => false;
        ThemeMotionRuntime.Started.Clear();
    }

    public void Dispose()
    {
        ThemeLayoutSource.Clear();
        ThemeMotionRuntime.Configure(new Dictionary<string, JsonObject>(), ThemeEffectQuality.High, reducedMotion: false);
        ThemeMotionRuntime.IsBatteryLow = BatteryStatusInterop.IsThrottled;
        foreach (string id in ThemeExtensionRegistry.ProviderIds.ToList())
        {
            ThemeExtensionRegistry.Unregister(id);
        }

        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private HomeScreenViewModel NewHomeVm() =>
        new(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { }, goPreferencesAnchor: anchor => _anchors.Add(anchor));

    private static void Publish(string? homeLayout, bool enabled = true, Dictionary<string, JsonObject>? homePatches = null)
    {
        var layouts = new Dictionary<string, JsonObject>();
        if (homeLayout is not null)
        {
            layouts["Home"] = (JsonObject)JsonNode.Parse(homeLayout)!;
        }

        var patches = new Dictionary<string, Dictionary<string, JsonObject>>();
        if (homePatches is not null)
        {
            patches["Home"] = homePatches;
        }

        ThemeLayoutSource.Set(enabled, "Test theme", layouts, patches, _ => null, _ => null);
        TestDispatcher.Drain();
    }

    /// <summary>Hosts a Home screen in a window of the given width and runs <paramref name="body"/> with it.</summary>
    private void WithHome(Action<Window, HomeScreen, ThemeLayoutHost> body, double width = 1300)
    {
        WithThemeAndTokens(() =>
        {
            using var resources = AppResources();
            using var styles = AppStyles();
            var screen = new HomeScreen { DataContext = NewHomeVm() };
            var window = new Window { Content = screen, Width = width, Height = 900 };
            window.Show();
            try
            {
                RunLayout(window);
                TestDispatcher.Drain();
                RunLayout(window);
                body(window, screen, screen.GetVisualDescendants().OfType<ThemeLayoutHost>().Single());
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Settle(Window window)
    {
        TestDispatcher.Drain();
        RunLayout(window);
        TestDispatcher.Drain();
        RunLayout(window);
    }

    private static void AssertActive(ThemeLayoutHost host) =>
        Assert.True(host.IsCustomLayoutActive, "The theme layout is not showing: " + string.Join(" | ", host.Diagnostics.Select(d => d.Code + " " + d.Message)));

    private static T? Find<T>(Visual root, string automationId)
        where T : Control => root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == automationId);

    // ===================== Built-in layout and fallback =====================

    [Fact]
    public void WithNoThemeLayout_HomeShowsItsBuiltInLayout()
    {
        WithHome((_, screen, host) =>
        {
            Assert.False(host.IsCustomLayoutActive);
            Assert.NotNull(screen.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "PageScroller"));
            Assert.Single(screen.GetVisualDescendants().OfType<HomeMasthead>());
            Assert.Single(screen.GetVisualDescendants().OfType<HomeSections>());
            Assert.NotNull(Find<TextBox>(screen, "HomeSearchBox"));
        });
    }

    [Fact]
    public void AThemeLayout_IsNotUsed_UntilTheUserAllowsThemeLayouts()
    {
        WithHome((window, screen, host) =>
        {
            Publish(SidebarLayout, enabled: false);
            Settle(window);

            Assert.False(host.IsCustomLayoutActive);
            Assert.True(ThemeLayoutSource.HasLayoutFor("Home"));                    // the UI can still say one is available
            Assert.Single(screen.GetVisualDescendants().OfType<HomeMasthead>());
        });
    }

    [Theory]
    [InlineData("""{ "root": { "id": "root", "type": "Stack", "children": [ { "id": "s", "type": "Home.Sections" } ] } }""")]
    [InlineData("""{ "root": { "id": "root", "type": "TotallyMadeUp" } }""")]
    [InlineData("""{ "nope": 1 }""")]
    public void ARefusedLayout_FallsBackToTheBuiltInOne_AndSaysWhy(string layout)
    {
        WithHome((window, screen, host) =>
        {
            Publish(layout);
            Settle(window);

            Assert.False(host.IsCustomLayoutActive);
            Assert.Single(screen.GetVisualDescendants().OfType<HomeMasthead>());
            Assert.Single(screen.GetVisualDescendants().OfType<HomeSections>());
            Assert.Contains(host.Diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Error && d.FallbackUsed);
            Assert.Contains(ThemeLayoutSource.Diagnostics, d => d.Code == ThemeDiagnosticCodes.InvalidLayout);
        });
    }

    [Fact]
    public void ALayoutThatFailsWhileBeingBuilt_FallsBackToTheBuiltInOne()
    {
        WithHome((window, screen, host) =>
        {
            host.CreateSlot = _ => throw new InvalidOperationException("a host component blew up");

            Publish(SidebarLayout);
            Settle(window);

            Assert.False(host.IsCustomLayoutActive);
            Assert.NotNull(screen.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "PageScroller"));
            Assert.Contains(host.Diagnostics, d => d.Message.Contains("could not be built"));
        });
    }

    // ===================== A theme's layout =====================

    [Fact]
    public void AThemeLayout_RearrangesHome_WithoutReimplementingIt()
    {
        WithHome((window, screen, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);

            AssertActive(host);
            Assert.Null(screen.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "PageScroller"));   // the built-in arrangement is not on screen
            Assert.Empty(screen.GetVisualDescendants().OfType<HomeMasthead>());                                                // this layout chose the bare search, not the masthead
            Assert.Single(screen.GetVisualDescendants().OfType<HomeSections>());                                               // the real section list, not a copy of its logic

            // The sidebar sits to the left of the sections.
            var side = host.FindNode("side")!;
            var sections = host.FindNode("sections")!;
            Assert.Equal(360, side.Bounds.Width);
            Assert.True(side.TranslatePoint(default, window)!.Value.X < sections.TranslatePoint(default, window)!.Value.X);

            // Published values are shown, and only those.
            var vm = (HomeScreenViewModel)screen.DataContext!;
            Assert.Equal($"{vm.Greeting} - Paperbunkr", ((TextBlock)host.FindNode("hello")!).Text);
            Assert.False(host.FindNode("empty")!.IsVisible);                        // there are sections, so the "nothing to show" line is hidden

            // The real section content is there.
            Assert.Contains(sections.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Kilo Station");
        });
    }

    [Fact]
    public void AThemeLayoutButton_RunsTheScreensOwnCommand()
    {
        WithHome((window, _, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);
            var button = (Button)host.FindNode("customize")!;

            Assert.Equal("Customize", AutomationProperties.GetName(button));        // named by its text, whatever icon the theme draws
            Assert.True(button.IsEnabled);
            button.Command!.Execute(null);

            Assert.Equal(new[] { "appearance.home" }, _anchors);                    // the same command the built-in "Choose what to show" link runs
        });
    }

    [Fact]
    public void TheSearchBox_InAThemeLayout_IsTheRealSearch()
    {
        WithHome((window, screen, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);
            var vm = (HomeScreenViewModel)screen.DataContext!;
            var box = Find<TextBox>(host, "HomeSearchBox");

            Assert.NotNull(box);                                                    // Ctrl+F finds it by the same id as in the built-in layout
            box!.Text = "kilo";
            Assert.Equal("kilo", vm.SearchQuery);
        });
    }

    [Fact]
    public void TwoThemes_ArrangeTheSameScreenDifferently()
    {
        WithHome((window, screen, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);
            Assert.NotNull(host.FindNode("side"));
            Assert.Empty(screen.GetVisualDescendants().OfType<TabControl>());

            Publish(TabbedLayout);
            Settle(window);

            AssertActive(host);
            Assert.Null(host.FindNode("side"));
            Assert.Single(screen.GetVisualDescendants().OfType<HomeMasthead>());
            var tabs = Assert.Single(screen.GetVisualDescendants().OfType<TabControl>());
            Assert.Equal(new[] { "Reading", "Discover (" + ((HomeScreenViewModel)screen.DataContext!).Sections.Count + ")" }, tabs.Items.OfType<TabItem>().Select(t => t.Header));

            // One section placed on its own shows that section; one the library has nothing for stays hidden.
            var continueReading = (HomeSectionSlot)host.FindNode("continue")!;
            Assert.True(continueReading.IsVisible);
            Assert.Equal("continueReading", continueReading.SectionKey);
            Assert.NotNull(continueReading.Content);
        });
    }

    [Fact]
    public void SwitchingToAThemeWithoutALayout_GoesBackToTheBuiltInOne()
    {
        WithHome((window, screen, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);
            AssertActive(host);
            int applied = 0;
            host.LayoutApplied += () => applied++;

            Publish(null);
            Settle(window);

            Assert.False(host.IsCustomLayoutActive);
            Assert.Equal(1, applied);                                               // the screen is told, so it can put focus back
            Assert.NotNull(screen.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "PageScroller"));
            Assert.Single(screen.GetVisualDescendants().OfType<HomeMasthead>());
        });
    }

    [Fact]
    public void ReapplyingTheSameTheme_DoesNotRebuildTheScreen()
    {
        WithHome((window, _, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);
            var before = host.FindNode("sections");

            Publish(SidebarLayout);                                                 // what every True Black toggle or override change does
            Settle(window);

            Assert.Same(before, host.FindNode("sections"));                         // same controls: scroll position and section state survive
        });
    }

    // ===================== Responsive, and the user's own adjustments =====================

    [Fact]
    public void ABreakpoint_AppliesBelowItsWidth()
    {
        WithHome((window, _, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);

            Assert.Equal("compact", host.ActiveBreakpoint);
            Assert.Equal(220, host.FindNode("side")!.Bounds.Width);
        }, width: 640);
    }

    [Fact]
    public void CrossingABreakpoint_ReappliesTheLayout()
    {
        WithHome((window, _, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);
            Assert.Null(host.ActiveBreakpoint);
            Assert.Equal(360, host.FindNode("side")!.Bounds.Width);

            window.Width = 640;
            Settle(window);

            Assert.Equal("compact", host.ActiveBreakpoint);
            Assert.Equal(220, host.FindNode("side")!.Bounds.Width);
        });
    }

    [Fact]
    public void AUsersLayoutPatch_IsApplied_AndOneForANodeThatNoLongerExists_IsReportedNotApplied()
    {
        WithHome((window, _, host) =>
        {
            Publish(SidebarLayout, homePatches: new Dictionary<string, JsonObject>
            {
                ["side"] = new() { ["width"] = 300 },
                ["customize"] = new() { ["visible"] = false },
                ["goneInAnUpdate"] = new() { ["width"] = 1 },
            });
            Settle(window);

            AssertActive(host);
            Assert.Equal(300, host.FindNode("side")!.Bounds.Width);
            Assert.False(host.FindNode("customize")!.IsVisible);
            Assert.Contains(host.Diagnostics, d => d.Message.Contains("'goneInAnUpdate' no longer matches"));
        });
    }

    // ===================== Through the theme service =====================

    [Fact]
    public void AnInstalledTheme_ShipsALayout_TheServicePublishesIt_AndTheSwitchTurnsItOn()
    {
        string folder = Path.Combine(ThemePaths.ExtractedDirectory, "sidebar");
        Directory.CreateDirectory(Path.Combine(folder, "layouts"));
        File.WriteAllText(Path.Combine(folder, "layouts", "home.json"), SidebarLayout);
        File.WriteAllText(Path.Combine(folder, "theme.json"), """
            { "schemaVersion": 2, "id": "org.test.sidebar", "name": "Sidebar", "mode": "Dark",
              "base": { "palette": { "canvas": "#101418", "ink": "#E6EAF0", "accent": "#4DA3FF" }, "layouts": { "Home": "layouts/home.json" } } }
            """);
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var service = new ThemeService(() => new PaperbunkrDbContext(options));

        WithHome((window, _, host) =>
        {
            service.ApplyTheme("sidebar");
            Settle(window);
            Assert.Equal(new[] { "Home" }, service.ActiveThemeLayoutScreens);
            Assert.False(host.IsCustomLayoutActive);                                // installed and active, but layouts are off by default

            service.SetAllowThemeLayouts(true);
            Settle(window);
            AssertActive(host);
            Assert.NotNull(host.FindNode("side"));

            service.SetLayoutPatch("Home", "side", new JsonObject { ["width"] = 280 });
            Settle(window);
            Assert.Equal(280, host.FindNode("side")!.Bounds.Width);
            Assert.Equal(1, service.CountLayoutPatches());

            service.ResetLayoutPatches();
            Settle(window);
            Assert.Equal(360, host.FindNode("side")!.Bounds.Width);

            service.ApplyTheme("default");
            Settle(window);
            Assert.False(host.IsCustomLayoutActive);                                // a built-in theme has no layouts

            service.SetAllowThemeLayouts(false);
        });
    }

    // ===================== Motion policy =====================

    private static readonly Dictionary<string, JsonObject> Breathing = new()
    {
        ["breathe"] = (JsonObject)JsonNode.Parse("""{ "iterations": "infinite", "duration": 2000, "keyframes": [ { "at": 0, "opacity": 1 }, { "at": 0.5, "opacity": 0.8 }, { "at": 1, "opacity": 1 } ] }""")!,
    };

    [Fact]
    public void AMotionOnALayoutNode_PlaysWhenItAppears()
    {
        WithHome((window, _, _) =>
        {
            Publish(SidebarLayout);
            Settle(window);

            Assert.Contains("fadeUp", ThemeMotionRuntime.Started);
        });
    }

    /// <summary>Motion v2 §4.2: Reduced plays no theme motion. The Performance tier now keeps finite Reveal motion (a load fade is a
    /// cheap opacity/transform change, Q7) and drops only continuous Ambient - see <see cref="AContinuousMotion_PlaysOnlyAtHighQuality_AndNotWhenTheBatteryIsLow"/>.</summary>
    [Theory]
    [InlineData(true, ThemeEffectQuality.High)]
    [InlineData(true, ThemeEffectQuality.Performance)]
    public void ReduceMotion_PlaysNoThemeMotion_AndTheScreenIsSimplyShown(bool reducedMotion, ThemeEffectQuality quality)
    {
        ThemeMotionRuntime.Configure(new Dictionary<string, JsonObject>(), quality, reducedMotion);

        WithHome((window, _, host) =>
        {
            Publish(SidebarLayout);
            Settle(window);

            Assert.Empty(ThemeMotionRuntime.Started);
            AssertActive(host);
            Assert.Equal(1, host.FindNode("sections")!.Opacity);                    // at rest, fully visible
        });
    }

    [Fact]
    public void AContinuousMotion_PlaysOnlyAtHighQuality_AndNotWhenTheBatteryIsLow()
    {
        ThemeMotionRuntime.Configure(Breathing, ThemeEffectQuality.High, reducedMotion: false);
        var breathe = ThemeMotionRuntime.Find("breathe")!;
        var fade = ThemeMotionRuntime.Find("fadeIn")!;
        Assert.True(ThemeMotionRuntime.WouldPlay(breathe));

        ThemeMotionRuntime.IsBatteryLow = () => true;
        Assert.False(ThemeMotionRuntime.WouldPlay(breathe));
        Assert.True(ThemeMotionRuntime.WouldPlay(fade));                            // a short finite motion is not what drains a battery

        ThemeMotionRuntime.IsBatteryLow = () => false;
        ThemeMotionRuntime.Configure(Breathing, ThemeEffectQuality.Balanced, reducedMotion: false);
        Assert.False(ThemeMotionRuntime.WouldPlay(breathe));
        Assert.True(ThemeMotionRuntime.WouldPlay(fade));
    }

    [Fact]
    public void ARefusedMotion_IsReported_AndAnUnknownMotionNameDoesNothing()
    {
        var motions = new Dictionary<string, JsonObject>
        {
            ["bad"] = (JsonObject)JsonNode.Parse("""{ "keyframes": [ { "at": 0, "width": 0 }, { "at": 1, "width": 100 } ] }""")!,
            ["chain"] = (JsonObject)JsonNode.Parse("""{ "then": "nowhere", "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""")!,
        };

        ThemeMotionRuntime.Configure(motions, ThemeEffectQuality.High, reducedMotion: false);

        Assert.Null(ThemeMotionRuntime.Find("bad"));
        Assert.NotNull(ThemeMotionRuntime.Find("chain"));
        Assert.Contains(ThemeMotionRuntime.Diagnostics, d => d.Code == ThemeDiagnosticCodes.InvalidMotion && d.PropertyPath == "motion.bad");
        Assert.Contains(ThemeMotionRuntime.Diagnostics, d => d.PropertyPath == "motion.chain.then");

        ThemeMotionRuntime.Play(new Border(), "noSuchMotion");                       // must not throw
        Assert.Empty(ThemeMotionRuntime.Started);
    }

    // ===================== Extension attachment point (no plugin runtime) =====================

    private const string ExtensionLayout = """
        { "root": { "id": "root", "type": "Stack", "children": [
            { "id": "masthead", "type": "Home.Masthead" },
            { "id": "weather", "type": "Extension", "provider": "org.example.weather", "component": "forecast", "children": [ { "id": "fallback", "type": "Text", "text": "Weather unavailable" } ] },
            { "id": "bare", "type": "Extension", "provider": "org.example.nothing", "component": "x" },
            { "id": "sections", "type": "Home.Sections" } ] } }
        """;

    private sealed class FakeProvider(string id, Func<string, Control?> create) : IThemeComponentProvider
    {
        public string Id => id;

        public IReadOnlyCollection<string> Components => new[] { "forecast" };

        public Control? Create(string component, ThemeLayoutNode node) => create(component);
    }

    [Fact]
    public void AnExtensionComponent_WithNoProviderInstalled_ShowsItsFallback_AndNothingIsRun()
    {
        WithHome((window, _, host) =>
        {
            Publish(ExtensionLayout);
            Settle(window);

            AssertActive(host);
            Assert.Equal("Weather unavailable", ((TextBlock)host.FindNode("fallback")!).Text);
            Assert.NotNull(host.FindNode("bare"));                                  // no fallback given: an empty placeholder, not an error
            Assert.Contains(host.Diagnostics, d => d.Code == ThemeDiagnosticCodes.MissingProvider && d.Message.Contains("org.example.weather") && d.FallbackUsed);
            Assert.Empty(ThemeExtensionRegistry.ProviderIds);                       // naming a provider does not register, install or enable anything
        });
    }

    [Fact]
    public void ARegisteredProvider_SuppliesTheComponent_AndOneThatDeclinesOrThrows_LeavesTheFallback()
    {
        WithHome((window, _, host) =>
        {
            ThemeExtensionRegistry.Register(new FakeProvider("org.example.weather", component => new TextBlock { Text = "Sunny: " + component }));
            Publish(ExtensionLayout);
            Settle(window);

            Assert.Equal("Sunny: forecast", ((TextBlock)host.FindNode("weather")!).Text);
            Assert.Null(host.FindNode("fallback"));
            Assert.DoesNotContain(host.Diagnostics, d => d.Message.Contains("org.example.weather"));

            ThemeExtensionRegistry.Register(new FakeProvider("org.example.weather", _ => throw new InvalidOperationException("provider crashed")));
            Settle(window);
            Assert.Equal("Weather unavailable", ((TextBlock)host.FindNode("fallback")!).Text);    // a failing provider is treated as absent

            ThemeExtensionRegistry.Unregister("org.example.weather");
            Settle(window);
            Assert.Equal("Weather unavailable", ((TextBlock)host.FindNode("fallback")!).Text);
        });
    }
}
