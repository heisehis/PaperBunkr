using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Xunit;

namespace Paperbunkr.Data.Tests;

public class PublisherContentTypeClassifierTests
{
    [Theory]
    [InlineData("Marvel", ContentType.Comic, ReadingMode.LeftToRight)]
    [InlineData("Marvel Comics", ContentType.Comic, ReadingMode.LeftToRight)]
    [InlineData("DC Comics", ContentType.Comic, ReadingMode.LeftToRight)]
    [InlineData("Boom! Studios", ContentType.Comic, ReadingMode.LeftToRight)]
    [InlineData("Viz", ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData("VIZ Media LLC", ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData("Shueisha Inc.", ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData("Square Enix", ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData("WEBTOON", ContentType.Manhwa, ReadingMode.Webtoon)]
    [InlineData("Lezhin Comics", ContentType.Manhwa, ReadingMode.Webtoon)]
    [InlineData("Kuaikan Manhua", ContentType.Manhua, ReadingMode.Webtoon)]
    [InlineData("Bilibili Comics", ContentType.Manhua, ReadingMode.Webtoon)]
    public void TryClassify_KnownPublisher_ReturnsTrueAndExpectedValues(string publisher, ContentType expectedContentType, ReadingMode expectedReadingMode)
    {
        bool matched = PublisherContentTypeClassifier.TryClassify(publisher, out var contentType, out var readingMode);

        Assert.True(matched);
        Assert.Equal(expectedContentType, contentType);
        Assert.Equal(expectedReadingMode, readingMode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Some Indie Publisher")]
    public void TryClassify_UnknownPublisher_ReturnsFalse(string? publisher)
    {
        bool matched = PublisherContentTypeClassifier.TryClassify(publisher, out var contentType, out var readingMode);

        Assert.False(matched);
        Assert.Equal(default, contentType);
        Assert.Equal(default, readingMode);
    }

    /// <summary>
    /// Dark Horse publishes both Western comics and licensed manga; Tapas is a mixed-content
    /// webtoon/manhwa platform. Both are deliberately excluded from the lookup table (design doc's
    /// "Deliberately excluded" list) rather than guessed at, since a confident-looking wrong answer
    /// is worse than staying Unknown.
    /// </summary>
    [Theory]
    [InlineData("Dark Horse Comics")]
    [InlineData("Tapas")]
    public void TryClassify_DeliberatelyExcludedAmbiguousPublisher_ReturnsFalse(string publisher)
    {
        bool matched = PublisherContentTypeClassifier.TryClassify(publisher, out _, out _);

        Assert.False(matched);
    }
}
