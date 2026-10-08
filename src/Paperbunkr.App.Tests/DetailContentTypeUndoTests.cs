using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>The Detail header's Undo for an automatically classified series, and the manual picker locking it (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailContentTypeUndoTests : IDisposable
{
    private readonly string? _original;
    private readonly string _dbPath;

    public DetailContentTypeUndoTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_detailundo_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = Ctx();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext Ctx() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private int AddAutoClassified()
    {
        using var context = Ctx();
        var series = new Series { Name = "Solo Leveling" };
        series.Issues.Add(new Issue { FilePath = "/x/sl.cbz", Number = "1" });
        SeriesContentTypeEditor.ApplyAuto(series, ContentType.Manhwa, 0.97, null, DateTime.UtcNow.AddDays(-90)); // long past the queue's 30-day window
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static DetailScreenViewModel CreateViewModel(Action<int>? goDetailForSeries = null) =>
        new(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { }, goDetailForSeries: goDetailForSeries);

    [Fact]
    public void AnAutoClassifiedSeries_OffersUndo_NoMatterHowLongAgo()
    {
        int id = AddAutoClassified();
        var vm = CreateViewModel();

        vm.LoadSeries(id);

        Assert.True(vm.CanUndoContentType);
        Assert.Equal("Auto-classified: was Unknown", vm.UndoContentTypeLabel);
    }

    [Fact]
    public void Undo_RestoresTheTypeAndLocks_ThenReloadsTheScreen()
    {
        int id = AddAutoClassified();
        int reloaded = 0;
        var vm = CreateViewModel(_ => reloaded++);
        vm.LoadSeries(id);

        vm.UndoContentTypeCommand.Execute(null);
        TestDispatcher.Drain();

        using var verify = Ctx();
        var series = verify.Series.Single(s => s.Id == id);
        Assert.Equal(ContentType.Unknown, series.ContentType);
        Assert.Equal(ReadingMode.LeftToRight, series.ReadingMode);
        Assert.True(series.ContentTypeLocked);
        Assert.Equal(1, reloaded);

        vm.LoadSeries(id);
        Assert.False(vm.CanUndoContentType);
    }

    [Fact]
    public void ChoosingATypeInThePicker_LocksTheSeries_AndRemovesTheUndo()
    {
        int id = AddAutoClassified();
        var vm = CreateViewModel(_ => { });
        vm.LoadSeries(id);

        vm.SelectedContentType = ContentType.Manga;

        using var verify = Ctx();
        var series = verify.Series.Single(s => s.Id == id);
        Assert.Equal(ContentType.Manga, series.ContentType);
        Assert.Equal(ContentTypeSource.Manual, series.ContentTypeSource);
        Assert.True(series.ContentTypeLocked);
        Assert.Null(series.PreviousContentType);
    }

    [Fact]
    public void ASeriesNobodyAutoClassified_HasNoUndo()
    {
        int id;
        using (var context = Ctx())
        {
            var series = new Series { Name = "Plain", ContentType = ContentType.Comic };
            series.Issues.Add(new Issue { FilePath = "/x/p.cbz", Number = "1" });
            context.Series.Add(series);
            context.SaveChanges();
            id = series.Id;
        }

        var vm = CreateViewModel();
        vm.LoadSeries(id);

        Assert.False(vm.CanUndoContentType);
    }
}
