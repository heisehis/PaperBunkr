using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.FontAwesome;
using Optris.Icons.Avalonia.MaterialDesign;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.ScrollHarness;

/// <summary>
/// Scripted-scroll measurement for the Library Poster grid (docs/superpowers/specs/
/// 2026-09-19-library-scroll-smoothness-design.md, Step 0 and every later step's before/after).
///
/// Everything runs on this process's main thread with a real Skia raster (headless, no window on
/// screen), so dispatcher and compositor ownership are consistent. For each scenario it sets
/// <see cref="ScrollViewer.Offset"/> once per simulated 60 Hz frame and records how long the UI
/// thread was busy in that frame (layout + realization + bindings + applying finished cover decodes
/// + CPU raster), how many newly visible cards had no cover yet, and the pipeline counters.
///
/// It is a relative before/after tool: CPU raster and layout are measured, GPU behavior is not.
/// </summary>
internal static class Program
{
    private const double FrameMs = 1000.0 / 60.0;

    /// <summary>Stubs out Skia rasterization (layout, bindings and composition serialization still run) to separate raster cost from UI-thread cost.</summary>
    private static bool s_stubDrawing;

    /// <summary>When set, saves the rendered frame at the top of the grid to this PNG path (needs real drawing, not --stub-drawing) so template changes can be compared by eye.</summary>
    private static string? s_snapshotPath;

    /// <summary>Prices each part of a poster card by building the real template many times and pruning one part type at a time.</summary>
    private static bool s_cardCost;

    /// <summary>When set, moves the (simulated) mouse over the first card and saves that frame, to check hover-only parts (selection checkbox) appear.</summary>
    private static string? s_hoverPath;

    /// <summary>Scroll the per-series card grid instead of the per-issue one.</summary>
    private static bool s_seriesGranularity;

    /// <summary>Use the Tiles cover-fit (row-style cards) instead of Poster.</summary>
    private static bool s_tiles;

    /// <summary>Prints how far the stock ScrollViewer moves for one simulated mouse-wheel notch (the distance smooth scrolling must keep).</summary>
    private static bool s_wheelProbe;

    private sealed record Scenario(string Name, double PixelsPerFrame, int Frames, int NotchEvery = 0);

    private static readonly Scenario[] Scenarios =
    {
        new("slow drag (8 px/frame)", 8, 240),
        new("medium (40 px/frame)", 40, 240),
        new("fling (160 px/frame)", 160, 120),
        new("wheel notches (96 px every 6th frame)", 96, 240, NotchEvery: 6),
    };

