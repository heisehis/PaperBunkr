using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Issue Properties editor on a chapter of a confirmed manga series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D13): editing an
/// inherited field makes it an exception, resetting puts the series value back, and a Comic's editor is unaffected. Joins <see cref="AvaloniaTestCollection"/>
/// because the editor's cover lookup touches Avalonia media types.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MangaChapterEditorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mangaeditor_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public MangaChapterEditorTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext Context() => new(_options);

    private (int SeriesId, List<int> IssueIds) Seed(ContentType type, bool confirm)
    {
        using var context = Context();
        var series = new Series { Name = "Series", ContentType = type };
        for (int n = 1; n <= 3; n++)
        {
            series.Issues.Add(new Issue { FilePath = $"c{n}.cbz", Number = n.ToString(), Writer = "A", Publisher = "X" });
        }

        context.Series.Add(series);
        context.SaveChanges();
        if (confirm)
        {
            var service = new MangaSeriesMetadataService(Context);
            service.Save(service.Load(series.Id)!);
        }

        return (series.Id, context.Issues.OrderBy(i => i.Id).Select(i => i.Id).ToList());
    }

    private IssuePropertiesScreenViewModel Editor() => new(() => { }, Context);

    [Fact]
    public void EditingAnInheritedField_OnAMangaChapter_BecomesAnException()
    {
        var (_, issues) = Seed(ContentType.Manga, confirm: true);
        var editor = Editor();
        editor.Load(issues[1]);
        Assert.True(editor.IsInheritingFromSeries);

        editor.Writer = "Guest";
        editor.SaveCommand.Execute(null);

        using var context = Context();
        Assert.Contains(context.IssueFieldOverrides, o => o.IssueId == issues[1] && o.FieldKey == "Writer");
        Assert.DoesNotContain(context.IssueFieldOverrides, o => o.IssueId == issues[0]);

        // A series edit afterwards leaves the exception alone.
        var service = new MangaSeriesMetadataService(Context);
        var edit = service.Load(context.Series.Single().Id)!;
        edit.Values.Credits["Writer"] = new List<string> { "B" };
        service.Save(edit);

        using var verify = Context();
        var writers = verify.Issues.OrderBy(i => i.Id).Select(i => i.Writer).ToList();
        Assert.Equal(new[] { "B", "Guest", "B" }, writers);
    }

    [Fact]
    public void AnExceptionIsListed_AndResettingItPutsTheSeriesValueBack()
    {
        var (_, issues) = Seed(ContentType.Manga, confirm: true);
        var first = Editor();
        first.Load(issues[0]);
        first.Publisher = "Special";
        first.SaveCommand.Execute(null);

        var editor = Editor();
        editor.Load(issues[0]);
        var row = Assert.Single(editor.ChapterExceptions);
        Assert.Equal("Publisher", row.Key);
        Assert.Equal("X", row.SeriesValueText);

        row.ResetCommand.Execute(null);
        Assert.Equal("X", editor.Publisher);
        editor.SaveCommand.Execute(null);

        using var context = Context();
        Assert.Equal("X", context.Issues.Single(i => i.Id == issues[0]).Publisher);
        Assert.Empty(context.IssueFieldOverrides);
    }

    [Fact]
    public void AComicChapter_NeverGetsOverridesOrTheInheritanceBanner()
    {
        var (_, issues) = Seed(ContentType.Comic, confirm: false);
        var editor = Editor();
        editor.Load(issues[0]);
        Assert.False(editor.IsInheritingFromSeries);

        editor.Writer = "Someone else";
        editor.SaveCommand.Execute(null);

        using var context = Context();
        Assert.Empty(context.IssueFieldOverrides);
        Assert.Equal("Someone else", context.Issues.Single(i => i.Id == issues[0]).Writer);
    }
}
