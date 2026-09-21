using System.Reflection;
using System.Text.Json;
using Paperbunkr.Sharing.Protocol;

namespace Paperbunkr.Sharing.Tests;

public class CatalogDtoTests
{
    // Host-personal state that must never cross the wire (spec §5 "Never sent"). Matched by property
    // name across every catalog DTO, so adding one - even under a different DTO - fails this test.
    private static readonly string[] Forbidden =
    {
        "FilePath", "FileSize", "FileModifiedTime", "FileCreationTime", "FileIsMissing",
        "OpenedTime", "OpenCount", "LastPageRead", "AddedTime", "Checked",
        "Rating", "Review", "Notes", "ScanInformation", "CustomThumbnailKey",
        "BookNotes", "BookOwner", "BookPrice", "BookStore", "BookCondition", "BookLocation", "BookCollectionStatus", "BookAge",
        "Bookmarks", "ReadingStatus", "ReadingModeOverride", "PageFitModeOverride", "PageLayoutModeOverride",
        "AutoRotateOverride", "BrightnessOverride", "ContrastOverride", "SaturationOverride", "GammaOverride",
        "IsPlaceholder", "MissingAcknowledged", "MissingVerificationCount", "DuplicateAcknowledged", "EmptyRowAcknowledged",
        "TrackingLinks", "TrackerPromptShown",
    };

    [Theory]
    [InlineData(typeof(CatalogIssueDto))]
    [InlineData(typeof(CatalogSeriesDto))]
    [InlineData(typeof(IssueTagDto))]
    [InlineData(typeof(CatalogPage))]
    public void CatalogDtos_NeverCarryHostLocalState(Type dto)
    {
        var names = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();

        Assert.Empty(names.Intersect(Forbidden));
    }

    [Fact]
    public void CatalogPage_RoundTripsThroughJson()
    {
        var page = new CatalogPage(
            "v42",
            new[] { new CatalogSeriesDto(1, "Saga", "Saga", "Comic", "LeftToRight", "Ongoing", "Image", "Sci-Fi", "s", "Vaughan") },
            new[]
            {
                new CatalogIssueDto(
                    10, 1, "Chapter One", "1", 54, null, null, null, null, null, null, null, false, "sum",
                    2012, 3, 14, "BKV", null, null, null, null, null, null, null, "Image", null, null,
                    22, "en", "Series", "Teen", null, null, null, null, 4.5f, null, "Unknown", null, 0.65,
                    new[] { new IssueTagDto("Tags", "Theme", "Core", "Space") }),
            },
            "next-cursor");

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var back = JsonSerializer.Deserialize<CatalogPage>(JsonSerializer.Serialize(page, options), options)!;

        Assert.Equal("v42", back.CatalogVersion);
        Assert.Equal("next-cursor", back.NextCursor);
        Assert.Equal("Saga", back.Series[0].Name);
        Assert.Equal("Space", back.Issues[0].Tags[0].Value);
        Assert.Equal(0.65, back.Issues[0].CoverAspectRatio);
    }
}
