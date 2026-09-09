using cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Step 2 of the reader-pipeline plan: the non-default engine sessions
/// (<see cref="ZipSharpAccessorSession"/>, <see cref="SharpCompressAccessorSession"/>). Both are
/// exercised directly against a synthetic .cbz - the same held-open contract as the default 7z
/// session, verified to read the same bytes in and out of order.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class AccessorSessionTests : IDisposable
{
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"pb_accsess_{Guid.NewGuid():N}.cbz");

    public void Dispose()
    {
        try { if (File.Exists(_cbzPath)) File.Delete(_cbzPath); } catch (IOException) { }
    }

    private (string name, byte[] bytes)[] ExpectedEntries(int pageCount)
    {
        var list = new List<(string, byte[])>();
        using var zip = System.IO.Compression.ZipFile.OpenRead(_cbzPath);
        foreach (var e in zip.Entries.Where(e => e.Name.EndsWith(".png")).OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            list.Add((e.FullName, ms.ToArray()));
        }
        return list.ToArray();
    }

    [Fact]
    public void ZipSharpSession_ReadsAllEntries_InAndOutOfOrder()
    {
        CbzFixture.Create(_cbzPath, pageCount: 6);
        var expected = ExpectedEntries(6);

        using var session = ZipSharpAccessorSession.TryOpen(_cbzPath);
        Assert.NotNull(session);
        Assert.Equal(6, session!.Count);

        foreach (int i in new[] { 0, 1, 2, 3, 4, 5, 5, 0, 3 })
        {
            Assert.Equal(expected[i].bytes, session.ReadEntryBytes(expected[i].name));
        }
        Assert.Null(session.ReadEntryBytes("missing.png"));
    }

    [Fact]
    public void SharpCompressSession_ReadsAllEntries_InAndOutOfOrder()
    {
        CbzFixture.Create(_cbzPath, pageCount: 6);
        var expected = ExpectedEntries(6);

        using var session = SharpCompressAccessorSession.TryOpen(_cbzPath);
        Assert.NotNull(session);
        Assert.Equal(6, session!.Count);

        foreach (int i in new[] { 5, 0, 2, 2, 4, 1 })
        {
            Assert.Equal(expected[i].bytes, session.ReadEntryBytes(expected[i].name));
        }
    }

    [Fact]
    public void SharpCompressSession_NullForMissingFile()
    {
        Assert.Null(SharpCompressAccessorSession.TryOpen(Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.cbz")));
        Assert.Null(ZipSharpAccessorSession.TryOpen(Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.cbz")));
    }
}
