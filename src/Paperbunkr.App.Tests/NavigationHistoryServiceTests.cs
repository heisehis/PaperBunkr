using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="NavigationHistoryService"/> directly (docs/superpowers/specs/2026-08-30-
/// app-shell-navigation-history-design.md) - pure C# list+cursor logic, no Avalonia app context
/// needed.
/// </summary>
public class NavigationHistoryServiceTests
{
    private static NavigationEntry Entry(int id, string screenKey = "detail") =>
        new(screenKey, NavigationEntryKind.Series, id, $"Series {id}");

    [Fact]
    public void NewService_StartsWithNoHistory_RootHome()
    {
        var history = new NavigationHistoryService();

        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.Equal("home", history.RootScreenKey);
        Assert.Empty(history.BreadcrumbTrail);
    }

    [Fact]
    public void ResetRoot_SetsRootAndClearsExistingChain()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));

        history.ResetRoot("library");

        Assert.Equal("library", history.RootScreenKey);
        Assert.False(history.CanGoBack);
        Assert.Empty(history.BreadcrumbTrail);
    }

    [Fact]
    public void Push_SingleEntry_CanGoBackNotForward()
    {
        var history = new NavigationHistoryService();

        history.Push(Entry(1));

        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.Equal(new[] { Entry(1) }, history.BreadcrumbTrail);
    }

    [Fact]
    public void Push_MultipleEntries_TrailReflectsAllOfThem()
    {
        var history = new NavigationHistoryService();

        history.Push(Entry(1));
        history.Push(Entry(2));
        history.Push(Entry(3));

        Assert.Equal(new[] { Entry(1), Entry(2), Entry(3) }, history.BreadcrumbTrail);
    }

    [Fact]
    public void Back_AtFirstEntry_ReturnsNull_MeaningGoToRoot()
    {
        var history = new NavigationHistoryService();
        history.ResetRoot("library");
        history.Push(Entry(1));

        var result = history.Back();

        Assert.Null(result);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);
    }

    [Fact]
    public void Back_WithMultipleEntries_ReturnsPreviousEntry()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));
        history.Push(Entry(2));

        var result = history.Back();

        Assert.Equal(Entry(1), result);
        Assert.True(history.CanGoBack);
        Assert.True(history.CanGoForward);
    }

    [Fact]
    public void Back_WhenCanGoBackIsFalse_NoOps()
    {
        var history = new NavigationHistoryService();

        var result = history.Back();

        Assert.Null(result);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Forward_AfterBack_ReturnsToTheSameEntry()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));
        history.Push(Entry(2));
        history.Back();

        var result = history.Forward();

        Assert.Equal(Entry(2), result);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void Forward_WhenCanGoForwardIsFalse_NoOps()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));

        var result = history.Forward();

        Assert.Null(result);
    }

    [Fact]
    public void Push_AfterBack_TruncatesAbandonedForwardBranch()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));
        history.Push(Entry(2));
        history.Back(); // cursor now at entry 1, entry 2 is the abandoned forward branch

        history.Push(Entry(3));

        Assert.False(history.CanGoForward);
        Assert.Equal(new[] { Entry(1), Entry(3) }, history.BreadcrumbTrail);
    }

    [Fact]
    public void JumpTo_MidTrailIndex_TruncatesPastIt()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));
        history.Push(Entry(2));
        history.Push(Entry(3));

        var result = history.JumpTo(0);

        Assert.Equal(Entry(1), result);
        Assert.Equal(new[] { Entry(1) }, history.BreadcrumbTrail);
        Assert.True(history.CanGoForward);
    }

    [Fact]
    public void JumpTo_NegativeOne_ReturnsNull_MeaningRoot()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));
        history.Push(Entry(2));

        var result = history.JumpTo(-1);

        Assert.Null(result);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);
        Assert.Empty(history.BreadcrumbTrail);
    }

    [Fact]
    public void BreadcrumbTrail_ExcludesEntriesPastTheCursor()
    {
        var history = new NavigationHistoryService();
        history.Push(Entry(1));
        history.Push(Entry(2));
        history.Push(Entry(3));
        history.Back();

        Assert.Equal(new[] { Entry(1), Entry(2) }, history.BreadcrumbTrail);
    }
}
