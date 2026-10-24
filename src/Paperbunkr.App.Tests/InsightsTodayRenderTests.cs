using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Not an assertion: with PB_RENDER_OUT set, writes the Insights Today tab as a PNG per theme for a human look
/// (docs/superpowers/specs/2026-10-20-insights-today-redesign-design.md §Testing). The data is stand-in rows - no covers, so the cover plates are blank.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InsightsTodayRenderTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public InsightsTodayRenderTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_today_render_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
        context.ReadingGoals.Add(new ReadingGoal
        {
            Title = "Read 30 issues", Target = 30, PeriodStart = DateOnly.FromDateTime(DateTime.Today.AddDays(-20)),
            PeriodEnd = DateOnly.FromDateTime(DateTime.Today.AddDays(10)), CreatedUtc = DateTime.UtcNow,
        });
        context.SaveChanges();
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

    private sealed class FakeDialogService : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(true);
    }

    private static AttentionSeries Series(int id, string name, string subtitle, int? resume = null) => new(id, name, subtitle, resume);

    [Fact]
    public void RenderToday()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        foreach (string theme in new[] { "default", "daylight" })
        {
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var styles = AppStyles();
                var app = Application.Current!;
                var before = app.RequestedThemeVariant;
                app.RequestedThemeVariant = theme == "daylight" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                try
                {
                    var vm = new InsightsScreenViewModel(_ => { }, _ => { }, _ => { }, () => { }, new FakeDialogService(),
                        nowUtc: () => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));
                    vm.Refresh();

                    var almost = new[] { Series(1, "Absolute Catwoman", "1 issue left"), Series(2, "Teen Titans", "2 issues left"), Series(3, "Dark Nights: Metal", "3 issues left") };
                    var diveIn = new[]
                    {
                        Series(4, "Ultimate X-Men", "own 100 issues · #1–100, never opened"), Series(5, "Saga", "own 72 issues · #1–72, never opened"),
                        Series(6, "The Scorched", "own 55 issues · #1–55, never opened"), Series(7, "Spider-Verse Unlimited", "own 53 issues · #1–53, never opened"),
                        Series(8, "Hal Jordan And The Green Lantern Corps", "own 50 issues · #1–50, never opened"), Series(9, "Spider-Man Deadpool", "own 50 issues · #1–50, never opened"),
                    };
                    var gaps = new[]
                    {
                        new CollectionGap(10, "Invincible", new[] { 25 }), new CollectionGap(11, "Radiant Black", new[] { 31 }),
                        new CollectionGap(12, "X-Men", new[] { 37 }), new CollectionGap(13, "Dune: House Corrino", new[] { 4 }),
                        new CollectionGap(14, "Savage Avengers", new[] { 20, 21 }), new CollectionGap(15, "I Was Mistaken For The Villain", new[] { 6, 7, 10, 11, 21 }),
                    };
                    vm.Snapshot = new InsightsSnapshot(DateTime.UtcNow, new[] { Series(20, "Convergence", "0 of 9 read", 99) }, almost, diveIn, gaps);
                    vm.ContinueRows.Add(new AttentionRow("Convergence", "0 of 9 read", 99, 20, null));
                    foreach (var s in almost) vm.AlmostDoneRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, null, s.SeriesId, null));
                    foreach (var s in diveIn) vm.DiveInRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, null, s.SeriesId, null));
                    foreach (var g in gaps) vm.GapRows.Add(new GapRow(g.SeriesName, string.Join(", ", g.MissingNumbers.Select(n => "#" + n)), g.SeriesId, null));

                    var window = new Window
                    {
                        Content = new InsightsScreen { DataContext = vm }, Width = 1296, Height = 900,
                        Background = (IBrush)app.FindResource("PbBgBrush")!,
                    };
                    window.Show();
                    RunLayout(window);
                    var rtb = new RenderTargetBitmap(new PixelSize(1296, 900));
                    rtb.Render(window);
                    rtb.Save(Path.Combine(outDir, $"insights-today-{theme}.png"));
                    window.Close();
                }
                finally
                {
                    app.RequestedThemeVariant = before;
                }
            });
        }
    }
}
