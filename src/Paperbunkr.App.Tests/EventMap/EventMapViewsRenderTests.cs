using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>
/// Screenshots of M6a (List, Atlas) and M6b (progress line, You are here) at 1366×768 in Default, Daylight and Matrix. They only run with
/// <c>PB_RENDER_OUT</c> set (like the M1-M3 renders); a person checks the PNGs. Each still has to load and lay out without throwing.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMapViewsRenderTests
{
    [Fact]
    public void RenderListAtlasAndProgressLineInThreeThemes()
    {
        string? output = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        // Band art would read event covers through the app database; a test draws a fixed placeholder cover instead.
        var previousLoader = Paperbunkr.App.Controls.EventMap.EventMapCanvas.BandArtLoader;
        Paperbunkr.App.Controls.EventMap.EventMapCanvas.BandArtLoader = _ => null;
        try
        {
        foreach (string theme in new[] { "default", "daylight", "matrix" })
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var styles = AppStyles();
                var app = Application.Current!;
                var previous = app.RequestedThemeVariant;
                app.RequestedThemeVariant = theme == "daylight" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                try
                {
                    // Spine event: List view and the map with the progress line.
                    var spineRows = SpineSampleRows().Select((r, i) => r with { ReadState = i < 4 ? EventMapReadState.Read : EventMapReadState.Unread }).ToArray();
                    var vm = new EventMapViewModel(load: _ => Source("Crisis", spineRows), activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
                        runInBackground: work => Task.FromResult(work()));
                    vm.LoadAsync(1).GetAwaiter().GetResult();
                    var window = new Window { Width = 1366, Height = 768, Content = new EventMapView { DataContext = vm },
                        Background = (IBrush)app.FindResource("PbBgBrush")! };
                    window.Show(); RunLayout(window); TestDispatcher.Drain(); RunLayout(window);
                    Save(window, Path.Combine(output, $"map-progress-{theme}.png"));
                    vm.ShowListModeCommand.Execute(null);
                    RunLayout(window); TestDispatcher.Drain(); RunLayout(window);
                    Save(window, Path.Combine(output, $"list-{theme}.png"));
                    window.Close();

                    // Continuity: the Atlas.
                    var data = ContinuityMapTests.Sample();
                    var cvm = new EventMapViewModel(loadContinuity: _ => data, activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
                        runInBackground: work => Task.FromResult(work()));
                    cvm.LoadContinuityAsync(7).GetAwaiter().GetResult();
                    cvm.ShowAtlasModeCommand.Execute(null);
                    var cwindow = new Window { Width = 1366, Height = 768, Content = new EventMapView { DataContext = cvm },
                        Background = (IBrush)app.FindResource("PbBgBrush")! };
                    cwindow.Show(); RunLayout(cwindow); TestDispatcher.Drain(); RunLayout(cwindow);
                    Save(cwindow, Path.Combine(output, $"atlas-{theme}.png"));
                    cvm.ShowMapModeCommand.Execute(null);
                    RunLayout(cwindow); TestDispatcher.Drain(); RunLayout(cwindow);
                    Save(cwindow, Path.Combine(output, $"continuity-map-{theme}.png"));
                    cwindow.Close();
                }
                finally { app.RequestedThemeVariant = previous; }
            });
        }
        finally { Paperbunkr.App.Controls.EventMap.EventMapCanvas.BandArtLoader = previousLoader; }
    }

    [Fact]
    public void RenderMissingIssuesColourByAndBracketsInThreeThemes()
    {
        string? output = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        foreach (string theme in new[] { "default", "daylight", "matrix" })
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var styles = AppStyles();
                var app = Application.Current!;
                var previous = app.RequestedThemeVariant;
                app.RequestedThemeVariant = theme == "daylight" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                try
                {
                    var rows = Enumerable.Range(0, 10).Select(i => Row(i < 7 ? SeriesA : SeriesB, $"{i + 1}", i + 1,
                        read: i < 3 ? EventMapReadState.Read : EventMapReadState.Unread)).ToArray();
                    Paperbunkr.Data.Metadata.EventGhost Ghost(string series, string number, int? after, int? lane, Paperbunkr.Data.Metadata.EventGhostKind kind) =>
                        new(kind, new Paperbunkr.Data.Metadata.EventArcEntry(series, number, 2015, null, null, null, null, null, null, "1", "2"),
                            "Metron", DateTime.UtcNow, kind == Paperbunkr.Data.Metadata.EventGhostKind.OwnedNotMember ? 9 : null, after, lane);
                    var ghosts = new[]
                    {
                        Ghost("Adventure", "4", rows[2].IssueId, SeriesA, Paperbunkr.Data.Metadata.EventGhostKind.Missing),
                        Ghost("Weirdworld", "1", rows[7].IssueId, null, Paperbunkr.Data.Metadata.EventGhostKind.Missing),
                        Ghost("Brave", "4", rows[8].IssueId, SeriesB, Paperbunkr.Data.Metadata.EventGhostKind.OwnedNotMember),
                    };
                    var structure = new EventMapStructure(rows.ToDictionary(r => r.IssueId, r => new MapIssueStructure(Array.Empty<int>(), Array.Empty<int>(),
                        new MapCreator(r.SeriesId == SeriesA ? 1 : 2, r.SeriesId == SeriesA ? "Chris Claremont" : "John Byrne"), null, null, null)),
                        Array.Empty<MapEntity>(),
                        new[]
                        {
                            new MapBracket(SeriesA, MapBracketKind.Run, "Writer", 1, "Chris Claremont · #1–#7", "Claremont", rows.Take(7).Select(r => r.IssueId).ToList()),
                            new MapBracket(SeriesA, MapBracketKind.Arc, "", null, "Dark Phoenix", "Dark Phoenix", rows.Skip(1).Take(4).Select(r => r.IssueId).ToList()),
                        }, true);
                    var source = Source("No match", rows) with
                    {
                        Missing = new EventMissingData(ghosts, DateTime.UtcNow, true, new Dictionary<int, IReadOnlyList<string>>()),
                        Structure = structure,
                    };
                    var vm = new EventMapViewModel(load: _ => source, activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
                        runInBackground: work => Task.FromResult(work()));
                    vm.LoadAsync(1).GetAwaiter().GetResult();
                    vm.ShowMissing = true;
                    vm.ColourByText = "Writer";
                    vm.ShowArcs = true;
                    var window = new Window { Width = 1366, Height = 768, Content = new EventMapView { DataContext = vm },
                        Background = (IBrush)app.FindResource("PbBgBrush")! };
                    window.Show(); RunLayout(window); TestDispatcher.Drain(); RunLayout(window);
                    vm.Select(vm.Cards.First(c => c.IsMissingGhost).Index);
                    vm.OpenInspectorCommand.Execute(null);
                    RunLayout(window); TestDispatcher.Drain(); RunLayout(window);
                    Save(window, Path.Combine(output, $"missing-colourby-{theme}.png"));
                    window.Close();
                }
                finally { app.RequestedThemeVariant = previous; }
            });
    }

    private static void Save(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(1366, 768));
        bitmap.Render(window);
        bitmap.Save(path);
    }
}
