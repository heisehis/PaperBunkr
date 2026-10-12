using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists.CeImport;

/// <summary>The outcome of importing one ComicRack smart-list file.</summary>
/// <param name="List">The saved list, or null when the import was refused.</param>
/// <param name="Warnings">Every condition that could not be kept.</param>
/// <param name="RefusedReason">Why nothing was saved (every condition was unsupported), or null.</param>
public sealed record SmartListImportResult(SmartList? List, IReadOnlyList<CeImportWarning> Warnings, string? RefusedReason);

/// <summary>
/// Saves a ComicRack smart list as a native <see cref="SmartList"/> (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md). A list whose every
/// condition is unsupported is refused rather than saved: an empty AND list matches the whole library, which is worse than no list.
/// </summary>
public static class SmartListFileImporter
{
    public static SmartListImportResult Import(PaperbunkrDbContext context, string path, CeSmartListReader? reader = null)
    {
        using FileStream stream = File.OpenRead(path);
        CeSmartListDraft draft = (reader ?? new CeSmartListReader()).Read(stream, Path.GetFileNameWithoutExtension(path));
        return Save(context, draft);
    }

    public static SmartListImportResult Save(PaperbunkrDbContext context, CeSmartListDraft draft)
    {
        if (draft.IsEmpty)
        {
            return new SmartListImportResult(
                null,
                draft.Warnings,
                $"'{draft.Name}' was not imported: none of its conditions can be used here, and a list with no conditions would match your whole library.");
        }

        SmartList list = draft.List;
        list.Name = UniqueName(context, draft.Name);
        list.SortOrder = context.SmartLists.Count();
        context.SmartLists.Add(list);
        context.SaveChanges();
        return new SmartListImportResult(list, draft.Warnings, null);
    }

    /// <summary>"Name", then "Name (2)", "Name (3)"... against the existing smart lists, ignoring case.</summary>
    private static string UniqueName(PaperbunkrDbContext context, string name)
    {
        var existing = context.SmartLists.Select(s => s.Name).ToList().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string candidate = $"{name} ({n})";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
