using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
namespace Paperbunkr.Data.Tests;

public class CreatorRunDetectorTests
{
    private static List<Issue> Issues(int count) => Enumerable.Range(1, count).Select(n => new Issue { Id = n, Number = n.ToString(), PageCount = 20, LastPageRead = n <= 2 ? 19 : 0 }).ToList();
    private static List<CreatorCredit> Credits(IEnumerable<int> ids, string role = "Writer") => ids.Select(id => new CreatorCredit
        { CreatorId = 1, Creator = new Creator { Id = 1, Name = "Claremont" }, IssueId = id, Role = role }).ToList();

    [Fact] public void CleanRun_ReportsNaturalOrderReadCountAndRange()
    {
        var issues = Issues(10).AsEnumerable().Reverse();
        var run = Assert.Single(CreatorRunDetector.Find(issues, Credits(Enumerable.Range(1, 10)), "Writer"));
        Assert.Equal("#1–#10", run.Range); Assert.Equal(2, run.ReadCount); Assert.Equal(10, run.CreditedCount);
    }
    [Theory] [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 2)]
    public void FillIns_AllowTwoButSplitAtThree(int gaps, int expected)
    {
        var issues = Issues(12 + gaps);
        var ids = Enumerable.Range(1, 6).Concat(Enumerable.Range(7 + gaps, 6));
        var runs = CreatorRunDetector.Find(issues, Credits(ids), "Writer");
        Assert.Equal(expected, runs.Count);
        Assert.Equal(12, runs.Sum(r => r.CreditedCount));
        Assert.Equal(expected == 1 ? 12 + gaps : 12, runs.Sum(r => r.Issues.Count));
    }
    [Fact] public void PlaceholdersAreNeutral_AndDoNotCountTowardSix()
    {
        var issues = Issues(10); foreach (var i in issues.Where(i => i.Id is >= 4 and <= 7)) i.IsPlaceholder = true;
        var run = Assert.Single(CreatorRunDetector.Find(issues, Credits(new[] { 1, 2, 3, 8, 9, 10 }), "Writer"));
        Assert.Equal(6, run.Issues.Count);
        Assert.Empty(CreatorRunDetector.Find(issues, Credits(new[] { 1, 2, 3, 8, 9 }), "Writer"));
    }
    [Fact] public void ArtistCombinesPencillerAndArtist_ButKeepsWriterSeparate()
    {
        var credits = Credits(new[] { 1, 2, 3 }, "Penciller").Concat(Credits(new[] { 4, 5, 6 }, "Artist"));
        Assert.Single(CreatorRunDetector.Find(Issues(6), credits, "Artist"));
        Assert.Empty(CreatorRunDetector.Find(Issues(6), credits, "Writer"));
    }
    [Fact] public void OfferRule_HidesSingleCreatorManga_AndAllowsOutsideIssues()
    {
        var issues = Issues(6);
        var credits = Credits(Enumerable.Range(1, 6)).Concat(Credits(Enumerable.Range(1, 6), "Penciller"));
        var runs = CreatorRunDetector.FindAll(issues, credits);
        Assert.Single(runs); Assert.False(CreatorRunDetector.ShouldOffer(runs, issues));
        Assert.True(CreatorRunDetector.ShouldOffer(runs, Issues(7)));
    }
}
