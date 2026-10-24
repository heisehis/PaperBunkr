using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.Data.Tests;

/// <summary>Edit Series save and fan-out (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md, acceptance A, C, D, F).</summary>
public class MangaSeriesMetadataServiceTests : IDisposable
{
    private readonly MangaTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private MangaSeriesMetadataService Service() => new(_db.Factory);

    /// <summary>Loads, confirms the proposed values as-is, and saves, so later tests start from a confirmed series.</summary>
    private MangaSeriesEdit Confirm(int seriesId)
    {
        var service = Service();
        var edit = service.Load(seriesId)!;
        service.Save(edit);
        return service.Load(seriesId)!;
    }

    [Fact]
    public void Load_BeforeConfirmation_ProposesTheValueTheChaptersAgreeOn()
    {
        int id = _db.SeedSeries(10);

        var edit = Service().Load(id)!;

        Assert.Equal(0, edit.CanonicalReviewVersion);
        Assert.Equal("X", edit.Values.Text[MangaInheritedFields.Publisher]);
        Assert.Equal(new[] { "A" }, edit.Values.Credits["Writer"]);
        Assert.Equal("Action", edit.Values.Genres.Single().Value);
        Assert.False(edit.NeedsReview);
    }

    [Fact]
    public void Load_WhenChaptersDisagree_ProposesTheMostCommonAndFlagsNeedsReview()
    {
        int id = _db.SeedSeries(10, (n, issue) => issue.Writer = n <= 7 ? "A" : "B");

        var edit = Service().Load(id)!;

        Assert.Equal(new[] { "A" }, edit.Values.Credits["Writer"]);
        Assert.True(edit.NeedsReview);
        Assert.Equal(new[] { "A", "B" }, edit.ReviewCandidates["Writer"]);
        Assert.DoesNotContain("Publisher", edit.ReviewCandidates.Keys);

        // Loading writes nothing.
        using var context = _db.CreateContext();
        Assert.Equal(0, context.Series.Single().CanonicalReviewVersion);
        Assert.Empty(context.SeriesCredits);
    }

    [Fact]
    public void Load_BlankChaptersDoNotCountAsDisagreement()
    {
        int id = _db.SeedSeries(10, (n, issue) => issue.Imprint = n == 1 ? "Imp" : null);

        var edit = Service().Load(id)!;

        Assert.Equal("Imp", edit.Values.Text[MangaInheritedFields.Imprint]);
        Assert.DoesNotContain("Imprint", edit.ReviewCandidates.Keys);
    }

    [Fact]
    public void FirstSave_StoresEveryAgreedValueOnTheSeries_ButLocksNothingTheUserDidNotEdit()
    {
        int id = _db.SeedSeries(5);

        var result = Service().Save(Service().Load(id)!);

        Assert.True(result.Saved);
        using var context = _db.CreateContext();
        var series = context.Series.Include(s => s.Credits).Include(s => s.SeriesTags).Single();
        Assert.Equal(1, series.CanonicalReviewVersion);
        Assert.Equal("X", series.Publisher);
        Assert.Equal("A", series.Credits.Single(c => c.Role == "Writer").Name);
        Assert.Equal("Action", series.SeriesTags.Single().Value);
        Assert.Null(series.LockedFields);
        Assert.Empty(result.ChangedIssueIds);
    }

    [Fact]
    public void ChangingPublisherAndWriter_UpdatesEveryChapterWithoutTouchingChapterOwnFields()
    {
        int id = _db.SeedSeries(100);
        var edit = Confirm(id);
        edit.Values.Text[MangaInheritedFields.Publisher] = "Y";
        edit.Values.Credits["Writer"] = new List<string> { "B" };

        var result = Service().Save(edit);

        Assert.True(result.Saved);
        Assert.Equal(100, result.ChangedIssueIds.Count);
        using var context = _db.CreateContext();
        var issues = context.Issues.OrderBy(i => i.Id).ToList();
        Assert.All(issues, i =>
        {
            Assert.Equal("Y", i.Publisher);
            Assert.Equal("B", i.Writer);
            Assert.Equal("Group", i.Translator);
            Assert.Equal("Scans", i.ScanInformation);
        });
        Assert.Equal("Chapter 7", issues[6].Title);
        Assert.Equal("7", issues[6].Number);
        Assert.Equal(7 % 3, issues[6].LastPageRead);

        var series = context.Series.Single();
        Assert.Equal("Y", series.Publisher);
        Assert.Contains("Publisher", series.LockedFields!.Split(','));
        Assert.Contains("Writer", series.LockedFields!.Split(','));
    }

