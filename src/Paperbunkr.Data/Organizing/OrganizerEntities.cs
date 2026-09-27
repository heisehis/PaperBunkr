using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Paperbunkr.Data.Organizing;

/// <summary>
/// A named organizer configuration (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 4.1): CE's Library Organizer profile, kept as the
/// full-parity list of named profiles rather than one active configuration. Stored in the core database (the plugin kept them in its own LiteDB file).
/// </summary>
public class OrganizerProfile
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // CE's own fallback defaults (losettings.py:396-397), used when a brand new profile is created with nothing else specified.
    public string FolderTemplate { get; set; } = @"{<publisher>}\{<imprint>}\{<series>}{ (<startyear>{ <format>})}";

    public string FileTemplate { get; set; } = "{<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}";

    public OrganizerMode Mode { get; set; } = OrganizerMode.Move;

    public AutomationCollisionPolicy AutomationCollisionPolicy { get; set; } = AutomationCollisionPolicy.Rename;

    public bool RemoveEmptyFolders { get; set; } = true;

    /// <summary>The scheduled "Organize library" task (off by default) uses the first profile with this set; it never asks anything, so collisions follow <see cref="AutomationCollisionPolicy"/>.</summary>
    public bool UseForScheduledRun { get; set; }

    public string BaseFolder { get; set; } = string.Empty;

    /// <summary>The plugin's <c>UseFolder</c> / <c>UseFileName</c> (`losettings.py:75-77`): turn either half of the rename off to organize by
    /// folder only (files keep their names) or to rename in place (files stay in their folders). Both default on.</summary>
    public bool UseFolder { get; set; } = true;

    public bool UseFileName { get; set; } = true;

    /// <summary>Folder name used where a folder template segment comes out empty (`EmptyFolder`, `losettings.py:58`). Blank, the default,
    /// makes an empty segment vanish from the path instead of leaving a gap.</summary>
    public string EmptyFolder { get; set; } = string.Empty;

    /// <summary>The <see cref="EmptyData"/> table as JSON (token name -> text used when that token comes out empty).</summary>
    public string EmptyDataJson { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, string> EmptyData
    {
        get => DeserializeOrEmpty<Dictionary<string, string>>(EmptyDataJson) ?? new Dictionary<string, string>();
        set => EmptyDataJson = value is { Count: > 0 } ? JsonSerializer.Serialize(value) : string.Empty;
    }

    /// <summary>The plugin's <c>FailEmptyValues</c> + <c>FailedFields</c> (`losettings.py:96-99`): a book whose listed tokens come out empty
    /// is left where it is and reported, rather than filed into a folder with a hole in its name.</summary>
    public bool FailEmptyValues { get; set; }

    public string FailedFieldsJson { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<string> FailedFields
    {
        get => DeserializeOrEmpty<List<string>>(FailedFieldsJson) ?? new List<string>();
        set => FailedFieldsJson = value is { Count: > 0 } ? JsonSerializer.Serialize(value) : string.Empty;
    }

    /// <summary>Folders whose books are never organized - a book is excluded when its current path contains one of these (a plain substring
    /// match, `locommon.py:634-641`).</summary>
    public string ExcludeFoldersJson { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<string> ExcludeFolders
    {
        get => DeserializeOrEmpty<List<string>>(ExcludeFoldersJson) ?? new List<string>();
        set => ExcludeFoldersJson = value is { Count: > 0 } ? JsonSerializer.Serialize(value) : string.Empty;
    }

    private static T? DeserializeOrEmpty<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Serialized plugin-API condition group evaluated by the app's rules engine; null means "no exclude rule - organize everything" (CE's zero-rules default).</summary>
    public string? ExcludeRuleJson { get; set; }

    /// <summary>The <see cref="MonthNames"/> table as JSON (empty = CE's English default). A JSON column because a dictionary doesn't map to columns usefully.</summary>
    public string MonthNamesJson { get; set; } = string.Empty;

    /// <summary>CE's per-profile month-name table; empty means <see cref="Naming.FieldResolvers.DefaultMonthNames"/>. Not mapped: it is a view over <see cref="MonthNamesJson"/>.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<int, string> MonthNames
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MonthNamesJson))
            {
                return new Dictionary<int, string>();
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<int, string>>(MonthNamesJson) ?? new Dictionary<int, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<int, string>();
            }
        }
        set => MonthNamesJson = value is { Count: > 0 } ? JsonSerializer.Serialize(value) : string.Empty;
    }
}

