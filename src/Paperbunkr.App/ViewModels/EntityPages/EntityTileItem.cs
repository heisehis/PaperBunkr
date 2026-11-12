using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels.EntityPages;

/// <summary>
/// What the shared <c>EntityTile</c> shows (entity pages redesign §5.1): a portrait, or a monogram with the first-appearance cover as a small inset
/// when there's no portrait. <see cref="CoverKey"/> is that inset's cover id, null when the cover file isn't cached.
/// </summary>
public sealed record EntityTileItem(ComicMetadataEntityKind Kind, int Id, string Name, string Caption, string? ImagePath, string? CoverKey, string Initials)
{
    public bool HasImage => ImagePath is not null;
    public bool ShowMonogram => !HasImage;
    public bool HasInsetCover => !HasImage && CoverKey is not null;
    public bool CanOpenOnMap => Kind is ComicMetadataEntityKind.Character or ComicMetadataEntityKind.Team;

    /// <summary>Builds a tile from a relation row, choosing the image the same way every entity surface does.</summary>
    public static EntityTileItem From(EntityRelation relation, string caption, System.Func<int, bool>? coverExists = null)
    {
        var image = EntityImageResolver.Choose(relation.Name, relation.CustomImagePath, relation.ImageUrl, relation.FirstIssueId);
        return new EntityTileItem(relation.Kind, relation.Id, relation.Name, caption, image.Path, CoverKeyFor(image.CoverIssueId ?? relation.FirstIssueId, coverExists), image.Initials);
    }

    /// <summary>The cover id as the image key, or null when there's no cached cover to draw.</summary>
    public static string? CoverKeyFor(int? issueId, System.Func<int, bool>? coverExists = null)
    {
        if (issueId is not int id) return null;
        bool exists = coverExists?.Invoke(id) ?? CoverThumbnailService.GetEffectiveCoverPath(id) is not null;
        return exists ? id.ToString() : null;
    }
}
