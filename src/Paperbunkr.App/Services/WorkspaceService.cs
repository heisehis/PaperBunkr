using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// CRUD for saved <see cref="Workspace"/> rows (docs/superpowers/specs/2026-09-03-library-saved-
/// workspaces-design.md). Same no-DI, own-context-per-call, <see cref="Func{PaperbunkrDbContext}"/>
/// test-seam shape as <see cref="KeyBindingService"/>.
///
/// <see cref="Workspace.IsBuiltIn"/> is enforced here, not just in the UI - a stale command or a
/// test can't rename / re-point / delete a seeded starter.
/// </summary>
public class WorkspaceService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public WorkspaceService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal WorkspaceService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>Every workspace for one screen, built-ins first, then by manual <see cref="Workspace.SortOrder"/>, then id.</summary>
    public IReadOnlyList<Workspace> List(WorkspaceScreen screen)
    {
        using var context = _contextFactory();
        return context.Workspaces
            .Where(w => w.Screen == screen)
            .OrderByDescending(w => w.IsBuiltIn)
            .ThenBy(w => w.SortOrder)
            .ThenBy(w => w.Id)
            .ToList();
    }

    public Workspace Create(WorkspaceScreen screen, string name, string stateJson)
    {
        using var context = _contextFactory();
        int nextOrder = context.Workspaces.Where(w => w.Screen == screen).Select(w => (int?)w.SortOrder).Max() + 1 ?? 0;
        var workspace = new Workspace
        {
            Screen = screen,
            Name = name,
            SortOrder = nextOrder,
            IsBuiltIn = false,
            StateJson = stateJson,
        };
        context.Workspaces.Add(workspace);
        context.SaveChanges();
        return workspace;
    }

    /// <summary>Re-snapshots a user workspace's captured state. No-op for a built-in.</summary>
    public void UpdateState(int id, string stateJson)
    {
        using var context = _contextFactory();
        var workspace = context.Workspaces.FirstOrDefault(w => w.Id == id);
        if (workspace is null || workspace.IsBuiltIn)
        {
            return;
        }

        workspace.StateJson = stateJson;
        context.SaveChanges();
    }

    /// <summary>No-op for a built-in.</summary>
    public void Rename(int id, string name)
    {
        using var context = _contextFactory();
        var workspace = context.Workspaces.FirstOrDefault(w => w.Id == id);
        if (workspace is null || workspace.IsBuiltIn)
        {
            return;
        }

        workspace.Name = name;
        context.SaveChanges();
    }

    /// <summary>No-op for a built-in.</summary>
    public void Delete(int id)
    {
        using var context = _contextFactory();
        var workspace = context.Workspaces.FirstOrDefault(w => w.Id == id);
        if (workspace is null || workspace.IsBuiltIn)
        {
            return;
        }

        context.Workspaces.Remove(workspace);
        context.SaveChanges();
    }

    /// <summary>Assigns <see cref="Workspace.SortOrder"/> = position in <paramref name="orderedIds"/>,
    /// for the user (non-built-in) rows named. Ids not on this screen, or built-in, are skipped.</summary>
    public void Reorder(WorkspaceScreen screen, IReadOnlyList<int> orderedIds)
    {
        using var context = _contextFactory();
        var rows = context.Workspaces.Where(w => w.Screen == screen && !w.IsBuiltIn).ToList();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var row = rows.FirstOrDefault(w => w.Id == orderedIds[i]);
            if (row is not null)
            {
                row.SortOrder = i;
            }
        }

        context.SaveChanges();
    }

    /// <summary>
    /// Idempotently inserts the read-only starter workspaces (design's tables). Keyed on
    /// <c>(Screen, Name, IsBuiltIn)</c> - re-running touches nothing, and a user's own
    /// identically-named workspace neither blocks a starter nor is disturbed by one.
    /// </summary>
    public void EnsureBuiltInsSeeded()
    {
        using var context = _contextFactory();
        var existing = context.Workspaces
            .Where(w => w.IsBuiltIn)
            .Select(w => new { w.Screen, w.Name })
            .ToList();

        bool Missing(WorkspaceScreen screen, string name) =>
            !existing.Any(e => e.Screen == screen && e.Name == name);

        int order;

        // --- Library ---
        // All PosterGrid: it's the only view mode with a virtualizing panel, so it's the only one
        // that stays responsive on a multi-thousand-issue library. (A "Recently added" preset was
        // dropped - PosterGrid already sorts by date-added-descending by default, so it was
        // identical to "All comics"; a Details-view variant spiked memory on large libraries.)
        var library = new (string Name, LibraryWorkspaceState State)[]
        {
            ("All comics", new LibraryWorkspaceState()),
            ("Currently reading", new LibraryWorkspaceState(
                IssueListSortField: IssueListSortField.Opened,
                IssueListSortDirection: SortDirection.Descending,
                FilterUnreadOnly: true)),
            ("Manga", new LibraryWorkspaceState(
                Granularity: LibraryContentGranularity.Series,
                SortField: LibrarySortField.Name,
                SortDirection: SortDirection.Ascending,
                ActiveContentType: ContentType.Manga)),
        };

        order = 0;
        foreach (var (name, state) in library)
        {
            if (Missing(WorkspaceScreen.Library, name))
            {
                context.Workspaces.Add(new Workspace
                {
                    Screen = WorkspaceScreen.Library,
                    Name = name,
                    SortOrder = order,
                    IsBuiltIn = true,
                    StateJson = WorkspaceStateJson.Serialize(state),
                });
            }

            order++;
        }

        // --- Books ---
        var books = new (string Name, BooksWorkspaceState State)[]
        {
            ("All books", new BooksWorkspaceState()),
            ("Recently added", new BooksWorkspaceState(BooksSortField.RecentlyAdded, SortDirection.Descending, BooksGroupField.None)),
            ("By series", new BooksWorkspaceState(BooksSortField.Title, SortDirection.Ascending, BooksGroupField.Series)),
        };

        order = 0;
        foreach (var (name, state) in books)
        {
            if (Missing(WorkspaceScreen.Books, name))
            {
                context.Workspaces.Add(new Workspace
                {
                    Screen = WorkspaceScreen.Books,
                    Name = name,
                    SortOrder = order,
                    IsBuiltIn = true,
                    StateJson = WorkspaceStateJson.Serialize(state),
                });
            }

            order++;
        }

        context.SaveChanges();
    }
}
