using System;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Which image a character, team or creator tile shows.</summary>
public enum EntityImageKind
{
    /// <summary>The user's own image.</summary>
    Custom,

    /// <summary>The cached Metron or ComicVine image.</summary>
    Provider,

    /// <summary>The cover of the first appearance in the library, cropped to the tile.</summary>
    FirstAppearanceCover,

    /// <summary>Initials on a tinted square.</summary>
    Initials,
}

/// <summary>The image choice: a file path for <see cref="EntityImageKind.Custom"/>/<see cref="EntityImageKind.Provider"/>, an issue id for the cover, initials for the rest.</summary>
public sealed record EntityImageChoice(EntityImageKind Kind, string? Path, int? CoverIssueId, string Initials);

/// <summary>
/// The display order from docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3: custom image, then the cached provider
/// image, then the first appearance's cover, then initials. Pure apart from the two file-exists checks, which are passed in for tests.
/// </summary>
public static class EntityImageResolver
{
    public static EntityImageChoice Choose(
        string name, string? customImagePath, string? imageUrl, int? firstAppearanceIssueId,
        Func<string?, bool>? hasCustom = null, Func<string?, bool>? hasProvider = null)
    {
        hasCustom ??= EntityImageCache.HasCustomImage;
        hasProvider ??= EntityImageCache.HasProviderImage;
        string initials = Initials(name);

        if (hasCustom(customImagePath))
        {
            return new EntityImageChoice(EntityImageKind.Custom, EntityImageCache.CustomPath(customImagePath!), null, initials);
        }

        if (hasProvider(imageUrl))
        {
            return new EntityImageChoice(EntityImageKind.Provider, EntityImageCache.ProviderPath(imageUrl!), null, initials);
        }

        return firstAppearanceIssueId is int issueId
            ? new EntityImageChoice(EntityImageKind.FirstAppearanceCover, null, issueId, initials)
            : new EntityImageChoice(EntityImageKind.Initials, null, null, initials);
    }

    public static EntityImageChoice Choose(Character c, int? firstAppearanceIssueId) => Choose(c.Name, c.CustomImagePath, c.ImageUrl, firstAppearanceIssueId);

    public static EntityImageChoice Choose(Team t, int? firstAppearanceIssueId) => Choose(t.Name, t.CustomImagePath, t.ImageUrl, firstAppearanceIssueId);

    public static EntityImageChoice Choose(Creator c, int? firstAppearanceIssueId) => Choose(c.Name, c.CustomImagePath, c.ImageUrl, firstAppearanceIssueId);

    /// <summary>Up to two letters: the first letters of the first and last words ("Kitty Pryde" → "KP", "X-Men" → "X").</summary>
    public static string Initials(string? name)
    {
        var words = (name ?? string.Empty)
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.FirstOrDefault(char.IsLetterOrDigit))
            .Where(ch => ch != default)
            .ToList();
        return words.Count switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(words[0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(words[0]), char.ToUpperInvariant(words[^1])),
        };
    }
}
