using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Auto-levels, sharpen and auto-crop through the reader view model, settings, profiles, palette and page menu (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md). The pixel work itself is
/// covered in <see cref="PageImageProcessingTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderImageQualityTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_image_quality_test_{Guid.NewGuid():N}.db");
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_image_quality_test_{Guid.NewGuid():N}.cbz");
    private readonly int _issueId;
    private readonly int _seriesId;

    public ReaderImageQualityTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        CbzFixture.Create(_cbzPath, pageCount: 4);
        var series = new Series { Name = "Quality" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPath };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
        _seriesId = series.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in new[] { _dbPath, _cbzPath })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private ReaderScreenViewModel Load()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueId);
        return vm;
    }

    private void Configure(Action<AppSettings> apply)
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        apply(settings);
        context.SaveChanges();
    }

    private Issue StoredIssue()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Single(i => i.Id == _issueId);
    }

    // ===== Defaults, overrides, reset =====

    [Fact]
    public void Defaults_AreOff_AndTheSettingsFlowIn()
    {
        var plain = Load();
        Assert.False(plain.AutoLevels);
        Assert.Equal(0, plain.Sharpen);
        Assert.False(plain.AutoCrop);

        Configure(s => { s.DefaultAutoLevels = true; s.DefaultSharpen = 2; s.AutoCropMargins = true; });
        var configured = Load();

        Assert.True(configured.AutoLevels);
        Assert.Equal(2, configured.Sharpen);
        Assert.True(configured.AutoCrop);
        var processing = Assert.IsAssignableFrom<IReaderPageProcessing>(configured.Decoder);
        Assert.Equal(new PageProcessingOptions(true, true), processing.Processing);   // the pipeline was told
    }

    [Fact]
    public void ChangingAutoLevelsAndSharpen_IsStoredOnTheIssue_OnlyWhenItDiffersFromTheDefault()
    {
        var vm = Load();

        vm.AutoLevels = true;
        vm.Sharpen = 3;
        Assert.True(StoredIssue().AutoLevelsOverride);
        Assert.Equal(3, StoredIssue().SharpenOverride);
        Assert.Equal(new PageProcessingOptions(true, false), ((IReaderPageProcessing)vm.Decoder!).Processing);

        vm.AutoLevels = false;                                    // back to the default: the override goes away
        vm.Sharpen = 0;
        Assert.Null(StoredIssue().AutoLevelsOverride);
        Assert.Null(StoredIssue().SharpenOverride);
    }

    [Fact]
    public void AnIssuesOverrides_BeatTheDefaults_OnTheNextLoad()
    {
        Configure(s => { s.DefaultAutoLevels = true; s.DefaultSharpen = 1; });
        using (var context = PaperbunkrDb.CreateContext())
        {
            var issue = context.Issues.Single(i => i.Id == _issueId);
            issue.AutoLevelsOverride = false;
            issue.SharpenOverride = 3;
            context.SaveChanges();
        }

        var vm = Load();

        Assert.False(vm.AutoLevels);
        Assert.Equal(3, vm.Sharpen);
    }

    [Fact]
    public void SharpenIsClampedToCEsRange()
    {
        var vm = Load();

        vm.Sharpen = 9;

        Assert.Equal(3, vm.Sharpen);
        vm.CycleSharpenCommand.Execute(null);
        Assert.Equal(0, vm.Sharpen);                              // 3 wraps round to off
        vm.CycleSharpenCommand.Execute(null);
        Assert.Equal(1, vm.Sharpen);
    }

    [Fact]
    public void ResetAdjustment_AlsoClearsAutoLevelsAndSharpen()
    {
        var vm = Load();
        vm.AutoLevels = true;
        vm.Sharpen = 2;

        vm.ResetAdjustmentCommand.Execute(null);

        Assert.False(vm.AutoLevels);
        Assert.Equal(0, vm.Sharpen);
        Assert.Null(StoredIssue().AutoLevelsOverride);
        Assert.Null(StoredIssue().SharpenOverride);
        Assert.False(((IReaderPageProcessing)vm.Decoder!).Processing.AutoLevels);
    }

    [Fact]
    public void RefreshDisplaySettings_PicksUpAChangedDefault_InAnOpenReader()
    {
        var vm = Load();
        Assert.False(vm.AutoLevels);

        Configure(s => s.DefaultAutoLevels = true);
        vm.RefreshDisplaySettings();

        Assert.True(vm.AutoLevels);
        Assert.True(((IReaderPageProcessing)vm.Decoder!).Processing.AutoLevels);
    }

    // ===== Auto-crop: the visit switch and per-page overrides =====

    [Fact]
    public void ToggleAutoCrop_IsForTheVisitOnly()
    {
        var vm = Load();
        var toasts = new List<ToastRequest>();
        vm.ToastRequested += toasts.Add;

        vm.ToggleAutoCropCommand.Execute(null);

        Assert.True(vm.AutoCrop);
        Assert.True(((IReaderPageProcessing)vm.Decoder!).Processing.AutoCrop);
        Assert.Contains(toasts, t => t.Title == "Auto-crop on");
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.False(context.GetOrCreateAppSettings().AutoCropMargins);    // the setting is untouched
        }

        vm.GoBackCommand.Execute(null);                           // leaving the reader ends the visit's switch
        Assert.False(vm.AutoCrop);
    }

    [Fact]
    public void PageCropOverride_IsStored_ReloadedAndRemovedWhenSetBackToAuto()
    {
        var vm = Load();
        vm.GoToPage(2);

        vm.SetCurrentPageCropNeverCommand.Execute(null);
        using (var context = PaperbunkrDb.CreateContext())
        {
            var row = context.PageCropOverrides.Single();
            Assert.Equal(_issueId, row.IssueId);
            Assert.Equal(2, row.PageNumber);
            Assert.Equal(PageCropMode.Never, row.Mode);
        }

        vm.SetCurrentPageCropAlwaysCommand.Execute(null);         // the same page: the row changes, it is not duplicated
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(PageCropMode.Always, context.PageCropOverrides.Single().Mode);
        }

        Assert.Equal(PageCropMode.Always, vm.CurrentPageCropMode);
        Assert.Equal(PageCropMode.Always, Load().GoToPageAndGetCropMode(2));   // a later visit reads it back

        vm.SetCurrentPageCropAutoCommand.Execute(null);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Empty(context.PageCropOverrides);
        }

        Assert.Equal(PageCropMode.Auto, vm.CurrentPageCropMode);
    }

    [Fact]
    public void ShowCrop_ReportsWhatTheDetectorFinds()
    {
        var vm = Load();
        var toasts = new List<ToastRequest>();
        vm.ToastRequested += toasts.Add;

        vm.ShowCropCommand.Execute(null);                         // the fixture pages are solid colour: no border to find

        Assert.Contains(toasts, t => t.Title == "Nothing to trim");
    }

    // ===== Profiles =====

    [Fact]
    public void AProfile_CanSetAutoLevelsSharpenAndAutoCrop()
    {
        var settings = new AppSettings();
        var applied = ReaderProfileOverlay.Apply(settings, new ReaderProfileState(AutoLevels: true, Sharpen: 2, AutoCrop: true));

        Assert.True(applied.DefaultAutoLevels);
        Assert.Equal(2, applied.DefaultSharpen);
        Assert.True(applied.AutoCropMargins);
        Assert.False(settings.DefaultAutoLevels);                 // the stored settings are not touched

        var unset = ReaderProfileOverlay.Apply(new AppSettings { DefaultSharpen = 1 }, new ReaderProfileState());
        Assert.Equal(1, unset.DefaultSharpen);                    // a field a profile does not set leaves the default alone
    }

    [Fact]
    public void ACapturedProfile_IncludesTheLiveValues()
    {
        var vm = Load();
        vm.AutoLevels = true;
        vm.Sharpen = 1;

        var state = vm.CaptureProfileState()!;

        Assert.True(state.AutoLevels);
        Assert.Equal(1, state.Sharpen);
        Assert.False(state.AutoCrop);
    }

    // ===== Palette and page menu =====

    [Fact]
    public void ThePalette_OffersTheNewCommands()
    {
        var titles = Load().BuildPaletteEntries().Select(e => e.Title).ToList();

        Assert.Contains("Auto levels", titles);
        Assert.Contains("Sharpen: next level (off, 1, 2, 3)", titles);
        Assert.Contains("Auto-crop margins (this visit)", titles);
        Assert.Contains("Crop this page: never", titles);
        Assert.Contains("Show crop (what auto-crop finds)", titles);
    }

    [Fact]
    public void ThePageMenu_HasAnAutoCropSubmenu_InPagedMode()
    {
        var vm = Load();

        var entries = new ReaderPageContextMenuBuilder(vm).Build(null)!;

        Assert.Contains(entries, e => e.Header == "Auto-crop this page");
    }

    // ===== Sharpen and sampling (the paint-level pieces the canvas relies on) =====

    [Fact]
    public void TheSharpenKernel_RaisesTheContrastOfAnEdge_AndLeavesAFlatAreaAlone()
    {
        static (byte Dark, byte Light) EdgeAfter(int level)
        {
            using var source = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(source);
            canvas.Clear(new SKColor(200, 200, 200));
            using (var dark = new SKPaint { Color = new SKColor(60, 60, 60) })
            {
                canvas.DrawRect(new SKRect(0, 0, 8, 16), dark);         // a vertical edge between 60 and 200 at x = 8
            }

            using var target = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using var targetCanvas = new SKCanvas(target);
            using var image = SKImage.FromBitmap(source);
            using var filter = level == 0
                ? null
                : SKImageFilter.CreateMatrixConvolution(new SKSizeI(3, 3), ImageAdjustmentMath.CreateSharpenKernel(level), 1f, 0f, new SKPointI(1, 1), SKShaderTileMode.Clamp, false);
            using var paint = new SKPaint { ImageFilter = filter };
            targetCanvas.DrawImage(image, 0, 0, paint);
            return (target.GetPixel(7, 8).Red, target.GetPixel(8, 8).Red);
        }

        var plain = EdgeAfter(0);
        var sharp = EdgeAfter(3);

        Assert.Equal(60, plain.Dark);
        Assert.Equal(200, plain.Light);
        Assert.True(sharp.Dark < plain.Dark, $"dark side {sharp.Dark}");         // the dark side of the edge gets darker
        Assert.True(sharp.Light > plain.Light, $"light side {sharp.Light}");     // and the light side lighter
    }

    [Fact]
    public void Sampling_DownscalesThroughMipmaps_AndNeverUsesNearestNeighbour()
    {
        var down = ReaderPageVisualHandler.SamplingFor(highQuality: true, drawWidth: 600, sourceWidth: 1000);
        var up = ReaderPageVisualHandler.SamplingFor(highQuality: true, drawWidth: 1500, sourceWidth: 1000);
        var low = ReaderPageVisualHandler.SamplingFor(highQuality: false, drawWidth: 600, sourceWidth: 1000);

        Assert.Equal(SKMipmapMode.Linear, down.Mipmap);
        Assert.Equal(SKFilterMode.Linear, down.Filter);
        Assert.True(up.UseCubic);                                                // an enlargement is smoothed, not blocky
        Assert.Equal(SKFilterMode.Linear, low.Filter);
    }
}

internal static class ReaderImageQualityTestExtensions
{
    /// <summary>Goes to a page and returns its own auto-crop choice (a small helper for the reload test).</summary>
    public static PageCropMode GoToPageAndGetCropMode(this ReaderScreenViewModel vm, int page)
    {
        vm.GoToPage(page);
        return vm.CurrentPageCropMode;
    }
}
