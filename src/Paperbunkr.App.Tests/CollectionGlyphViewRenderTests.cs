using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>The shared collection icon control draws every kind (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class CollectionGlyphViewRenderTests : IDisposable
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" r=\"5\" fill=\"#2a6\"/></svg>";

    private readonly string _file = "pb-test.svg";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-glyphview-" + Guid.NewGuid().ToString("N"));
    private readonly string? _originalRoot;

    // The control reads the real "My icons" folder, so the app-data root points at a temp folder for the test (as MarkResolverUserIconTests does).
    public CollectionGlyphViewRenderTests()
    {
        _originalRoot = Paperbunkr.Data.AppDataPaths.RootOverride;
        Paperbunkr.Data.AppDataPaths.RootOverride = _root;
        Directory.CreateDirectory(CollectionIconFolder.Path);
        File.WriteAllText(Path.Combine(CollectionIconFolder.Path, _file), Svg);
    }

    public void Dispose()
    {
        Paperbunkr.Data.AppDataPaths.RootOverride = _originalRoot;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("Rocket", CollectionIconKind.Symbol)]
    [InlineData("hero:domino-mask", CollectionIconKind.Hero)]
    [InlineData("publisher:Marvel", CollectionIconKind.Publisher)]
    public void EachKind_DrawsItsOwnChild(string value, CollectionIconKind kind)
    {
        var view = new CollectionGlyphView { Value = value, IconSize = 24, Accent = Brushes.OrangeRed };

        Assert.Equal(kind, view.DrawnKind);
        Assert.NotNull(view.Child);
    }

    [Fact]
    public void AFileIcon_DrawsAnImage_AndAMissingOneDrawsNothing()
    {
        var view = new CollectionGlyphView { Value = "file:" + _file, IconSize = 24 };
        Assert.Equal(CollectionIconKind.File, view.DrawnKind);
        Assert.IsType<Image>(view.Child);

        view.Value = "file:Gone.svg";
        Assert.Null(view.DrawnKind);
        Assert.Null(view.Child);
    }

    [Fact]
    public void AHeroShape_TakesTheAccent_OrTheInheritedForeground()
    {
        var view = new CollectionGlyphView { Value = "hero:cape", IconSize = 24, Accent = Brushes.OrangeRed };
        Assert.Same(Brushes.OrangeRed, ((Avalonia.Controls.Shapes.Path)view.Child!).Fill);

        var host = new Border { Child = new CollectionGlyphView { Value = "hero:cape", IconSize = 24 } };
        Avalonia.Controls.Documents.TextElement.SetForeground(host, Brushes.Teal);
        var window = new Window { Content = host, Width = 100, Height = 100 };
        window.Show();
        var path = (Avalonia.Controls.Shapes.Path)((CollectionGlyphView)host.Child!).Child!;
        Assert.Same(Brushes.Teal, path.Fill);
        window.Close();
    }

    [Fact]
    public void AllKinds_Render()
    {
        // BrandMark's template lives in Marks.axaml, which the bare test application does not load.
        var marks = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/Marks.axaml") };
        Application.Current!.Styles.Add(marks);
        try
        {
            RenderAllKinds();
        }
        finally
        {
            Application.Current!.Styles.Remove(marks);
        }
    }

    private void RenderAllKinds()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8), Background = Brushes.White };
        foreach (string value in new[] { "Rocket", "hero:domino-mask", "hero:cape", "hero:fist", "hero:power-lightning", "publisher:Marvel", "publisher:DC Comics", "file:" + _file })
        {
            row.Children.Add(new CollectionGlyphView { Value = value, IconSize = 24, Accent = Brushes.OrangeRed, VerticalAlignment = VerticalAlignment.Center });
        }

        var window = new Window { Content = row, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Thread.Sleep(200); // the file icon renders on a worker thread
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.All(row.Children.OfType<CollectionGlyphView>(), v => Assert.NotNull(v.Child));
        if (Environment.GetEnvironmentVariable("PB_RENDER_OUT") is { Length: > 0 } outDir)
        {
            var size = new PixelSize((int)Math.Ceiling(row.Bounds.Width + 16), (int)Math.Ceiling(row.Bounds.Height + 16));
            using var bitmap = new RenderTargetBitmap(size);
            bitmap.Render(row);
            Directory.CreateDirectory(outDir);
            bitmap.Save(Path.Combine(outDir, "collection-glyph-kinds.png"));
        }

        window.Close();
    }
}
