using System;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Cleans up a raw pasted tracker credential (access token or Personal Access Token) before it's
/// stored - confirmed live 2026-09-18 that AniList's real API returns the exact same generic
/// <c>400 "Invalid token"</c> error for a garbage/malformed token as it does for a genuinely
/// expired/revoked one, so a silently-corrupted paste fails at push time with no way to tell it
/// apart from a real auth problem. Every one of <see cref="Adapters.AniListTrackerAdapter"/>/
/// <see cref="Adapters.BangumiTrackerAdapter"/>/<see cref="Adapters.MangaBakaTrackerAdapter"/>'s own
/// <c>CompleteConnect</c> stores whatever the user pastes verbatim, with no exchange/validation
/// call in between (unlike every OAuth-code-based adapter in this file, where a malformed paste
/// fails immediately and visibly at the exchange step) - these three are the ones actually exposed
/// to this failure mode.
/// </summary>
public static class TrackerCredentialSanitizer
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Trims whitespace/newlines and strips an accidental <c>"Bearer "</c> prefix - for
    /// Bangumi/MangaBaka's plain pasted Personal Access Token.</summary>
    public static string SanitizeToken(string pasted) => StripBearerPrefix(pasted.Trim());

    /// <summary>Same as <see cref="SanitizeToken"/>, plus extracts the value out of an
    /// <c>access_token=...&amp;token_type=...&amp;expires_in=...</c> fragment (or a full pin-page
    /// URL containing one) - AniList's implicit-grant "pin" redirect page
    /// (https://anilist.co/api/v2/oauth/pin) hands the user text to copy, and it's easy to select
    /// more of that page than just the bare token.</summary>
    public static string SanitizeFragmentToken(string pasted)
    {
        string trimmed = pasted.Trim();
        const string marker = "access_token=";
        int index = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            string afterMarker = trimmed[(index + marker.Length)..];
            int ampersand = afterMarker.IndexOf('&');
            trimmed = (ampersand >= 0 ? afterMarker[..ampersand] : afterMarker).Trim();
        }

        return StripBearerPrefix(trimmed);
    }

    private static string StripBearerPrefix(string value) =>
        value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) ? value[BearerPrefix.Length..].Trim() : value;
}