    [STAThread]
    private static int Main(string[] args)
    {
        int issueCount = ReadIntArg(args, "--issues", 3000);
        int seriesCount = ReadIntArg(args, "--series", 500);
        string label = ReadArg(args, "--label") ?? "run";
        bool noShadow = Array.IndexOf(args, "--no-shadow") >= 0;
        bool noEntrance = Array.IndexOf(args, "--no-entrance") >= 0;
        s_stubDrawing = Array.IndexOf(args, "--stub-drawing") >= 0;
        s_snapshotPath = ReadArg(args, "--snapshot");
        s_cardCost = Array.IndexOf(args, "--card-cost") >= 0;
        s_hoverPath = ReadArg(args, "--hover-snapshot");
        s_seriesGranularity = Array.IndexOf(args, "--series-granularity") >= 0;
        s_tiles = Array.IndexOf(args, "--tiles") >= 0;
        s_wheelProbe = Array.IndexOf(args, "--wheel-probe") >= 0;
        CoverPipelineStats.ForceLegacyCoverPath = Array.IndexOf(args, "--legacy-covers") >= 0;
        CoverPipelineStats.SimulatedDecodeDelayMs = ReadIntArg(args, "--decode-delay", 0);

        string root = Path.Combine(Path.GetTempPath(), $"pb_scroll_harness_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            return Run(root, seriesCount, issueCount, label, noShadow, noEntrance);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static int Run(string root, int seriesCount, int issueCount, string label, bool noShadow, bool noEntrance)
    {
        // Isolated database and cover folders: never touch the user's real library or thumbnails.
        PaperbunkrDbContext.DatabasePathOverride = Path.Combine(root, "harness.db");
        CoverThumbnailPaths.ThumbnailDirectory = Path.Combine(root, "thumbnails");
        BookCoverThumbnailPaths.ThumbnailDirectory = Path.Combine(root, "book-thumbnails");
        CustomCoverPaths.Directory = Path.Combine(root, "custom-covers");
        CustomBookCoverPaths.Directory = Path.Combine(root, "custom-book-covers");
        CoverCacheState.FilePath = Path.Combine(root, "cover-cache-state.json");
        Directory.CreateDirectory(CoverThumbnailPaths.ThumbnailDirectory);

        IconProvider.Current
            .Register<FontAwesomeIconProvider>()
            .Register<MaterialDesignIconProvider>();

        AppBuilder.Configure<Paperbunkr.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = s_stubDrawing })
            .SetupWithoutStarting();

        Console.WriteLine($"Seeding {issueCount} issues / {seriesCount} series...");
        var stems = Seed(seriesCount, issueCount);
        WriteThumbnails(stems);

        using (var context = PaperbunkrDb.CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            settings.LibraryViewMode = LibraryViewMode.PosterGrid;
            settings.LibraryGridCoverFit = s_tiles ? LibraryGridCoverFit.Tiles : LibraryGridCoverFit.Poster;
            context.SaveChanges();
        }

        var vm = new LibraryScreenViewModel(_ => { }, _ => { }, (_, _, _) => { });
        if (s_seriesGranularity)
        {
            vm.Granularity = LibraryContentGranularity.Series;
        }

        var view = new LibraryScreen { DataContext = vm };
        var window = new Window { Width = 1600, Height = 1000, Content = view };
        if (noShadow)
        {
            // Measurement-only override (never shipped): drop the cover Border's blurred BoxShadow to see what it costs.
            window.Styles.Add(new Style(x => x.OfType<Border>().Class("cover"))
            {
                Setters = { new Setter(Border.BoxShadowProperty, default(BoxShadows)) },
            });
            view.Styles.Add(new Style(x => x.OfType<Border>().Class("cover"))
            {
                Setters = { new Setter(Border.BoxShadowProperty, default(BoxShadows)) },
            });
        }

        window.Show();
        Settle(window);
        if (noEntrance)
        {
            // Measurement-only: switch the (never-reset) entrance flag off, as the planned one-shot fix would after the first burst.
            vm.PlayEntranceAnimation = false;
            Settle(window);
        }

        var scroll = view.FindControl<ScrollViewer>(s_tiles ? "TilesScrollViewer" : "PosterGridScrollViewer")
            ?? throw new InvalidOperationException("grid scroll viewer not found");
        if (!scroll.IsVisible)
        {
            throw new InvalidOperationException($"Poster grid is not the visible mode (ViewMode={vm.ViewMode}, fit={vm.GridCoverFit}, granularity={vm.Granularity})");
        }

        Console.WriteLine(
            $"Viewport {scroll.Viewport.Width:F0}x{scroll.Viewport.Height:F0}, extent {scroll.Extent.Height:F0} px, " +
            $"cards={vm.IssueList.FlatRows.Count} (granularity {vm.Granularity}), realized images at top={CountCoverImages(scroll)}.");
        if (s_snapshotPath is not null && !s_stubDrawing)
        {
            for (int i = 0; i < 20; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(80); // let the first covers decode and apply
            }

            window.GetLastRenderedFrame()?.Save(s_snapshotPath);
            Console.WriteLine($"Saved snapshot to {s_snapshotPath}");
        }

        if (s_hoverPath is not null && !s_stubDrawing)
        {
            window.MouseMove(new Point(98, 190), RawInputModifiers.None);
            for (int i = 0; i < 6; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(60);
            }

            window.GetLastRenderedFrame()?.Save(s_hoverPath);
            Console.WriteLine($"Saved hover snapshot to {s_hoverPath}");
            window.MouseMove(new Point(1400, 900), RawInputModifiers.None);
        }

        int realizedCards = CountCoverImages(scroll);
        int visuals = scroll.GetVisualDescendants().Count();
        Console.WriteLine($"Visual tree under the scroll viewer: {visuals} visuals for {realizedCards} realized cards (~{(realizedCards == 0 ? 0 : visuals / realizedCards)} per card, includes panel/scrollbar chrome).");
        Console.WriteLine();
        if (s_wheelProbe)
        {
            var origin = new Point(400, 400);
            window.MouseMove(origin, RawInputModifiers.None);
            Settle(window);

            foreach (bool smooth in new[] { false, true })
            {
                SmoothScrollSettings.Enabled = smooth;
                foreach (double notches in new[] { -1.0, -3.0 })
                {
                    scroll.Offset = new Vector(0, 5000);
                    Settle(window);
                    double before = scroll.Offset.Y;
                    window.MouseWheel(origin, new Vector(0, notches), RawInputModifiers.None);

                    // Step animation frames the way the render loop would and record where the offset is after each.
                    var path = new List<double>();
                    for (int frame = 0; frame < 40; frame++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Thread.Sleep(17);
                        path.Add(scroll.Offset.Y - before);
                    }

                    // The headless renderer ticks far slower than 60 Hz, so this shows the end state (and that the notch is consumed and
                    // animated), not the easing shape; the shape is covered by the WheelEasingTests unit tests.
                    Console.WriteLine($"smooth={smooth,-5} wheel delta {notches:F0}: settled at {path[^1]:F1} px");
                }
            }

            // Fast notches: three notches 30 ms apart must accumulate into one 150 px scroll, not restart.
            SmoothScrollSettings.Enabled = true;
            scroll.Offset = new Vector(0, 5000);
            Settle(window);
            double start = scroll.Offset.Y;
            for (int i = 0; i < 3; i++)
            {
                window.MouseWheel(origin, new Vector(0, -1), RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(30);
            }

            for (int frame = 0; frame < 60; frame++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(17);
            }

            Console.WriteLine($"3 fast notches accumulate to {scroll.Offset.Y - start:F1} px (expected 150)");

            // A touchpad-style fractional delta must pass through to the stock handler untouched.
            scroll.Offset = new Vector(0, 5000);
            Settle(window);
            double touchpadBefore = scroll.Offset.Y;
            window.MouseWheel(origin, new Vector(0, -0.2), RawInputModifiers.None);
            Settle(window);
            Console.WriteLine($"fractional (touchpad-style) delta -0.2 moved {scroll.Offset.Y - touchpadBefore:F1} px via the stock handler");

            window.Close();
            return 0;
        }

        if (s_cardCost)
        {
            RunCardCost(view, vm);
            window.Close();
            return 0;
        }

        Console.WriteLine($"=== {label} ({issueCount} issues / {seriesCount} series, Debug={IsDebug()}, noShadow={noShadow}, noEntrance={noEntrance}, stubDrawing={s_stubDrawing}) ===");
        Console.WriteLine(
            $"{"scenario",-40} {"frames",6} {"busy p50",9} {"p95",7} {"max",7} {">16.7ms",8} {"layout p50",11} {"jobs+raster p50",15} {"pop-in",7} " +
            $"{"decodes",8} {"applied",8} {"wasted",7} {"measures",9} {"realize",8} {"anim timers",12} {"MB",6} {"gridMB",7} {"grid#",6} {"shared#",8} {"dequeued",9} {"dropped",8}");

        foreach (var scenario in Scenarios)
        {
            RunScenario(window, scroll, scenario);
        }

        window.Close();
        return 0;
    }

    // ---------------------------------------------------------------- card cost attribution

    private static void RunCardCost(LibraryScreen view, LibraryScreenViewModel vm)
    {
        if (!view.TryFindResource("PosterGridIssueTemplate", out var resource) || resource is not Avalonia.Controls.Templates.IDataTemplate template)
        {
            throw new InvalidOperationException("PosterGridIssueTemplate not found");
        }

        var rows = vm.IssueList.FlatRows.OfType<Paperbunkr.App.Models.IssueListRow>().Take(240).ToList();
        var host = new WrapPanel { Width = 1500 };

        // The template's settings bindings walk $parent[UserControl].DataContext, so host the cards under a UserControl carrying the VM.
        var hostRoot = new UserControl { DataContext = vm, Content = new ScrollViewer { Content = host } };
        var costWindow = new Window { Width = 1600, Height = 1000, Content = hostRoot };
        costWindow.Show();
        Settle(costWindow);

        static bool IsCoverImage(Image image) => AsyncCoverImage.GetSourceId(image) is not null;

        // Variants that are not "remove a part": clear Transitions on a class via a style override, or drop a card's ToolTip.Tip.
        Style ClearTransitions(string cls) => new(x => x.OfType<Border>().Class(cls)) { Setters = { new Setter(Avalonia.Animation.Animatable.TransitionsProperty, null) } };
        Style ClearCheckBoxTransitions() => new(x => x.OfType<CheckBox>().Class("tileSelect")) { Setters = { new Setter(Avalonia.Animation.Animatable.TransitionsProperty, null) } };

        (string Name, Func<Control, bool> Remove)[] variants =
        {
            ("full card (baseline)", _ => false),
            ("- CheckBox (selection)", c => c is CheckBox),
            ("- BrandMark (publisher mark)", c => c is Paperbunkr.App.Controls.BrandMark),
            ("- Ellipse (unread dot)", c => c is Avalonia.Controls.Shapes.Ellipse),
            ("- dog-ear + plugin overlay Images", c => c is Image image && !IsCoverImage(image)),
            ("- both TextBlocks (title/subtitle)", c => c is TextBlock),
            ("- Border.posterScrim", c => c is Border b && b.Classes.Contains("posterScrim")),
            ("- all optional parts (bare cover)", c => c is CheckBox or Paperbunkr.App.Controls.BrandMark or Avalonia.Controls.Shapes.Ellipse or TextBlock
                || (c is Border bb && (bb.Classes.Contains("posterScrim") || bb.Classes.Contains("pbChip") || bb.Classes.Contains("countPill") || bb.Classes.Contains("ratingBadge")))
                || (c is Image im && !IsCoverImage(im))),
        };

        Console.WriteLine($"Card cost over {rows.Count} cards, attach + template apply + bindings + layout (Build excluded), best of 5 runs, Debug={IsDebug()}:");
        double? baseline = null;

        var extras = new (string Name, Style? Style, Action<Control>? Pre)[]
        {
            ("~ scrim Transitions cleared", ClearTransitions("posterScrim"), null),
            ("~ CheckBox Transitions cleared", ClearCheckBoxTransitions(), null),
            ("~ title tooltips removed", null, c =>
            {
                foreach (var tb in c.GetLogicalDescendants().OfType<TextBlock>())
                {
                    ToolTip.SetTip(tb, null);
                }
            }),
        };

        foreach (var (name, remove, style, pre) in variants.Select(v => (v.Name, v.Remove, (Style?)null, (Action<Control>?)null))
                     .Concat(extras.Select(e => (e.Name, (Func<Control, bool>)(_ => false), e.Style, e.Pre))))
        {
            if (style is not null)
            {
                hostRoot.Styles.Add(style);
            }

            double best = double.MaxValue;
            int visuals = 0;
            for (int run = 0; run < 5; run++)
            {
                host.Children.Clear();
                Settle(costWindow);

                var cards = new List<Control>(rows.Count);
                foreach (var row in rows)
                {
                    var card = template.Build(row) ?? throw new InvalidOperationException("template built null");
                    card.DataContext = row;
                    Prune(card, remove);
                    pre?.Invoke(card);
                    cards.Add(card);
                }

                long t0 = Stopwatch.GetTimestamp();
                foreach (var card in cards)
                {
                    host.Children.Add(card);
                }

                costWindow.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                double total = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                best = Math.Min(best, total);
                if (run == 0)
                {
                    visuals = cards.Sum(c => c.GetVisualDescendants().Count()) / cards.Count;
                }
            }

            if (style is not null)
            {
                hostRoot.Styles.Remove(style);
            }

            baseline ??= best;
            Console.WriteLine($"  {name,-38} {best,8:F1} ms  {best / rows.Count,6:F2} ms/card  saves {(baseline.Value - best) / rows.Count,6:F2} ms/card  (~{visuals} visuals/card)");
        }
    }

    /// <summary>Removes every logical descendant of <paramref name="root"/> matching <paramref name="remove"/> from its parent, before the card is attached.</summary>
    private static void Prune(Control root, Func<Control, bool> remove)
    {
        var matches = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<Control>().Where(remove).ToList();
        foreach (var match in matches)
        {
            switch (match.GetLogicalParent())
            {
                case Panel panel:
                    panel.Children.Remove(match);
                    break;
                case Decorator decorator when ReferenceEquals(decorator.Child, match):
                    decorator.Child = null;
                    break;
                case ContentControl content when ReferenceEquals(content.Content, match):
                    content.Content = null;
                    break;
            }
        }
    }

    private static bool IsDebug()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static void RunScenario(Window window, ScrollViewer scroll, Scenario scenario)
    {
        // Cold start every scenario: top of the list, empty in-memory cover cache, zeroed counters.
        scroll.Offset = new Vector(0, 0);
        CoverImageCache.Clear();
        Settle(window);
        CoverPipelineStats.Reset();
        GC.Collect();

        double maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var busy = new List<double>();
        var layoutMs = new List<double>();
        var rasterMs = new List<double>();
        double popInSum = 0;
        int popInFrames = 0;
        double offset = 0;

        for (int frame = 0; frame < scenario.Frames && offset < maxOffset; frame++)
        {
            double delta = scenario.NotchEvery > 0
                ? (frame % scenario.NotchEvery == 0 ? scenario.PixelsPerFrame : 0)
                : scenario.PixelsPerFrame;
            offset = Math.Min(maxOffset, offset + delta);

            var sample = RunFrame(window, scroll, offset);
            busy.Add(sample.Busy);
            layoutMs.Add(sample.Layout);
            rasterMs.Add(sample.Raster);

            var (visible, pending) = CountVisibleCovers(scroll);
            if (visible > 0)
            {
                popInSum += (double)pending / visible;
                popInFrames++;
            }
        }

        busy.Sort();
        layoutMs.Sort();
        rasterMs.Sort();
        double p50 = Percentile(busy, 0.50);
        double p95 = Percentile(busy, 0.95);
        double max = busy.Count == 0 ? 0 : busy[^1];
        int over = busy.Count(b => b > FrameMs);
        double popIn = popInFrames == 0 ? 0 : popInSum / popInFrames;
        double mb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

        Console.WriteLine($"    [{scenario.Name}] grid requests={CoverPipelineStats.GridRequests} wasted: stale={CoverPipelineStats.WastedStale} empty={CoverPipelineStats.WastedEmpty}; " +
            $"avg decode {(CoverPipelineStats.TimedDecodes == 0 ? 0 : CoverPipelineStats.DecodeMicros / 1000.0 / CoverPipelineStats.TimedDecodes):F2} ms, avg queue wait {(CoverPipelineStats.TimedDecodes == 0 ? 0 : CoverPipelineStats.QueueWaitMicros / 1000.0 / CoverPipelineStats.TimedDecodes):F2} ms, cores={Environment.ProcessorCount}");
        Console.WriteLine(
            $"{scenario.Name,-40} {busy.Count,6} {p50,8:F2}m {p95,6:F2}m {max,6:F1}m {over,8} {Percentile(layoutMs, 0.5),10:F2}m {Percentile(rasterMs, 0.5),14:F2}m {popIn,6:P0} " +
            $"{CoverPipelineStats.DecodesStarted,8} {CoverPipelineStats.DecodesApplied,8} {CoverPipelineStats.DecodesWasted,7} " +
            $"{CoverPipelineStats.PanelMeasurePasses,9} {CoverPipelineStats.PanelRealizeChanges,8} {CoverPipelineStats.EntranceTimersArmed,12} {mb,6:F0} {GridCoverCache.Shared.Bytes / (1024.0 * 1024.0),7:F1} {GridCoverCache.Shared.Count,6} {CoverImageCache.CachedCount,8} {CoverDecodeQueue.Shared.Dequeued,9} {CoverDecodeQueue.Shared.OverflowDropped,8}");
    }

    /// <summary>One simulated 60 Hz frame: apply the scroll, run layout and render, then keep pumping the dispatcher
    /// for the rest of the frame (that is where finished cover decodes get applied). Returns the UI-thread busy time.</summary>
    private readonly record struct FrameSample(double Busy, double Layout, double Raster);

    private static FrameSample RunFrame(Window window, ScrollViewer scroll, double offsetY)
    {
        long frameStart = Stopwatch.GetTimestamp();
        double busyMs = 0;

        long t0 = Stopwatch.GetTimestamp();
        scroll.Offset = new Vector(scroll.Offset.X, offsetY);

        // Layout pass alone (measure/arrange, container realization and binding), then everything
        // else the dispatcher has queued: render/raster jobs and applying finished cover decodes.
        window.UpdateLayout();
        double layout = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        long t1 = Stopwatch.GetTimestamp();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        double raster = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
        busyMs += layout + raster;

        while (Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds < FrameMs)
        {
            long t = Stopwatch.GetTimestamp();
            Dispatcher.UIThread.RunJobs();
            busyMs += Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            Thread.Sleep(1);
        }

        return new FrameSample(busyMs, layout, raster);
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(30);
        }
    }

    private static int CountCoverImages(ScrollViewer scroll) =>
        scroll.GetVisualDescendants().OfType<Image>().Count(i => AsyncCoverImage.GetSourceId(i) is not null);

    /// <summary>Cards whose cover <see cref="Image"/> is inside the viewport, and how many of those have no bitmap yet.</summary>
    private static (int Visible, int Pending) CountVisibleCovers(ScrollViewer scroll)
    {
        int visible = 0;
        int pending = 0;
        double height = scroll.Bounds.Height;
        foreach (var image in scroll.GetVisualDescendants().OfType<Image>())
        {
            if (AsyncCoverImage.GetSourceId(image) is null || !image.IsEffectivelyVisible)
            {
                continue;
            }

            var origin = image.TranslatePoint(new Point(0, 0), scroll);
            if (origin is not { } point || point.Y + image.Bounds.Height <= 0 || point.Y >= height)
            {
                continue;
            }

            visible++;
            if (image.Source is null)
            {
                pending++;
            }
        }

        return (visible, pending);
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];

    // ---------------------------------------------------------------- data

    private static readonly string[] Words =
    {
        "Batman", "Superman", "Spider", "Saga", "Hellboy", "Berserk", "Akira", "Warhammer", "Sandman",
        "Invincible", "Watchmen", "Preacher", "Locke", "Monstress", "Paper", "Girls", "Descender", "Bone",
        "Fables", "Transmet", "Ultimate", "Absolute", "Dark", "Knight", "Noir", "Origins",
    };

    private static List<string> Seed(int seriesCount, int issueCount)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
        var random = new Random(1234);

        var series = new List<Series>();
        for (int s = 0; s < seriesCount; s++)
        {
            series.Add(new Series
            {
                Name = $"{Words[random.Next(Words.Length)]} {Words[random.Next(Words.Length)]} {s}",
                ContentType = ContentType.Comic,
                Publisher = s % 3 == 0 ? "Marvel" : "Image",
            });
        }

        context.Series.AddRange(series);
        context.SaveChanges();

        var issues = new List<Issue>(issueCount);
        for (int i = 0; i < issueCount; i++)
        {
            var owner = series[i % seriesCount];
            issues.Add(new Issue
            {
                SeriesId = owner.Id,
                Number = (((i / seriesCount) + 1)).ToString(CultureInfo.InvariantCulture),
                Writer = "Writer " + (i % 40),
                Publisher = owner.Publisher,
                FilePath = $@"C:\Comics\{owner.Name}\{owner.Name} {(i / seriesCount) + 1:000}.cbz",
                FileSize = 50_000_000 + i,
                AddedTime = DateTime.UtcNow.AddMinutes(-i),
                LastPageRead = i % 4 == 0 ? 3 : null,
            });
        }

        context.Issues.AddRange(issues);
        context.SaveChanges();

        return issues.Select(i => CoverFingerprint.Stem(i.Id, i.FilePath, i.FileSize)).ToList();
    }

