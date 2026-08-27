using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class SeriesStatusNormalizerTests
{
    [Theory]
    [InlineData("FINISHED", SeriesStatus.Completed)]
    [InlineData("completed", SeriesStatus.Completed)]
    [InlineData("RELEASING", SeriesStatus.Ongoing)]
    [InlineData("releasing", SeriesStatus.Ongoing)]
    [InlineData("CANCELLED", SeriesStatus.Cancelled)]
    [InlineData("HIATUS", SeriesStatus.Hiatus)]
    public void Normalize_KnownProviderValues_MapToExpectedStatus(string raw, SeriesStatus expected) =>
        Assert.Equal(expected, SeriesStatusNormalizer.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT_YET_RELEASED")]
    [InlineData("some_unrecognized_value")]
    public void Normalize_EmptyOrUnrecognizedValues_ResolveToUnknown(string? raw) =>
        Assert.Equal(SeriesStatus.Unknown, SeriesStatusNormalizer.Normalize(raw));
}
