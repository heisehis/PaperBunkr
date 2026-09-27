using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.Data.Tests.Organizing;

public sealed class OrganizeReportingTests
{
    private static Issue MakeIssue(int id) => new() { Id = id, SeriesId = 1, Series = new Series { Id = 1, Name = "Batman" }, Number = id.ToString(), FilePath = $"C:/in/{id}.cbz" };

    private static OrganizePlan MixedPlan() => new(new[]
    {
        new PlannedMove(MakeIssue(1), "C:/in/1.cbz", "C:/lib/Batman/Batman #1.cbz", false),
        new PlannedMove(MakeIssue(2), "C:/in/2.cbz", "C:/lib/Batman/Batman #2.cbz", true),
        new PlannedMove(MakeIssue(3), "C:/lib/Batman/Batman #3.cbz", "C:/lib/Batman/Batman #3.cbz", false, IsAlreadyInPlace: true),
        new PlannedMove(MakeIssue(4), "C:/in/4.cbz", "C:/in/4.cbz", false, Problem: "bad token"),
        new PlannedMove(MakeIssue(5), "C:/in/5.cbz", "C:/in/5.cbz", false, SkipReason: "required field(s) empty: publisher"),
    });

    [Fact]
    public void ThePlanSummary_CountsEachKind_AndOnlySamplesTheBooksThatWouldReallyMove()
    {
        var summary = OrganizePlanSummary.From(MixedPlan());

        Assert.Equal(5, summary.Total);
        Assert.Equal(2, summary.Moving);
        Assert.Equal(1, summary.Collisions);
        Assert.Equal(1, summary.AlreadyInPlace);
        Assert.Equal(1, summary.Problems);
        Assert.Equal(1, summary.Skipped);
        Assert.True(summary.HasWork);
        Assert.Equal(new[] { "Batman #1", "Batman #2" }, summary.Sample.Select(r => r.Label));
        Assert.Equal("Batman #4: bad token", Assert.Single(summary.ProblemMessages));
    }

    [Fact]
    public void ThePlanSummary_SamplesOnlyTheFirstFewRows()
    {
        var plan = new OrganizePlan(Enumerable.Range(1, 30).Select(i => new PlannedMove(MakeIssue(i), $"C:/in/{i}.cbz", $"C:/lib/{i}.cbz", false)).ToList());

        var summary = OrganizePlanSummary.From(plan, sampleSize: 5);

        Assert.Equal(30, summary.Moving);
        Assert.Equal(5, summary.Sample.Count);
    }

    [Fact]
    public void ThereIsNothingToConfirm_WhenNothingWouldMove()
    {
        var plan = new OrganizePlan(new[] { new PlannedMove(MakeIssue(1), "C:/a", "C:/a", false, IsAlreadyInPlace: true) });

        Assert.False(OrganizePlanSummary.From(plan).HasWork);
    }

    [Fact]
    public void TheSimulationReport_ListsWhatWouldHappen_WithReasons()
    {
        var text = OrganizeReport.FromPlan(new OrganizerProfile { Name = "P", BaseFolder = "C:/lib", Mode = OrganizerMode.Simulate }, MixedPlan());

        Assert.Contains("nothing was moved", text);
        Assert.Contains("Would move (2)", text);
        Assert.Contains("(a file is already there)", text);
        Assert.Contains("Would fail (1)", text);
        Assert.Contains("bad token", text);
        Assert.Contains("required field(s) empty: publisher", text);
    }

    [Fact]
    public void TheRunReport_NamesEveryFailedAndSkippedBook()
    {
        var result = new OrganizeResult();
        var move = new PlannedMove(MakeIssue(1), "C:/in/1.cbz", "C:/lib/1.cbz", false);
        result.Succeeded.Add(move);
        result.Failed.Add((new PlannedMove(MakeIssue(2), "C:/in/2.cbz", "C:/lib/2.cbz", false), "access denied"));
        result.Skipped.Add(new PlannedMove(MakeIssue(3), "C:/in/3.cbz", "C:/lib/3.cbz", true));
        result.ReplacedIssueIds.Add(9);

        var text = OrganizeReport.FromResult(new OrganizerProfile { Name = "P", Mode = OrganizerMode.Move }, result);

        Assert.Contains("Organized (1)", text);
        Assert.Contains("C:/in/2.cbz: access denied", text);
        Assert.Contains("an existing file was kept", text);
        Assert.Contains("issue 9", text);
    }
}
