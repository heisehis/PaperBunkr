using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md §2 - direct coverage of
/// the controller extracted from DetailTabsViewModel's original ToggleIssueSelection, independent
/// of any View or owning ViewModel.
/// </summary>
public partial class TileSelectionControllerTests
{
    private sealed partial class FakeCard : ObservableObject, ISelectableCard
    {
        public int Id { get; init; }

        [ObservableProperty]
        private bool _isSelected;
    }

    private static List<FakeCard> Cards(int count)
    {
        var list = new List<FakeCard>();
        for (int i = 1; i <= count; i++)
        {
            list.Add(new FakeCard { Id = i });
        }

        return list;
    }

    [Fact]
    public void Toggle_PlainClick_AddsToSelectionWithoutClearingOthers()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(3);

        controller.Toggle(cards, cards[0], isShiftHeld: false);
        controller.Toggle(cards, cards[1], isShiftHeld: false);

        Assert.True(cards[0].IsSelected);
        Assert.True(cards[1].IsSelected);
        Assert.False(cards[2].IsSelected);
        Assert.Equal(2, controller.Count);
    }

    [Fact]
    public void Toggle_SameItemTwice_TogglesOffAgain()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(2);

        controller.Toggle(cards, cards[0], isShiftHeld: false);
        controller.Toggle(cards, cards[0], isShiftHeld: false);

        Assert.False(cards[0].IsSelected);
        Assert.Equal(0, controller.Count);
    }

    [Fact]
    public void Toggle_ShiftClick_SelectsContiguousRangeWithoutClearing()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(5);

        controller.Toggle(cards, cards[0], isShiftHeld: false);
        controller.Toggle(cards, cards[3], isShiftHeld: true);

        Assert.True(cards[0].IsSelected);
        Assert.True(cards[1].IsSelected);
        Assert.True(cards[2].IsSelected);
        Assert.True(cards[3].IsSelected);
        Assert.False(cards[4].IsSelected);
        Assert.Equal(4, controller.Count);
    }

    [Fact]
    public void Toggle_ShiftClickBackwards_SelectsRangeRegardlessOfDirection()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(5);

        controller.Toggle(cards, cards[3], isShiftHeld: false);
        controller.Toggle(cards, cards[1], isShiftHeld: true);

        Assert.True(cards[1].IsSelected);
        Assert.True(cards[2].IsSelected);
        Assert.True(cards[3].IsSelected);
        Assert.Equal(3, controller.Count);
    }

    [Fact]
    public void Toggle_ShiftClickWithNoPriorAnchor_FallsBackToPlainToggle()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(3);

        controller.Toggle(cards, cards[1], isShiftHeld: true);

        Assert.True(cards[1].IsSelected);
        Assert.Equal(1, controller.Count);
    }

    [Fact]
    public void Toggle_ReanchorsLastToggledIndexAfterEveryGesture()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(5);

        controller.Toggle(cards, cards[0], isShiftHeld: false);
        controller.Toggle(cards, cards[4], isShiftHeld: true); // range 0..4
        controller.Clear(cards);
        controller.Toggle(cards, cards[2], isShiftHeld: false); // new anchor at 2
        controller.Toggle(cards, cards[3], isShiftHeld: true); // range 2..3, not 0..3

        Assert.False(cards[0].IsSelected);
        Assert.False(cards[1].IsSelected);
        Assert.True(cards[2].IsSelected);
        Assert.True(cards[3].IsSelected);
        Assert.False(cards[4].IsSelected);
    }

    [Fact]
    public void Clear_WithVisibleItems_ResetsTheirIsSelectedFlags()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(3);
        controller.Toggle(cards, cards[0], isShiftHeld: false);
        controller.Toggle(cards, cards[1], isShiftHeld: false);

        controller.Clear(cards);

        Assert.False(cards[0].IsSelected);
        Assert.False(cards[1].IsSelected);
        Assert.Equal(0, controller.Count);
    }

    [Fact]
    public void Clear_WithoutVisibleItems_StillClearsTheIdSet()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(2);
        controller.Toggle(cards, cards[0], isShiftHeld: false);

        controller.Clear();

        Assert.Equal(0, controller.Count);
        Assert.False(controller.IsSelected(cards[0].Id));
    }

    [Fact]
    public void UnionForAction_NothingSelected_ReturnsJustTheClickedId()
    {
        var controller = new TileSelectionController<FakeCard>();

        var ids = controller.UnionForAction(rightClickedId: 42);

        Assert.Equal(new[] { 42 }, ids);
    }

    [Fact]
    public void UnionForAction_WithExistingSelection_IncludesTheClickedIdWithoutMutatingSelection()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(5);
        controller.Toggle(cards, cards[0], isShiftHeld: false);
        controller.Toggle(cards, cards[1], isShiftHeld: false);

        var ids = controller.UnionForAction(rightClickedId: cards[4].Id);

        Assert.Equal(3, ids.Count);
        Assert.Contains(cards[0].Id, ids);
        Assert.Contains(cards[1].Id, ids);
        Assert.Contains(cards[4].Id, ids);
        // The persisted selection itself is untouched - cards[4] doesn't become visibly selected.
        Assert.False(cards[4].IsSelected);
        Assert.Equal(2, controller.Count);
    }

    [Fact]
    public void UnionForAction_ClickedItemAlreadySelected_DoesNotDuplicate()
    {
        var controller = new TileSelectionController<FakeCard>();
        var cards = Cards(3);
        controller.Toggle(cards, cards[0], isShiftHeld: false);

        var ids = controller.UnionForAction(rightClickedId: cards[0].Id);

        Assert.Single(ids);
    }
}
