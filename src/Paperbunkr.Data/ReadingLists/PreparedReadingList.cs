using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Matching;
using Paperbunkr.Data.ReadingLists.Sources;
using Paperbunkr.Data.SmartLists.CeImport;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Detached, read-only input: file bytes and remote responses are consumed once, before acceptance.</summary>
public sealed record PreparedReadingList(string Name, IReadOnlyList<ComicReference> References)
{
    public IReadOnlyList<string> Skipped { get; init; } = [];
    public int? Reported { get; init; }
    public string? Source { get; init; }
    public string? ArcId { get; init; }
    public string? ArcName { get; init; }
    public string? Description { get; init; }
    public string? CoverImageUrl { get; init; }
    public IReadOnlyList<ArcIssue>? ArcIssues { get; init; }
    public IReadOnlyList<string> Folders { get; init; } = [];
    public ReadingList CreateList() => new()
    {
        Name = Name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Source = Source, ArcId = ArcId, ArcName = Source is null ? null : ArcName ?? Name,
        Description = Description, CoverImageUrl = CoverImageUrl,
        Type = Source is null ? ReadingListType.User : ReadingListType.Community,
    };
}

public sealed record PreparedImport(IReadOnlyList<PreparedReadingList> Lists, IReadOnlyList<string> Failures);
public sealed record ImportReviewList(PreparedReadingList Input, Resolution Resolution, IReadOnlyList<string?> Identities);
public sealed record ImportReviewChoice(int ListIndex, int EntryIndex, LibraryIssue Candidate, string Identity);

public static class ReadingListImportReview
{
    public static IReadOnlyList<ImportReviewList> Resolve(PaperbunkrDbContext db, PreparedImport input)
    {
        var library = LibraryIndex.Load(db);
        return input.Lists.Select(list =>
        {
            var resolution = ReadingListResolver.Resolve(library, list.References);
            return new ImportReviewList(list, resolution, resolution.Entries.Select(e => e.IssueId is int id
                ? ReadingListReviewService.CandidateIdentity(db, library.Find(id)!) : null).ToList());
        }).ToList();
    }

    public static IReadOnlyList<int> Commit(PaperbunkrDbContext db, IReadOnlyList<ImportReviewList> manifest,
        IReadOnlyList<ImportReviewChoice> choices, bool keepUnresolved, int? parentFolderId)
    {
        using var tx = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        var library = LibraryIndex.Load(db);
        var prepared = new List<(ImportReviewList List, Resolution Resolution)>();
        foreach (var (list, listIndex) in manifest.Select((l, n) => (l, n)))
        {
            var fresh = ReadingListResolver.Resolve(library, list.Input.References);
            var entries = list.Resolution.Entries.ToList();
            for (int n = 0; n < entries.Count; n++)
            {
                var old = entries[n];
                if (JsonSerializer.Serialize(fresh.Entries[n]) != JsonSerializer.Serialize(old)
                    || old.IssueId is int oldId && ReadingListReviewService.CandidateIdentity(db, library.Find(oldId)!) != list.Identities[n])
                    throw new InvalidOperationException($"{old.Reference.Display} changed during preview. Recheck before importing.");
                var choice = choices.SingleOrDefault(c => c.ListIndex == listIndex && c.EntryIndex == n);
                if (choice is not null)
                {
                    if (library.Find(choice.Candidate.Id) is not { } current || current != choice.Candidate
                        || ReadingListReviewService.CandidateIdentity(db, current) != choice.Identity)
                        throw new InvalidOperationException($"The chosen comic for {old.Reference.Display} changed. Recheck it.");
                    entries[n] = new(old.Reference, new MatchResult(old.Reference, MatchClass.Strong, [new(current, [])], false), EntryOutcome.MatchedByMetadata);
                }
            }
            var taken = new HashSet<int>();
            foreach (var entry in entries.Where(e => e.Outcome != EntryOutcome.Duplicate))
                if (entry.IssueId is int id && !taken.Add(id))
                    throw new InvalidOperationException($"{entry.Reference.Display}: that comic is already used in this list.");
            prepared.Add((list, new Resolution(entries.Where(e => keepUnresolved || e.IssueId is not null).ToList(), library)));
        }
        var folderIds = new Dictionary<string, int>();
        var ids = new List<int>();
        foreach (var (list, resolution) in prepared)
        {
            int? folder = parentFolderId;
            string path = "";
            foreach (string name in list.Input.Folders)
            {
                path += "/" + name;
                if (!folderIds.TryGetValue(path, out int folderId))
                {
                    var created = ReadingListFolders.Create(db, name, folder);
                    db.SaveChanges(); folderIds[path] = folderId = created.Id;
                }
                folder = folderId;
            }
            var entity = list.Input.CreateList();
            var items = ReadingListResolver.Materialize(db, resolution);
            int order = 0;
            foreach (var item in items)
            {
                int originalIndex = list.Input.References.ToList().IndexOf(item.Entry.Reference);
                var row = new ReadingListItem { Issue = item.Issue, Source = item.Source, SortOrder = order++ };
                if (choices.Any(c => c.ListIndex == manifest.ToList().IndexOf(list) && c.EntryIndex == originalIndex)) row.Source.Method = MatchMethod.User;
                entity.Items.Add(row);
            }
            if (list.Input.ArcIssues is { } arcs)
                ArcReadingListBuilder.DetectRoles(items.Select((item, n) => (arcs[list.Input.References.ToList().IndexOf(item.Entry.Reference)], item.Issue, entity.Items.ElementAt(n))).ToList());
            db.ReadingLists.Add(entity);
            ReadingListFolders.PlaceNewList(db, entity, folder);
            ReadingListManager.RecordCreatedWithItems(db, entity);
            db.SaveChanges(); ids.Add(entity.Id);
        }
        tx?.Commit();
        return ids;
    }

    public static PreparedImport PrepareFolder(string directory)
    {
        var lists = new List<PreparedReadingList>(); var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).Equals(".cbl", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Path.GetExtension(file).Equals(".cbl", StringComparison.OrdinalIgnoreCase) && CblFile.Detect(file) == CblFileKind.SmartList)
                { failures.Add($"{Path.GetRelativePath(directory, file)}: smart list; import separately on the Smart screen."); continue; }
                var list = Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? CsvReadingListIO.Prepare(file) : CblReadingListIO.Prepare(file);
                string relative = Path.GetRelativePath(directory, Path.GetDirectoryName(file)!);
                var folders = new List<string> { Path.GetFileName(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)) };
                if (relative != ".") folders.AddRange(relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                lists.Add(list with { Folders = folders });
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
            { failures.Add($"{Path.GetRelativePath(directory, file)}: {ex.Message}"); }
        }
        return new(lists, failures);
    }
}
