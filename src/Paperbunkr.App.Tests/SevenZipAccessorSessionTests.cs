using cYo.Projects.ComicRack.Engine.IO.Provider.Readers;
using cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Step 1 of the reader-pipeline plan (docs/superpowers/specs/2026-09-08-reader-decode-cache-
/// prefetch-pipeline-plan.md): the keep-open <see cref="IComicAccessorSession"/> off the default
/// 7z.dll engine. Verifies the session reads the same bytes the stateless
/// <see cref="cYo.Projects.ComicRack.Engine.IO.Provider.ImageProvider.GetByteImage"/> path
/// returns, in and out of order, from a single held-open handle.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SevenZipAccessorSessionTests : IDisposable
{
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"pb_session_test_{Guid.NewGuid():N}.cbz");

    public void Dispose()
    {
        try { if (File.Exists(_cbzPath)) File.Delete(_cbzPath); } catch (IOException) { }
    }

    private ArchiveComicProvider OpenProvider()
    {
        var provider = PageDecodeCore.TryOpenProvider(_cbzPath);
        Assert.NotNull(provider);
        return Assert.IsAssignableFrom<ArchiveComicProvider>(provider);
    }

    [Fact]
    public void Session_OpensAndReportsEntryCount()
    {
        CbzFixture.Create(_cbzPath, pageCount: 5);
        using var provider = OpenProvider();

        using var session = provider.TryOpenReaderSession();

        Assert.NotNull(session);
        Assert.Equal(5, session!.Count);
    }

    [Fact]
    public void Session_ReadsSameBytesAsStatelessPath_Sequential()
    {
        CbzFixture.Create(_cbzPath, pageCount: 6);
        using var provider = OpenProvider();
        using var session = provider.TryOpenReaderSession()!;

        for (int i = 0; i < provider.Count; i++)
        {
            var expected = provider.GetByteImage(i);
            var actual = session.ReadEntryBytes(provider.GetFile(i).Name);

            Assert.NotNull(actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Session_ReadsCorrectBytes_OutOfOrderAndRepeated()
    {
        CbzFixture.Create(_cbzPath, pageCount: 8);
        using var provider = OpenProvider();
        using var session = provider.TryOpenReaderSession()!;

        foreach (int i in new[] { 7, 0, 3, 3, 5, 1 })
        {
            var expected = provider.GetByteImage(i);
            var actual = session.ReadEntryBytes(provider.GetFile(i).Name);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Session_ReturnsNull_ForUnknownEntryName()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2);
        using var provider = OpenProvider();
        using var session = provider.TryOpenReaderSession()!;

        Assert.Null(session.ReadEntryBytes("nope_not_here.png"));
    }
}
