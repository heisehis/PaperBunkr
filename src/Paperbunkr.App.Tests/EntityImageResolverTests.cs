using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

public class EntityImageResolverTests
{
    [Theory]
    [InlineData(true, true, 1, EntityImageKind.Custom)]
    [InlineData(false, true, 1, EntityImageKind.Provider)]
    [InlineData(false, false, 1, EntityImageKind.FirstAppearanceCover)]
    [InlineData(false, false, null, EntityImageKind.Initials)]
    public void ChoosesFirstAvailableSource(bool custom, bool provider, int? firstIssue, EntityImageKind expected)
    {
        var choice = EntityImageResolver.Choose("Kitty Pryde", "kitty.jpg", "https://provider/kitty", firstIssue, _ => custom, _ => provider);
        Assert.Equal(expected, choice.Kind);
        Assert.Equal("KP", choice.Initials);
    }

    [Theory]
    [InlineData("X-Men", "X")]
    [InlineData("", "?")]
    [InlineData("  Kitty  Pryde ", "KP")]
    public void InitialsHandleEmptyAndMultipleWords(string name, string expected) => Assert.Equal(expected, EntityImageResolver.Initials(name));
}