    [Fact]
    public void ChangingWriter_RederivesTheCreatorIndexForEveryChapter()
    {
        int id = _db.SeedSeries(5);
        var edit = Confirm(id);
        edit.Values.Credits["Writer"] = new List<string> { "B", "C" };

        Service().Save(edit);

        using var context = _db.CreateContext();
        Assert.Equal(10, context.CreatorCredits.Count(c => c.Role == "Writer"));
        Assert.DoesNotContain(context.Creators, c => c.Name == "A");
    }

    [Fact]
    public void Tags_FanOutAndDropTheOldOnes()
    {
        int id = _db.SeedSeries(4);
        var edit = Confirm(id);
        edit.Values.Genres.Clear();
        edit.Values.Genres.Add(new MangaTagValue("Drama", "Genre", IssueTagWeight.Core));

        Service().Save(edit);

        using var context = _db.CreateContext();
        var tags = context.IssueTags.Where(t => t.Field == IssueTagField.Genre).ToList();
        Assert.Equal(4, tags.Count);
        Assert.All(tags, t =>
        {
            Assert.Equal("Drama", t.Value);
            Assert.Equal(IssueTagWeight.Core, t.Weight);
        });
    }

    [Fact]
    public void AnExplicitOverride_KeepsItsOwnValue_WhenTheSeriesValueChanges()
    {
        int id = _db.SeedSeries(10);
        var edit = Confirm(id);
        int guestId;
        using (var context = _db.CreateContext())
        {
            var chapter = context.Issues.OrderBy(i => i.Id).Skip(4).First();
            chapter.Writer = "Guest";
            chapter.FieldOverrides.Add(new IssueFieldOverride { FieldKey = "Writer" });
            context.SaveChanges();
            guestId = chapter.Id;
        }

        edit.Values.Credits["Writer"] = new List<string> { "B" };
        Service().Save(edit);

        using var verify = _db.CreateContext();
        Assert.Equal("Guest", verify.Issues.Single(i => i.Id == guestId).Writer);
        Assert.Equal(9, verify.Issues.Count(i => i.Writer == "B"));
    }

    [Fact]
    public void ChaptersThatAlreadyDiffer_AreKeptAsExceptionsByDefault()
    {
        int id = _db.SeedSeries(10, (n, issue) => issue.Publisher = n <= 3 ? "Other" : "X");
        var edit = Confirm(id);
        // After the first confirmation the series says "X"; the three "Other" chapters were not followers, so they were already kept.
        Assert.Equal("X", edit.Values.Text[MangaInheritedFields.Publisher]);
        using (var context = _db.CreateContext())
        {
            Assert.Equal(3, context.IssueFieldOverrides.Count(o => o.FieldKey == "Publisher"));
        }

        edit.Values.Text[MangaInheritedFields.Publisher] = "Y";
        Service().Save(edit);

        using var verify = _db.CreateContext();
        Assert.Equal(7, verify.Issues.Count(i => i.Publisher == "Y"));
        Assert.Equal(3, verify.Issues.Count(i => i.Publisher == "Other"));
    }

