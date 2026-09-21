using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.Acquisition;

public class ArcFollowServiceTests : AcquisitionTestBase
{
    private ReadingList ArcList(string name, bool follow, params (string Series, string Number)[] entries)
    {
        var list = new ReadingList { Name = name, Source = "FakeSource", ArcId = "77", FollowArc = follow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        int order = 0;
        foreach (var (series, number) in entries)
        {
            var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(Context, series, number, volume: null, year: 2016, format: null);
            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = order++ });
        }

        Context.ReadingLists.Add(list);
        Context.SaveChanges();
        return list;
    }

    private static FakeReadingListSource Source(params (string Series, string Number)[] issues) =>
        new("FakeSource", issues.Select(i => new ArcIssue(i.Series, i.Number, 2016, null)).ToList(), null);

    private static FakeComicVine SpawnCv()
    {
        var cv = new FakeComicVine();
        cv.SearchResults.Add(Volume(100, "Spawn", 2016));
        cv.IssuesByVolume[100] = new[] { "1", "2", "3" }.Select((n, i) => CvIssue(1000 + i, n)).ToList();
        return cv;
    }

    [Fact]
    public async Task ARun_RefreshesAFollowedArc_ThenRequestsWhatIsMissing()
    {
        ArcList("Arc", follow: true, ("Spawn", "1"));

        var result = await ArcFollowService.RunAsync(Context, SpawnCv(), CancellationToken.None, Source(("Spawn", "1"), ("Spawn", "2")));

        Assert.Equal(1, result.ListsChecked);
        Assert.Equal(1, result.IssuesAdded);            // issue 2 appeared in the arc since the list was built
        Assert.Equal(2, result.Requested);
        Assert.Empty(result.Problems);
        Assert.Equal(2, Context.WantedIssues.Count(w => w.Status == WantedIssueStatus.Wanted));
        Assert.NotNull(Context.ReadingLists.AsNoTracking().Single().LastFollowedAt);
    }

    [Fact]
    public async Task ARun_IsIdempotent_AndNeverTouchesListsThatAreNotFollowed()
    {
        ArcList("Followed", follow: true, ("Spawn", "1"));
        ArcList("Ignored", follow: false, ("Spawn", "3"));
        var source = Source(("Spawn", "1"));

        await ArcFollowService.RunAsync(Context, SpawnCv(), CancellationToken.None, source);
        var again = await ArcFollowService.RunAsync(Context, SpawnCv(), CancellationToken.None, source);

        Assert.Equal(1, again.ListsChecked);
        Assert.Equal(0, again.Requested);
        Assert.Equal(1, Context.WantedIssues.Count());   // only the followed list's issue, and not duplicated
        Assert.Null(Context.ReadingLists.AsNoTracking().Single(l => l.Name == "Ignored").LastFollowedAt);
    }

    [Fact]
    public async Task WithoutAComicVineKey_TheListIsStillRefreshed_ButNothingIsRequested()
    {
        ArcList("Arc", follow: true, ("Spawn", "1"));

        var result = await ArcFollowService.RunAsync(Context, comicVine: null, CancellationToken.None, Source(("Spawn", "1"), ("Spawn", "2")));

        Assert.Equal(1, result.IssuesAdded);
        Assert.Equal(0, result.Requested);
        Assert.Empty(Context.WantedIssues);
    }

    [Fact]
    public async Task ARateLimit_StopsTheRun_InsteadOfFailingEveryRemainingList()
    {
        ArcList("A", follow: true, ("Spawn", "1"));
        ArcList("B", follow: true, ("Spawn", "2"));
        var limited = new FakeComicVine { ThrowOnSearch = new ComicVineException("Rate limit", 107) };

        var result = await ArcFollowService.RunAsync(Context, limited, CancellationToken.None, Source(("Spawn", "1"), ("Spawn", "2")));

        Assert.Single(result.Problems);
        Assert.Equal(0, result.ListsChecked);
    }
}
