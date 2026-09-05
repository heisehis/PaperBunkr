using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="CoverFingerprint.Stem"/> is the identity token folded into every cover cache-file
/// name (docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-design.md). These
/// tests pin the properties the whole scheme relies on: deterministic, path-normalized,
/// size-sensitive, and with well-defined behaviour for the null cases.
/// </summary>
public class CoverFingerprintTests
{
    [Fact]
    public void Stem_IsDeterministic_ForIdenticalInputs()
    {
        Assert.Equal(
            CoverFingerprint.Stem(7, @"C:\comics\a.cbz", 1234),
            CoverFingerprint.Stem(7, @"C:\comics\a.cbz", 1234));
    }

    [Fact]
    public void Stem_StartsWithTheId_AndAnEightHexFingerprint()
    {
        Assert.Matches(@"^7-[0-9a-f]{8}$", CoverFingerprint.Stem(7, @"C:\comics\a.cbz", 1234));
    }

    [Theory]
    [InlineData(@"C:\comics\a.cbz", "c:/comics/a.cbz")]
    [InlineData(@"C:\Comics\A.CBZ", @"c:\comics\a.cbz")]
    public void Stem_NormalizesCaseAndSeparators(string a, string b)
    {
        Assert.Equal(
            CoverFingerprint.Stem(1, a, 500),
            CoverFingerprint.Stem(1, b, 500));
    }

    [Fact]
    public void Stem_DiffersWhenPathDiffers()
    {
        Assert.NotEqual(
            CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 500),
            CoverFingerprint.Stem(1, @"C:\comics\b.cbz", 500));
    }

    [Fact]
    public void Stem_DiffersWhenSizeDiffers()
    {
        Assert.NotEqual(
            CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 500),
            CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 501));
    }

    [Fact]
    public void Stem_DiffersWhenIdDiffers()
    {
        Assert.NotEqual(
            CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 500),
            CoverFingerprint.Stem(2, @"C:\comics\a.cbz", 500));
    }

    [Fact]
    public void Stem_WithNullSize_IsStableAndPathOnly()
    {
        string once = CoverFingerprint.Stem(1, @"C:\comics\a.cbz", null);
        Assert.Equal(once, CoverFingerprint.Stem(1, @"C:\comics\a.cbz", null));
        // Path-only differs from the same path with a known size.
        Assert.NotEqual(once, CoverFingerprint.Stem(1, @"C:\comics\a.cbz", 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Stem_WithNoPath_IsTheFilelessSentinel(string? path)
    {
        Assert.Equal("42-nofile", CoverFingerprint.Stem(42, path, null));
    }
}
