using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists.Matching;

public sealed record ReviewSource(ComicReference Reference, MatchMethod Method, MatchState State, DateTime? CheckedUtc);
public sealed record ReviewEntry(int ItemId, int ListId, string ListName, int IssueId, bool Placeholder,
    ReviewSource? Source, string IssueState, string Label, MatchResult Match);
public sealed record ReviewChoice(ReviewEntry Entry, LibraryIssue Candidate, string CandidateIdentity);
public sealed record ReviewChange(ReviewEntry Before, ReviewSource? After, int TargetId, bool Undone = false, int? RestoredIssueId = null);
public sealed record ReviewUndoRow(ReviewChange Change, string? Conflict);
public sealed record ReviewUndoPreview(string BatchId, IReadOnlyList<ReviewUndoRow> Rows);

/// <summary>Staging reads never write. Apply and history are committed together; undo owns only link/source state.</summary>
public static class ReadingListReviewService
{
    public static IReadOnlyList<ReviewEntry> Load(PaperbunkrDbContext db, IReadOnlyCollection<int> listIds, int? itemId = null)
    {
        var library = LibraryIndex.Load(db);
        return db.ReadingListItems.AsNoTracking().Include(i => i.ReadingList).Include(i => i.Source)
            .Include(i => i.Issue).ThenInclude(i => i!.Series)
            .Where(i => listIds.Contains(i.ReadingListId) && (itemId == null ? i.Issue!.IsPlaceholder : i.Id == itemId))
            .OrderBy(i => i.ReadingListId).ThenBy(i => i.SortOrder).AsEnumerable()
            .Select(i => Capture(db, i, library)).ToList();
    }

    public static ReviewChoice Choose(PaperbunkrDbContext db, ReviewEntry entry, int issueId)
    {
        var library = LibraryIndex.Load(db);
        var candidate = library.Find(issueId) ?? throw new InvalidOperationException("That comic is no longer available.");
        return new(entry, candidate, CandidateIdentity(db, candidate));
    }

