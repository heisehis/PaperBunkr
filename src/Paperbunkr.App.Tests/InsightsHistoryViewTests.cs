using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views.Insights;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Headless smoke test for <see cref="InsightsHistoryView"/> (docs/superpowers/specs/2026-09-29-insights-reading-history-
/// design.md §4): the compiled XAML loads, day headers and rows realize in the virtualized list, and each row state shows
/// the right action button / tag. Same theme-and-tokens harness as <c>LibraryScreenViewTests</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InsightsHistoryViewTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;

    public InsightsHistoryViewTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_history_view_{Guid.NewGuid():N}.db");
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

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
            ["PbRadiusChip"] = new CornerRadius(9),
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

    private void Seed()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        using (var ctx = NewContext())
        {
            var inProgress = new Series { Name = "One Piece", ContentType = ContentType.Manga };
            var gone = new Series { Name = "Invincible" };
            ctx.AddRange(inProgress, gone);
            ctx.SaveChanges();
            ctx.Issues.Add(new Issue { SeriesId = inProgress.Id, Number = "1090", PageCount = 17, LastPageRead = 5 });
            ctx.Issues.Add(new Issue { SeriesId = gone.Id, Number = "12", PageCount = 20 });
            ctx.Books.Add(new Book { Title = "Blame", FilePath = "blame.pdf", Format = BookFormat.Pdf });
            ctx.SaveChanges();
        }

        using (var ctx = NewContext())
        {
            foreach (var issue in ctx.Issues.ToList())
            {
                recorder.RecordOpened(ReadingItemType.Comic, issue.Id, issue.SeriesId, null, null);
            }

            recorder.RecordOpened(ReadingItemType.Novel, ctx.Books.Single().Id, null, null, null);
        }

        using (var ctx = NewContext())
        {
            // Spread over two days, then delete Invincible so its row greys out.
            foreach (var e in ctx.ReadingEvents.ToList())
            {
                e.TimestampUtc = e.ItemType == ReadingItemType.Novel ? Now.AddHours(-1) : Now.AddDays(-1).AddHours(-e.Id);
            }

            ctx.Issues.RemoveRange(ctx.Issues.Where(i => i.Series!.Name == "Invincible"));
            ctx.Series.Remove(ctx.Series.Single(s => s.Name == "Invincible"));
            ctx.SaveChanges();
        }
    }

    [Fact]
    public void Renders_Headers_Rows_AndPerStateActions()
    {
        WithThemeAndTokens(() =>
        {
            Seed();
            var vm = new HistoryTabViewModel(_ => { }, _ => { }, (_, _) => { }, _ => { }, new NoDialogs(),
                new ReadingEventRecorder(NewContext), contextFactory: NewContext, nowUtc: () => Now, timeZone: TimeZoneInfo.Utc,
                runInBackground: work => Task.FromResult(work()), post: a => a());
            vm.IsActive = true;
            vm.Refresh();

            var view = new InsightsHistoryView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 700 };
            window.Show();
            window.UpdateLayout();
            TestDispatcher.Drain();
            window.UpdateLayout();

            var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("TODAY", texts);
            Assert.Contains("YESTERDAY", texts);
            Assert.Contains("One Piece", texts);
            Assert.Contains("Blame", texts);
            Assert.Contains("Invincible", texts);
            Assert.Contains("NO LONGER IN LIBRARY", texts);
            Assert.Contains(texts, t => t?.StartsWith("#1090 · page 6 of 17") == true);

            var actionButtons = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("historyAction") && b.IsEffectivelyVisible)
                .ToList();
            Assert.Equal(2, actionButtons.Count); // ▶ for One Piece, Open for the PDF; none for the greyed row
            Assert.Contains(actionButtons, b => Equals(b.Content, "Open"));

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
