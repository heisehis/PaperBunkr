using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Covers;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="CoverCacheUpgrade"/> - the one-time on-disk flatten of the retired
/// <c>{id}-{hash}.jpg</c> cover files to <c>{id}.jpg</c>
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2).
/// </summary>
public class CoverCacheUpgradeTests : IDisposable
{
    private readonly string _origThumbs;
    private readonly string _origBookThumbs;
    private readonly string _origState;
    private readonly string _root;
    private readonly string _thumbs;

    public CoverCacheUpgradeTests()
    {
        _origThumbs = CoverThumbnailPaths.ThumbnailDirectory;
        _origBookThumbs = BookCoverThumbnailPaths.ThumbnailDirectory;
        _origState = CoverCacheState.FilePath;
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_covupgrade_{Guid.NewGuid():N}");
        _thumbs = Path.Combine(_root, "thumbnails");
        CoverThumbnailPaths.ThumbnailDirectory = _thumbs;
        BookCoverThumbnailPaths.ThumbnailDirectory = Path.Combine(_root, "book-thumbnails");
        CoverCacheState.FilePath = Path.Combine(_root, "cover-cache-state.json");
        Directory.CreateDirectory(_thumbs);
    }

    public void Dispose()
    {
        CoverThumbnailPaths.ThumbnailDirectory = _origThumbs;
        BookCoverThumbnailPaths.ThumbnailDirectory = _origBookThumbs;
        CoverCacheState.FilePath = _origState;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(_thumbs, name), bytes);

    [Fact]
    public void RunOnce_RenamesLegacyFile_ToBareId()
    {
        Write("7-deadbeef.jpg", new byte[] { 1, 2, 3 });

        CoverCacheUpgrade.RunOnce();

        Assert.True(File.Exists(Path.Combine(_thumbs, "7.jpg")));
        Assert.False(File.Exists(Path.Combine(_thumbs, "7-deadbeef.jpg")));
        Assert.Equal(CoverCacheState.CurrentSchemaVersion, CoverCacheState.Read().SchemaVersion);
    }

    [Fact]
    public void RunOnce_TwoHashesForOneId_KeepsNewest_AtticsTheRest()
    {
        Write("9-aaaa1111.jpg", new byte[] { 1 });
        Write("9-bbbb2222.jpg", new byte[] { 2, 2 });
        File.SetLastWriteTimeUtc(Path.Combine(_thumbs, "9-aaaa1111.jpg"), DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(Path.Combine(_thumbs, "9-bbbb2222.jpg"), DateTime.UtcNow);

        CoverCacheUpgrade.RunOnce();

        Assert.Equal(2, new FileInfo(Path.Combine(_thumbs, "9.jpg")).Length); // the newer one
        Assert.Single(Directory.GetFiles(Path.Combine(_thumbs, ".attic"), "*.jpg"));
    }

    [Fact]
    public void RunOnce_LeavesABareIdFileAlone()
    {
        Write("3.jpg", new byte[] { 5, 5, 5 });

        CoverCacheUpgrade.RunOnce();

        Assert.Equal(3, new FileInfo(Path.Combine(_thumbs, "3.jpg")).Length);
    }

    [Fact]
    public void RunOnce_IsIdempotent()
    {
        Write("7-deadbeef.jpg", new byte[] { 1, 2, 3 });
        CoverCacheUpgrade.RunOnce();
        var afterFirst = File.GetLastWriteTimeUtc(Path.Combine(_thumbs, "7.jpg"));

        CoverCacheUpgrade.RunOnce();

        Assert.Equal(afterFirst, File.GetLastWriteTimeUtc(Path.Combine(_thumbs, "7.jpg")));
    }
}
