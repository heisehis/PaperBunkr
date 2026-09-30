using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;

namespace Paperbunkr.App.ViewModels;

/// <summary>One row of "Show in List ▸": a reading list or a collection that contains the book.</summary>
public sealed record LibraryListMembership(string Name, bool IsCollection, int Id);

/// <summary>
/// "Show in List ▸" (CE's <c>miShowInList_DropDownOpening</c>, <c>ComicBrowserControl.cs:3087</c>): every reading list and collection that
/// contains the clicked book, each a link there (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §2).
/// </summary>
public partial class LibraryScreenViewModel
{
    private Action<int> _goReadingList = _ => { };

    /// <summary>Opens a reading list - set by the shell (<c>MainViewModel.GoReadingWithList</c>).</summary>
    internal Action<int> GoReadingList
    {
        get => _goReadingList;
        set => _goReadingList = value;
    }

    /// <summary>Reading lists (in sidebar order) then collections (in collection order) containing <paramref name="issueId"/>. A collection
    /// counts when it holds the book, its series, or matches either through its smart rules.</summary>
    internal IReadOnlyList<LibraryListMembership> ListsContaining(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId);
        if (issue is null)
        {
            return Array.Empty<LibraryListMembership>();
        }

        var result = context.ReadingListItems
            .Where(i => i.IssueId == issueId)
            .Select(i => i.ReadingList!)
            .Distinct()
            .OrderBy(l => l.SortOrder)
            .Select(l => new { l.Id, l.Name })
            .ToList()
            .Select(l => new LibraryListMembership(l.Name, false, l.Id))
            .ToList();

        var manual = context.CollectionItems
            .Where(ci => ci.IssueId == issueId || ci.SeriesId == issue.SeriesId)
            .Select(ci => ci.CollectionId)
            .Distinct()
            .ToHashSet();

        foreach (var collection in context.Collections.OrderBy(c => c.SortOrder).ToList())
        {
            bool contains = manual.Contains(collection.Id);
            if (!contains && collection.IsSmart)
            {
                contains = CollectionResolver.GetMembers(context, collection.Id).Any(m =>
                    (m.Kind == CollectionMemberKind.Issue && m.TargetId == issueId) ||
                    (m.Kind == CollectionMemberKind.Series && m.TargetId == issue.SeriesId));
            }

            if (contains)
            {
                result.Add(new LibraryListMembership(collection.Name, true, collection.Id));
            }
        }

        return result;
    }

    [RelayCommand]
    private void ShowInList(LibraryListMembership membership)
    {
        // Deferred: navigating swaps the whole screen while the menu item's click is still routing.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (membership.IsCollection)
            {
                SelectCollectionById(membership.Id);
            }
            else
            {
                _goReadingList(membership.Id);
            }
        });
    }
}
