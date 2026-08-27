using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Maps a provider's raw <see cref="ExternalMediaMetadata.Status"/> string to <see cref="SeriesStatus"/>
/// (docs/superpowers/specs/2026-08-23-apply-from-provider-design.md) - provider-agnostic, kept
/// separate from any one normalizer since more than one provider's raw values need mapping.
/// Unrecognized values resolve to <see cref="SeriesStatus.Unknown"/> rather than guessing.
/// </summary>
public static class SeriesStatusNormalizer
{
    /// <summary>
    /// AniList values are its own GraphQL <c>MediaStatus</c> enum, confirmed by
    /// <see cref="AniListNormalizer"/> passing <c>media.Status</c> straight through. MangaBaka
    /// values confirmed live against real `v2/series/{id}` responses (`"releasing"`, `"completed"`)
    /// - `hiatus`/`cancelled`/`not_yet_released` weren't observed on any real series fetched this
    /// session, so those three are an educated guess from AniList's own naming convention, not
    /// independently confirmed; they fall through safely to <see cref="SeriesStatus.Unknown"/> if
    /// wrong; a future session should confirm them against a real hiatus/cancelled series rather
    /// than trust this comment alone. <c>NOT_YET_RELEASED</c>/`not_yet_released` map to
    /// <see cref="SeriesStatus.Unknown"/>, not <see cref="SeriesStatus.Ongoing"/> - nothing has
    /// actually started releasing yet, so "ongoing" would overstate what's known.
    /// </summary>
    public static SeriesStatus Normalize(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return SeriesStatus.Unknown;
        }

        return rawStatus.Trim().ToUpperInvariant() switch
        {
            "FINISHED" or "COMPLETED" => SeriesStatus.Completed,
            "RELEASING" or "ONGOING" => SeriesStatus.Ongoing,
            "CANCELLED" or "CANCELED" => SeriesStatus.Cancelled,
            "HIATUS" => SeriesStatus.Hiatus,
            _ => SeriesStatus.Unknown,
        };
    }
}
