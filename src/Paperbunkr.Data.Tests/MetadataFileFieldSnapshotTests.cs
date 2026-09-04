using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="MetadataFileFieldSnapshot"/> + <see cref="PaperbunkrSidecar"/> - the
/// change-detection that stops file metadata write-back from re-writing a file when nothing
/// file-mapped actually changed (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
/// </summary>
public class MetadataFileFieldSnapshotTests
{
    private static Issue SampleIssue()
    {
        var issue = new Issue
        {
            Series = new Series { Name = "Kilo Station" },
            Number = "5",
            Summary = "Original.",
            Rating = 3f,
        };
        issue.MergeFrom(IssueTagField.Genre, new[] { "Sci-Fi" });
        return issue;
    }

    [Fact]
    public void Differ_IdenticalIssue_False()
    {
        var before = MetadataFileFieldSnapshot.Capture(SampleIssue());
        var after = MetadataFileFieldSnapshot.Capture(SampleIssue());
        Assert.False(MetadataFileFieldSnapshot.Differ(before, after));
    }

    [Fact]
    public void Differ_ChangedComicInfoField_True()
    {
        var issue = SampleIssue();
        var before = MetadataFileFieldSnapshot.Capture(issue);
        issue.Summary = "Edited.";
        var after = MetadataFileFieldSnapshot.Capture(issue);

        Assert.True(MetadataFileFieldSnapshot.Differ(before, after));
    }

    [Fact]
    public void Differ_TagWeightOnlyChange_True_EvenThoughGenreCsvUnchanged()
    {
        var issue = SampleIssue();
        var before = MetadataFileFieldSnapshot.Capture(issue);

        issue.Tags.Single(t => t.Field == IssueTagField.Genre).Weight = IssueTagWeight.Defining;
        var after = MetadataFileFieldSnapshot.Capture(issue);

        // The flat Genre CSV that ComicInfo.xml carries is unchanged...
        Assert.Equal(before.ComicInfoContent, after.ComicInfoContent);
        // ...but the sidecar keeps the weight, so a write is still warranted.
        Assert.NotEqual(before.SidecarContent, after.SidecarContent);
        Assert.True(MetadataFileFieldSnapshot.Differ(before, after));
    }

    [Fact]
    public void Differ_PersonalRatingChange_True_ViaSidecar()
    {
        var issue = SampleIssue();
        var before = MetadataFileFieldSnapshot.Capture(issue);
        issue.Rating = 5f;
        var after = MetadataFileFieldSnapshot.Capture(issue);

        Assert.Equal(before.ComicInfoContent, after.ComicInfoContent);
        Assert.True(MetadataFileFieldSnapshot.Differ(before, after));
    }

    [Fact]
    public void Sidecar_RoundTripsThroughJson()
    {
        var issue = SampleIssue();
        issue.IsFinalIssue = true;
        issue.BookCondition = "Near Mint";
        issue.Tags.Single().Category = "Primary";
        issue.Tags.Single().Weight = IssueTagWeight.Defining;

        var original = PaperbunkrSidecar.FromIssue(issue);
        var parsed = PaperbunkrSidecar.TryParse(original.ToJsonBytes());

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.Schema);
        Assert.Equal(3f, parsed.Rating);
        Assert.True(parsed.IsFinalIssue);
        Assert.Equal("Near Mint", parsed.BookCondition);
        var tag = Assert.Single(parsed.Tags);
        Assert.Equal("Genre", tag.Field);
        Assert.Equal("Sci-Fi", tag.Value);
        Assert.Equal("Primary", tag.Category);
        Assert.Equal("Defining", tag.Weight);
    }

    [Fact]
    public void Sidecar_TryParse_Garbage_ReturnsNull()
    {
        Assert.Null(PaperbunkrSidecar.TryParse(System.Text.Encoding.UTF8.GetBytes("{not json")));
    }
}