    /// <summary>Real 267x400 JPEGs (the app's 400 px-longest-edge thumbnail size, quality 85), a handful of
    /// distinct images with real entropy copied per cover so decode cost is realistic.</summary>
    private static void WriteThumbnails(List<string> stems)
    {
        var templates = new List<string>();
        for (int t = 0; t < 12; t++)
        {
            string path = Path.Combine(CoverThumbnailPaths.ThumbnailDirectory, $"__template{t}.jpg");
            using (var target = new RenderTargetBitmap(new PixelSize(267, 400), new Vector(96, 96)))
            {
                using (var ctx = target.CreateDrawingContext())
                {
                    var random = new Random(t * 977);
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))), null, new Rect(0, 0, 267, 400));
                    for (int i = 0; i < 140; i++)
                    {
                        var color = Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                        ctx.DrawRectangle(new SolidColorBrush(color), null, new Rect(random.Next(267), random.Next(400), random.Next(10, 120), random.Next(10, 120)));
                    }
                }

                target.Save(path, new JpegBitmapEncoderOptions { Quality = 85 });
            }

            templates.Add(path);
        }

        for (int i = 0; i < stems.Count; i++)
        {
            File.Copy(templates[i % templates.Count], CoverThumbnailPaths.GetCachePath(stems[i]), overwrite: true);
        }

        foreach (string template in templates)
        {
            File.Delete(template);
        }

        Console.WriteLine($"Wrote {stems.Count} thumbnails.");
    }

    // ---------------------------------------------------------------- args

    private static string? ReadArg(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ReadIntArg(string[] args, string name, int fallback) =>
        int.TryParse(ReadArg(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
}
