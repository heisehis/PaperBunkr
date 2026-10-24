using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

public class TaskbarBadgeIconsTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(-3, "")]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    public void Label_caps_at_nine_plus(int count, string expected) => Assert.Equal(expected, TaskbarBadgeIcons.LabelFor(count));
}
