using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #13 - the empty-state line illustrations.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EmptyIllustrationTests
{
    [Fact]
    public void Measures_ToItsFixedDrawingSize()
    {
        var illustration = new EmptyIllustration();

        illustration.Measure(new Avalonia.Size(500, 500));

        Assert.Equal(EmptyIllustration.DrawingWidth, illustration.DesiredSize.Width);
        Assert.Equal(EmptyIllustration.DrawingHeight, illustration.DesiredSize.Height);
    }

    [Fact]
    public void EveryKind_DrawsSomething_OnABlackBackground()
    {
        foreach (var kind in Enum.GetValues<EmptyIllustrationKind>())
        {
            Assert.True(Lit(kind).Count > 30, $"{kind} drew (almost) nothing");
        }
    }

    [Fact]
    public void KindsLookDifferentFromEachOther()
    {
        var kinds = Enum.GetValues<EmptyIllustrationKind>();
        var shapes = kinds.ToDictionary(k => k, Lit);

        for (int i = 0; i < kinds.Length; i++)
        {
            for (int j = i + 1; j < kinds.Length; j++)
            {
                Assert.False(shapes[kinds[i]].SequenceEqual(shapes[kinds[j]]), $"{kinds[i]} and {kinds[j]} drew the same pixels");
            }
        }
    }

    /// <summary>The indices of every lit pixel - a signature of what was drawn.</summary>
    private static List<int> Lit(EmptyIllustrationKind kind)
    {
        var illustration = new EmptyIllustration { Kind = kind };
        // A white faint-foreground so it shows on black regardless of the test theme.
        var host = new Grid { Background = Brushes.Black, Children = { illustration } };
        host.Resources["PbTextFaintBrush"] = Brushes.White;
        var window = new Window { Width = 140, Height = 110, SizeToContent = SizeToContent.Manual, Content = host };
        window.Show();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            }

            var frame = (WriteableBitmap)window.GetLastRenderedFrame()!;
            using var fb = frame.Lock();
            var row = new byte[fb.RowBytes];
            var lit = new List<int>();
            for (int y = 0; y < fb.Size.Height; y++)
            {
                Marshal.Copy(fb.Address + (y * fb.RowBytes), row, 0, fb.RowBytes);
                for (int x = 0; x < fb.Size.Width; x++)
                {
                    if (row[x * 4] + row[(x * 4) + 1] + row[(x * 4) + 2] > 300)
                    {
                        lit.Add((y * fb.Size.Width) + x);
                    }
                }
            }

            return lit;
        }
        finally
        {
            window.Close();
        }
    }
}
