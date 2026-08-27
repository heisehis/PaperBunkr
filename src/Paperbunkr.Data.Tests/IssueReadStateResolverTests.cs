using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class IssueReadStateResolverTests
{
    [Fact]
    public void MarkAsRead_MultiPageIssue_ReachesHasBeenRead()
    {
        var issue = new Issue { PageCount = 20 };

        IssueReadStateResolver.MarkAsRead(issue);

        Assert.Equal(19, issue.LastPageRead);
        Assert.True(issue.HasBeenRead());
    }

    /// <summary>Ports CE's own documented `// HACK` (ComicBook.MarkAsRead) - index 0 over a 1-page count would be 0%, not "read".</summary>
    [Fact]
    public void MarkAsRead_SinglePageIssue_UsesOutOfBoundsHackValue_AndStillReadsAsRead()
    {
        var issue = new Issue { PageCount = 1 };

        IssueReadStateResolver.MarkAsRead(issue);

        Assert.Equal(1, issue.LastPageRead);
        Assert.True(issue.HasBeenRead());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void MarkAsRead_UnknownOrZeroPageCount_NoOps(int? pageCount)
    {
        var issue = new Issue { PageCount = pageCount, LastPageRead = 5 };

        IssueReadStateResolver.MarkAsRead(issue);

        Assert.Equal(5, issue.LastPageRead);
    }

    [Fact]
    public void MarkAsUnread_FullyReadIssue_ReachesIsUnread()
    {
        var issue = new Issue { PageCount = 20, LastPageRead = 19 };

        IssueReadStateResolver.MarkAsUnread(issue);

        Assert.Equal(0, issue.LastPageRead);
        Assert.True(issue.IsUnread());
    }

    [Fact]
    public void MarkAsUnread_UnknownPageCount_StillZeroesLastPageRead()
    {
        var issue = new Issue { PageCount = null, LastPageRead = 5 };

        IssueReadStateResolver.MarkAsUnread(issue);

        Assert.Equal(0, issue.LastPageRead);
    }
}
