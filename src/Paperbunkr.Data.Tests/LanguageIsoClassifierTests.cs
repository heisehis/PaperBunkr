using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Xunit;

namespace Paperbunkr.Data.Tests;

public class LanguageIsoClassifierTests
{
    [Theory]
    [InlineData("ja", ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData("ja-JP", ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData("ko", ContentType.Manhwa, ReadingMode.Webtoon)]
    [InlineData("ko-KR", ContentType.Manhwa, ReadingMode.Webtoon)]
    [InlineData("zh", ContentType.Manhua, ReadingMode.Webtoon)]
    [InlineData("zh-CN", ContentType.Manhua, ReadingMode.Webtoon)]
    [InlineData("zh-Hant", ContentType.Manhua, ReadingMode.Webtoon)]
    [InlineData("zh-Hans", ContentType.Manhua, ReadingMode.Webtoon)]
    public void TryClassify_MappedLanguage_ReturnsTrueAndExpectedValues(string languageIso, ContentType expectedContentType, ReadingMode expectedReadingMode)
    {
        bool matched = LanguageIsoClassifier.TryClassify(languageIso, out var contentType, out var readingMode);

        Assert.True(matched);
        Assert.Equal(expectedContentType, contentType);
        Assert.Equal(expectedReadingMode, readingMode);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("fr")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-culture")]
    public void TryClassify_UnmappedOrUnparseableLanguage_ReturnsFalse(string? languageIso)
    {
        bool matched = LanguageIsoClassifier.TryClassify(languageIso, out var contentType, out var readingMode);

        Assert.False(matched);
        Assert.Equal(default, contentType);
        Assert.Equal(default, readingMode);
    }
}
