using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>"Undo this batch" (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 3).</summary>
public sealed class ScrapeUndoStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pb-undo-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")}").Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void Undo_puts_the_scraped_fields_back_and_leaves_the_rest_alone()
    {
        int id;
        using (var context = CreateDbContext())
        {
            var issue = new Issue { Series = new Series { Name = "Batman Beyond" }, Number = "1", Title = "Mine", Writer = null, Year = null, OpenCount = 0 };
            context.Issues.Add(issue);
            context.SaveChanges();
            id = issue.Id;
        }

        var store = new ScrapeUndoStore(Path.Combine(_root, "undo"));
        ScrapeUndoBatch batch;
        using (var context = CreateDbContext())
        {
            batch = ScrapeUndoStore.Capture(context, new[] { id }, "1 comic");
        }

        using (var context = CreateDbContext())
        {
            var issue = context.Issues.Single(i => i.Id == id);
            issue.Title = "Neo-Gothic";
            issue.Writer = "Jurgens";
            issue.Year = 2023;
            issue.MetadataSource = ComicProvider.Metron;
            issue.OpenCount = 3; // read after the scrape: not the scrape's to undo
            context.SaveChanges();
        }

        store.Save(batch, new[] { id });
        var saved = Assert.Single(store.List());

        using (var context = CreateDbContext())
        {
            Assert.Equal(new[] { id }, store.Undo(context, saved));
        }

        using (var context = CreateDbContext())
        {
            var issue = context.Issues.Single(i => i.Id == id);
            Assert.Equal("Mine", issue.Title);
            Assert.Null(issue.Writer);
            Assert.Null(issue.Year);
            Assert.Null(issue.MetadataSource);
            Assert.Equal(3, issue.OpenCount);
        }

        Assert.Empty(store.List());
    }

    [Fact]
    public void Only_changed_comics_are_kept_and_old_batches_are_pruned()
    {
        int id;
        using (var context = CreateDbContext())
        {
            var issue = new Issue { Series = new Series { Name = "X" }, Number = "1" };
            context.Issues.Add(issue);
            context.SaveChanges();
            id = issue.Id;
        }

        var store = new ScrapeUndoStore(Path.Combine(_root, "undo"));
        using (var context = CreateDbContext())
        {
            store.Save(ScrapeUndoStore.Capture(context, new[] { id }, "nothing applied"), Array.Empty<int>());
            Assert.Empty(store.List());

            store.Save(ScrapeUndoStore.Capture(context, new[] { id }, "applied"), new[] { id });
        }

        Assert.Single(store.List());
        Assert.Empty(store.List(DateTime.UtcNow.AddDays(31)));
    }
}
