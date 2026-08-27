using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tracking;
using Xunit;

namespace Paperbunkr.Data.Tests;

public class TrackerProgressCalculatorTests
{
    [Fact]
    public void ComputeChapterProgress_NoReadOrInProgressIssues_ReturnsNull()
    {
        var issues = new[] { new Issue { Number = "5" } };

        Assert.Null(TrackerProgressCalculator.ComputeChapterProgress(issues));
    }

    [Fact]
    public void ComputeChapterProgress_ReturnsHighestReadOrInProgressNumber()
    {
        var issues = new[]
        {
            new Issue { Number = "1", LastPageRead = 20, PageCount = 20 },
            new Issue { Number = "3", LastPageRead = 20, PageCount = 20 },
            new Issue { Number = "5" }, // not read, not in progress - excluded
        };

        Assert.Equal(3, TrackerProgressCalculator.ComputeChapterProgress(issues));
    }

    [Fact]
    public void ComputeChapterProgress_InProgressIssueCounts_EvenIfNotFullyRead()
    {
        var issues = new[]
        {
            new Issue { Number = "1", LastPageRead = 20, PageCount = 20 },
            new Issue { Number = "2", LastPageRead = 5, PageCount = 20 }, // in progress, not "read"
        };

        Assert.Equal(2, TrackerProgressCalculator.ComputeChapterProgress(issues));
    }
}
