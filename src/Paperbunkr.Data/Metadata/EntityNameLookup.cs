using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
namespace Paperbunkr.Data.Metadata;
public static class EntityNameLookup
{
    public static int? Find(PaperbunkrDbContext context, ComicMetadataEntityKind kind, string name)
    {
        string key = name.Trim().ToLowerInvariant();
        var rows = kind switch
        {
            ComicMetadataEntityKind.Character => context.Characters.Select(e => new { e.Id, e.Name }),
            ComicMetadataEntityKind.Team => context.Teams.Select(e => new { e.Id, e.Name }),
            ComicMetadataEntityKind.Location => context.Locations.Select(e => new { e.Id, e.Name }),
            ComicMetadataEntityKind.Creator => context.Creators.Select(e => new { e.Id, e.Name }),
            ComicMetadataEntityKind.Publisher => context.Publishers.Select(e => new { e.Id, e.Name }),
            _ => context.Characters.Where(e => false).Select(e => new { e.Id, e.Name }),
        };
        // SQLite lower() is ASCII-only. Keep the cheap path; only a name with non-ASCII letters needs the in-memory ordinal case fold, which
        // reads the whole table, so a plain ASCII miss no longer pays for it.
        int? hit = rows.Where(e => e.Name.ToLower() == key).Select(e => (int?)e.Id).FirstOrDefault();
        if (hit is not null || name.All(char.IsAscii))
        {
            return hit;
        }

        return rows.AsEnumerable().FirstOrDefault(e => string.Equals(e.Name, name.Trim(), System.StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// <see cref="Find"/>, and when the name is not indexed yet, indexes just the issues whose text mentions it and looks again. A click on a
    /// writer, artist or character then opens their page even before the one-time rebuild has run (on 2026-11-10 the user's library had
    /// 161 issues pencilled by an artist missing from the index, so the click fell through to a Library search).
    /// </summary>
    public static int? FindOrIndex(PaperbunkrDbContext context, ComicMetadataEntityKind kind, string name)
    {
        if (Find(context, kind, name) is int found)
        {
            return found;
        }

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        string pattern = "%" + trimmed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var issues = context.Issues.AsNoTracking();
        var candidates = kind switch
        {
            ComicMetadataEntityKind.Character => issues.Where(i => EF.Functions.Like(i.Characters, pattern, "\\")),
            ComicMetadataEntityKind.Team => issues.Where(i => EF.Functions.Like(i.Teams, pattern, "\\")),
            ComicMetadataEntityKind.Location => issues.Where(i => EF.Functions.Like(i.Locations, pattern, "\\")),
            ComicMetadataEntityKind.Creator => issues.Where(i =>
                EF.Functions.Like(i.Writer, pattern, "\\") || EF.Functions.Like(i.Penciller, pattern, "\\") ||
                EF.Functions.Like(i.Inker, pattern, "\\") || EF.Functions.Like(i.Colorist, pattern, "\\") ||
                EF.Functions.Like(i.Letterer, pattern, "\\") || EF.Functions.Like(i.CoverArtist, pattern, "\\") ||
                EF.Functions.Like(i.Editor, pattern, "\\") || EF.Functions.Like(i.Translator, pattern, "\\")),
            _ => null,
        };
        if (candidates is null)
        {
            return null;
        }

        var ids = candidates.Select(i => i.Id).ToList();
        if (ids.Count == 0)
        {
            return null;
        }

        EntityIndexBulk.Sync(context, ids);
        return Find(context, kind, name);
    }

    public static string? Name(PaperbunkrDbContext context, ComicMetadataEntityKind kind, int id) => kind switch
    {
        ComicMetadataEntityKind.Character => context.Characters.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
        ComicMetadataEntityKind.Team => context.Teams.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
        ComicMetadataEntityKind.Location => context.Locations.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
        ComicMetadataEntityKind.Creator => context.Creators.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
        ComicMetadataEntityKind.Publisher => context.Publishers.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
        _ => null,
    };
}
