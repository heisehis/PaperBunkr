using Paperbunkr.App.Services.Covers;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="CoverCacheAttic"/> - the soft-delete holding area
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2).
/// </summary>
public class CoverCacheAtticTests : IDisposable
{
    private readonly string _root;
    private readonly string _cache;
    private readonly string _attic;

    public CoverCacheAtticTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_attic_{Guid.NewGuid():N}");
        _cache = Path.Combine(_root, "thumbnails");
        _attic = Path.Combine(_cache, ".attic");
        Directory.CreateDirectory(_cache);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Cache(int id)
    {
        string p = Path.Combine(_cache, $"{id}.jpg");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        return p;
    }

    [Fact]
    public void MoveToAttic_CreatesATimestampedCopy_AndRemovesTheOriginal()
    {
        string src = Cache(7);
        CoverCacheAttic.MoveToAttic(src, _attic);

        Assert.False(File.Exists(src));
        var atticFiles = Directory.GetFiles(_attic, "*.jpg");
        Assert.Single(atticFiles);
        Assert.StartsWith("7.", Path.GetFileName(atticFiles[0]));
    }

    [Fact]
    public void Prune_DeletesEntriesOlderThan14Days()
    {
        string src = Cache(1);
        CoverCacheAttic.MoveToAttic(src, _attic);
        string atticFile = Directory.GetFiles(_attic, "*.jpg")[0];
        File.SetLastWriteTimeUtc(atticFile, DateTime.UtcNow.AddDays(-20));

        CoverCacheAttic.Prune(_attic);

        Assert.False(File.Exists(atticFile));
    }

    [Fact]
    public void Prune_EvictsOldestFirst_WhenOverTheSizeCap()
    {
        // Two ~300 MB entries in a 500 MB attic -> the older one must go.
        Directory.CreateDirectory(_attic);
        string older = Path.Combine(_attic, "1.100.jpg");
        string newer = Path.Combine(_attic, "2.200.jpg");
        File.WriteAllBytes(older, new byte[300 * 1024 * 1024]);
        File.WriteAllBytes(newer, new byte[300 * 1024 * 1024]);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        CoverCacheAttic.Prune(_attic);

        Assert.False(File.Exists(older));
        Assert.True(File.Exists(newer));
    }

    [Fact]
    public void TryRestoreById_MovesTheNewestAtticFileBack()
    {
        Directory.CreateDirectory(_attic);
        string old = Path.Combine(_attic, "5.100.jpg");
        string recent = Path.Combine(_attic, "5.200.jpg");
        File.WriteAllBytes(old, new byte[] { 1 });
        File.WriteAllBytes(recent, new byte[] { 2, 2 });
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow);

        bool restored = CoverCacheAttic.TryRestoreById(5, _cache, _attic);

        Assert.True(restored);
        Assert.True(File.Exists(Path.Combine(_cache, "5.jpg")));
        Assert.Equal(2, new FileInfo(Path.Combine(_cache, "5.jpg")).Length); // the newer one
    }

    [Fact]
    public void TryRestoreById_NoMatch_ReturnsFalse_AndDoesNothing()
    {
        Directory.CreateDirectory(_attic);
        Assert.False(CoverCacheAttic.TryRestoreById(42, _cache, _attic));
        Assert.False(File.Exists(Path.Combine(_cache, "42.jpg")));
    }

    [Fact]
    public void AtticEverything_MovesAllCoversOut_ButNotTheAtticItself()
    {
        Cache(1);
        Cache(2);
        Cache(3);

        CoverCacheAttic.AtticEverything(_cache, _attic);

        Assert.Empty(Directory.GetFiles(_cache, "*.jpg"));
        Assert.Equal(3, Directory.GetFiles(_attic, "*.jpg").Length);
    }
}
