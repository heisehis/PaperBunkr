using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Scraper;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>Scrape on import and the review list (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md sections 2 and 9).</summary>
public sealed class ScrapeAutomationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pb-scrape-auto-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")}").Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private (int Comic, int Manga, int Scraped) Seed()
    {
        using var context = NewContext();
        var comic = new Issue { Series = new Series { Name = "Batman Beyond" }, Number = "1", FilePath = "C:/x/1.cbz" };
        var manga = new Issue { Series = new Series { Name = "Blame!", ContentType = ContentType.Manga }, Number = "1", FilePath = "C:/x/m.cbz" };
        var scraped = new Issue { Series = new Series { Name = "Done" }, Number = "1", FilePath = "C:/x/d.cbz", MetadataSource = ComicProvider.Metron };
        context.Issues.AddRange(comic, manga, scraped);
        context.SaveChanges();
        return (comic.Id, manga.Id, scraped.Id);
    }

    [Fact]
    public void Only_new_unscraped_comics_are_scraped_and_only_with_Metron_connected()
    {
        var (comic, manga, scraped) = Seed();
        var all = new[] { comic, manga, scraped };
        using var import = new ScrapeOnImport(new LibraryEvents(), NewContext, _ => throw new InvalidOperationException());

        Assert.Empty(import.Eligible(all)); // no Metron yet

        using (var context = NewContext())
        {
            CredentialStore.Set(context, "Metron", CredentialKind.ApiToken, "abc");
        }

        Assert.Equal(new[] { comic }, import.Eligible(all));

        using (var context = NewContext())
        {
            new ScrapeSettings { ScrapeNewFiles = false }.Save(context);
        }

        Assert.Empty(import.Eligible(all));
    }

    [Fact]
    public void The_review_list_groups_by_series_and_Never_marks_the_comics()
    {
        var (comic, _, _) = Seed();
        var queue = new ScrapeReviewQueue(Path.Combine(_root, "review.json"));
        queue.Add(new[] { new ScrapeReviewItem(comic, "Batman Beyond #1", "Couldn't confirm the cover match", DateTime.UtcNow), new ScrapeReviewItem(99999, "Gone #1", null, DateTime.UtcNow) });

        IReadOnlyList<int>? resolved = null;
        var vm = new ScrapeReviewQueueDialogViewModel(queue, NewContext, ids => resolved = ids);

        var group = Assert.Single(vm.Groups);              // the comic no longer in the library dropped out
        Assert.Equal("Batman Beyond", group.SeriesName);
        Assert.Single(queue.Load());

        vm.ReviewCommand.Execute(group);
        Assert.Equal(new[] { comic }, resolved);

        vm.NeverCommand.Execute(group);
        Assert.Empty(queue.Load());
        using var context = NewContext();
        Assert.True(context.Issues.Single(i => i.Id == comic).ScrapePermanentlySkipped);
    }

    [Fact]
    public void The_preview_applies_only_the_kept_comics()
    {
        var none = new Paperbunkr.Data.ComicVine.ComicVineDatePart(null, null, null);
        var volume = new Paperbunkr.Data.ComicVine.Scraping.ComicVineVolumeSearchResult(1, "Lobo", "2026", "DC Comics", 9, null);
        ScrapeProposal Proposal(int id, bool withIssue) => new(id, $"Lobo #{id}", ComicProvider.Metron, volume,
            withIssue ? new Paperbunkr.Data.ComicVine.ComicVineIssueDetails(id, 1, "Lobo", id.ToString(), "T", null, none, none, null,
                Array.Empty<Paperbunkr.Data.ComicVine.ComicVineIdName>(), Array.Empty<Paperbunkr.Data.ComicVine.ComicVineIdName>(), Array.Empty<Paperbunkr.Data.ComicVine.ComicVineIdName>(),
                Array.Empty<Paperbunkr.Data.ComicVine.ComicVineIdName>(), Array.Empty<Paperbunkr.Data.ComicVine.ComicVineCredit>()) : null,
            new[] { new ScrapeFieldChange("Title", "—", "T", false) });

        IReadOnlyCollection<int>? kept = null;
        var vm = new ScrapePreviewDialogViewModel(new[] { Proposal(1, true), Proposal(2, false), Proposal(3, true) }, ids => kept = ids);

        Assert.Equal("Apply 3", vm.ApplyLabel);
        Assert.Contains("1 matched the series only", vm.Subtitle);
        vm.DropSeriesOnlyCommand.Execute(null);
        vm.SelectedRow = vm.Rows[2];
        vm.ToggleSelectedCommand.Execute(null);
        Assert.Equal("Apply 1", vm.ApplyLabel);

        vm.ApplyCommand.Execute(null);
        Assert.Equal(new[] { 1 }, kept);
    }

    [Fact]
    public void The_review_list_starts_on_the_first_series_for_the_keyboard()
    {
        var (comic, _, _) = Seed();
        var queue = new ScrapeReviewQueue(Path.Combine(_root, "review2.json"));
        queue.Add(new[] { new ScrapeReviewItem(comic, "Batman Beyond #1", null, DateTime.UtcNow) });

        var vm = new ScrapeReviewQueueDialogViewModel(queue, NewContext, _ => { });

        Assert.Same(vm.Groups[0], vm.SelectedGroup);
    }
}
