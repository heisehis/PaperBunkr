using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="MetadataLinkResolver"/> (docs/superpowers/specs/2026-08-19-metadata-model-
/// anilist-search-and-link-design.md) against a fake <see cref="IMetadataProvider"/> - no real
/// network involved, same rationale as <see cref="HomeFeedResolverTests"/>'s real-SQLite approach.
/// </summary>
public class MetadataLinkResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public MetadataLinkResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_metadata_link_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
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

    private static int SeedSeries(PaperbunkrDbContext context, string name)
    {
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private sealed class FakeProvider : IMetadataProvider, IRelationsProvider
    {
        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.AniList;

        public List<MetadataSearchResult> SearchResults { get; } = new();

        public ExternalMediaMetadata? GetResult { get; set; }

        /// <summary>Keyed by externalId, so <see cref="MetadataLinkResolver.RefreshRelationsAsync"/>
        /// tests can control what a follow-up relation-target lookup returns without a second fake type.</summary>
        public Dictionary<string, ExternalMediaMetadata> GetResultsById { get; } = new();

        public List<ProviderRelation> Relations { get; } = new();

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(SearchResults);

        public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(GetResultsById.TryGetValue(externalId, out var byId) ? byId : GetResult);

        public Task<IReadOnlyList<ProviderRelation>> GetRelationsAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderRelation>>(Relations);
    }

    // --- SearchAsync ---

    [Fact]
    public async Task SearchAsync_ScoresAndOrdersByConfidenceDescending()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider();
        provider.SearchResults.Add(new MetadataSearchResult("2", "Naruto", "https://example/2"));
        provider.SearchResults.Add(new MetadataSearchResult("1", "One Piece", "https://example/1"));

        var matches = await MetadataLinkResolver.SearchAsync(provider, context, seriesId, "one piece", CancellationToken.None);

        Assert.Equal(2, matches.Count);
        Assert.Equal("1", matches[0].Result.ExternalId);
        Assert.Equal(MatchTier.Auto, matches[0].Tier);
        Assert.Equal("2", matches[1].Result.ExternalId);
        Assert.True(matches[0].Confidence > matches[1].Confidence);
    }

    [Fact]
    public async Task SearchAsync_MatchesAgainstKnownAlternateTitleToo()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Attack on Titan");
        context.SeriesTitles.Add(new SeriesTitle { SeriesId = seriesId, Value = "進撃の巨人", Type = SeriesTitleType.Native });
        context.SaveChanges();
        var provider = new FakeProvider();
        provider.SearchResults.Add(new MetadataSearchResult("1", "進撃の巨人", "https://example/1"));

        var matches = await MetadataLinkResolver.SearchAsync(provider, context, seriesId, "shingeki", CancellationToken.None);

        Assert.Equal(MatchTier.Auto, Assert.Single(matches).Tier);
    }

    [Fact]
    public async Task SearchAsync_UnknownSeries_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var provider = new FakeProvider();
        provider.SearchResults.Add(new MetadataSearchResult("1", "One Piece", null));

        var matches = await MetadataLinkResolver.SearchAsync(provider, context, seriesId: 999, "one piece", CancellationToken.None);

        Assert.Empty(matches);
    }

    // --- LinkAsync ---

    [Fact]
    public async Task LinkAsync_CreatesExternalMediaIdAndSnapshot()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("30013", "One Piece", "https://anilist.co/manga/30013", "desc", "RELEASING", 1100, 105),
        };

        bool linked = await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        Assert.True(linked);
        var link = Assert.Single(context.ExternalMediaIds.Where(e => e.SeriesId == seriesId));
        Assert.Equal(ExternalMetadataProvider.AniList, link.Provider);
        Assert.Equal("30013", link.ExternalId);
        Assert.NotNull(link.LastFetchedAt);
        Assert.Single(context.ExternalMetadataSnapshots.Where(s => s.SeriesId == seriesId));
    }

    [Fact]
    public async Task LinkAsync_CalledTwice_UpsertsRatherThanDuplicatingTheLink()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider { GetResult = new ExternalMediaMetadata("30013", "One Piece", null, null, null, null, null) };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);
        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        Assert.Single(context.ExternalMediaIds.Where(e => e.SeriesId == seriesId));
        // Snapshot is an append-only audit log, unlike the link itself - two calls means two snapshots.
        Assert.Equal(2, context.ExternalMetadataSnapshots.Count(s => s.SeriesId == seriesId));
    }

    [Fact]
    public async Task LinkAsync_AddsNativeRomanizedAndLocalizedTitles()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Attack on Titan");
        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("16498", "Attack on Titan", null, null, null, null, null,
                TitleEnglish: "Attack on Titan", TitleRomaji: "Shingeki no Kyojin", TitleNative: "進撃の巨人"),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "16498", CancellationToken.None);

        var titles = context.SeriesTitles.Where(t => t.SeriesId == seriesId).ToList();
        Assert.Contains(titles, t => t.Value == "進撃の巨人" && t.Type == SeriesTitleType.Native);
        Assert.Contains(titles, t => t.Value == "Shingeki no Kyojin" && t.Type == SeriesTitleType.Romanized);
        // "Attack on Titan" (TitleEnglish) is not added as a SeriesTitle - it's already Series.Name.
        Assert.DoesNotContain(titles, t => t.Value == "Attack on Titan");
        Assert.Equal(2, titles.Count);
    }

    [Fact]
    public async Task LinkAsync_CalledTwice_DoesNotDuplicateTitles()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Attack on Titan");
        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("16498", "Attack on Titan", null, null, null, null, null,
                TitleNative: "進撃の巨人"),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "16498", CancellationToken.None);
        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "16498", CancellationToken.None);

        Assert.Single(context.SeriesTitles.Where(t => t.SeriesId == seriesId));
    }

    [Fact]
    public async Task LinkAsync_ProviderReturnsNull_ReturnsFalseAndCreatesNothing()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider { GetResult = null };

        bool linked = await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        Assert.False(linked);
        Assert.Empty(context.ExternalMediaIds.Where(e => e.SeriesId == seriesId));
    }

    [Fact]
    public async Task LinkAsync_UnknownSeries_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var provider = new FakeProvider { GetResult = new ExternalMediaMetadata("1", "X", null, null, null, null, null) };

        bool linked = await MetadataLinkResolver.LinkAsync(provider, context, seriesId: 999, "1", CancellationToken.None);

        Assert.False(linked);
    }

    // --- LinkAsync: Series-scoped Summary/Status/Genre proposals (docs/superpowers/specs/
    // 2026-08-23-apply-from-provider-design.md) ---

    [Fact]
    public async Task LinkAsync_CreatesAcceptedSeriesScopedProposals_AndWritesFieldsDirectly()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("30013", "One Piece", null, "A pirate crew.", "RELEASING", 1100, 105, Genre: "Action, Adventure"),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        var series = context.Series.Find(seriesId)!;
        Assert.Equal("A pirate crew.", series.Summary);
        Assert.Equal(SeriesStatus.Ongoing, series.Status);
        Assert.Equal("Action, Adventure", series.Genre);

        var proposals = context.MetadataProposals.Where(p => p.SeriesId == seriesId).ToList();
        Assert.Equal(3, proposals.Count);
        Assert.All(proposals, p =>
        {
            Assert.Null(p.IssueId);
            Assert.Equal(MetadataProposalStatus.Accepted, p.Status);
            Assert.Equal(MetadataProposalSource.MetadataProvider, p.Source);
            Assert.Equal(ExternalMetadataProvider.AniList, p.ProviderKey);
            Assert.NotNull(p.ResolvedAt);
        });
        Assert.Contains(proposals, p => p.Field == MetadataProposalField.Summary && p.ProposedValue == "A pirate crew.");
        Assert.Contains(proposals, p => p.Field == MetadataProposalField.Status && p.ProposedValue == "Ongoing");
        Assert.Contains(proposals, p => p.Field == MetadataProposalField.Genre && p.ProposedValue == "Action, Adventure");
    }

    [Fact]
    public async Task LinkAsync_Relink_OverwritesManuallySetValueAndSnapshotsItAsCurrentValue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var series = context.Series.Find(seriesId)!;
        series.Summary = "My own hand-written summary.";
        context.SaveChanges();

        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("30013", "One Piece", null, "Provider summary.", null, null, null),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        Assert.Equal("Provider summary.", context.Series.Find(seriesId)!.Summary);
        var proposal = context.MetadataProposals.Single(p => p.SeriesId == seriesId && p.Field == MetadataProposalField.Summary);
        Assert.Equal("My own hand-written summary.", proposal.CurrentValue);
        Assert.Equal("Provider summary.", proposal.ProposedValue);
    }

    [Fact]
    public async Task LinkAsync_ProviderOmitsGenre_CreatesNoGenreProposal_AndLeavesExistingGenreUnchanged()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var series = context.Series.Find(seriesId)!;
        series.Genre = "Existing Genre";
        context.SaveChanges();

        // MangaBaka's real v2 API never returns genres (confirmed live, docs/mangabaka-metadata-ui-
        // research.md finding 14) - Genre defaults to null here, same shape.
        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("708", "Kagurabachi", null, "desc", "releasing", 128, 10),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "708", CancellationToken.None);

        Assert.Equal("Existing Genre", context.Series.Find(seriesId)!.Genre);
        Assert.Empty(context.MetadataProposals.Where(p => p.SeriesId == seriesId && p.Field == MetadataProposalField.Genre));
    }

    [Fact]
    public async Task LinkAsync_UnrecognizedStatusString_CreatesNoStatusProposal_AndLeavesExistingStatusUnchanged()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var series = context.Series.Find(seriesId)!;
        series.Status = SeriesStatus.Ongoing;
        context.SaveChanges();

        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("1", "X", null, null, "SOME_FUTURE_STATUS_VALUE", null, null),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "1", CancellationToken.None);

        Assert.Equal(SeriesStatus.Ongoing, context.Series.Find(seriesId)!.Status);
        Assert.Empty(context.MetadataProposals.Where(p => p.SeriesId == seriesId && p.Field == MetadataProposalField.Status));
    }

    // --- LinkAsync: Creator (docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-
    // design.md §3) ---

    [Fact]
    public async Task LinkAsync_CreatorProvided_WritesToSeriesCreator()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("30013", "One Piece", null, null, null, null, null, Creator: "Eiichiro Oda"),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        Assert.Equal("Eiichiro Oda", context.Series.Find(seriesId)!.Creator);
    }

    [Fact]
    public async Task LinkAsync_ProviderOmitsCreator_LeavesExistingCreatorUnchanged()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Kagurabachi");
        context.Series.Find(seriesId)!.Creator = "Existing Creator";
        context.SaveChanges();
        var provider = new FakeProvider { GetResult = new ExternalMediaMetadata("708", "Kagurabachi", null, null, null, null, null) };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "708", CancellationToken.None);

        Assert.Equal("Existing Creator", context.Series.Find(seriesId)!.Creator);
    }

    // --- LinkAsync: tag import (docs/superpowers/specs/2026-09-18-external-metadata-full-
    // extraction-design.md §4) ---

    [Fact]
    public async Task LinkAsync_TagsProvided_WritesToEveryIssueInTheSeries()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var issueA = new Issue { SeriesId = seriesId, Number = "1" };
        var issueB = new Issue { SeriesId = seriesId, Number = "2" };
        context.Issues.AddRange(issueA, issueB);
        context.SaveChanges();

        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("30013", "One Piece", null, null, null, null, null,
                GenreTags: new[] { "Action", "Adventure" },
                OtherTags: new[] { ("Pirates", "Setting") }),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        foreach (var issue in context.Issues.Include(i => i.Tags).Where(i => i.SeriesId == seriesId))
        {
            Assert.Contains(issue.Tags, t => t.Field == IssueTagField.Genre && t.Value == "Action");
            Assert.Contains(issue.Tags, t => t.Field == IssueTagField.Genre && t.Value == "Adventure");
            Assert.Contains(issue.Tags, t => t.Field == IssueTagField.Tags && t.Value == "Pirates" && t.Category == "Setting");
            Assert.Equal(IssueTagWeight.Unset, Assert.Single(issue.Tags, t => t.Value == "Pirates").Weight);
        }
    }

    [Fact]
    public async Task LinkAsync_Relink_SurvivingTagValue_KeepsItsHandSetWeight()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var issue = new Issue { SeriesId = seriesId, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        issue.Tags.Add(new IssueTag { IssueId = issue.Id, Field = IssueTagField.Tags, Value = "Pirates", Category = "Setting", Weight = IssueTagWeight.Core });
        context.SaveChanges();

        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("30013", "One Piece", null, null, null, null, null,
                OtherTags: new[] { ("Pirates", "Setting") }),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "30013", CancellationToken.None);

        var tag = Assert.Single(context.Issues.Include(i => i.Tags).First(i => i.Id == issue.Id).Tags);
        Assert.Equal(IssueTagWeight.Core, tag.Weight); // never re-inferred/overwritten on import
    }

    // --- LinkAsync: cross-reference auto-linking (docs/superpowers/specs/2026-09-18-external-
    // metadata-full-extraction-design.md §8) ---

    [Fact]
    public async Task LinkAsync_CrossReferenceProvided_InsertsExternalMediaId_WhenNoneExists()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("377", "One Piece", null, null, null, null, null,
                CrossReferences: new[] { (ExternalMetadataProvider.Kitsu, "38") }),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "377", CancellationToken.None);

        var kitsuLink = context.ExternalMediaIds.Single(e => e.SeriesId == seriesId && e.Provider == ExternalMetadataProvider.Kitsu);
        Assert.Equal("38", kitsuLink.ExternalId);
        Assert.Null(kitsuLink.LastFetchedAt); // asserted, not yet independently fetched
    }

    [Fact]
    public async Task LinkAsync_CrossReferenceProvided_NeverOverwritesAnExistingDifferingId()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = seriesId, Provider = ExternalMetadataProvider.Kitsu, ExternalId = "MANUALLY-LINKED-ID" });
        context.SaveChanges();

        var provider = new FakeProvider
        {
            GetResult = new ExternalMediaMetadata("377", "One Piece", null, null, null, null, null,
                CrossReferences: new[] { (ExternalMetadataProvider.Kitsu, "38") }),
        };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "377", CancellationToken.None);

        var kitsuLink = context.ExternalMediaIds.Single(e => e.SeriesId == seriesId && e.Provider == ExternalMetadataProvider.Kitsu);
        Assert.Equal("MANUALLY-LINKED-ID", kitsuLink.ExternalId); // left untouched, a real identity conflict
    }

    // --- LinkAsync: relation auto-upgrade (docs/superpowers/specs/2026-09-18-external-metadata-
    // full-extraction-design.md §5) ---

    [Fact]
    public async Task LinkAsync_MatchingPlaceholderExists_UpgradesToRealMediaRelation_AndDeletesPlaceholder()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceSeriesId = SeedSeries(context, "Prequel Series");
        int targetSeriesId = SeedSeries(context, "One Piece");
        context.ExternalMediaRelations.Add(new ExternalMediaRelation
        {
            SourceSeriesId = sourceSeriesId,
            Provider = ExternalMetadataProvider.AniList,
            TargetExternalId = "377",
            TargetTitle = "One Piece",
            RelationType = RelationType.Sequel,
        });
        context.SaveChanges();

        var provider = new FakeProvider { GetResult = new ExternalMediaMetadata("377", "One Piece", null, null, null, null, null) };

        await MetadataLinkResolver.LinkAsync(provider, context, targetSeriesId, "377", CancellationToken.None);

        Assert.Empty(context.ExternalMediaRelations);
        var relation = Assert.Single(context.MediaRelations);
        Assert.Equal(sourceSeriesId, relation.SourceSeriesId);
        Assert.Equal(targetSeriesId, relation.TargetSeriesId);
        Assert.Equal(RelationType.Sequel, relation.RelationType);
    }

    [Fact]
    public async Task LinkAsync_NoMatchingPlaceholder_IsANoOp()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider { GetResult = new ExternalMediaMetadata("377", "One Piece", null, null, null, null, null) };

        await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "377", CancellationToken.None);

        Assert.Empty(context.MediaRelations);
    }

    // --- RefreshRelationsAsync (docs/superpowers/specs/2026-09-18-external-metadata-full-
    // extraction-design.md §5/§7) - lazy, called separately from LinkAsync ---

    [Fact]
    public async Task RefreshRelationsAsync_TargetNotLinkedLocally_CreatesPlaceholder()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider();
        provider.Relations.Add(new ProviderRelation("40135", "Prequel Series", "https://anilist.co/manga/40135", RelationType.Prequel));

        await MetadataLinkResolver.RefreshRelationsAsync(provider, context, seriesId, "377", CancellationToken.None);

        var placeholder = Assert.Single(context.ExternalMediaRelations);
        Assert.Equal(seriesId, placeholder.SourceSeriesId);
        Assert.Equal("40135", placeholder.TargetExternalId);
        Assert.Equal("Prequel Series", placeholder.TargetTitle);
        Assert.Equal(RelationType.Prequel, placeholder.RelationType);
        Assert.Empty(context.MediaRelations);
    }

    [Fact]
    public async Task RefreshRelationsAsync_TargetAlreadyLinkedLocally_CreatesRealMediaRelation_NotAPlaceholder()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceSeriesId = SeedSeries(context, "One Piece");
        int targetSeriesId = SeedSeries(context, "Prequel Series");
        context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = targetSeriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "40135" });
        context.SaveChanges();

        var provider = new FakeProvider();
        provider.Relations.Add(new ProviderRelation("40135", "Prequel Series", null, RelationType.Prequel));

        await MetadataLinkResolver.RefreshRelationsAsync(provider, context, sourceSeriesId, "377", CancellationToken.None);

        Assert.Empty(context.ExternalMediaRelations);
        var relation = Assert.Single(context.MediaRelations);
        Assert.Equal(sourceSeriesId, relation.SourceSeriesId);
        Assert.Equal(targetSeriesId, relation.TargetSeriesId);
    }

    [Fact]
    public async Task RefreshRelationsAsync_CalledTwice_DoesNotDuplicate()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeProvider();
        provider.Relations.Add(new ProviderRelation("40135", "Prequel Series", null, RelationType.Prequel));

        await MetadataLinkResolver.RefreshRelationsAsync(provider, context, seriesId, "377", CancellationToken.None);
        await MetadataLinkResolver.RefreshRelationsAsync(provider, context, seriesId, "377", CancellationToken.None);

        Assert.Single(context.ExternalMediaRelations);
    }
}
