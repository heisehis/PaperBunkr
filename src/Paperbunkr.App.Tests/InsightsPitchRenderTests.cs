using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.App.Views.Insights;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Headless renders of the real Insights screen with seeded data (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md): the
/// compiled XAML for the five new cards loads and binds, the cards show the seeded figures, and a PNG of each is written for a human look
/// (set PAPERBUNKR_RENDER_DIR to keep them; otherwise they go to the temp folder).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InsightsPitchRenderTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public InsightsPitchRenderTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pitch_render_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static string OutDir()
    {
        string dir = Environment.GetEnvironmentVariable("PAPERBUNKR_RENDER_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(OutDir(), $"insights-pitch-{name}.png"));
    }

    /// <summary>A library with: three series by one writer (rated, read), six stopped Marvel series, five finished DC series,
    /// sized files (one duplicate pair), reading sessions across the week, and a run of daily finishes.</summary>
    private void Seed()
    {
        var rng = new Random(7);
        var now = DateTime.UtcNow;
        using var ctx = PaperbunkrDb.CreateContext();

        var vaughan = new Creator { Name = "Brian K. Vaughan" };
        ctx.Creators.Add(vaughan);
        ctx.SaveChanges();

        Issue Add(Series s, int n, string publisher, float? rating, bool read, long size, string ext = "cbz")
        {
            var issue = new Issue
            {
                SeriesId = s.Id, Number = n.ToString(), Publisher = publisher, Rating = rating, PageCount = 24,
                LastPageRead = read ? 24 : 0, FileSize = size, FilePath = $@"C:\lib\{s.Name}\{n}.{ext}",
            };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            return issue;
        }

        foreach (string name in new[] { "Saga", "Paper Girls", "Y: The Last Man" })
        {
            var s = new Series { Name = name, ContentType = ContentType.Comic };
            ctx.Series.Add(s);
            ctx.SaveChanges();
            for (int n = 1; n <= 3; n++)
            {
                var issue = Add(s, n, "Image", 5f, true, (30 + rng.Next(80)) * (1L << 20));
                ctx.CreatorCredits.Add(new CreatorCredit { CreatorId = vaughan.Id, IssueId = issue.Id, Role = "Writer" });
            }
        }

        for (int i = 0; i < 6; i++)
        {
            var s = new Series { Name = "Marvel Run " + i, ContentType = ContentType.Comic };
            ctx.Series.Add(s);
            ctx.SaveChanges();
            for (int n = 1; n <= 6; n++)
            {
                Add(s, n, "Marvel", null, n <= 2, (25 + rng.Next(60)) * (1L << 20), n % 3 == 0 ? "cbr" : "cbz");
            }

            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic, ItemId = 1000 + i, Kind = ReadingEventKind.Opened, SeriesId = s.Id,
                TimestampUtc = now.AddDays(-60 - i),
            });
        }

        for (int i = 0; i < 5; i++)
        {
            var s = new Series { Name = "DC Miniseries " + i, ContentType = i == 0 ? ContentType.Manga : ContentType.Comic };
            ctx.Series.Add(s);
            ctx.SaveChanges();
            for (int n = 1; n <= 3; n++)
            {
                Add(s, n, "DC", 4f, true, (20 + rng.Next(40)) * (1L << 20));
            }
        }

        var dup = ctx.Series.First(x => x.Name == "Saga");
        ctx.Issues.Add(new Issue { SeriesId = dup.Id, Number = "1", Publisher = "Image", PageCount = 24, FileSize = 18L << 20, FilePath = @"C:\lib\Saga\1-copy.cbz" });

        // Reading sessions: evenings and weekend mornings, with timing on most.
        for (int i = 0; i < 60; i++)
        {
            int dayOffset = rng.Next(1, 28);
            var day = now.Date.AddDays(-dayOffset);
            int hour = rng.Next(3) == 0 ? 9 + rng.Next(3) : 19 + rng.Next(4);
            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic, ItemId = 2000 + i, Kind = ReadingEventKind.Opened,
                TimestampUtc = day.AddHours(hour).AddMinutes(rng.Next(60)),
                ActiveSeconds = i % 5 == 0 ? null : 120 + rng.Next(3000),
            });
        }

        // A run of daily finishes (Opened an hour earlier so none looks like import data).
        for (int d = 0; d < 12; d++)
        {
            var finish = now.AddDays(-d - 1);
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 3000 + d, Kind = ReadingEventKind.Opened, TimestampUtc = finish.AddHours(-1), SeriesTitle = "Saga" });
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 3000 + d, Kind = ReadingEventKind.Finished, TimestampUtc = finish, SeriesTitle = "Saga" });
        }

        ctx.SaveChanges();
    }

    private static List<string> VisibleTexts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();

    [Fact]
    public void TrendsCards_RenderTheSeededFigures()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            Seed();
            var preferences = new InsightsPreferences();
            var vm = new InsightsScreenViewModel(_ => { }, _ => { }, _ => { }, () => { }, new NoDialogs(),
                new ReadingEventRecorder(PaperbunkrDb.CreateContext), openDuplicateReview: () => { }, preferences: preferences);
            vm.Stats.RunSynchronously = true;
            var window = new Window { Content = new InsightsScreen { DataContext = vm }, Width = 1100, Height = 1300 };
            window.Show();

            vm.SelectTrendsTabCommand.Execute(null);
            vm.Stats.SetRangeCommand.Execute(InsightsRange.AllTime);
            vm.Stats.SetTrendsGroupCommand.Execute(TrendsGroup.Activity);
            RunLayout(window);
            var activity = VisibleTexts(window);
            Assert.Contains("Reading rhythm", activity);
            Assert.Contains(activity, t => t.StartsWith("Most reading starts", StringComparison.Ordinal));
            Assert.Contains("Session length", activity);
            Save(window, "trends-activity");

            vm.Stats.SetTrendsGroupCommand.Execute(TrendsGroup.Composition);
            RunLayout(window);
            var composition = VisibleTexts(window);
            Assert.Contains("Where you stop", composition);
            Assert.Contains("Marvel", composition);
            Assert.Contains("Storage", composition);
            Assert.Contains(composition, t => t.StartsWith("Duplicates could free up to", StringComparison.Ordinal));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Review duplicates"));
            Save(window, "trends-composition");

            vm.Stats.SetTrendsGroupCommand.Execute(TrendsGroup.TopLists);
            RunLayout(window);
            var topLists = VisibleTexts(window);
            Assert.Contains("You love these", topLists);
            Assert.Contains("Brian K. Vaughan", topLists);
            Save(window, "trends-toplists");

            window.Close();
        });
    }

    [Fact]
    public void HistoryMilestoneStrip_RendersEarnedAndLockedBadges()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            Seed();
            var vm = new HistoryTabViewModel(_ => { }, _ => { }, (_, _) => { }, _ => { }, new NoDialogs(),
                new ReadingEventRecorder(PaperbunkrDb.CreateContext), contextFactory: PaperbunkrDb.CreateContext,
                runInBackground: work => Task.FromResult(work()), post: a => a())
            { Preferences = new InsightsPreferences() };
            vm.IsActive = true;
            vm.Refresh();
            var view = new InsightsHistoryView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 500 };
            window.Show();
            RunLayout(window);

            var texts = VisibleTexts(view);
            Assert.Contains("MILESTONES", texts);
            Assert.Contains("First item finished", texts);
            Assert.Contains("7-day reading streak", texts);
            Assert.Contains("50 items finished", texts);
            Save(window, "history-milestones");

            window.Close();
        });
    }

    private sealed class NoDialogs : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(false);
    }
}
