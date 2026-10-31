using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.App.Services;

public enum BookShelfKind
{
    Favorites,
    Unfinished,
    NotRated,
    SmartList,
    Collection,
}

/// <summary>One entry of the Books "Shelves" menu. <see cref="Id"/> is the smart list / collection id.</summary>
public sealed record BookShelf(string Key, string Name, BookShelfKind Kind, int? Id = null)
{
    public string Section => Kind switch
    {
        BookShelfKind.SmartList => "Smart shelves",
        BookShelfKind.Collection => "Collections",
        _ => "Built-in",
    };

    public bool IsBuiltIn => Kind is BookShelfKind.Favorites or BookShelfKind.Unfinished or BookShelfKind.NotRated;
}

/// <summary>
/// The Books "Shelves" menu's contents and what each shelf contains (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §4):
/// built-ins, the user's novel smart lists, and Collections that hold books. A shelf resolves to the set of book ids it allows.
/// </summary>
public sealed class BookShelfService
{
    /// <summary>"Favorites" is a rating of 4 or more (CE defines its Favorites list as a rating above 3).</summary>
    public const float FavoriteMinRating = 4f;

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public BookShelfService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    internal BookShelfService(Func<PaperbunkrDbContext> contextFactory) => _contextFactory = contextFactory;

    public IReadOnlyList<BookShelf> List()
    {
        using var context = _contextFactory();

        var shelves = new List<BookShelf>
        {
            new("favorites", "Favorites", BookShelfKind.Favorites),
            new("unfinished", "Unfinished", BookShelfKind.Unfinished),
            new("not-rated", "Not rated", BookShelfKind.NotRated),
        };

        foreach (var list in context.SmartLists.AsNoTracking().Where(l => l.TargetKind == SmartListTargetKind.Novel).OrderBy(l => l.SortOrder).ThenBy(l => l.Name).ToList())
        {
            shelves.Add(new BookShelf($"smart-{list.Id}", list.Name, BookShelfKind.SmartList, list.Id));
        }

        // Only collections that actually hold a book (manually, or through a rule) - an all-comics collection would be an empty shelf.
        foreach (var collection in context.Collections.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList())
        {
            if (CollectionResolver.GetMembers(context, collection.Id).Any(m => m.Kind == CollectionMemberKind.Book))
            {
                shelves.Add(new BookShelf($"collection-{collection.Id}", collection.Name, BookShelfKind.Collection, collection.Id));
            }
        }

        return shelves;
    }

    /// <summary>The ids of the books on <paramref name="shelf"/> right now.</summary>
    public IReadOnlySet<int> Resolve(BookShelf shelf)
    {
        using var context = _contextFactory();

        switch (shelf.Kind)
        {
            case BookShelfKind.Favorites:
                return context.Books.Where(b => b.Rating != null && b.Rating >= FavoriteMinRating).Select(b => b.Id).ToHashSet();

            case BookShelfKind.NotRated:
                return context.Books.Where(b => b.Rating == null || b.Rating <= 0f).Select(b => b.Id).ToHashSet();

            case BookShelfKind.Unfinished:
                // Started and not done: Reading, or any saved progress that is not Finished/Abandoned.
                return context.Books
                    .Where(b => b.Status != BookReadingStatus.Finished && b.Status != BookReadingStatus.Abandoned
                                && (b.Status == BookReadingStatus.Reading || b.LastChapterIndex > 0 || b.LastBlockId != null))
                    .Select(b => b.Id).ToHashSet();

            case BookShelfKind.SmartList when shelf.Id is int smartId:
            {
                var list = SmartListTreeLoader.LoadWithTree(context, smartId);
                return list is null
                    ? new HashSet<int>()
                    : NovelSmartListQueryBuilder.Build(context, list).Select(b => b.Id).ToHashSet();
            }

            case BookShelfKind.Collection when shelf.Id is int collectionId:
                return CollectionResolver.GetMembers(context, collectionId)
                    .Where(m => m.Kind == CollectionMemberKind.Book)
                    .Select(m => m.TargetId)
                    .ToHashSet();

            default:
                return new HashSet<int>();
        }
    }
}
