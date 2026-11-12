using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Small images on character, team and creator chips (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.4). Local cache
/// only: a chip never starts a provider request. Chips are filled in one batch off the UI thread after they are built (one query per kind for the
/// few entities that have an image, instead of a database round trip per chip), and again whenever <see cref="Changed"/> fires: a profile image
/// arriving, or a custom image set or reset. Before this the chips read their image once at build time, so an image that arrived while the Detail
/// screen was kept alive (open a writer's page, come back) never showed.
/// </summary>
public static class EntityChipImages
{
    /// <summary>An entity's image changed somewhere in the app. Handlers always run on the UI thread (they read the chips' collections).</summary>
    public static event Action? Changed;

    /// <summary>Call from any thread after an entity's image was saved.</summary>
    public static void RaiseChanged()
    {
        if (Changed is null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }
    }

    /// <summary>One chip's image path, or null. Kept for single lookups; chips use <see cref="FillAsync"/>.</summary>
    public static string? Get(ComicMetadataEntityKind kind, string name) =>
        Resolve(new[] { (kind, name) }).TryGetValue(Key(kind, name), out var path) ? path : null;

    /// <summary>Image paths for a set of (kind, name) pairs, keyed by <see cref="Key"/>. Names match the index case-insensitively.</summary>
    public static IReadOnlyDictionary<string, string> Resolve(IEnumerable<(ComicMetadataEntityKind Kind, string Name)> wanted)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var byKind = wanted.Where(w => w.Kind is ComicMetadataEntityKind.Character or ComicMetadataEntityKind.Team or ComicMetadataEntityKind.Creator or ComicMetadataEntityKind.Location)
            .GroupBy(w => w.Kind).ToList();
        if (byKind.Count == 0)
        {
            return result;
        }

        using var context = PaperbunkrDb.CreateContext();
        foreach (var group in byKind)
        {
            var withImages = group.Key switch
            {
                ComicMetadataEntityKind.Character => context.Characters.Where(e => e.ImageUrl != null || e.CustomImagePath != null)
                    .Select(e => new { e.Name, e.ImageUrl, e.CustomImagePath }).ToList(),
                ComicMetadataEntityKind.Team => context.Teams.Where(e => e.ImageUrl != null || e.CustomImagePath != null)
                    .Select(e => new { e.Name, e.ImageUrl, e.CustomImagePath }).ToList(),
                ComicMetadataEntityKind.Location => context.Locations.Where(e => e.ImageUrl != null || e.CustomImagePath != null)
                    .Select(e => new { e.Name, e.ImageUrl, e.CustomImagePath }).ToList(),
                _ => context.Creators.Where(e => e.ImageUrl != null || e.CustomImagePath != null)
                    .Select(e => new { e.Name, e.ImageUrl, e.CustomImagePath }).ToList(),
            };
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in withImages)
            {
                string? path = EntityImageCache.HasCustomImage(row.CustomImagePath) ? EntityImageCache.CustomPath(row.CustomImagePath!)
                    : EntityImageCache.HasProviderImage(row.ImageUrl) ? EntityImageCache.ProviderPath(row.ImageUrl!)
                    : null;
                if (path is not null)
                {
                    byName.TryAdd(row.Name.Trim(), path);
                }
            }

            foreach (var (kind, name) in group)
            {
                if (byName.TryGetValue(name.Trim(), out var path))
                {
                    result[Key(kind, name)] = path;
                }
            }
        }

        return result;
    }

    public static string Key(ComicMetadataEntityKind kind, string name) => $"{(int)kind}|{name.Trim().ToUpperInvariant()}";

    /// <summary>Fills the chips' images off the UI thread, then sets them on the UI thread. Chips without an <c>EntityKind</c> are ignored.</summary>
    public static Task FillAsync(IReadOnlyList<TagPillViewModel> chips)
    {
        var entityChips = chips.Where(c => c.EntityKind is not null).ToList();
        if (entityChips.Count == 0)
        {
            return Task.CompletedTask;
        }

        var wanted = entityChips.Select(c => (c.EntityKind!.Value, c.Value)).Distinct().ToList();
        return Task.Run(() =>
        {
            IReadOnlyDictionary<string, string> paths;
            try
            {
                paths = Resolve(wanted);
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var chip in entityChips)
                {
                    chip.ImagePath = paths.TryGetValue(Key(chip.EntityKind!.Value, chip.Value), out var path) ? path : null;
                }
            });
        });
    }
}
