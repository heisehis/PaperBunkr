using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Storage for list layouts (docs/superpowers/specs/2026-10-04-list-layouts-design.md): the named <see cref="ListLayout"/>
/// rows and the per-list <see cref="ListLayoutAssignment"/> rows. Same no-DI, own-context-per-call,
/// <see cref="Func{PaperbunkrDbContext}"/> test-seam shape as <see cref="WorkspaceService"/>.
///
/// It only stores JSON strings: what a layout contains is each screen's business (<c>ListLayoutState</c> for Library,
/// <c>BooksWorkspaceState</c> for Books).
/// </summary>
public class ListLayoutService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public ListLayoutService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal ListLayoutService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // --- named layouts ---

    /// <summary>Every named layout for one screen, by manual <see cref="ListLayout.SortOrder"/>, then id.</summary>
    public IReadOnlyList<ListLayout> ListNamed(WorkspaceScreen screen)
    {
        using var context = _contextFactory();
        return context.ListLayouts
            .Where(l => l.Screen == screen)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .ToList();
    }

    /// <summary>
    /// CE's Save List Layout (<c>MainForm.cs</c>): a layout with the same name (case-insensitive) is replaced in place,
    /// keeping its position; any other name is appended.
    /// </summary>
    public ListLayout SaveNamed(WorkspaceScreen screen, string name, string stateJson)
    {
        name = name.Trim();
        using var context = _contextFactory();
        var rows = context.ListLayouts.Where(l => l.Screen == screen).ToList();
        var existing = rows.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.StateJson = stateJson;
            context.SaveChanges();
            return existing;
        }

        var layout = new ListLayout
        {
            Screen = screen,
            Name = name,
            SortOrder = rows.Count == 0 ? 0 : rows.Max(l => l.SortOrder) + 1,
            StateJson = stateJson,
        };
        context.ListLayouts.Add(layout);
        context.SaveChanges();
        return layout;
    }

    /// <summary>False (and nothing changes) when the name is blank or another layout on the screen already has it.</summary>
    public bool Rename(int id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return false;
        }

        using var context = _contextFactory();
        var layout = context.ListLayouts.FirstOrDefault(l => l.Id == id);
        if (layout is null)
        {
            return false;
        }

        bool taken = context.ListLayouts
            .Where(l => l.Screen == layout.Screen && l.Id != id)
            .AsEnumerable()
            .Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            return false;
        }

        layout.Name = name;
        context.SaveChanges();
        return true;
    }

    /// <summary>Removes the named layout only. Lists that were given it keep their own copy.</summary>
    public void Delete(int id)
    {
        using var context = _contextFactory();
        var layout = context.ListLayouts.FirstOrDefault(l => l.Id == id);
        if (layout is null)
        {
            return;
        }

        context.ListLayouts.Remove(layout);
        context.SaveChanges();
    }

    /// <summary>Assigns <see cref="ListLayout.SortOrder"/> = position in <paramref name="orderedIds"/>. Ids not on this screen are skipped.</summary>
    public void Reorder(WorkspaceScreen screen, IReadOnlyList<int> orderedIds)
    {
        using var context = _contextFactory();
        var rows = context.ListLayouts.Where(l => l.Screen == screen).ToList();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var row = rows.FirstOrDefault(l => l.Id == orderedIds[i]);
            if (row is not null)
            {
                row.SortOrder = i;
            }
        }

        context.SaveChanges();
    }

    /// <summary>Marks a screen as having had its starter layouts seeded; stored beside the per-list rows so it needs no schema of its own.</summary>
    internal const string TemplatesSeededKey = "#templates";

    /// <summary>
    /// Adds the starter layouts once per screen. A name the user already has is left alone, and because the seeding is
    /// recorded, a starter the user later deletes or renames is never added again.
    /// </summary>
    public void EnsureTemplatesSeeded(WorkspaceScreen screen, IReadOnlyList<(string Name, string StateJson)> templates)
    {
        using var context = _contextFactory();
        if (context.ListLayoutAssignments.Any(a => a.Screen == screen && a.SelectionKey == TemplatesSeededKey))
        {
            return;
        }

        var rows = context.ListLayouts.Where(l => l.Screen == screen).ToList();
        int order = rows.Count == 0 ? 0 : rows.Max(l => l.SortOrder) + 1;
        foreach (var (name, stateJson) in templates)
        {
            if (!rows.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                context.ListLayouts.Add(new ListLayout { Screen = screen, Name = name, SortOrder = order++, StateJson = stateJson });
            }
        }

        context.ListLayoutAssignments.Add(new ListLayoutAssignment { Screen = screen, SelectionKey = TemplatesSeededKey, StateJson = "{}" });
        context.SaveChanges();
    }

    // --- per-list assignments ---

    /// <summary>The layout one list remembers, or null when it has none of its own.</summary>
    public string? GetAssignment(WorkspaceScreen screen, string selectionKey)
    {
        using var context = _contextFactory();
        return context.ListLayoutAssignments
            .Where(a => a.Screen == screen && a.SelectionKey == selectionKey)
            .Select(a => a.StateJson)
            .FirstOrDefault();
    }

    public void SetAssignment(WorkspaceScreen screen, string selectionKey, string stateJson)
    {
        using var context = _contextFactory();
        var row = context.ListLayoutAssignments.FirstOrDefault(a => a.Screen == screen && a.SelectionKey == selectionKey);
        if (row is null)
        {
            context.ListLayoutAssignments.Add(new ListLayoutAssignment { Screen = screen, SelectionKey = selectionKey, StateJson = stateJson });
        }
        else
        {
            row.StateJson = stateJson;
        }

        context.SaveChanges();
    }

    /// <summary>Forgets one list's own layout, so it falls back to the screen default.</summary>
    public void ClearAssignment(WorkspaceScreen screen, string selectionKey)
    {
        using var context = _contextFactory();
        var row = context.ListLayoutAssignments.FirstOrDefault(a => a.Screen == screen && a.SelectionKey == selectionKey);
        if (row is not null)
        {
            context.ListLayoutAssignments.Remove(row);
            context.SaveChanges();
        }
    }

    /// <summary>
    /// CE's "set on all lists" (<c>ComicLibrary.ResetDisplayConfigs</c>): the screen default becomes this layout and every
    /// list's own row is dropped, so all of them (and any list created later) show it.
    /// </summary>
    public void SetOnAllLists(WorkspaceScreen screen, string stateJson)
    {
        using var context = _contextFactory();
        var rows = context.ListLayoutAssignments.Where(a => a.Screen == screen).ToList();
        context.ListLayoutAssignments.RemoveRange(rows.Where(a => a.SelectionKey != ListLayoutAssignment.DefaultKey && a.SelectionKey != TemplatesSeededKey));
        var fallback = rows.FirstOrDefault(a => a.SelectionKey == ListLayoutAssignment.DefaultKey);
        if (fallback is null)
        {
            context.ListLayoutAssignments.Add(new ListLayoutAssignment { Screen = screen, SelectionKey = ListLayoutAssignment.DefaultKey, StateJson = stateJson });
        }
        else
        {
            fallback.StateJson = stateJson;
        }

        context.SaveChanges();
    }
}
