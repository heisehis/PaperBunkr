using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="NavigationCliArgs.TryParseOpenArg"/> (docs/superpowers/specs/2026-08-30-
/// app-shell-navigation-history-design.md) - pure string parsing, no Avalonia app context needed.
/// </summary>
public class NavigationCliArgsTests
{
    [Theory]
    [InlineData("series")]
    [InlineData("issue")]
    [InlineData("book")]
    [InlineData("collection")]
    public void TryParseOpenArg_KnownKindWithValidId_Succeeds(string kind)
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--open", $"{kind}:123" }, out var target);

        Assert.True(result);
        Assert.NotNull(target);
        Assert.Equal(kind, target!.Kind);
        Assert.Equal(123, target.Id);
    }

    [Fact]
    public void TryParseOpenArg_NoOpenFlag_Fails()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--verbose" }, out var target);

        Assert.False(result);
        Assert.Null(target);
    }

    [Fact]
    public void TryParseOpenArg_EmptyArgs_Fails()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(System.Array.Empty<string>(), out var target);

        Assert.False(result);
        Assert.Null(target);
    }

    [Fact]
    public void TryParseOpenArg_OpenFlagWithNoFollowingValue_Fails()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--open" }, out var target);

        Assert.False(result);
        Assert.Null(target);
    }

    [Fact]
    public void TryParseOpenArg_MalformedId_Fails()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--open", "series:abc" }, out var target);

        Assert.False(result);
        Assert.Null(target);
    }

    [Fact]
    public void TryParseOpenArg_UnrecognizedKind_Fails()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--open", "reader:123" }, out var target);

        Assert.False(result);
        Assert.Null(target);
    }

    [Fact]
    public void TryParseOpenArg_MissingColon_Fails()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--open", "series123" }, out var target);

        Assert.False(result);
        Assert.Null(target);
    }

    [Fact]
    public void TryParseOpenArg_MissingId_Fails()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--open", "series:" }, out var target);

        Assert.False(result);
        Assert.Null(target);
    }

    [Fact]
    public void TryParseOpenArg_OpenFlagAmongOtherArgs_StillFinds()
    {
        bool result = NavigationCliArgs.TryParseOpenArg(new[] { "--minimized", "--open", "issue:42" }, out var target);

        Assert.True(result);
        Assert.Equal("issue", target!.Kind);
        Assert.Equal(42, target.Id);
    }
}
