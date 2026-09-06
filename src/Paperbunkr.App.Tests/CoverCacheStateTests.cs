using Paperbunkr.App.Services.Covers;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="CoverCacheState"/> - the JSON sidecar tracking cover-cache identity + health
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2).
/// </summary>
public class CoverCacheStateTests : IDisposable
{
    private readonly string _original;
    private readonly string _dir;

    public CoverCacheStateTests()
    {
        _original = CoverCacheState.FilePath;
        _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_covstate_{Guid.NewGuid():N}");
        CoverCacheState.FilePath = Path.Combine(_dir, "cover-cache-state.json");
    }

    public void Dispose()
    {
        CoverCacheState.FilePath = _original;
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Read_MissingFile_ReturnsDefault()
    {
        var s = CoverCacheState.Read();
        Assert.Equal(0, s.SchemaVersion);
        Assert.Equal(string.Empty, s.Generation);
        Assert.False(s.RebuildPending);
    }

    [Fact]
    public void Read_CorruptFile_ReturnsDefault()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(CoverCacheState.FilePath, "{ not json");
        Assert.Equal(0, CoverCacheState.Read().SchemaVersion);
    }

    [Fact]
    public void RecordCounts_RoundTrips_AndStampsSchemaAndGeneration()
    {
        CoverCacheState.RecordCounts(issueCount: 120, bookCount: 30);

        var s = CoverCacheState.Read();
        Assert.Equal(CoverCacheState.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal(120, s.IssueCount);
        Assert.Equal(30, s.BookCount);
        Assert.NotEqual(string.Empty, s.Generation);
    }

    [Fact]
    public void RecordIssueCount_And_RecordBookCount_DoNotClobberEachOther()
    {
        CoverCacheState.RecordIssueCount(50);
        CoverCacheState.RecordBookCount(9);
        CoverCacheState.RecordIssueCount(51);

        var s = CoverCacheState.Read();
        Assert.Equal(51, s.IssueCount);
        Assert.Equal(9, s.BookCount);
    }

    [Fact]
    public void NewGeneration_ChangesTheToken_AndClearsRebuildPending()
    {
        CoverCacheState.RecordCounts(1, 1);
        string first = CoverCacheState.Read().Generation;
        CoverCacheState.MarkRebuildPending();
        Assert.True(CoverCacheState.Read().RebuildPending);

        CoverCacheState.NewGeneration();

        var s = CoverCacheState.Read();
        Assert.NotEqual(first, s.Generation);
        Assert.False(s.RebuildPending);
    }

    [Theory]
    [InlineData(100, 0, 40, 0, true)]   // issues collapsed to <50%
    [InlineData(100, 0, 60, 0, false)]  // issues only dipped
    [InlineData(0, 0, 0, 0, false)]     // nothing recorded yet -> never fires
    [InlineData(10, 200, 10, 20, true)] // books collapsed
    public void LooksLikeUnannouncedRebuild(int recIssues, int recBooks, int curIssues, int curBooks, bool expected)
    {
        if (recIssues > 0 || recBooks > 0)
        {
            CoverCacheState.RecordCounts(recIssues, recBooks);
        }

        Assert.Equal(expected, CoverCacheState.LooksLikeUnannouncedRebuild(curIssues, curBooks));
    }
}
