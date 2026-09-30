using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.ReadingLists;

/// <summary>
/// Data halves of docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md: continuity-linked lists and their Rebuild
/// (§2), and the check against a provider's canonical order (§3).
/// </summary>
public class ReadingListBuildFromEventsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_events_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public ReadingListBuildFromEventsTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private (int ContinuityId, int[] Issues) SeedContinuity(int count)
    {
        using var context = NewContext();
        var series = new Series { Name = "Crisis" };
        var issues = Enumerable.Range(1, count).Select(n => new Issue { Series = series, Number = n.ToString(), Year = 1985 }).ToList();
        context.Issues.AddRange(issues);
        var continuity = new Continuity { Name = "DC Universe" };
        context.Continuities.Add(continuity);
        context.SaveChanges();
        return (continuity.Id, issues.Select(i => i.Id).ToArray());
    }

    private static List<ContinuityOrderEntry> Order(params (int Issue, string? Label)[] entries) =>
        entries.Select(e => new ContinuityOrderEntry(e.Issue, e.Label)).ToList();

    [Fact]
    public void CreateFromOrder_LinksTheContinuity_KeepsFirstAppearance_AndLabels()
    {
        var (continuityId, ids) = SeedContinuity(3);
        int folderId;
        using (var context = NewContext())
        {
            var folder = ReadingListFolders.Create(context, "Events", null);
            context.SaveChanges();
            folderId = folder.Id;
        }

        int listId;
        using (var context = NewContext())
        {
            var list = ContinuityReadingListBuilder.CreateFromOrder(context, continuityId,
                Order((ids[1], "Crisis"), (ids[0], "Crisis"), (ids[1], "Legends"), (ids[2], null)), ContinuityOrderKind.StoryOrder, folderId);
            listId = list.Id;
        }

        using var check = NewContext();
        var saved = check.ReadingLists.Include(r => r.Items).Single(r => r.Id == listId);
        Assert.Equal("DC Universe (story order)", saved.Name);
        Assert.Equal(ReadingListType.Chronological, saved.Type);
        Assert.Equal(continuityId, saved.ContinuityId);
        Assert.Equal(ContinuityOrderKind.StoryOrder, saved.ContinuityOrderKind);
        Assert.Equal(folderId, saved.FolderId);
        var items = saved.Items.OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }, items.Select(i => i.IssueId));
        Assert.Equal(new[] { "Crisis", "Crisis", null }, items.Select(i => i.GroupLabel));
    }

    [Fact]
    public void PublicationOrder_HasNoGroupLabels()
    {
        var (continuityId, _) = SeedContinuity(2);
        using var context = NewContext();
        context.ContinuityMemberships.Add(new ContinuityMembership { ContinuityId = continuityId, SeriesId = context.Series.Single().Id });
        context.SaveChanges();

        var list = ContinuityReadingListBuilder.CreateFromContinuity(context, continuityId);

        Assert.Equal(ContinuityOrderKind.PublicationOrder, list.ContinuityOrderKind);
        Assert.All(list.Items, i => Assert.Null(i.GroupLabel));
    }

    [Fact]
    public void Rebuild_MovesAddsRemoves_KeepsNotesAndRoles_RewritesLabels_AndAnnouncesOnce()
    {
        var (continuityId, ids) = SeedContinuity(4);
        int placeholderId;
        int listId;
        using (var context = NewContext())
        {
            var placeholder = new Issue { SeriesId = context.Series.Single().Id, Number = "99", IsPlaceholder = true, FileIsMissing = true };
            context.Issues.Add(placeholder);
            context.SaveChanges();
            placeholderId = placeholder.Id;
            var list = ContinuityReadingListBuilder.CreateFromOrder(context, continuityId,
                Order((ids[0], "A"), (ids[1], "A"), (placeholderId, "B")), ContinuityOrderKind.StoryOrder);
            listId = list.Id;
            var second = context.ReadingListItems.Single(i => i.ReadingListId == listId && i.IssueId == ids[1]);
            second.Notes = "mine";
            second.Role = EventMembershipRole.TieIn;
            context.SaveChanges();
        }

        var events = new LibraryEvents();
        var heard = new List<ReadingListChangedEvent>();
        events.ReadingListChanged += heard.Add;
        using (var context = NewContext())
        {
            var result = ContinuityReadingListBuilder.RebuildFromOrder(context, listId, Order((ids[1], "Legends"), (ids[0], "Crisis"), (ids[3], "Crisis")), events);
            Assert.Equal((1, 1), (result.Added, result.Removed));
            Assert.True(result.Moved > 0);
        }

        using var check = NewContext();
        var items = check.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(new[] { ids[1], ids[0], ids[3] }, items.Select(i => i.IssueId));
        Assert.Equal(new[] { "Legends", "Crisis", "Crisis" }, items.Select(i => i.GroupLabel));
        Assert.Equal("mine", items[0].Notes);
        Assert.Equal(EventMembershipRole.TieIn, items[0].Role);
        Assert.Null(check.Issues.Find(placeholderId));        // orphaned placeholder deleted
        var announced = Assert.Single(heard);
        Assert.Equal(new[] { ids[3] }, announced.AddedIssueIds);
        Assert.Equal(new[] { placeholderId }, announced.RemovedIssueIds);
    }

    [Fact]
    public void Rebuild_RefusesAnEmptyOrder()
    {
        var (continuityId, ids) = SeedContinuity(1);
        using var context = NewContext();
        var list = ContinuityReadingListBuilder.CreateFromOrder(context, continuityId, Order((ids[0], null)), ContinuityOrderKind.StoryOrder);
        Assert.Throws<InvalidOperationException>(() => ContinuityReadingListBuilder.RebuildFromOrder(context, list.Id, Array.Empty<ContinuityOrderEntry>()));
    }

    // --- Canonical diff ---

    private static ArcIssue Arc(string number) => new("Crisis", number, 1985, null);

    /// <summary>Library owns Crisis #1-#4; the list holds #3, #1, #2 (and a foreign #50 in between).</summary>
    private (int ListId, Dictionary<string, int> Ids) SeedDiffList()
    {
        using var context = NewContext();
        var series = new Series { Name = "Crisis" };
        var issues = new[] { "1", "2", "3", "4", "50" }.Select(n => new Issue { Series = series, Number = n, Year = 1985 }).ToList();
        context.Issues.AddRange(issues);
        context.SaveChanges();
        var ids = issues.ToDictionary(i => i.Number!, i => i.Id);
        var list = new ReadingList { Name = "Crisis" };
        int order = 0;
        foreach (var n in new[] { "3", "50", "1", "2" })
        {
            list.Items.Add(new ReadingListItem { IssueId = ids[n], SortOrder = order++ });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return (list.Id, ids);
    }

    [Fact]
    public void Compute_FindsMissing_AndCountsOutOfOrder()
    {
        var (listId, ids) = SeedDiffList();
        using var context = NewContext();
        // Provider: #1, #2, #3, #4, #5 (#5 not in the library).
        var diff = ReadingListCanonicalDiff.Compute(context, listId, "ComicVine", new[] { Arc("1"), Arc("2"), Arc("3"), Arc("4"), Arc("5") });

        Assert.Equal(new[] { "4", "5" }, diff.Missing.Select(m => m.Arc.Number));
        Assert.Equal(ids["4"], diff.Missing[0].LocalIssueId);
        Assert.Null(diff.Missing[1].LocalIssueId);
        Assert.Equal(1, diff.OutOfOrderCount);              // positions in list order: 2, 0, 1 -> longest run 2
        Assert.Equal("ComicVine", diff.SourceName);
        Assert.Equal(0, context.Issues.Count(i => i.IsPlaceholder));   // compute never creates placeholders
    }

    [Fact]
    public void InsertMissing_PlacesAtProviderPositions_AndCreatesPlaceholders()
    {
        var (listId, ids) = SeedDiffList();
        using (var context = NewContext())
        {
            // Provider: #0 (not owned), #1, #2, #3, #4, #5 (not owned).
            var diff = ReadingListCanonicalDiff.Compute(context, listId, "Metron", new[] { Arc("0"), Arc("1"), Arc("2"), Arc("3"), Arc("4"), Arc("5") });
            Assert.Equal(3, ReadingListCanonicalDiff.InsertMissing(context, diff));
            context.SaveChanges();
        }

        using var check = NewContext();
        var numbers = check.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder)
            .Select(i => i.Issue!.Number).ToList();
        // #0 has no earlier provider entry -> top; #4 follows #3 (the item holding position 3); #5 follows the just-inserted #4.
        Assert.Equal(new[] { "0", "3", "4", "5", "50", "1", "2" }, numbers);
        Assert.Equal(2, check.Issues.Count(i => i.IsPlaceholder));
    }

    [Fact]
    public void ReorderToMatch_SortsKnownItemsWithinTheirSlots_LeavingForeignItemsInPlace()
    {
        var (listId, _) = SeedDiffList();
        using (var context = NewContext())
        {
            var diff = ReadingListCanonicalDiff.Compute(context, listId, "ComicVine", new[] { Arc("1"), Arc("2"), Arc("3") });
            Assert.Equal(3, ReadingListCanonicalDiff.ReorderToMatch(context, diff));
            context.SaveChanges();
        }

        using var check = NewContext();
        var numbers = check.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder)
            .Select(i => i.Issue!.Number).ToList();
        Assert.Equal(new[] { "1", "50", "2", "3" }, numbers);
    }

    [Fact]
    public void ChooseSource_PrefersComicVine_NeedsAnIdAndCredentials()
    {
        var both = new StoryEvent { Name = "Crisis", ComicVineArcId = "cv1", MetronArcId = "m1" };
        var cv = new FakeReadingListSource("ComicVine", Array.Empty<ArcIssue>(), null);
        var metron = new FakeReadingListSource("Metron", Array.Empty<ArcIssue>(), null);
        IReadingListSource? All(string key) => key == "ComicVine" ? cv : metron;
        IReadingListSource? MetronOnly(string key) => key == "Metron" ? metron : null;

        Assert.Equal(("ComicVine", "cv1"), Pick(ReadingListCanonicalDiff.ChooseSource(both, All)));
        Assert.Equal(("Metron", "m1"), Pick(ReadingListCanonicalDiff.ChooseSource(both, All, preferMetron: true)));
        Assert.Equal(("Metron", "m1"), Pick(ReadingListCanonicalDiff.ChooseSource(both, MetronOnly)));
        Assert.Null(ReadingListCanonicalDiff.ChooseSource(new StoryEvent { Name = "x", MetronArcId = "m1" }, key => key == "ComicVine" ? cv : null));
        Assert.Equal(new[] { "ComicVine", "Metron" }, ReadingListCanonicalDiff.AvailableSourceKeys(both, All));

        static (string, string)? Pick((IReadingListSource Source, string ArcId)? c) => c is { } v ? (v.Source.SourceKey, v.ArcId) : null;
    }

    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 0, 1, 2 }, 3)]
    [InlineData(new[] { 2, 0, 1 }, 2)]
    [InlineData(new[] { 3, 1, 2, 0, 4 }, 3)]
    public void LongestIncreasingRun(int[] values, int expected) =>
        Assert.Equal(expected, ReadingListCanonicalDiff.LongestIncreasingRun(values));
}
