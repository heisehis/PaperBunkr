namespace Paperbunkr.Data.ReadingLists;

/// <summary>What a folder import did.</summary>
public sealed record ReadingListFolderImportResult(int ListsImported, int FoldersCreated, IReadOnlyList<string> Failures);

/// <summary>
/// A reading-list folder as a directory of <c>.cbl</c> files and back (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-
/// design.md §4). Export matches ComicRack CE's Export Folder (<c>ComicListLibraryBrowser</c>): one <c>.cbl</c> per list, a sub-directory
/// per sub-folder. Import is the reverse (decision Q9), taking <c>.csv</c> lists too.
/// </summary>
public static class ReadingListFolderIO
{
    /// <summary>Writes the folder into <paramref name="directory"/> (created if needed). Returns how many lists were written; stops at the first failure.</summary>
    public static int Export(PaperbunkrDbContext context, int folderId, string directory)
    {
        Directory.CreateDirectory(directory);
        int written = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in ReadingListFolders.Children(context, folderId))
        {
            string name = Unique(SafeName(child.Name), usedNames);
            if (child.IsFolder)
            {
                written += Export(context, child.Id, Path.Combine(directory, name));
            }
            else
            {
                CblReadingListIO.Export(context, child.Id, Path.Combine(directory, name + ".cbl"));
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// Imports <paramref name="directory"/> as a folder under <paramref name="parentFolderId"/>: a folder per directory (skipping any with
    /// no .cbl/.csv anywhere below), each file imported into its folder. A file that fails is reported and the rest continue.
    /// </summary>
    public static ReadingListFolderImportResult Import(PaperbunkrDbContext context, string directory, int? parentFolderId)
    {
        var failures = new List<string>();
        int lists = 0;
        int folders = 0;

        void ImportDirectory(string dir, int? parent)
        {
            if (!ContainsLists(dir))
            {
                return;
            }

            var folder = ReadingListFolders.Create(context, Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), parent);
            context.SaveChanges();
            folders++;

            foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
            {
                ImportDirectory(sub, folder.Id);
            }

            foreach (var file in Directory.EnumerateFiles(dir).Where(IsListFile).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var list = string.Equals(Path.GetExtension(file), ".csv", StringComparison.OrdinalIgnoreCase)
                        ? CsvReadingListIO.Import(context, file).List
                        : CblReadingListIO.Import(context, file);
                    ReadingListFolders.PlaceNewList(context, list, folder.Id);
                    context.SaveChanges();
                    lists++;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
                {
                    failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    context.ChangeTracker.Clear();
                }
            }
        }

        ImportDirectory(directory, parentFolderId);
        return new ReadingListFolderImportResult(lists, folders, failures);
    }

    /// <summary>A list or folder name made safe as a file or directory name.</summary>
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return safe.Length == 0 ? "Untitled" : safe;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        string candidate = name;
        for (int n = 2; !used.Add(candidate); n++)
        {
            candidate = $"{name} ({n})";
        }

        return candidate;
    }

    private static bool IsListFile(string path) =>
        Path.GetExtension(path) is var ext && (ext.Equals(".cbl", StringComparison.OrdinalIgnoreCase) || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsLists(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(IsListFile);
}
