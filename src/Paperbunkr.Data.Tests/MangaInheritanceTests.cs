using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.Data.Tests;

/// <summary>New-chapter inheritance, override sync/reset and Manga to Comic restore (acceptance B, C, K).</summary>
public class MangaInheritanceTests : IDisposable
{
    private readonly MangaTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private int ConfirmedSeries(int chapters, Action<MangaSeriesEdit>? change = null)
    {
        int id = _db.SeedSeries(chapters);
        var service = new MangaSeriesMetadataService(_db.Factory);
        var edit = service.Load(id)!;
        service.Save(edit);
        edit = service.Load(id)!;
        change?.Invoke(edit);
        service.Save(edit);
        return id;
    }

    [Fact]
    public void ANewChapter_TakesTheSeriesValues_AndKeepsItsOwnFields()
    {
        int id = ConfirmedSeries(5, e =>
        {
            e.Values.Text[MangaInheritedFields.Publisher] = "Y";
            e.Values.Credits["Writer"] = new List<string> { "B" };
        });

        using var context = _db.CreateContext();
        var inheritance = MangaSeriesInheritance.Load(context, id)!;
        var chapter = new Issue { SeriesId = id, FilePath = "chapter101.cbz", Number = "101", Publisher = "From the file", Writer = "Old Writer", Translator = "New group" };

        var changed = inheritance.Apply(chapter);

        Assert.Equal("Y", chapter.Publisher);
        Assert.Equal("B", chapter.Writer);
        Assert.Equal("New group", chapter.Translator);
        Assert.Equal("101", chapter.Number);
        Assert.Contains("Publisher", changed);
        Assert.True(chapter.EmbeddedDirty);
    }

    [Fact]
    public void ABlankSeriesValue_NeverBlanksAChapter()
    {
        int id = ConfirmedSeries(3);
        using var context = _db.CreateContext();
        var inheritance = MangaSeriesInheritance.Load(context, id)!;
        var chapter = new Issue { SeriesId = id, Imprint = "Chapter imprint" };

        inheritance.Apply(chapter);

        Assert.Equal("Chapter imprint", chapter.Imprint);
    }

    [Fact]
    public void Load_IsNull_ForAnUnconfirmedSeries_AComic_AndARemoteSeries()
    {
        int unconfirmed = _db.SeedSeries(2);
        int comic = _db.SeedSeries(2, type: ContentType.Comic);
        using var context = _db.CreateContext();
        Assert.Null(MangaSeriesInheritance.Load(context, unconfirmed));

        // Confirm both, then compare: only the manga one inherits.
        var service = new MangaSeriesMetadataService(_db.Factory);
        service.Save(service.Load(unconfirmed)!);
        service.Save(service.Load(comic)!);
        using var after = _db.CreateContext();
        Assert.NotNull(MangaSeriesInheritance.Load(after, unconfirmed));
        Assert.Null(MangaSeriesInheritance.Load(after, comic));
    }

    [Fact]
    public void ARereadFromFile_DoesNotUndoTheSeriesValue_ExceptOnOverriddenChapters()
    {
        int id = ConfirmedSeries(4, e => e.Values.Text[MangaInheritedFields.Publisher] = "Y");
        using var context = _db.CreateContext();
        var issues = context.Issues.OrderBy(i => i.Id).ToList();
        issues[0].FieldOverrides.Add(new IssueFieldOverride { FieldKey = "Publisher" });
        context.SaveChanges();

        // A re-read puts the file's old value on every chapter...
        foreach (var issue in issues)
        {
            issue.Publisher = "Stale from file";
        }

        var inheritance = MangaSeriesInheritance.Load(context, id)!;
        var overrides = MangaSeriesInheritance.LoadOverrides(context, issues.Select(i => i.Id));
        foreach (var issue in issues)
        {
            inheritance.Apply(issue, overrides.GetValueOrDefault(issue.Id));
        }

        // ...and inheritance puts the series value back, except where the chapter is an exception.
        Assert.Equal("Stale from file", issues[0].Publisher);
        Assert.All(issues.Skip(1), i => Assert.Equal("Y", i.Publisher));
    }

