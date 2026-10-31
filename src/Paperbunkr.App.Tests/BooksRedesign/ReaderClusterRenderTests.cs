using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Views;
using Paperbunkr.App.Views.Reader;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The reader's floating clusters (books redesign §5.2). Asserts the parts show the right buttons; with PB_RENDER_OUT set, also writes
/// them on a Sepia and a Dark page for a human look.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderClusterRenderTests
{
    private static Grid Page(BookTheme theme)
    {
        var bg = ReaderChromeBackgroundConverter.Instance.Convert(theme, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture) as IBrush;
        var fg = ReaderChromeForegroundConverter.Instance.Convert(theme, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture) as IBrush;
        var noop = new RelayCommand(() => { });
        ReaderCluster Cluster(ReaderClusterPart part, HorizontalAlignment h, VerticalAlignment v) => new()
        {
            Part = part, HorizontalAlignment = h, VerticalAlignment = v, ClusterBackground = bg, ClusterForeground = fg,
            Title = "The Glass Orchard", Subtitle = "Nine Winters", BackCommand = noop, ContentsCommand = noop, SearchCommand = noop,
            BookmarkCommand = noop, IsBookmarked = true, NotesCommand = noop, SettingsCommand = noop,
            ProgressFraction = 0.37, PartTicks = [0.2, 0.52, 0.78], LeftLabel = "p. 214", RightLabel = "of 640",
        };

        var grid = new Grid { Background = Paperbunkr.App.Models.BookThemeBrushes.ContentBackground(theme) };
        grid.Children.Add(Cluster(ReaderClusterPart.Navigate, HorizontalAlignment.Left, VerticalAlignment.Top));
        grid.Children.Add(Cluster(ReaderClusterPart.Actions, HorizontalAlignment.Right, VerticalAlignment.Top));
        grid.Children.Add(Cluster(ReaderClusterPart.Page, HorizontalAlignment.Center, VerticalAlignment.Bottom));
        return grid;
    }

    [Fact]
    public void EachPart_ShowsOnlyItsOwnButtons()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            using var styles = AppStyles();
            var window = new Window { Content = Page(BookTheme.Sepia), Width = 1310, Height = 768 };
            window.Show();
            RunLayout(window);

            var visibleNames = window.GetLogicalDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible)
                .Select(b => AutomationProperties.GetName(b)).ToList();
            Assert.Contains("Back", visibleNames);
            Assert.Contains("Contents", visibleNames);
            Assert.Contains("Notes", visibleNames);
            Assert.Contains("Next page", visibleNames);
            Assert.DoesNotContain("Captures", visibleNames); // no command bound: hidden
            window.Close();
        });
    }

    [Fact]
    public void RenderClusters()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            using var styles = AppStyles();
            foreach (var theme in new[] { BookTheme.Sepia, BookTheme.Dark, BookTheme.Light })
            {
                var window = new Window { Content = Page(theme), Width = 1310, Height = 768 };
                window.Show();
                RunLayout(window);
                var rtb = new RenderTargetBitmap(new PixelSize(1310, 768));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, $"reader-clusters-{theme}.png"));
                window.Close();
            }
        });
    }
}
