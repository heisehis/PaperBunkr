using System;
using System.IO;
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

/// <summary>
/// Exercises <see cref="NavigationCliArgs.TryParseFilePathArg"/> (docs/superpowers/specs/2026-09-13-
/// open-file-on-launch-design.md) - the bare file-path shape Windows' own file-association launch
/// command line produces, distinct from <see cref="NavigationCliArgs.TryParseOpenArg"/>'s
/// <c>--open kind:id</c> convention.
/// </summary>
public class NavigationCliArgsFilePathTests : IDisposable
{
    private readonly string _root;

    public NavigationCliArgsFilePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_clifilepath_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TryParseFilePathArg_ExistingSupportedFile_Succeeds()
    {
        string file = CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2020).cbz"), pageCount: 1);

        bool result = NavigationCliArgs.TryParseFilePathArg(new[] { file }, out var path);

        Assert.True(result);
        Assert.Equal(file, path);
    }

    [Fact]
    public void TryParseFilePathArg_OpenFlagWithKindId_Fails()
    {
        bool result = NavigationCliArgs.TryParseFilePathArg(new[] { "--open", "issue:42" }, out var path);

        Assert.False(result);
        Assert.Null(path);
    }

    [Fact]
    public void TryParseFilePathArg_NonexistentPath_Fails()
    {
        bool result = NavigationCliArgs.TryParseFilePathArg(new[] { Path.Combine(_root, "missing.cbz") }, out var path);

        Assert.False(result);
        Assert.Null(path);
    }

    [Fact]
    public void TryParseFilePathArg_UnsupportedExtension_Fails()
    {
        string file = Path.Combine(_root, "notes.txt");
        File.WriteAllText(file, "not a comic");

        bool result = NavigationCliArgs.TryParseFilePathArg(new[] { file }, out var path);

        Assert.False(result);
        Assert.Null(path);
    }

    [Fact]
    public void TryParseFilePathArg_EmptyArgs_Fails()
    {
        bool result = NavigationCliArgs.TryParseFilePathArg(Array.Empty<string>(), out var path);

        Assert.False(result);
        Assert.Null(path);
    }
}

/// <summary>
/// Exercises <see cref="NavigationCliArgs.TryParseBookFilePathArg"/> (docs/superpowers/specs/
/// 2026-09-16-book-file-associations-design.md) - the Books-side counterpart to
/// <see cref="NavigationCliArgsFilePathTests"/>, against the independent Books extension set.
/// </summary>
public class NavigationCliArgsBookFilePathTests : IDisposable
{
    private readonly string _root;

    public NavigationCliArgsBookFilePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_clibookfilepath_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("book.epub", Paperbunkr.Data.Entities.BookFormat.Epub)]
    [InlineData("book.fb2", Paperbunkr.Data.Entities.BookFormat.Fb2)]
    [InlineData("book.mobi", Paperbunkr.Data.Entities.BookFormat.Mobi)]
    [InlineData("book.azw", Paperbunkr.Data.Entities.BookFormat.Mobi)]
    [InlineData("book.azw3", Paperbunkr.Data.Entities.BookFormat.Mobi)]
    public void TryParseBookFilePathArg_ExistingSupportedFile_SucceedsWithFormat(string fileName, Paperbunkr.Data.Entities.BookFormat expectedFormat)
    {
        string file = Path.Combine(_root, fileName);
        File.WriteAllText(file, "fixture");

        bool result = NavigationCliArgs.TryParseBookFilePathArg(new[] { file }, out var path, out var format);

        Assert.True(result);
        Assert.Equal(file, path);
        Assert.Equal(expectedFormat, format);
    }

    // ".zip"/".fb2.zip" deliberately stay unrecognized here - Windows has no way to key an
    // association off a compound extension, so a bare ".zip" argument is indistinguishable from an
    // ordinary comic archive at this layer and stays routed through TryParseFilePathArg instead
    // (see the method's own doc comment).
    [Fact]
    public void TryParseBookFilePathArg_ZipExtension_Fails()
    {
        string file = Path.Combine(_root, "book.fb2.zip");
        File.WriteAllText(file, "fixture");

        bool result = NavigationCliArgs.TryParseBookFilePathArg(new[] { file }, out var path, out _);

        Assert.False(result);
        Assert.Null(path);
    }

    // ".pdf" deliberately stays comic-only (FileAssociationService.BookFormats' own doc comment).
    [Fact]
    public void TryParseBookFilePathArg_PdfExtension_Fails()
    {
        string file = Path.Combine(_root, "book.pdf");
        File.WriteAllText(file, "fixture");

        bool result = NavigationCliArgs.TryParseBookFilePathArg(new[] { file }, out var path, out _);

        Assert.False(result);
        Assert.Null(path);
    }

    [Fact]
    public void TryParseBookFilePathArg_NonexistentPath_Fails()
    {
        bool result = NavigationCliArgs.TryParseBookFilePathArg(new[] { Path.Combine(_root, "missing.epub") }, out var path, out _);

        Assert.False(result);
        Assert.Null(path);
    }

    [Fact]
    public void TryParseBookFilePathArg_OpenFlagWithKindId_Fails()
    {
        bool result = NavigationCliArgs.TryParseBookFilePathArg(new[] { "--open", "book:42" }, out var path, out _);

        Assert.False(result);
        Assert.Null(path);
    }
}
