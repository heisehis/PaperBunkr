using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Library Health's Content Type queue after the tracker-driven classifier (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md):
/// suggestion rows, rows a person already decided dropping out, the four row actions, bulk accept, and the recently-auto-classified list with Undo.
/// Same DatabasePathOverride approach as <see cref="NeedsReviewViewModelTests"/> (the view model has no context-factory seam).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContentTypeReviewQueueTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ContentTypeReviewQueueTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_ctqueue_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = Ctx();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
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

    private static NeedsReviewViewModel CreateViewModel() => new(onOpenSeriesDetail: _ => { });

    private static string Evidence(ContentType type, double score, string raw, ExternalMetadataProvider provider = ExternalMetadataProvider.MangaBaka, bool queueOnly = false) =>
        ContentTypeEvidence.Serialize(new[] { new ContentTypeEvidenceItem(provider, "1", "Title", score, raw, type, queueOnly) })!;

    private int AddSeries(string name, Action<Series>? configure = null, string? publisher = null)
    {
        using var context = Ctx();
        var series = new Series { Name = name };
        series.Issues.Add(new Issue { FilePath = $"/x/{name}.cbz", Number = "1", Publisher = publisher });
        configure?.Invoke(series);
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void Queue_ListsUnknownAndSuggestedSeries_ButNotLockedOrSkippedOnes()
    {
        AddSeries("Plain Unknown");
        AddSeries("Suggested", s =>
        {
            s.ContentTypeSuggestion = ContentType.Manhwa;
            s.ContentTypeConfidence = 0.97;
            s.ContentTypeEvidence = Evidence(ContentType.Manhwa, 0.97, "manhwa");
        });
        AddSeries("Guessed Manga With A Better Idea", s =>
        {
            s.ContentType = ContentType.Manga;
            s.ContentTypeSource = ContentTypeSource.Publisher;
            s.ContentTypeSuggestion = ContentType.Manhwa;
            s.ContentTypeConfidence = 0.8;
            s.ContentTypeEvidence = Evidence(ContentType.Manhwa, 0.8, "manhwa");
        });
        AddSeries("Locked Unknown", s => s.ContentTypeLocked = true);
        AddSeries("Skipped", s => s.ContentTypeCheck = ContentTypeCheck.Skipped);
        AddSeries("Known Manga", s => s.ContentType = ContentType.Manga);

        var vm = CreateViewModel();

        Assert.Equal(new[] { "Suggested", "Guessed Manga With A Better Idea", "Plain Unknown" }, vm.ContentTypeItems.Select(i => i.SeriesName).ToArray());
        var suggested = vm.ContentTypeItems[0];
        Assert.True(suggested.HasSuggestion);
        Assert.Equal("97%", suggested.ConfidenceLabel);
        Assert.True(suggested.IsHighConfidence);
        Assert.Equal("MangaBaka: manhwa", Assert.Single(suggested.Chips).Text);
        Assert.False(vm.ContentTypeItems[1].IsHighConfidence); // 80% is the review tier, not the auto tier
    }

    [Fact]
    public void Queue_ConflictingSources_AreFlagged_AndNeverHighConfidence()
    {
        AddSeries("Disputed", s =>
        {
            s.ContentTypeSuggestion = ContentType.Manhwa;
            s.ContentTypeConfidence = 0.97;
            s.ContentTypeEvidence = ContentTypeEvidence.Serialize(new[]
            {
                new ContentTypeEvidenceItem(ExternalMetadataProvider.MangaBaka, "1", "Disputed", 0.97, "manhwa", ContentType.Manhwa, false),
                new ContentTypeEvidenceItem(ExternalMetadataProvider.AniList, "2", "Disputed", 0.97, "JP", ContentType.Manga, false),
            });
        });

        var vm = CreateViewModel();

        var item = Assert.Single(vm.ContentTypeItems);
        Assert.True(item.IsConflict);
        Assert.False(item.IsHighConfidence);
        Assert.Equal(0, vm.HighConfidenceCount);
    }

    [Fact]
    public void Queue_ExplainsRowsWithNoSuggestion_AndSummarisesWhatItIsNotAsking()
    {
        AddSeries("Never Looked Up");
        AddSeries("No Match Found", s =>
        {
            s.ContentTypeCheck = ContentTypeCheck.NoMatch;
            s.ContentTypeCheckedUtc = DateTime.UtcNow;
        });
        AddSeries("Ghost Of Western", s => s.GcdSeriesId = 42);

        var vm = CreateViewModel();

        string StatusOf(string name) => vm.ContentTypeItems.Single(i => i.SeriesName == name).StatusLabel;
        Assert.Equal("Not looked up yet", StatusOf("Never Looked Up"));
        Assert.Equal("No match on the tracker sites", StatusOf("No Match Found"));
        Assert.Equal("Not looked up: matched in the grand comics database", StatusOf("Ghost Of Western"));
        Assert.Equal("1 not looked up (Western evidence) · 1 with no tracker match", vm.ContentTypeSummary);
    }

    [Fact]
    public void Accept_AppliesTheSuggestion_Locks_AndLeavesTheQueue()
    {
        int id = AddSeries("Solo Leveling", s =>
        {
            s.ContentTypeSuggestion = ContentType.Manhwa;
            s.ContentTypeConfidence = 0.97;
        });
        var vm = CreateViewModel();

        vm.AcceptContentTypeCommand.Execute(vm.ContentTypeItems.Single());
        TestDispatcher.Drain(); // the list refresh is deferred one dispatcher tick

        Assert.Empty(vm.ContentTypeItems);
        using var verify = Ctx();
        var series = verify.Series.Single(s => s.Id == id);
        Assert.Equal(ContentType.Manhwa, series.ContentType);
        Assert.Equal(ReadingMode.Webtoon, series.ReadingMode);
        Assert.True(series.ContentTypeLocked);
        Assert.Null(series.ContentTypeSuggestion);
    }

    [Fact]
    public void Keep_Skip_AndChange_AllLockTheSeries()
    {
        int keep = AddSeries("Keep Me", s => s.ContentTypeSuggestion = ContentType.Manga);
        int skip = AddSeries("Skip Me");
        int change = AddSeries("Change Me", s => s.ContentTypeSuggestion = ContentType.Manga);
        var vm = CreateViewModel();

        vm.KeepContentTypeCommand.Execute(vm.ContentTypeItems.Single(i => i.SeriesId == keep));
        vm.SkipContentTypeCommand.Execute(vm.ContentTypeItems.Single(i => i.SeriesId == skip));
        vm.ChangeContentTypeToManhuaCommand.Execute(vm.ContentTypeItems.Single(i => i.SeriesId == change));
        TestDispatcher.Drain();

        Assert.Empty(vm.ContentTypeItems);
        using var verify = Ctx();
        var kept = verify.Series.Single(s => s.Id == keep);
        Assert.True(kept.ContentTypeLocked);
        Assert.Equal(ContentType.Unknown, kept.ContentType); // Keep changes nothing but the lock
        var skipped = verify.Series.Single(s => s.Id == skip);
        Assert.True(skipped.ContentTypeLocked);
        Assert.Equal(ContentTypeCheck.Skipped, skipped.ContentTypeCheck);
        var changed = verify.Series.Single(s => s.Id == change);
        Assert.Equal(ContentType.Manhua, changed.ContentType);
        Assert.True(changed.ContentTypeLocked);
    }

    [Fact]
    public void AcceptAllHighConfidence_TakesOnlyTheConfidentUnconflictedRows()
    {
        int sure = AddSeries("Sure Thing", s =>
        {
            s.ContentTypeSuggestion = ContentType.Manhwa;
            s.ContentTypeConfidence = 0.97;
            s.ContentTypeEvidence = Evidence(ContentType.Manhwa, 0.97, "manhwa");
        });
        int doubtful = AddSeries("Doubtful", s =>
        {
            s.ContentTypeSuggestion = ContentType.Manhwa;
            s.ContentTypeConfidence = 0.8;
            s.ContentTypeEvidence = Evidence(ContentType.Manhwa, 0.8, "manhwa");
        });
        var vm = CreateViewModel();
        Assert.Equal(1, vm.HighConfidenceCount);

        vm.AcceptAllHighConfidenceConfirm.TriggerCommand.Execute(null);   // arms
        vm.AcceptAllHighConfidenceConfirm.TriggerCommand.Execute(null);   // commits
        TestDispatcher.Drain();

        using var verify = Ctx();
        Assert.Equal(ContentType.Manhwa, verify.Series.Single(s => s.Id == sure).ContentType);
        Assert.Equal(ContentType.Unknown, verify.Series.Single(s => s.Id == doubtful).ContentType);
        Assert.Equal("Doubtful", Assert.Single(vm.ContentTypeItems).SeriesName);
    }

    [Fact]
    public void RecentlyAutoClassified_ListsLastThirtyDays_AndUndoRestoresAndLocks()
    {
        int recent = AddSeries("Recently Done", s =>
        {
            SeriesContentTypeEditor.ApplyAuto(s, ContentType.Manhwa, 0.97, Evidence(ContentType.Manhwa, 0.97, "KR", ExternalMetadataProvider.AniList), DateTime.UtcNow.AddDays(-2));
        });
        AddSeries("Long Ago", s =>
        {
            SeriesContentTypeEditor.ApplyAuto(s, ContentType.Manga, 0.97, null, DateTime.UtcNow.AddDays(-45));
        });
        var vm = CreateViewModel();

        var row = Assert.Single(vm.AutoClassifiedItems);
        Assert.Equal("Recently Done", row.SeriesName);
        Assert.Equal("Unknown", row.CurrentLabel);
        Assert.Equal("Manhwa", row.SuggestionLabel);
        Assert.True(vm.HasAutoClassifiedItems);

        vm.UndoAutoClassifiedCommand.Execute(row);
        TestDispatcher.Drain();

        Assert.Empty(vm.AutoClassifiedItems);
        using var verify = Ctx();
        var series = verify.Series.Single(s => s.Id == recent);
        Assert.Equal(ContentType.Unknown, series.ContentType);
        Assert.Equal(ReadingMode.LeftToRight, series.ReadingMode);
        Assert.True(series.ContentTypeLocked);
        Assert.DoesNotContain(vm.ContentTypeItems, i => i.SeriesId == recent); // locked: it does not come back as an unknown series
    }
}