    [Fact]
    public void EditingAChapterDirectly_CreatesAnOverride_AndMatchingTheSeriesRemovesIt()
    {
        int id = ConfirmedSeries(3);
        using var context = _db.CreateContext();
        var chapter = context.Issues.Include(i => i.Tags).Include(i => i.FieldOverrides).OrderBy(i => i.Id).First();

        chapter.Writer = "Guest";
        var created = MangaChapterOverrides.SyncAfterEdit(context, chapter);
        context.SaveChanges();

        Assert.Equal(new[] { "Writer" }, created);
        Assert.True(MangaChapterOverrides.IsOverridden(chapter, "Writer"));

        chapter.Writer = "A";
        var removed = MangaChapterOverrides.SyncAfterEdit(context, chapter);
        context.SaveChanges();

        Assert.Equal(new[] { "Writer" }, removed);
        Assert.False(MangaChapterOverrides.IsOverridden(chapter, "Writer"));
    }

    [Fact]
    public void SyncAfterEdit_DoesNothing_ForAComic()
    {
        int comic = _db.SeedSeries(2, type: ContentType.Comic);
        using var context = _db.CreateContext();
        var chapter = context.Issues.Include(i => i.Tags).Include(i => i.FieldOverrides).First();
        chapter.Writer = "Different";

        Assert.Empty(MangaChapterOverrides.SyncAfterEdit(context, chapter));
    }

    [Fact]
    public void ResetToSeries_RestoresOnlyThatChapter_AndTheOtherOverridesStay()
    {
        int id = ConfirmedSeries(4);
        using var context = _db.CreateContext();
        var chapters = context.Issues.Include(i => i.Tags).Include(i => i.FieldOverrides).OrderBy(i => i.Id).ToList();
        foreach (var c in chapters.Take(2))
        {
            c.Writer = "Guest";
            MangaChapterOverrides.SyncAfterEdit(context, c);
        }

        context.SaveChanges();

        Assert.True(MangaChapterOverrides.Reset(context, chapters[0], "Writer"));
        context.SaveChanges();

        using var verify = _db.CreateContext();
        var reloaded = verify.Issues.Include(i => i.FieldOverrides).OrderBy(i => i.Id).ToList();
        Assert.Equal("A", reloaded[0].Writer);
        Assert.Empty(reloaded[0].FieldOverrides);
        Assert.Equal("Guest", reloaded[1].Writer);
        Assert.Single(reloaded[1].FieldOverrides);
    }

    [Fact]
    public void ReclassifyingMangaToComic_RestoresEachChaptersOwnHistory()
    {
        // Chapters start with different writers (the deliberate per-issue history), the series is confirmed with "Keep as exceptions" off (Replace all), then flipped to Comic.
        int id = _db.SeedSeries(4, (n, issue) => issue.Writer = $"Writer {n}");
        var service = new MangaSeriesMetadataService(_db.Factory);
        var edit = service.Load(id)!;
        edit.Values.Credits["Writer"] = new List<string> { "Series Writer" };
        service.Save(edit, MangaExceptionChoice.ReplaceAll);

        using (var context = _db.CreateContext())
        {
            Assert.All(context.Issues, i => Assert.Equal("Series Writer", i.Writer));
            var series = context.Series.Single();
            series.ContentType = ContentType.Comic;
            context.SaveChanges();
        }

        using var verify = _db.CreateContext();
        var writers = verify.Issues.OrderBy(i => i.Id).Select(i => i.Writer).ToList();
        Assert.Equal(new[] { "Writer 1", "Writer 2", "Writer 3", "Writer 4" }, writers);
        Assert.Contains(verify.CreatorCredits.Include(c => c.Creator), c => c.Creator!.Name == "Writer 3");
    }

    [Fact]
    public void ReclassifyingComicToManga_ChangesNothingUntilTheUserSaves()
    {
        int id = _db.SeedSeries(3, (n, issue) => issue.Writer = $"Writer {n}", ContentType.Comic);

        using (var context = _db.CreateContext())
        {
            context.Series.Single().ContentType = ContentType.Manga;
            context.SaveChanges();
        }

        using var verify = _db.CreateContext();
        Assert.Equal(new[] { "Writer 1", "Writer 2", "Writer 3" }, verify.Issues.OrderBy(i => i.Id).Select(i => i.Writer).ToList());
        Assert.Empty(verify.IssueMetadataSnapshots);
    }
}