/// <summary>One organize run that moved files, kept so it can be undone (and so what moved where stays auditable).</summary>
public class OrganizeBatch
{
    public int Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public string ProfileName { get; set; } = string.Empty;

    /// <summary>The profile's base folder at the time of the run - the boundary an undo may clean up empty folders up to. Null for runs recorded before this was kept.</summary>
    public string? BaseFolder { get; set; }

    public List<OrganizeMove> Moves { get; set; } = new();
}

/// <summary>
/// One real move recorded for undo. An undo marks it <see cref="IsReverted"/> instead of deleting it, so the history of where files have been stays
/// intact and a batch that was already undone is never offered again.
/// </summary>
public class OrganizeMove
{
    public int Id { get; set; }

    public int BatchId { get; set; }

    public OrganizeBatch? Batch { get; set; }

    public string OldPath { get; set; } = string.Empty;

    public string NewPath { get; set; } = string.Empty;

    public DateTime MovedAtUtc { get; set; }

    public bool IsReverted { get; set; }
}

/// <summary>CRUD for <see cref="OrganizerProfile"/> in the core database.</summary>
public sealed class OrganizerProfileStore(Func<PaperbunkrDbContext> createContext)
{
    public IReadOnlyList<OrganizerProfile> GetAll()
    {
        using var context = createContext();
        return context.OrganizerProfiles.AsEnumerable().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public OrganizerProfile? Get(int id)
    {
        using var context = createContext();
        return context.OrganizerProfiles.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Inserts a new profile (Id 0) or updates an existing one, returning it with its assigned Id.</summary>
    public OrganizerProfile Save(OrganizerProfile profile)
    {
        using var context = createContext();
        if (profile.Id == 0)
        {
            context.OrganizerProfiles.Add(profile);
        }
        else
        {
            context.OrganizerProfiles.Update(profile);
        }

        context.SaveChanges();
        return profile;
    }

    public void Delete(int id)
    {
        using var context = createContext();
        var existing = context.OrganizerProfiles.FirstOrDefault(p => p.Id == id);
        if (existing is not null)
        {
            context.OrganizerProfiles.Remove(existing);
            context.SaveChanges();
        }
    }
}

/// <summary>
/// The undo log for moves (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 4.1). Only <see cref="OrganizerMode.Move"/> is undoable (a copy
/// leaves the original untouched). Write ordering matters: the file move and the issue's path update are authoritative and happen first; recording here comes after, best
/// effort, so a failure only costs that one item its undo coverage.
/// </summary>
public sealed class OrganizeUndoLog(Func<PaperbunkrDbContext> createContext)
{
    /// <summary>Starts a batch and returns its id.</summary>
    public int BeginBatch(string profileName, string? baseFolder = null)
    {
        using var context = createContext();
        var batch = new OrganizeBatch { StartedAtUtc = DateTime.UtcNow, ProfileName = profileName, BaseFolder = baseFolder };
        context.OrganizeBatches.Add(batch);
        context.SaveChanges();
        return batch.Id;
    }

    public void Record(int batchId, string oldPath, string newPath)
    {
        using var context = createContext();
        context.OrganizeMoves.Add(new OrganizeMove { BatchId = batchId, OldPath = oldPath, NewPath = newPath, MovedAtUtc = DateTime.UtcNow });
        context.SaveChanges();
    }

    /// <summary>The moves of the most recent organize run that moved anything, newest move first - empty once that run was
    /// undone. Only the last run is ever undoable (the plugin's own rule); an older run does not resurface after it.</summary>
    public IReadOnlyList<OrganizeMove> GetLastBatch()
    {
        using var context = createContext();
        int? latestBatchId = context.OrganizeMoves.Max(m => (int?)m.BatchId);
        if (latestBatchId is null)
        {
            return Array.Empty<OrganizeMove>();
        }

        return context.OrganizeMoves.AsNoTracking().Where(m => m.BatchId == latestBatchId && !m.IsReverted).OrderByDescending(m => m.Id).ToList();
    }

    /// <summary>The base folder recorded for a batch, or null when it was not kept.</summary>
    public string? GetBatchBaseFolder(int batchId)
    {
        using var context = createContext();
        return context.OrganizeBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.BaseFolder).FirstOrDefault();
    }

    /// <summary>Marks a batch's moves reverted (kept, not deleted) so it is never offered again.</summary>
    public void MarkBatchReverted(int batchId)
    {
        using var context = createContext();
        foreach (var move in context.OrganizeMoves.Where(m => m.BatchId == batchId && !m.IsReverted).ToList())
        {
            move.IsReverted = true;
        }

        context.SaveChanges();
    }
}
