using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.EventMap;

[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMapRoomRenderTests
{
    [Fact]
    public void RenderMatrixAndRollCallInThreeThemes()
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
                    var rows = Enumerable.Range(0, 72).Select(i => Row(10 + i % 8, (i + 1).ToString(), i + 1,
                        read: i % 3 == 0 ? EventMapReadState.Read : EventMapReadState.Unread, seriesName: $"Series {i % 8 + 1}")).ToArray();
                    var vm = new EventMapViewModel(load: _ => Source("Event", rows), activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
                        runInBackground: work => Task.FromResult(work()));
                    vm.LoadAsync(1).GetAwaiter().GetResult();
                    vm.ShowMatrixModeCommand.Execute(null); vm.IsRollCallOpen = true;
                    var window = new Window { Width = 1366, Height = 768, Content = new EventMapView { DataContext = vm },
                        Background = (IBrush)app.FindResource("PbBgBrush")! };
                    window.Show(); RunLayout(window); TestDispatcher.Drain(); RunLayout(window);
                    foreach (bool covers in new[] { false, true })
                    {
                        vm.MatrixCovers = covers; RunLayout(window);
                        using var bitmap = new RenderTargetBitmap(new PixelSize(1366, 768)); bitmap.Render(window);
                        bitmap.Save(Path.Combine(output, $"matrix-{theme}-{(covers ? "covers" : "compact")}.png"));
                    }
                    window.Close();
                }
                finally { app.RequestedThemeVariant = previous; }
            });
    }

    [Fact]
    public void RenderMapAtThreeScalesAndThemes()
    {
        string? output = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        foreach (string theme in new[] { "default", "daylight", "matrix" })
        {
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var styles = AppStyles();
                var app = Application.Current!;
                var previousVariant = app.RequestedThemeVariant;
                bool hadPanel = app.Resources.TryGetValue("PbPanelFloatingBackground", out var previousPanel);
                app.RequestedThemeVariant = theme == "daylight" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                // The real theme projector supplies component-role resources; WithFoundation applies only base colours.
                app.Resources["PbPanelFloatingBackground"] = app.FindResource("PbSurface3Brush")!;
                try
                {
                var rows = Enumerable.Range(0, 72).Select(i => Row(10 + (i % 8), (i + 1).ToString(), i + 1,
                    read: i % 3 == 0 ? EventMapReadState.Read : EventMapReadState.Unread, seriesName: $"Series {(i % 8) + 1}")).ToArray();
                var source = Source("The event", rows);
                foreach (var (name, scale) in new[] { ("dots", 0.1), ("compact", 0.7), ("covers", 1.4) })
                foreach (var (mode, width) in new[] { ("regular", 1000), ("maximized", 1366) })
                {
                    var vm = new EventMapViewModel(load: _ => source, activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
                        runInBackground: work => Task.FromResult(work()));
                    vm.LoadAsync(1).GetAwaiter().GetResult();
                    vm.Scale = scale;
                    vm.Select(0, reveal: false);
                    vm.OpenInspectorCommand.Execute(null);
                    var window = new Window { Width = width, Height = 768, Content = new EventMapView { DataContext = vm },
                        Background = (IBrush)Application.Current!.FindResource("PbBgBrush")! };
                    window.Show();
                    RunLayout(window);
                    TestDispatcher.Drain();
                    RunLayout(window);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(width, 768));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(output, $"map-room-{theme}-{name}-{mode}.png"));
                    if (theme == "default" && name == "compact" && mode == "maximized")
                    {
                        using var highDpi = new RenderTargetBitmap(new PixelSize(2049, 1152), new Vector(144, 144));
                        highDpi.Render(window);
                        highDpi.Save(Path.Combine(output, "map-room-default-compact-maximized-150dpi.png"));
                    }
                    window.Close();
                }
                }
                finally
                {
                    app.RequestedThemeVariant = previousVariant;
                    if (hadPanel) app.Resources["PbPanelFloatingBackground"] = previousPanel;
                    else app.Resources.Remove("PbPanelFloatingBackground");
                }
            });
        }
    }
}
