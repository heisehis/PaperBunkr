using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>Pure scoring + ranking coverage for <see cref="QuickOpenMatcher"/>.</summary>
public class QuickOpenMatcherTests
{
    private static QuickOpenEntry Entry(string primary, QuickOpenKind kind = QuickOpenKind.Series, DateTime? recency = null) =>
        new(kind, 1, primary, null, "x", recency);

    [Theory]
    [InlineData("batman", "Batman", true)]
    [InlineData("btm", "Batman", true)]
    [InlineData("btmn", "Batman", true)]
    [InlineData("aman", "Batman", true)]
    [InlineData("xyz", "Batman", false)]
    [InlineData("batmann", "Batman", false)]
    [InlineData("nmt", "Batman", false)]
    public void Score_SubsequenceHitOrMiss(string query, string target, bool matches)
    {
        Assert.Equal(matches, QuickOpenMatcher.Score(query, target) is not null);
    }

    [Fact]
    public void Score_PrefixBeatsMidWord()
    {
        int? prefix = QuickOpenMatcher.Score("bat", "Batman");
        int? mid = QuickOpenMatcher.Score("bat", "Combat Zone");
        Assert.True(prefix > mid);
    }

    [Fact]
    public void Score_WordBoundaryBeatsMidWord()
    {
        int? boundary = QuickOpenMatcher.Score("yo", "Batman: Year One");
        int? mid = QuickOpenMatcher.Score("yo", "Beyond");
        Assert.True(boundary > mid);
    }

    [Fact]
    public void Score_ShorterTargetWinsOnEqualSubsequence()
    {
        int? shortT = QuickOpenMatcher.Score("bm", "BM");
        int? longT = QuickOpenMatcher.Score("bm", "B..............................m");
        Assert.True(shortT > longT);
    }

    [Fact]
    public void Rank_EmptyQuery_ReturnsRecentIssuesAndBooksThenScreens()
    {
        var now = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);
        var index = new List<QuickOpenEntry>
        {
            Entry("Old issue", QuickOpenKind.Issue, now.AddDays(-40)),
            Entry("New issue", QuickOpenKind.Issue, now.AddDays(-1)),
            Entry("A book", QuickOpenKind.Book, now.AddDays(-2)),
            Entry("Untouched series", QuickOpenKind.Series),
            new(QuickOpenKind.Screen, null, "Library", null, "x", null, "library"),
        };

        var ranked = QuickOpenMatcher.Rank("", index, now);

        Assert.Equal(new[] { "New issue", "A book", "Old issue", "Library" }, ranked.Select(e => e.Primary));
    }

    [Fact]
    public void Rank_RecencyBoostBreaksAScoreTie()
    {
        var now = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);
        var index = new List<QuickOpenEntry>
        {
            Entry("Batman", QuickOpenKind.Book, now.AddDays(-2)),
            Entry("Batman", QuickOpenKind.Book, now.AddDays(-90)),
        };

        var ranked = QuickOpenMatcher.Rank("batman", index, now);

        Assert.Equal(now.AddDays(-2), ranked[0].RecencyUtc);
    }

    [Fact]
    public void Rank_KindPriorityBreaksAScoreAndRecencyTie()
    {
        var index = new List<QuickOpenEntry>
        {
            Entry("Batman", QuickOpenKind.Issue),
            Entry("Batman", QuickOpenKind.Series),
        };

        var ranked = QuickOpenMatcher.Rank("batman", index);

        Assert.Equal(QuickOpenKind.Series, ranked[0].Kind);
    }

    [Fact]
    public void Rank_CapsAtFifty()
    {
        var index = Enumerable.Range(0, 200).Select(n => Entry($"batman {n}")).ToList();
        Assert.Equal(50, QuickOpenMatcher.Rank("batman", index).Count);
    }
}
