using Avalonia.Media;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>Exercises <see cref="SortStrategies"/>/<see cref="GroupStrategies"/> (docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-group-design.md) in isolation, independent of any specific field.</summary>
public class SortGroupStrategiesTests
{
    private static readonly IBrush Brush = Brushes.Black;

    private static IssueListRow Row(string? writer = null, int? pageCount = null, DateTime? added = null, bool isMissing = false, bool? isFinalIssue = null) => new()
    {
        SeriesName = "S", Title = "T", Writer = writer, PageCount = pageCount, AddedTime = added, IsMissing = isMissing, CoverBrush = Brush, IsFinalIssue = isFinalIssue,
    };

    [Fact]
    public void CaseInsensitiveString_IgnoresCase()
    {
        var compare = SortStrategies.CaseInsensitiveString(r => r.Writer);
        Assert.Equal(0, compare(Row(writer: "alice"), Row(writer: "ALICE")));
        Assert.True(compare(Row(writer: "alice"), Row(writer: "bob")) < 0);
    }

    [Fact]
    public void Numeric_ComparesValues_NullsSortFirst()
    {
        var compare = SortStrategies.Numeric(r => r.PageCount);
        Assert.True(compare(Row(pageCount: 5), Row(pageCount: 10)) < 0);
        Assert.True(compare(Row(pageCount: null), Row(pageCount: 1)) < 0);
        Assert.Equal(0, compare(Row(pageCount: 7), Row(pageCount: 7)));
    }

    [Fact]
    public void Date_ComparesChronologically()
    {
        var compare = SortStrategies.Date(r => r.AddedTime);
        var earlier = new DateTime(2020, 1, 1);
        var later = new DateTime(2021, 1, 1);
        Assert.True(compare(Row(added: earlier), Row(added: later)) < 0);
    }

    [Fact]
    public void Boolean_FalseSortsBeforeTrue()
    {
        var compare = SortStrategies.Boolean(r => r.IsMissing);
        Assert.True(compare(Row(isMissing: false), Row(isMissing: true)) < 0);
    }

    [Fact]
    public void Alphabetical_GroupKey_FallsBackForBlankValues()
    {
        var (key, _) = GroupStrategies.Alphabetical(r => r.Writer, fallback: "Unknown");
        Assert.Equal("Unknown", key(Row(writer: null)));
        Assert.Equal("Unknown", key(Row(writer: "   ")));
        Assert.Equal("Alice", key(Row(writer: "Alice")));
    }

    [Fact]
    public void Alphabetical_GroupOrder_IsCaseInsensitive()
    {
        var (_, order) = GroupStrategies.Alphabetical(r => r.Writer);
        Assert.Equal(0, order("alice", "ALICE"));
        Assert.True(order("alice", "bob") < 0);
    }

    [Fact]
    public void NumericBucket_GroupsByIntValue_UnknownSortsSeparately()
    {
        var (key, order) = GroupStrategies.NumericBucket(r => r.PageCount);
        Assert.Equal("22", key(Row(pageCount: 22)));
        Assert.Equal("Unknown", key(Row(pageCount: null)));
        Assert.True(order("5", "22") < 0); // numeric, not lexical (would be "22" < "5" lexically)
    }

    [Fact]
    public void Boolean_GroupKey_UsesProvidedLabels()
    {
        var (key, _) = GroupStrategies.Boolean(r => r.IsMissing, "Missing", "Available");
        Assert.Equal("Missing", key(Row(isMissing: true)));
        Assert.Equal("Available", key(Row(isMissing: false)));
    }

    [Fact]
    public void TriState_GroupKey_UsesProvidedLabels_ForAllThreeStates()
    {
        var (key, _) = GroupStrategies.TriState(r => r.IsFinalIssue, "Final issue", "Not final", "Unknown");
        Assert.Equal("Final issue", key(Row(isFinalIssue: true)));
        Assert.Equal("Not final", key(Row(isFinalIssue: false)));
        Assert.Equal("Unknown", key(Row(isFinalIssue: null)));
    }

    [Fact]
    public void TriState_GroupOrder_IsUnknownThenNoThenYes_NotAlphabetical()
    {
        // Alphabetically "Final issue" < "Not final" < "Unknown" - the real rank order must differ.
        var (_, order) = GroupStrategies.TriState(r => r.IsFinalIssue, "Final issue", "Not final", "Unknown");
        Assert.True(order("Unknown", "Not final") < 0);
        Assert.True(order("Not final", "Final issue") < 0);
        Assert.True(order("Unknown", "Final issue") < 0);
    }
}
