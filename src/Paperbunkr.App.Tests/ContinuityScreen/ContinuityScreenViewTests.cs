using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// Headless checks that the Continuity screen's compiled XAML loads and lays out with real data (docs/superpowers/specs/
/// 2026-09-28-continuity-screen-redesign-design.md, "Testing"): the continuity page with its hero, collage and runs, the event page, and
/// the timeline histogram jumping to a year. A build alone doesn't prove XAML loads (CLAUDE.md), so each view is constructed and shown.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuityScreenViewTests : ContinuityScreenTestBase
{
    private static void RunLayout(Window window)
    {
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
    }

    /// <summary>The Fluent theme and the app tokens the views' StaticResources need (App.axaml isn't loaded headless).</summary>
    private static void WithThemeAndTokens(Action body)
    {
        TestAppBuilder.EnsureInitialized();
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var resources = Application.Current.Resources;
        var tokens = new Dictionary<string, object>
        {
            ["PbMotionEase"] = new Avalonia.Animation.Easings.CubicEaseOut(),
            ["PbIconSizeXs"] = 14d,
            ["PbIconSizeSm"] = 16d,
            ["PbIconSizeLg"] = 24d,
        };
        var added = tokens.Keys.Where(k => !resources.ContainsKey(k)).ToList();
        foreach (var key in added)
        {
            resources[key] = tokens[key];
        }

        try
        {
            body();
        }
        finally
        {
            foreach (var key in added)
            {
                resources.Remove(key);
            }

            Application.Current!.Styles.Remove(theme);
        }
    }

    [Fact]
    public void ContinuityPage_Renders_TheHeroCollage_AndTheRuns()
    {
        WithThemeAndTokens(() =>
        {
            int older = SeedSeries("Incredible Hulk (1968)", ("1", 1968));
            int newer = SeedSeries("Hulk (2008)", ("1", 2008));
            using (var context = PaperbunkrDb.CreateContext())
            {
                context.MediaRelations.Add(new Paperbunkr.Data.Entities.MediaRelation
                {
                    SourceSeriesId = newer, TargetSeriesId = older, RelationType = Paperbunkr.Data.Entities.RelationType.Continuation,
                });
                context.SaveChanges();
            }

            var screen = CreateScreen();
            screen.LoadContinuity(SeedContinuity("Earth-616", older, newer));
            var window = new Window { Content = new Views.ContinuityScreen { DataContext = screen }, Width = 1100, Height = 800 };
            window.Show();
            RunLayout(window);

            var hero = window.GetVisualDescendants().OfType<HeroBand>().First(h => h.IsEffectivelyVisible);
            var collage = hero.GetVisualDescendants().OfType<ItemsControl>().First();
            Assert.Equal(screen.ContinuityPage.CollageKeys.Count, collage.ItemCount);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.True(texts.Contains("Earth-616"), string.Join(" | ", texts));
            Assert.True(texts.Contains("Incredible Hulk · 1968–2008 · 2 series"), string.Join(" | ", texts));
            window.Close();
        });
    }

    [Fact]
    public void ContinuityPage_WithNoCovers_ShowsAPlainHero()
    {
        WithThemeAndTokens(() =>
        {
            var screen = CreateScreen();
            screen.LoadContinuity(SeedContinuity("Empty"));
            var window = new Window { Content = new Views.ContinuityScreen { DataContext = screen }, Width = 1100, Height = 800 };
            window.Show();
            RunLayout(window);

            var hero = window.GetVisualDescendants().OfType<HeroBand>().First(h => h.IsEffectivelyVisible);
            Assert.Empty(screen.ContinuityPage.CollageKeys);
            Assert.Equal(0, hero.GetVisualDescendants().OfType<ItemsControl>().First().ItemCount);
            window.Close();
        });
    }

    [Fact]
    public void EventPage_Renders_WithItsReadingList()
    {
        WithThemeAndTokens(() =>
        {
            var screen = CreateScreen();
            screen.LoadEvent(SeedEvent("Inferno", null, null, SeedIssue("X-Men", "1", year: 1988), SeedIssue("X-Factor", "1", year: 1988)));
            var window = new Window { Content = new Views.ContinuityScreen { DataContext = screen }, Width = 1100, Height = 800 };
            window.Show();
            RunLayout(window);

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "All 2" && t.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "X-Men #1" && t.IsEffectivelyVisible);
            window.Close();
        });
    }

    /// <summary>
    /// Regression (2026-09-28, reported on screen): opening Related events or the Needs review filter on an event page crashed the app
    /// with a stack overflow (0xc00000fd, so no crash log).
    /// </summary>
    [Fact]
    public void EventPage_OpeningRelatedEvents_AndTheNeedsReviewFilter_LaysOut()
    {
        WithThemeAndTokens(() =>
        {
            var members = Enumerable.Range(1, 30).Select(i => SeedIssue("Spider-Man", i.ToString(), format: i == 1 ? "Prologue" : null, year: 2015)).ToArray();
            int eventId = SeedEvent("Secret Wars", new DateTime(2015, 5, 1), null, members);
            int other = SeedEvent("Secret Empire", new DateTime(2015, 6, 1));
            using (var context = PaperbunkrDb.CreateContext())
            {
                Paperbunkr.Data.Metadata.EventRelationResolver.TryCreate(context, eventId, other, Paperbunkr.Data.Entities.RelationType.Crossover);
            }

            var screen = CreateScreen();
            screen.LoadEvent(eventId);
            screen.EventPage.DetectRolesCommand.Execute(null);
            Drain();
            Assert.True(screen.EventPage.HasRoleSuggestions);

            var window = new Window { Content = new Views.ContinuityScreen { DataContext = screen }, Width = 1100, Height = 800 };
            window.Show();
            RunLayout(window);

            screen.EventPage.ToggleRelatedEventsCommand.Execute(null);
            RunLayout(window);
            RunLayout(window);

            screen.EventPage.SetFilterCommand.Execute(Paperbunkr.App.Models.EventMemberFilter.NeedsReview);
            RunLayout(window);
            RunLayout(window);

            Assert.Single(screen.EventPage.VisibleMembers);
            window.Close();
        });
    }

    [Fact]
    public void Timeline_ClickingAYearBar_ScrollsToThatYearsFirstCover()
    {
        WithThemeAndTokens(() =>
        {
            var issues = Enumerable.Range(0, 60).Select(i => (i.ToString(), (int?)(1960 + i))).ToArray();
            int series = SeedSeries("Long Run", issues);
            var timeline = new ContinuityTimelineViewModel(_ => { });
            timeline.LoadForContinuity(SeedContinuity("C", series));
            var view = new ContinuityTimelineView { DataContext = timeline };
            var scroller = new ScrollViewer { Content = view };
            var window = new Window { Content = scroller, Width = 700, Height = 500 };
            window.Show();
            RunLayout(window);
            Assert.Equal(0, scroller.Offset.Y);

            timeline.JumpToYearCommand.Execute(timeline.YearBars.Last());
            RunLayout(window);

            Assert.True(scroller.Offset.Y > 0, "the scroller should have moved to the last year");
            window.Close();
        });
    }
}
