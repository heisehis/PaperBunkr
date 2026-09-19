using Paperbunkr.Data.Tracking;
using Xunit;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="TrackerCredentialSanitizer"/> - confirmed live 2026-09-18 that a
/// malformed/garbage AniList access token produces the exact same generic <c>400 "Invalid token"</c>
/// error as a genuinely revoked one, so a corrupted paste fails silently at push time.
/// </summary>
public class TrackerCredentialSanitizerTests
{
    [Fact]
    public void SanitizeToken_TrimsWhitespaceAndNewlines()
    {
        Assert.Equal("abc123", TrackerCredentialSanitizer.SanitizeToken("  abc123\n"));
    }

    [Fact]
    public void SanitizeToken_StripsBearerPrefix()
    {
        Assert.Equal("abc123", TrackerCredentialSanitizer.SanitizeToken("Bearer abc123"));
    }

    [Fact]
    public void SanitizeToken_CaseInsensitiveBearerPrefix()
    {
        Assert.Equal("abc123", TrackerCredentialSanitizer.SanitizeToken("bearer abc123"));
    }

    [Fact]
    public void SanitizeToken_PlainToken_PassesThroughUnchanged()
    {
        Assert.Equal("abc123", TrackerCredentialSanitizer.SanitizeToken("abc123"));
    }

    [Fact]
    public void SanitizeFragmentToken_PlainToken_PassesThroughUnchanged()
    {
        Assert.Equal("eyJhbGciOi", TrackerCredentialSanitizer.SanitizeFragmentToken("eyJhbGciOi"));
    }

    [Fact]
    public void SanitizeFragmentToken_ExtractsFromFullFragment()
    {
        Assert.Equal("eyJhbGciOi", TrackerCredentialSanitizer.SanitizeFragmentToken("access_token=eyJhbGciOi&token_type=Bearer&expires_in=31536000"));
    }

    [Fact]
    public void SanitizeFragmentToken_ExtractsFromFullUrlWithFragmentMarker()
    {
        Assert.Equal("eyJhbGciOi", TrackerCredentialSanitizer.SanitizeFragmentToken("https://anilist.co/api/v2/oauth/pin#access_token=eyJhbGciOi&token_type=Bearer&expires_in=31536000"));
    }

    [Fact]
    public void SanitizeFragmentToken_ExtractsWhenAccessTokenIsLastParam_NoTrailingAmpersand()
    {
        Assert.Equal("eyJhbGciOi", TrackerCredentialSanitizer.SanitizeFragmentToken("access_token=eyJhbGciOi"));
    }

    [Fact]
    public void SanitizeFragmentToken_StripsBearerPrefixAfterExtraction()
    {
        Assert.Equal("eyJhbGciOi", TrackerCredentialSanitizer.SanitizeFragmentToken("access_token=Bearer eyJhbGciOi&token_type=Bearer"));
    }

    [Fact]
    public void SanitizeFragmentToken_TrimsWhitespace()
    {
        Assert.Equal("abc123", TrackerCredentialSanitizer.SanitizeFragmentToken("  abc123  \n"));
    }
}
