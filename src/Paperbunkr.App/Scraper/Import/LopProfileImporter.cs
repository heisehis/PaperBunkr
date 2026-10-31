using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Scraper.Import;

/// <summary>
/// What importing one <c>.lop</c> file did. <see cref="Saved"/> are the profiles now in the store; <see cref="Notes"/> is everything worth telling the user, already
/// worded for display ("&lt;profile&gt;: …" for what a saved profile could not carry over, "&lt;profile&gt; was not imported: …" for a refused one, the file name and
/// problem for an unreadable file).
/// </summary>
public sealed record LopImportSummary(IReadOnlyList<OrganizerProfile> Saved, IReadOnlyList<string> Notes, int Rejected, string? FileError, int FilesFailed = 0)
{
    public bool Failed => FileError is not null;
}

/// <summary>
/// Saves the profiles <see cref="LopProfileReader"/> accepts (docs/superpowers/specs/2026-10-04-lop-profile-import-design.md). A name already in use gets " (2)", " (3)"
/// rather than replacing the profile that has it. Nothing here touches the file system except opening the file it is given.
/// </summary>
public static class LopProfileImporter
{
    public static LopImportSummary Import(OrganizerProfileStore store, IEnumerable<string> paths)
    {
        var saved = new List<OrganizerProfile>();
        var notes = new List<string>();
        int rejected = 0, filesFailed = 0;
        foreach (string path in paths)
        {
            LopImportSummary result = Import(store, path);
            saved.AddRange(result.Saved);
            string sourceName = Path.GetFileName(path);
            notes.AddRange(result.Notes.Select(note => note.StartsWith(sourceName + ":", StringComparison.OrdinalIgnoreCase)
                ? note
                : $"{sourceName}: {note}"));
            rejected += result.Rejected;
            if (result.Failed) filesFailed++;
        }
        string? error = filesFailed > 0 && saved.Count == 0 && rejected == 0 ? $"{filesFailed} file(s) could not be imported." : null;
        return new LopImportSummary(saved, notes, rejected, error, filesFailed);
    }

    public static LopImportSummary Import(OrganizerProfileStore store, string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Import(store, stream, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string problem = $"Couldn't read the file: {ex.Message}";
            return new LopImportSummary(Array.Empty<OrganizerProfile>(), new[] { $"{Path.GetFileName(path)}: {problem}" }, 0, problem);
        }
    }

    public static LopImportSummary Import(OrganizerProfileStore store, Stream stream, string sourceName)
    {
        LopReadResult read = LopProfileReader.Read(stream);
        if (read.FileError is not null)
        {
            return new LopImportSummary(Array.Empty<OrganizerProfile>(), new[] { $"{sourceName}: {read.FileError}" }, 0, read.FileError);
        }

        var taken = new HashSet<string>(store.GetAll().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var saved = new List<OrganizerProfile>();
        var notes = new List<string>();

        foreach (ImportedProfile imported in read.Profiles)
        {
            OrganizerProfile profile = imported.Profile;
            profile.Name = Unique(profile.Name, taken);
            taken.Add(profile.Name);
            saved.Add(store.Save(profile));
            notes.AddRange(imported.Notes.Select(n => $"{profile.Name}: {n}"));
        }

        notes.AddRange(read.Rejected.Select(r => $"{r.Name} was not imported: {r.Reason}"));
        return new LopImportSummary(saved, notes, read.Rejected.Count, null);
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string candidate = $"{name} ({n})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