    public static bool Equivalent(ComicReference a, ComicReference b)
    {
        static string? Id(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static bool Conflict(string? x, string? y) => Id(x) is { } left && Id(y) is { } right && left != right;
        if (Conflict(a.ComicVineIssueId, b.ComicVineIssueId) || Conflict(a.MetronIssueId, b.MetronIssueId)
            || Conflict(a.ComicVineSeriesId, b.ComicVineSeriesId) || Conflict(a.MetronSeriesId, b.MetronSeriesId)) return false;
        bool sameId = Id(a.ComicVineIssueId) is { } cv && cv == Id(b.ComicVineIssueId)
            || Id(a.MetronIssueId) is { } metron && metron == Id(b.MetronIssueId);
        if (sameId) return true;
        return a.SeriesKey.Length > 0 && a.NumberKey.Length > 0 && a.SeriesKey == b.SeriesKey && a.NumberKey == b.NumberKey
            && a.Year == b.Year && NormalizeVolume(a.Volume) == NormalizeVolume(b.Volume) && Text(a.Format) == Text(b.Format)
            && Id(a.ComicVineIssueId) == Id(b.ComicVineIssueId) && Id(a.MetronIssueId) == Id(b.MetronIssueId)
            && Id(a.ComicVineSeriesId) == Id(b.ComicVineSeriesId) && Id(a.MetronSeriesId) == Id(b.MetronSeriesId);
    }

    public static IReadOnlyList<ReviewEntry> EquivalentEntries(PaperbunkrDbContext db, ReviewEntry entry)
    {
        if (entry.Source is null) return [];
        return EquivalentEntries(db, entry.Source.Reference, entry.ListId);
    }

    public static IReadOnlyList<ReviewEntry> EquivalentEntries(PaperbunkrDbContext db, ComicReference reference, int? excludeListId = null)
    {
        var lists = db.ReadingLists.Select(l => l.Id).ToList();
        return Load(db, lists).Where(e => e.ListId != excludeListId && e.Source is { Method: not MatchMethod.User }
            && Equivalent(reference, e.Source.Reference)).ToList();
    }

    public static ReadingListReviewBatch Apply(PaperbunkrDbContext db, IReadOnlyList<ReviewChoice> choices, string label)
    {
        if (choices.Count == 0) throw new InvalidOperationException("Choose at least one comic first.");
        using var tx = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        var library = LibraryIndex.Load(db);
        var changes = new List<ReviewChange>();
        var rows = new List<(ReadingListItem Item, ReviewChoice Choice)>();
        if (choices.Select(c => c.Entry.ItemId).Distinct().Count() != choices.Count)
            throw new InvalidOperationException("An entry has more than one staged choice.");
        foreach (var choice in choices)
        {
            var item = db.ReadingListItems.Include(i => i.Source).Include(i => i.Issue).ThenInclude(i => i!.Series)
                .FirstOrDefault(i => i.Id == choice.Entry.ItemId);
            if (item is null || !SameEntry(choice.Entry, Capture(db, item, library)))
                throw new InvalidOperationException($"{choice.Entry.Label} changed during review. Recheck it before applying.");
            if (library.Find(choice.Candidate.Id) is not { } current || current != choice.Candidate
                || CandidateIdentity(db, current) != choice.CandidateIdentity)
                throw new InvalidOperationException($"The chosen comic for {choice.Entry.Label} changed. Recheck it before applying.");
            if (db.ReadingListItems.Any(i => i.ReadingListId == item.ReadingListId && i.Id != item.Id && i.IssueId == current.Id)
                || rows.Any(r => r.Item.ReadingListId == item.ReadingListId && r.Choice.Candidate.Id == current.Id))
                throw new InvalidOperationException($"{current.SeriesName} #{current.Number} is already used in this list. Leave this entry unresolved or choose another comic.");
            rows.Add((item, choice));
        }
        foreach (var (item, choice) in rows)
        {
            item.Source ??= ReadingListResolver.NewSource(choice.Entry.Match.Reference, MatchMethod.None, MatchState.Unresolved, null);
            ReadingListItemLinker.Repoint(db, item, choice.Candidate.Id, MatchMethod.User);
            changes.Add(new(choice.Entry, Source(item.Source), choice.Candidate.Id));
        }
        var batch = new ReadingListReviewBatch { Label = label, Payload = JsonSerializer.Serialize(changes) };
        var cutoff = DateTime.UtcNow.AddDays(-30);
        db.Set<ReadingListReviewBatch>().RemoveRange(db.Set<ReadingListReviewBatch>().Where(b => b.CreatedUtc < cutoff));
        db.Set<ReadingListReviewBatch>().Add(batch);
        db.SaveChanges();
        ReadingListHealer.RemoveUnused(db, choices.Where(c => c.Entry.Placeholder).Select(c => c.Entry.IssueId).Distinct().ToList());
        tx?.Commit();
        return batch;
    }

    public static IReadOnlyList<ReadingListReviewBatch> History(PaperbunkrDbContext db, DateTime? now = null)
    {
        var cutoff = (now ?? DateTime.UtcNow).AddDays(-30);
        return db.Set<ReadingListReviewBatch>().AsNoTracking().Where(b => b.CreatedUtc >= cutoff).OrderByDescending(b => b.CreatedUtc).ToList();
    }

    public static ReviewUndoPreview PreviewUndo(PaperbunkrDbContext db, string batchId)
    {
        var batch = History(db).FirstOrDefault(b => b.Id == batchId)
            ?? throw new InvalidOperationException("That review batch is no longer available.");
        var changes = JsonSerializer.Deserialize<List<ReviewChange>>(batch.Payload)!;
        var result = new List<ReviewUndoRow>();
        var restored = new HashSet<(int List, int Issue)>();
        foreach (var change in changes)
        {
            var before = change.Before;
            int originalId = change.RestoredIssueId ?? before.IssueId;
            var item = db.ReadingListItems.AsNoTracking().Include(i => i.Source).FirstOrDefault(i => i.Id == before.ItemId);
            string? conflict = change.Undone ? "Already undone" : item is null ? "Entry was deleted"
                : item.ReadingListId != before.ListId || item.IssueId != change.TargetId || Source(item.Source) != change.After ? "Link changed since this batch"
                : db.Issues.AsNoTracking().FirstOrDefault(i => i.Id == originalId) is { } original && !OriginalIdentity(before, original) ? "Original comic identity changed"
                : !db.Issues.Any(i => i.Id == originalId) && (!before.Placeholder || !db.Series.Any(s => s.Id == PlaceholderSeries(before))) ? "Original comic or series was deleted"
                : db.ReadingListItems.Any(i => i.ReadingListId == before.ListId && i.Id != before.ItemId && i.IssueId == originalId)
                    || !restored.Add((before.ListId, originalId)) ? "Original comic is already used in this list" : null;
            result.Add(new(change, conflict));
        }
        return new(batchId, result);
    }

    public static int Undo(PaperbunkrDbContext db, ReviewUndoPreview preview)
    {
        using var tx = db.Database.BeginTransaction();
        var current = PreviewUndo(db, preview.BatchId);
        // A confirmation applies only to the exact preview, never to newly eligible rows.
        var ids = preview.Rows.Where(r => r.Conflict is null).Select(r => r.Change.Before.ItemId).ToHashSet();
        var changes = current.Rows.Select(r => r.Change).ToList();
        var placeholders = new Dictionary<int, Issue>();
        int count = 0;
        foreach (var row in current.Rows.Where(r => r.Conflict is null && ids.Contains(r.Change.Before.ItemId)))
        {
            var change = row.Change;
            var before = change.Before;
            var item = db.ReadingListItems.Include(i => i.Source).First(i => i.Id == before.ItemId);
            var original = db.Issues.Find(change.RestoredIssueId ?? before.IssueId);
            if (original is null && !placeholders.TryGetValue(before.IssueId, out original))
            {
                original = new Issue();
                foreach (var p in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(before.IssueState)!)
                {
                    var property = db.Entry(original).Property(p.Key);
                    if (property.Metadata.IsPrimaryKey()) continue;
                    property.CurrentValue = p.Value.Deserialize(property.Metadata.ClrType);
                }
                db.Issues.Add(original);
                placeholders[before.IssueId] = original;
            }
            item.Issue = original;
            if (original!.Id != 0) item.IssueId = original.Id;
            if (before.Source is null && item.Source is { } addedSource)
            {
                db.ReadingListItemSources.Remove(addedSource); item.Source = null;
            }
            else RestoreSource(item.Source, before.Source);
            if (db.ReadingLists.Find(before.ListId) is { } list) list.UpdatedAt = DateTime.UtcNow;
            changes[changes.IndexOf(change)] = change with { Undone = true };
            count++;
        }
        db.SaveChanges();
        // Remember recreated identities for rows left conflicted, so a later partial undo shares the same placeholder.
        changes = changes.Select(c => placeholders.TryGetValue(c.Before.IssueId, out var restoredIssue)
            ? c with { RestoredIssueId = restoredIssue.Id } : c).ToList();
        var batch = db.Set<ReadingListReviewBatch>().First(b => b.Id == preview.BatchId);
        batch.Payload = JsonSerializer.Serialize(changes);
        db.SaveChanges();
        tx.Commit();
        return count;
    }

    public static string CandidateIdentity(PaperbunkrDbContext db, LibraryIssue issue) => JsonSerializer.Serialize(new
    {
        Issue = issue,
        Ids = db.ComicMetadataExternalIds.AsNoTracking().Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.EntityId == issue.Id)
            .OrderBy(e => e.Provider).ThenBy(e => e.ExternalId).Select(e => new { e.Provider, e.ExternalId }).ToList(),
    });

