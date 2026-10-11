using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The publisher sweep must never overwrite a decision (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md): before provenance it
/// could not tell a deliberate "Unknown" from a never-classified series and would reclassify it as soon as a publisher matched.
/// </summary>
public class ContentTypeSweepLockTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public ContentTypeSweepLockTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(_connection).Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static Series VizSeries(string name)
    {
        var series = new Series { Name = name };
        series.Issues.Add(new Issue { FilePath = $"/x/{name}.cbz", Publisher = "VIZ Media" });
        return series;
    }

    [Fact]
    public void Sweep_ClassifiesAnOpenUnknownSeries_AndStampsPublisher_ButLeavesALockedUnknownAlone()
    {
        using (var context = new PaperbunkrDbContext(_options))
        {
            var locked = VizSeries("Deliberately Unknown");
            SeriesContentTypeEditor.SetManual(locked, ContentType.Unknown);
            context.Series.AddRange(VizSeries("Open"), locked);
            context.SaveChanges();
        }

        int changed = new LibraryFolderScanner(() => new PaperbunkrDbContext(_options)).RunContentTypeSweepCore(CancellationToken.None);

        Assert.Equal(1, changed);
        using var verify = new PaperbunkrDbContext(_options);
        var open = verify.Series.Single(s => s.Name == "Open");
        Assert.Equal(ContentType.Manga, open.ContentType);
        Assert.Equal(ReadingMode.RightToLeft, open.ReadingMode);
        Assert.Equal(ContentTypeSource.Publisher, open.ContentTypeSource);
        Assert.False(open.ContentTypeLocked);

        var kept = verify.Series.Single(s => s.Name == "Deliberately Unknown");
        Assert.Equal(ContentType.Unknown, kept.ContentType);
        Assert.True(kept.ContentTypeLocked);
    }
}
