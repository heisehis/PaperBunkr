using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary><see cref="SeriesMergeHelper.MergeInto"/> keeps the removed series' identity (docs/superpowers/specs/2026-10-22-scrape-series-split-and-series-fields-design.md Q5).</summary>
public sealed class SeriesMergeIdentityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pb-merge-").FullName;

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

    [Fact]
    public void Merging_carries_links_and_titles_and_marks_the_survivor_keep_together()
    {
        int targetId;
        using (var context = NewContext())
        {
            var source = new Series { Name = "Secret Wars 2099", Titles = { new SeriesTitle { Value = "SW 2099", Type = SeriesTitleType.Localized } } };
            var target = new Series { Name = "Secret Wars" };
            context.Issues.Add(new Issue { Series = source, Number = "1" });
            context.Issues.Add(new Issue { Series = target, Number = "1", Volume = "2015" });
            context.SaveChanges();
            context.ComicMetadataExternalIds.AddRange(
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = source.Id, Provider = ComicProvider.Metron, ExternalId = "200" },
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = source.Id, Provider = ComicProvider.ComicVine, ExternalId = "9" },
                new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = target.Id, Provider = ComicProvider.Metron, ExternalId = "100" });
            context.SaveChanges();
            targetId = target.Id;

            var loadedSource = context.Series.Include(s => s.Issues).Single(s => s.Id == source.Id);
            var loadedTarget = context.Series.Include(s => s.Issues).Single(s => s.Id == target.Id);
            SeriesMergeHelper.MergeInto(context, loadedSource, loadedTarget);
            context.SaveChanges();
        }

        using var check = NewContext();
        var survivor = check.Series.Include(s => s.Titles).Single(s => s.Id == targetId);
        Assert.True(SeriesSplit.KeepsTogether(survivor));
        Assert.Equal("100", SeriesSplit.LinkOf(check, targetId, ComicProvider.Metron)); // the survivor's own link wins
        Assert.Equal("9", SeriesSplit.LinkOf(check, targetId, ComicProvider.ComicVine)); // a link it lacked is carried
        Assert.Contains(survivor.Titles, t => t.Value == "SW 2099");
        Assert.Equal(2, check.Issues.Count(i => i.SeriesId == targetId));
    }
}
