using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="CoverFingerprint.Stem"/> is now a thin shim over the bare id
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2) - the
/// retired <c>{id}-{hash(path)}.jpg</c> fingerprint scheme destroyed covers on every routine
/// file-path change. These tests pin the shim contract: the stem is exactly the id, the file
/// identity arguments are ignored, and <see cref="CoverFingerprint.TryGetId"/> round-trips it.
/// </summary>
public class CoverFingerprintTests
{
    [Fact]
    public void Stem_IsTheBareId()
    {
        Assert.Equal("7", CoverFingerprint.Stem(7, @"C:\comics\a.cbz", 1234));
    }

    [Fact]
    public void Stem_IgnoresPathAndSize()
    {
        Assert.Equal(
            CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 500),
            CoverFingerprint.Stem(1, @"C:\comics\b.cbz", 999));

        Assert.Equal(
            CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 500),
            CoverFingerprint.Stem(1, null, null));
    }

    [Fact]
    public void Stem_DiffersWhenIdDiffers()
    {
        Assert.NotEqual(
            CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 500),
            CoverFingerprint.Stem(2, @"C:\comics\a.cbz", 500));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9001)]
    public void TryGetId_RoundTripsTheStem(int id)
    {
        Assert.True(CoverFingerprint.TryGetId(CoverFingerprint.Stem(id, null, null), out int parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void TryGetId_RejectsNonNumericStems()
    {
        Assert.False(CoverFingerprint.TryGetId("7-abcd1234", out _));
        Assert.False(CoverFingerprint.TryGetId("nofile", out _));
    }
}
