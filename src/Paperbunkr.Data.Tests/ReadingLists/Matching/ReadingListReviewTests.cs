using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.Data.Tests.ReadingLists.Matching;

public sealed class ReadingListReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-review-" + Guid.NewGuid().ToString("N"));
    public ReadingListReviewTests() { Directory.CreateDirectory(_root); using var db = Open(); db.Database.EnsureCreated(); }
    private PaperbunkrDbContext Open() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")}").Options);
    private (int List, int Item, int Comic) Seed(string name = "List", bool sharedPlaceholder = false)
    {
        using var db = Open();
        var series = new Series { Name = "Title", SortName = "Title" };
        var comic = new Issue { Series = series, Number = "1", Year = 2025, FilePath = "comic-" + Guid.NewGuid() + ".cbz" };
        db.Issues.Add(comic);
        var placeholder = sharedPlaceholder ? db.Issues.FirstOrDefault(i => i.IsPlaceholder) : null;
        placeholder ??= new Issue { Series = series, Number = "1", Year = 2020, IsPlaceholder = true, FileIsMissing = true };
        var row = new ReadingListItem { Issue = placeholder, Notes = "my note", SortOrder = 7, Role = EventMembershipRole.Core,
            Source = ReadingListResolver.NewSource(new ComicReference("Title", "1", Year: 2020), MatchMethod.None, MatchState.NeedsChoice, null) };
        var list = new ReadingList { Name = name, Items = { row } };
        db.ReadingLists.Add(list); db.SaveChanges(); return (list.Id, row.Id, comic.Id);
    }
    private ReviewChoice Choice(int list, int comic)
    { using var db = Open(); return ReadingListReviewService.Choose(db, ReadingListReviewService.Load(db, [list]).Single(), comic); }

    [Fact] public void LoadIsReadOnlyAndApplyPersistsHistoryThenUndoRecreatesPlaceholder()
    {
        var seed = Seed(); var choice = Choice(seed.List, seed.Comic); string batchId;
        using (var db = Open())
        {
            Assert.Equal(2, db.Issues.Count()); Assert.Empty(db.ReadingListReviewBatches);
            batchId = ReadingListReviewService.Apply(db, [choice], "List").Id;
            Assert.Equal(seed.Comic, db.ReadingListItems.Single().IssueId);
            Assert.False(db.Issues.Any(i => i.IsPlaceholder));
        }
        using (var db = Open())
        {
            var row = db.ReadingListItems.Single(); row.Notes = "later note"; row.SortOrder = 99; row.Role = EventMembershipRole.Optional; db.SaveChanges();
        }
        using (var db = Open())
        {
            Assert.Single(ReadingListReviewService.History(db));
            var preview = ReadingListReviewService.PreviewUndo(db, batchId);
            Assert.Null(preview.Rows.Single().Conflict);
            Assert.Equal(1, ReadingListReviewService.Undo(db, preview));
        }
        using (var db = Open())
        {
            var row = db.ReadingListItems.Include(i => i.Issue).Include(i => i.Source).Single();
            Assert.True(row.Issue!.IsPlaceholder); Assert.Equal(2020, row.Issue.Year);
            Assert.Equal("later note", row.Notes); Assert.Equal(99, row.SortOrder); Assert.Equal(EventMembershipRole.Optional, row.Role);
            Assert.Equal(MatchMethod.None, row.Source!.Method);
            Assert.Equal("Already undone", ReadingListReviewService.PreviewUndo(db, batchId).Rows.Single().Conflict);
        }
    }
    [Fact] public void StaleEntryRejectsWholeBatchAndHistory()
    {
        var a = Seed("A"); var b = Seed("B"); var choices = new[] { Choice(a.List, a.Comic), Choice(b.List, b.Comic) };
        using (var db = Open()) { db.ReadingListItemSources.First(s => s.ReadingListItemId == b.Item).State = MatchState.Unresolved; db.SaveChanges(); }
        using (var db = Open()) Assert.Throws<InvalidOperationException>(() => ReadingListReviewService.Apply(db, choices, "two"));
        using (var db = Open()) { Assert.Empty(db.ReadingListReviewBatches); Assert.Equal(2, db.ReadingListItems.Include(i => i.Issue).Count(i => i.Issue!.IsPlaceholder)); }
    }
    [Fact] public void ChangedCandidateProviderIdentityRejectsApply()
    {
        var a = Seed(); var choice = Choice(a.List, a.Comic);
        using (var db = Open()) { db.ComicMetadataExternalIds.Add(new() { EntityId = a.Comic, EntityKind = ComicMetadataEntityKind.Issue, Provider = ComicProvider.Metron, ExternalId = "42" }); db.SaveChanges(); }
        using (var db = Open()) Assert.Throws<InvalidOperationException>(() => ReadingListReviewService.Apply(db, [choice], "test"));
    }
    [Fact] public void DuplicateInListBlocksApply()
    {
        var a = Seed(); var choice = Choice(a.List, a.Comic);
        using (var db = Open()) { db.ReadingListItems.Add(new() { ReadingListId = a.List, IssueId = a.Comic }); db.SaveChanges(); }
        using var check = Open(); Assert.Throws<InvalidOperationException>(() => ReadingListReviewService.Apply(check, [choice], "test"));
        Assert.Empty(check.ReadingListReviewBatches);
    }
    [Fact] public void UndoReportsConflictsAndRestoresOtherEntries()
    {
        var a = Seed("A"); var b = Seed("B"); string id;
        using (var db = Open()) id = ReadingListReviewService.Apply(db, [Choice(a.List, a.Comic), Choice(b.List, b.Comic)], "two").Id;
        using (var db = Open()) { db.ReadingListItemSources.First(s => s.ReadingListItemId == b.Item).LastCheckedUtc = DateTime.UtcNow.AddDays(1); db.SaveChanges(); }
        using (var db = Open())
        {
            var preview = ReadingListReviewService.PreviewUndo(db, id);
            Assert.Single(preview.Rows.Where(r => r.Conflict is null));
            Assert.Equal(1, ReadingListReviewService.Undo(db, preview));
        }
        using (var db = Open()) Assert.Equal(b.Comic, db.ReadingListItems.First(i => i.Id == b.Item).IssueId);
    }
    [Fact] public void SharedPlaceholderIsRestoredOnlyOnce()
    {
        var a = Seed("A"); var b = Seed("B", true); string id;
        using (var db = Open()) id = ReadingListReviewService.Apply(db, [Choice(a.List, a.Comic), Choice(b.List, a.Comic)], "shared").Id;
        using (var db = Open()) Assert.Equal(2, ReadingListReviewService.Undo(db, ReadingListReviewService.PreviewUndo(db, id)));
        using (var db = Open()) { Assert.Single(db.Issues.Where(i => i.IsPlaceholder)); Assert.Single(db.ReadingListItems.Select(i => i.IssueId).Distinct()); }
    }
    [Fact] public void ExpiredHistoryIsNotAvailable()
    {
        var a = Seed(); string id;
        using (var db = Open()) { id = ReadingListReviewService.Apply(db, [Choice(a.List, a.Comic)], "old").Id; db.ReadingListReviewBatches.Single().CreatedUtc = DateTime.UtcNow.AddDays(-31); db.SaveChanges(); }
        using var check = Open(); Assert.Empty(ReadingListReviewService.History(check)); Assert.Throws<InvalidOperationException>(() => ReadingListReviewService.PreviewUndo(check, id));
    }
    [Fact] public void EquivalenceIsStrictAndProviderConflictsNeverPropagate()
    {
        var a = new ComicReference("Title", "001", "01", 2025, "Annual");
        Assert.True(ReadingListReviewService.Equivalent(a, new("Title!", "#1", "1", 2025, " annual ")));
        Assert.False(ReadingListReviewService.Equivalent(a, a with { Year = null }));
        Assert.False(ReadingListReviewService.Equivalent(a, a with { Year = 2024 }));
        Assert.False(ReadingListReviewService.Equivalent(a, a with { Format = "TPB" }));
        Assert.False(ReadingListReviewService.Equivalent(a with { MetronIssueId = "7", ComicVineIssueId = "1" }, a with { MetronIssueId = "7", ComicVineIssueId = "2" }));
        Assert.True(ReadingListReviewService.Equivalent(a with { MetronIssueId = "7" }, a with { MetronIssueId = "7", Year = null }));
    }
    [Fact] public void CrossListChoicesExcludePreviousManualDecisions()
    {
        var a = Seed("A"); var b = Seed("B");
        using (var db = Open()) { db.ReadingListItemSources.First(s => s.ReadingListItemId == b.Item).Method = MatchMethod.User; db.SaveChanges(); }
        using var check = Open(); var entry = ReadingListReviewService.Load(check, [a.List]).Single();
        Assert.Empty(ReadingListReviewService.EquivalentEntries(check, entry));
    }
    [Fact] public void PreparedCsvHasNoWritesAndOmissionCreatesNoPlaceholders()
    {
        string file = Path.Combine(_root, "list.csv"); File.WriteAllText(file, "Series,Number\nUnknown,1\nBad,\n");
        var prepared = CsvReadingListIO.Prepare(file);
        using var db = Open();
        var manifest = ReadingListImportReview.Resolve(db, new([prepared], []));
        Assert.Empty(db.ReadingLists); Assert.Empty(db.Series); Assert.Empty(db.Issues);
        Assert.Single(prepared.Skipped);
        ReadingListImportReview.Commit(db, manifest, [], false, null);
        Assert.Single(db.ReadingLists); Assert.Empty(db.Issues); Assert.Empty(db.Series);
    }
    [Fact] public void ImportDetectsChangedAutomaticCandidateBeforeCreatingAnything()
    {
        var a = Seed();
        using var db = Open();
        var manifest = ReadingListImportReview.Resolve(db, new([new("Import", [new("Title", "1", Year: 2025)])], []));
        using (var other = Open()) { other.Issues.Find(a.Comic)!.Year = 2024; other.SaveChanges(); }
        Assert.Throws<InvalidOperationException>(() => ReadingListImportReview.Commit(db, manifest, [], true, null));
        Assert.Single(db.ReadingLists);
    }
    [Fact] public void FolderManifestReportsMalformedFilesAndPreservesHierarchyWithoutWrites()
    {
        string folder = Path.Combine(_root, "lists"); Directory.CreateDirectory(Path.Combine(folder, "child"));
        File.WriteAllText(Path.Combine(folder, "bad.csv"), "Invalid header\n");
        File.WriteAllText(Path.Combine(folder, "child", "good.csv"), "Series,Number\nUnknown,1");
        var input = ReadingListImportReview.PrepareFolder(folder);
        Assert.Single(input.Failures); Assert.Equal(new[] { "lists", "child" }, input.Lists.Single().Folders);
        using var db = Open(); var manifest = ReadingListImportReview.Resolve(db, input);
        Assert.Empty(db.ReadingListFolders);
        ReadingListImportReview.Commit(db, manifest, [], true, null);
        Assert.Equal(2, db.ReadingListFolders.Count()); Assert.Single(db.ReadingLists); Assert.Single(db.Issues);
    }
    [Fact] public void ReviewHistoryMigrationCanBeAppliedAndRolledBackWithoutChangingLibrary()
    {
        using var db = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "migration.db")}").Options);
        var migrations = db.Database.GetMigrations().ToList();
        string target = migrations.Single(m => m.EndsWith("_AddReadingListReviewHistory"));
        string prior = migrations[migrations.IndexOf(target) - 1];
        var migrator = db.GetService<IMigrator>();
        migrator.Migrate(prior);
        db.ReadingLists.Add(new ReadingList { Name = "Survives migration" }); db.SaveChanges();
        migrator.Migrate(target);
        db.ReadingListReviewBatches.Add(new() { Label = "History" }); db.SaveChanges();
        Assert.Single(db.ReadingListReviewBatches);
        migrator.Migrate(prior); db.ChangeTracker.Clear();
        Assert.Equal("Survives migration", db.ReadingLists.Single().Name);
        migrator.Migrate(target); Assert.Empty(db.ReadingListReviewBatches);
    }
    [Fact] public void AmbiguousImportChoiceIsManualAndDuplicateChoicesRollbackAllListsAndFolders()
    {
        var a = Seed();
        var input = new PreparedImport([new PreparedReadingList("Import", [new("Title", "1", Year: 2020), new("Other", "2")]) { Folders = ["New folder"] }], []);
        using var db = Open(); var manifest = ReadingListImportReview.Resolve(db, input);
        var candidate = LibraryIndex.Load(db).Find(a.Comic)!;
        string identity = ReadingListReviewService.CandidateIdentity(db, candidate);
        Assert.Throws<InvalidOperationException>(() => ReadingListImportReview.Commit(db, manifest,
            [new(0, 0, candidate, identity), new(0, 1, candidate, identity)], true, null));
        Assert.Single(db.ReadingLists); Assert.Empty(db.ReadingListFolders);
        var ids = ReadingListImportReview.Commit(db, manifest, [new(0, 0, candidate, identity)], true, null);
        var row = db.ReadingListItems.Include(i => i.Source).First(i => i.ReadingListId == ids.Single() && i.IssueId == a.Comic);
        Assert.Equal(MatchMethod.User, row.Source!.Method); Assert.Equal(2020, row.Source.Year);
    }
    [Fact] public void PartialUndoAcrossTwoVisitsReusesTheRecreatedSharedPlaceholder()
    {
        var a = Seed("A"); var b = Seed("B", true); string id;
        using (var db = Open()) id = ReadingListReviewService.Apply(db, [Choice(a.List, a.Comic), Choice(b.List, a.Comic)], "shared").Id;
        using (var db = Open())
        {
            // Force a different generated identity for the restored placeholder.
            db.Issues.Add(new Issue { Series = new Series { Name = "Unrelated" }, Number = "9" }); db.SaveChanges();
            var preview = ReadingListReviewService.PreviewUndo(db, id);
            var onlyA = new ReviewUndoPreview(id, preview.Rows.Select(r => r.Change.Before.ItemId == a.Item ? r : r with { Conflict = "Not selected" }).ToList());
            Assert.Equal(1, ReadingListReviewService.Undo(db, onlyA));
        }
        using (var db = Open()) Assert.Equal(1, ReadingListReviewService.Undo(db, ReadingListReviewService.PreviewUndo(db, id)));
        using (var db = Open()) { Assert.Single(db.Issues.Where(i => i.IsPlaceholder)); Assert.Single(db.ReadingListItems.Select(i => i.IssueId).Distinct()); }
    }
    [Fact] public void FailedCrossListChoiceRollsBackAnImportInTheSameTransaction()
    {
        var a = Seed(); var choice = Choice(a.List, a.Comic);
        using (var db = Open()) { db.ReadingListItemSources.Single().State = MatchState.Unresolved; db.SaveChanges(); }
        using (var db = Open())
        {
            var manifest = ReadingListImportReview.Resolve(db, new([new("New import", [new("Unknown", "1")])], []));
            using var transaction = db.Database.BeginTransaction();
            ReadingListImportReview.Commit(db, manifest, [], true, null);
            Assert.Throws<InvalidOperationException>(() => ReadingListReviewService.Apply(db, [choice], "stale"));
            transaction.Rollback();
        }
        using (var db = Open()) { Assert.Single(db.ReadingLists); Assert.Empty(db.ReadingListReviewBatches); Assert.False(db.Series.Any(s => s.Name == "Unknown")); }
    }
    [Fact] public void LegacyEntryGetsDurableManualStateAndUndoRestoresAbsentSource()
    {
        var a = Seed();
        using (var db = Open()) { db.ReadingListItemSources.RemoveRange(db.ReadingListItemSources); db.SaveChanges(); }
        string id;
        using (var db = Open()) id = ReadingListReviewService.Apply(db, [Choice(a.List, a.Comic)], "legacy").Id;
        using (var db = Open())
        {
            Assert.Equal(MatchMethod.User, db.ReadingListItemSources.Single().Method);
            Assert.Equal(1, ReadingListReviewService.Undo(db, ReadingListReviewService.PreviewUndo(db, id)));
        }
        using (var db = Open()) Assert.Empty(db.ReadingListItemSources);
    }
    public void Dispose()
    { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); }
}