    private static ReviewEntry Capture(PaperbunkrDbContext db, ReadingListItem item, LibraryIndex library)
    {
        var issue = item.Issue!;
        var reference = item.Source is { } source ? ReadingListResolver.ToReference(source)
            : new ComicReference(issue.Series?.Name ?? "", issue.Number ?? "", issue.Volume, issue.Year, issue.Format);
        string state = JsonSerializer.Serialize(db.Entry(issue).Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
        return new(item.Id, item.ReadingListId, item.ReadingList?.Name ?? db.ReadingLists.Where(l => l.Id == item.ReadingListId).Select(l => l.Name).First(),
            item.IssueId, issue.IsPlaceholder, Source(item.Source), state, reference.Display, MatchEngine.Match(reference, library));
    }
    private static bool SameEntry(ReviewEntry a, ReviewEntry b) => a.ItemId == b.ItemId && a.ListId == b.ListId
        && a.IssueId == b.IssueId && a.Source == b.Source && a.IssueState == b.IssueState;
    private static ReviewSource? Source(ReadingListItemSource? source) => source is null ? null
        : new(ReadingListResolver.ToReference(source), source.Method, source.State, source.LastCheckedUtc);
    private static void RestoreSource(ReadingListItemSource? target, ReviewSource? before)
    {
        if (target is null || before is null) return;
        target.Method = before.Method; target.State = before.State; target.LastCheckedUtc = before.CheckedUtc;
    }
    private static int PlaceholderSeries(ReviewEntry entry) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(entry.IssueState)!["SeriesId"].GetInt32();
    private static bool OriginalIdentity(ReviewEntry entry, Issue issue)
    {
        var saved = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(entry.IssueState)!;
        return saved["SeriesId"].GetInt32() == issue.SeriesId && saved["IsPlaceholder"].GetBoolean() == issue.IsPlaceholder
            && saved["Number"].Deserialize<string?>() == issue.Number && saved["Volume"].Deserialize<string?>() == issue.Volume
            && saved["Year"].Deserialize<int?>() == issue.Year;
    }
    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
    private static string? NormalizeVolume(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n.ToString(CultureInfo.InvariantCulture) : Text(s);
}
