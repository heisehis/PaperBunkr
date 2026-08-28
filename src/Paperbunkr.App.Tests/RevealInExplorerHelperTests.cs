using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers the pure folder/file resolution split out of <see cref="RevealInExplorerHelper"/> (the
/// actual shell P/Invoke can't be tested without touching the real Explorer). Added alongside
/// <see cref="RevealInExplorerHelper.ResolveBookFilePath"/> for the Book Details screen
/// (docs/superpowers/specs/2026-08-27-book-details-screen-design.md).
/// </summary>
public class RevealInExplorerHelperTests
{
    [Fact]
    public void ResolveBookFilePath_WithFile_ReturnsThePathUnchanged()
    {
        var book = new Book { FilePath = @"C:\books\Dune.epub" };

        Assert.Equal(@"C:\books\Dune.epub", RevealInExplorerHelper.ResolveBookFilePath(book));
    }

    [Fact]
    public void ResolveBookFilePath_NoFile_ReturnsNull()
    {
        Assert.Null(RevealInExplorerHelper.ResolveBookFilePath(new Book { FilePath = string.Empty }));
    }
}
