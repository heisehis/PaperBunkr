using System;
using System.IO;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// First real exercise of <see cref="ComicInfoWriteBackService"/> - and, transitively, of
/// <c>ComicExporter</c>/<c>PackedStorageProvider</c>, ported ComicRackCE code with zero other
/// callers anywhere in this codebase before docs/superpowers/specs/2026-08-23-weighted-categorized-
/// tags-design.md. Verifies against a real CBZ file on disk, not a mock - the whole point of this
/// service is a real archive rewrite, so a real round-trip is the only test that means anything.
/// </summary>
public class ComicInfoWriteBackServiceTests : IDisposable
{
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_writeback_test_{Guid.NewGuid():N}.cbz");

    public void Dispose()
    {
        if (File.Exists(_cbzPath))
        {
            File.Delete(_cbzPath);
        }
    }

    private static ComicInfo ReadBack(string path)
    {
        using var provider = Providers.Readers.CreateSourceProvider(path);
        provider.Open(async: false);
        return ((IInfoStorage)provider).LoadInfo(InfoLoadingMethod.Complete);
    }

    [Fact]
    public void WriteGenreTags_UpdatesEmbeddedComicInfo_PreservingOtherFields()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2, new ComicInfo { Genre = "Old Genre", Tags = "Old Tag", Summary = "Untouched summary", Writer = "Untouched Writer" });

        var outcome = ComicInfoWriteBackService.WriteGenreTags(_cbzPath, "New Genre, Second Genre", "New Tag");

        Assert.Equal(ComicInfoWriteBackResult.Success, outcome.Result);
        Assert.Null(outcome.ErrorMessage);

        var info = ReadBack(_cbzPath);
        Assert.Equal("New Genre, Second Genre", info.Genre);
        Assert.Equal("New Tag", info.Tags);
        Assert.Equal("Untouched summary", info.Summary);
        Assert.Equal("Untouched Writer", info.Writer);
    }

    [Fact]
    public void WriteGenreTags_PreservesPageCount()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3, new ComicInfo { Genre = "Old" });

        ComicInfoWriteBackService.WriteGenreTags(_cbzPath, "New", null);

        var info = ReadBack(_cbzPath);
        Assert.Equal(3, info.PageCount);
    }

    [Fact]
    public void WriteGenreTags_NonCbzExtension_SkipsWithoutTouchingTheFile()
    {
        string pdfPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_writeback_test_{Guid.NewGuid():N}.pdf");
        File.WriteAllText(pdfPath, "not a real pdf, just needs to exist");
        try
        {
            var outcome = ComicInfoWriteBackService.WriteGenreTags(pdfPath, "New Genre", "New Tag");

            Assert.Equal(ComicInfoWriteBackResult.SkippedNotCbz, outcome.Result);
            Assert.Equal("not a real pdf, just needs to exist", File.ReadAllText(pdfPath));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public void WriteGenreTags_FileDoesNotExist_ReturnsFailed_DoesNotThrow()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_writeback_missing_{Guid.NewGuid():N}.cbz");

        var outcome = ComicInfoWriteBackService.WriteGenreTags(missingPath, "New Genre", "New Tag");

        Assert.Equal(ComicInfoWriteBackResult.Failed, outcome.Result);
        Assert.NotNull(outcome.ErrorMessage);
    }
}
