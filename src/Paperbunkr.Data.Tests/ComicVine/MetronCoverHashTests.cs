using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>
/// <see cref="MetronCoverHash"/> against values produced by Python <c>imagehash.phash</c> 4.3.2, the
/// library Metron's server runs (docs/superpowers/specs/2026-10-05-metron-api-efficiency-and-matching-
/// design.md section 3). The fixtures are two small drawn images, not cover art. The port was also
/// checked by hand against the cover and hash Metron's API docs publish (<c>c585eb18bf1e5423</c>), which
/// it reproduces exactly; that image isn't ours to ship in a test.
/// </summary>
public sealed class MetronCoverHashTests
{
    internal const string FixtureA = "iVBORw0KGgoAAAANSUhEUgAAADAAAABICAIAAAA/EeF8AAAB0ElEQVR42s2aoXLDMAyGXV3QcPEKRhoyMF4yPDK6vlxJ32B4ZHygpChgfGSkuCDEFzu2JEuyjHptmnz9P8tRfN087j+Cg/F7Pc8vIDgb4CoeF0AxjTtlu/EIfuLZjccQwhB//LX9kb3e698LXpYvZXM8PYFSWT2BsrK8KIvj6QO0JqsPUEFWf2VpPNZAZVnWQFVZPZWtxWMHhJFlB4SU1UdZOR4LILwsCyCSLGtlmHh0gZ6+r1QaRaCYxp0yfDxaQDxZWkBsWRbKqPHIA8XxTIexc0KNsnSV8eKRBGqXJQkkIktLWUs8MkBSsuYxKE3q7WkfQriEW/zm89uDOlAaz4ySHZfPWxULZOdygWaBpT6pp8OIoakygZQsPE2ZacPbOF80y//TO+9XpfNJQBmbJpsTGDzZ6C6MC1nlkmGEBMb9l3BCqrLIQOljaLuv9DzgRxYNyEAWAYixZ2BX9qrxoIDMZKGALGWRlRnEUwEyllUBwsjC9MjUJgT8yCoBdZG1CkSqrHZrizOAH1l5IIaslpAqPTV7GeQxZb8FUrKoTGvHg2Bl4ZkKR4LsPQvDVD5mEK+s+XrZ7ha7+6GxDLJLD+wbDMI6ZL8MloA80Hj8l94dbnS4jRa5D7IAAAAASUVORK5CYII=";
    internal const ulong HashA = 0x916a6f8d6a292f92;

    internal const string FixtureB = "iVBORw0KGgoAAAANSUhEUgAAADAAAABICAIAAAA/EeF8AAAA+ElEQVR42u2asRHCMAxFE5cM4YqCioJxqDILk3gc6tSpGIKagoYL2MiWbEu+78pF9PX8lLskd5nvPkyalpuULQABaDig2Z+uIkGH4Dnlz+WBkQEIQAACEIAABCAAAQhAADIFtF5uir7LtjW8N8fzgpHF9ez2MBRRwpQ0lqGYDI6kgQylNRRLGsUQRUCZJFeJppjJ/shyD517vXFDZfdpVpVlQ5wHAr3WrCH+mxcxwbWhoecYHJmUHmKaNUOyeiiZpgzV0PM32Y6henrS+UYM1daT6OJ60cR6qR9ZSz0/O+o21F7Pd1/Fhnrp2XXXaqivnk8Gsf+HpNYLpgFsRRQl9iMAAAAASUVORK5CYII=";
    internal const ulong HashB = 0x9139e64e68c7aac9;

    [Theory]
    [InlineData(FixtureA, HashA)]
    [InlineData(FixtureB, HashB)]
    public void FromBytes_AgreesWithImagehash(string base64, ulong expected)
    {
        ulong actual = MetronCoverHash.FromBytes(Convert.FromBase64String(base64))!.Value;

        // A different resampler than Pillow's may move a bit; the match bar is 10.
        Assert.InRange(MetronCoverHash.Distance(actual, expected), 0, 2);
    }

    [Fact]
    public void DifferentPictures_AreFarApart()
    {
        Assert.False(MetronCoverHash.IsMatch(HashA, HashB));
        Assert.True(MetronCoverHash.IsMatch(HashA, HashA ^ 0b1011)); // three bits off: the same cover, rescanned
    }

    [Fact]
    public void Parse_ReadsMetronsSixteenHexDigits_AndNothingElse()
    {
        Assert.Equal(0xc585eb18bf1e5423, MetronCoverHash.Parse("c585eb18bf1e5423"));
        Assert.Equal("c585eb18bf1e5423", MetronCoverHash.Format(0xc585eb18bf1e5423));
        Assert.Null(MetronCoverHash.Parse(null));
        Assert.Null(MetronCoverHash.Parse(""));
        Assert.Null(MetronCoverHash.Parse("c585eb18"));
        Assert.Null(MetronCoverHash.Parse("zzzzzzzzzzzzzzzz"));
    }

    [Fact]
    public void AnUnreadableImage_HasNoHash()
    {
        Assert.Null(MetronCoverHash.FromBytes(new byte[] { 1, 2, 3, 4 }));
        Assert.Null(MetronCoverHash.FromBytes(null));
        Assert.Null(MetronCoverHash.FromFile(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png")));
    }
}
