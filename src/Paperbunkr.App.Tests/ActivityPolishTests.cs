using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #21 - Activity Center grouping by job type and the per-job progress ring.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ActivityPolishTests
{
    private static ActivityJob Job(ActivityJobKind kind, string title = "job") => new() { Kind = kind, Title = title };

    [Theory]
    [InlineData(ActivityJobKind.LibraryScan, "Library")]
    [InlineData(ActivityJobKind.LibraryVerify, "Library")]
    [InlineData(ActivityJobKind.GenerateCovers, "Covers and metadata")]
    [InlineData(ActivityJobKind.SyncMetadata, "Covers and metadata")]
    [InlineData(ActivityJobKind.TrackerFetch, "Covers and metadata")]
    [InlineData(ActivityJobKind.Acquisition, "Downloads and imports")]
    [InlineData(ActivityJobKind.Import, "Downloads and imports")]
    [InlineData(ActivityJobKind.RemoteSync, "Sharing")]
    [InlineData(ActivityJobKind.Plugin, "Plugins")]
    [InlineData(ActivityJobKind.Other, "Other")]
    public void CategoryFor_MapsEachKindToItsSection(ActivityJobKind kind, string expected)
        => Assert.Equal(expected, ActivityJobGrouping.CategoryFor(kind));

    [Fact]
    public void EveryJobKind_HasASection_SoNoNewKindEverFallsOutOfTheDrawer()
    {
        foreach (var kind in Enum.GetValues<ActivityJobKind>())
        {
            Assert.False(string.IsNullOrEmpty(ActivityJobGrouping.CategoryFor(kind)), $"{kind} has no section");
        }
    }

    [Fact]
    public void OneSection_IsReturnedAsThePlainJobList_NoLoneHeading()
    {
        var jobs = new[] { Job(ActivityJobKind.LibraryScan, "a"), Job(ActivityJobKind.LibraryVerify, "b") };

        var grouped = ActivityJobGrouping.Group(jobs);

        Assert.All(grouped, item => Assert.IsType<ActivityJob>(item));
        Assert.Equal(2, grouped.Count);
    }

    [Fact]
    public void NoJobs_IsEmpty()
        => Assert.Empty(ActivityJobGrouping.Group(Array.Empty<ActivityJob>()));

    [Fact]
    public void TwoOrMoreSections_GetAHeadingEach_InSectionOrder_KeepingJobOrderWithin()
    {
        var covers1 = Job(ActivityJobKind.GenerateCovers, "covers-1");
        var scan = Job(ActivityJobKind.LibraryScan, "scan");
        var covers2 = Job(ActivityJobKind.SyncMetadata, "meta-2");

        var grouped = ActivityJobGrouping.Group(new[] { covers1, scan, covers2 });

        Assert.Equal(5, grouped.Count);
        Assert.Equal(new ActivityGroupHeader("Library", 1), grouped[0]);
        Assert.Same(scan, grouped[1]);
        Assert.Equal(new ActivityGroupHeader("Covers and metadata", 2), grouped[2]);
        Assert.Same(covers1, grouped[3]);
        Assert.Same(covers2, grouped[4]);
    }

    [Fact]
    public void Ring_DrawsTheTrack_AndMoreArcAsProgressGrows()
    {
        int none = Lit(fraction: 0, indeterminate: false);
        int half = Lit(fraction: 0.5, indeterminate: false);
        int full = Lit(fraction: 1, indeterminate: false);
        int spinner = Lit(fraction: 0, indeterminate: true);

        Assert.True(none > 20, "the track is always drawn");
        Assert.True(half > none, "a half arc adds lit pixels over the bare track");
        Assert.True(full > half, "a full ring adds more still");
        Assert.True(spinner > none, "indeterminate shows a fixed quarter arc, not an empty ring");
    }

    [Fact]
    public void Ring_MeasuresToItsFixedSize()
    {
        var ring = new ProgressArcRing();

        ring.Measure(new Avalonia.Size(500, 500));

        Assert.Equal(ProgressArcRing.Size, ring.DesiredSize.Width);
        Assert.Equal(ProgressArcRing.Size, ring.DesiredSize.Height);
    }

    private static int Lit(double fraction, bool indeterminate)
    {
        var ring = new ProgressArcRing { Fraction = fraction, IsIndeterminate = indeterminate };
        var host = new Grid { Background = Brushes.Black, Children = { ring } };
        host.Resources["PbBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        host.Resources["PbAccentBrush"] = Brushes.White;
        var window = new Window { Width = 60, Height = 60, SizeToContent = SizeToContent.Manual, Content = host };
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
            int lit = 0;
            for (int y = 0; y < fb.Size.Height; y++)
            {
                Marshal.Copy(fb.Address + (y * fb.RowBytes), row, 0, fb.RowBytes);
                for (int x = 0; x < fb.Size.Width; x++)
                {
                    if (row[x * 4] + row[(x * 4) + 1] + row[(x * 4) + 2] > 150)
                    {
                        lit++;
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