    [Fact]
    public void ReplaceAll_OverwritesTheDifferingChapters()
    {
        int id = _db.SeedSeries(10, (n, issue) => issue.Publisher = n <= 3 ? "Other" : "X");
        var service = Service();
        var edit = service.Load(id)!;

        var exceptions = service.PreviewExceptions(edit);
        Assert.Equal(3, exceptions.Single(e => e.Key == "Publisher").ChapterCount);

        service.Save(edit, MangaExceptionChoice.ReplaceAll);

        using var context = _db.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("X", i.Publisher));
        Assert.Empty(context.IssueFieldOverrides);
    }

    [Fact]
    public void AnUnchangedSave_WritesNothing()
    {
        int id = _db.SeedSeries(5);
        var edit = Confirm(id);

        var result = Service().Save(edit);

        Assert.False(result.Saved);
        using var context = _db.CreateContext();
        Assert.Empty(context.MetadataProposals);
        Assert.DoesNotContain(context.Issues, i => i.EmbeddedDirty);
    }

    [Fact]
    public void ASave_MarksChangedChaptersDirty_AndSnapshotsEachOnce()
    {
        int id = _db.SeedSeries(5);
        var edit = Confirm(id);
        edit.Values.Text[MangaInheritedFields.Publisher] = "Y";
        Service().Save(edit);

        using (var context = _db.CreateContext())
        {
            Assert.All(context.Issues, i => Assert.True(i.EmbeddedDirty));
            Assert.Equal(5, context.IssueMetadataSnapshots.Count());
            Assert.All(context.IssueMetadataSnapshots, s => Assert.Contains("\"Publisher\":\"X\"", s.FieldsJson));
        }

        // A second change must not replace the original snapshot.
        var second = Service().Load(id)!;
        second.Values.Text[MangaInheritedFields.Publisher] = "Z";
        Service().Save(second);

        using var verify = _db.CreateContext();
        Assert.All(verify.IssueMetadataSnapshots, s => Assert.Contains("\"Publisher\":\"X\"", s.FieldsJson));
    }

    [Fact]
    public void WithSyncDisabled_InheritanceStillApplies_ButNothingIsMarkedDirty()
    {
        int id = _db.SeedSeries(5);
        var edit = Confirm(id);
        edit.EmbeddedSyncEnabled = false;
        edit.Values.Text[MangaInheritedFields.Publisher] = "Y";

        Service().Save(edit);

        using var context = _db.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("Y", i.Publisher));
        Assert.DoesNotContain(context.Issues, i => i.EmbeddedDirty);
        Assert.False(context.Series.Single().EmbeddedSyncEnabled);
    }

    [Fact]
    public void ManualChanges_AreRecordedAsAcceptedManualProposals()
    {
        int id = _db.SeedSeries(3);
        var edit = Confirm(id);
        edit.Values.Text[MangaInheritedFields.Publisher] = "Y";
        edit.Summary = "A new summary";
        Service().Save(edit);

        using var context = _db.CreateContext();
        var publisher = context.MetadataProposals.Single(p => p.Field == MetadataProposalField.Publisher);
        Assert.Equal(MetadataProposalSource.Manual, publisher.Source);
        Assert.Equal(MetadataProposalStatus.Accepted, publisher.Status);
        Assert.Equal("X", publisher.CurrentValue);
        Assert.Equal("Y", publisher.ProposedValue);
        Assert.Contains(context.MetadataProposals, p => p.Field == MetadataProposalField.Summary && p.ProposedValue == "A new summary");
    }

    [Fact]
    public void ASummaryChange_DoesNotOverwriteAChapterSynopsis_ButMarksBlankOnesDirty()
    {
        int id = _db.SeedSeries(3, (n, issue) => issue.Summary = n == 1 ? "Own synopsis" : null);
        var edit = Confirm(id);
        edit.Summary = "Series summary";
        Service().Save(edit);

        using var context = _db.CreateContext();
        var issues = context.Issues.OrderBy(i => i.Id).ToList();
        Assert.Equal("Own synopsis", issues[0].Summary);
        Assert.Null(issues[1].Summary);
        Assert.False(issues[0].EmbeddedDirty);
        Assert.True(issues[1].EmbeddedDirty);
    }

    [Fact]
    public void ARemoteSeries_CannotBeSaved()
    {
        int id = _db.SeedSeries(2);
        using (var context = _db.CreateContext())
        {
            var source = new RemoteSource { InstanceId = "x", DisplayName = "Elsewhere", Host = "h" };
            context.RemoteSources.Add(source);
            context.SaveChanges();
            context.Series.Single().RemoteSourceId = source.Id;
            context.SaveChanges();
        }

        // A mirrored series is not visible to these default-context services at all, so it cannot be loaded, edited or saved (remote books are read-only by construction).
        Assert.Null(Service().Load(id));
    }
}
