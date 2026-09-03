using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Builds the flat searchable index the Quick Open palette ranks over (docs/superpowers/specs/
/// 2026-09-03-quick-open-command-palette-design.md). Same no-DI, own-context-per-call,
/// <see cref="Func{PaperbunkrDbContext}"/> test-seam shape as <c>WorkspaceService</c>.
///
/// <see cref="BuildIndex"/> runs one projected <c>AsNoTracking</c> query per entity type - never
/// materializes an entity, never touches a cover - so it stays single-digit-milliseconds even on a
/// multi-thousand-issue library. Called once per palette open; not cached.
/// </summary>
public class QuickOpenService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public QuickOpenService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal QuickOpenService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>The 7 lateral shell screens, in rail order. <c>Key</c> matches <c>MainViewModel.CurrentScreen</c>.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Screens = new[]
    {
        ("home", "Home"),
        ("library", "Library"),
        ("books", "Books"),
        ("smart", "Smart Lists"),
        ("reading", "Reading Lists"),
        ("events", "Events and Continuity"),
        ("preferences", "Preferences"),
    };

    /// <summary>v1 action verbs. Each <c>Key</c> is dispatched by <c>MainViewModel.ActivateQuickOpenEntry</c>.
    /// Scan / Backup / Check-for-updates are deliberately absent - they live on
    /// <c>PreferencesScreenViewModel</c>, not <c>MainViewModel</c>, so they can't be invoked from here yet.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, string Icon)> Actions = new[]
    {
        ("addFolder", "Add folder…", "FolderOpen"),
        ("addIssue", "Add issue to library…", "Add"),
        ("newReadingList", "New reading list…", "Add"),
        ("importCe", "Import from ComicRack…", "ArrowDownload"),
    };

    public IReadOnlyList<QuickOpenEntry> BuildIndex()
    {
        using var context = _contextFactory();

        var series = context.Series.AsNoTracking()
            .Select(s => new { s.Id, s.Name })
            .ToList();

        // Alt / localized titles, folded into the owning series' Primary so the matcher (which only
        // scores Primary) can find a series by any of its names in one row.
        var altTitles = context.SeriesTitles.AsNoTracking()
            .Select(t => new { t.SeriesId, t.Value })
            .ToList()
            .GroupBy(t => t.SeriesId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToList());

        var issues = context.Issues.AsNoTracking()
            .Select(i => new { i.Id, i.Number, i.Title, SeriesName = i.Series!.Name, i.OpenedTime })
            .ToList();

        var books = context.Books.AsNoTracking()
            .Select(b => new { b.Id, b.Title, b.Author, b.LastOpenedTime })
            .ToList();

        var readingLists = context.ReadingLists.AsNoTracking().Select(x => new { x.Id, x.Name }).ToList();
        var smartLists = context.SmartLists.AsNoTracking().Select(x => new { x.Id, x.Name }).ToList();
        var collections = context.Collections.AsNoTracking().Select(x => new { x.Id, x.Name }).ToList();
        var storyEvents = context.StoryEvents.AsNoTracking().Select(x => new { x.Id, x.Name }).ToList();
        var continuities = context.Continuities.AsNoTracking().Select(x => new { x.Id, x.Name }).ToList();

        var entries = new List<QuickOpenEntry>(
            series.Count + issues.Count + books.Count + readingLists.Count + smartLists.Count +
            collections.Count + storyEvents.Count + continuities.Count + Screens.Count + Actions.Count);

        foreach (var s in series)
        {
            string primary = s.Name;
            if (altTitles.TryGetValue(s.Id, out var alts))
            {
                foreach (var a in alts.Where(a => !string.Equals(a, s.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    primary += "  " + a;
                }
            }

            entries.Add(new QuickOpenEntry(QuickOpenKind.Series, s.Id, primary, null, "BookOpen", null));
        }

        foreach (var i in issues)
        {
            string primary = IssueTitle(i.SeriesName, i.Number, i.Title);
            entries.Add(new QuickOpenEntry(QuickOpenKind.Issue, i.Id, primary, i.SeriesName, "Book", i.OpenedTime));
        }

        foreach (var b in books)
        {
            entries.Add(new QuickOpenEntry(QuickOpenKind.Book, b.Id, b.Title, b.Author, "Book", b.LastOpenedTime));
        }

        foreach (var x in readingLists)
            entries.Add(new QuickOpenEntry(QuickOpenKind.ReadingList, x.Id, x.Name, null, "List", null));
        foreach (var x in smartLists)
            entries.Add(new QuickOpenEntry(QuickOpenKind.SmartList, x.Id, x.Name, null, "Sparkle", null));
        foreach (var x in collections)
            entries.Add(new QuickOpenEntry(QuickOpenKind.Collection, x.Id, x.Name, null, "Apps", null));
        foreach (var x in storyEvents)
            entries.Add(new QuickOpenEntry(QuickOpenKind.StoryEvent, x.Id, x.Name, null, "Star", null));
        foreach (var x in continuities)
            entries.Add(new QuickOpenEntry(QuickOpenKind.Continuity, x.Id, x.Name, null, "Layer", null));

        foreach (var (key, label) in Screens)
            entries.Add(new QuickOpenEntry(QuickOpenKind.Screen, null, label, null, "Home", null, key));
        foreach (var (key, label, icon) in Actions)
            entries.Add(new QuickOpenEntry(QuickOpenKind.Action, null, label, null, icon, null, key));

        return entries;
    }

    private static string IssueTitle(string seriesName, string? number, string? title)
    {
        string head = string.IsNullOrWhiteSpace(number) ? seriesName : $"{seriesName} #{number}";
        return string.IsNullOrWhiteSpace(title) ? head : $"{head} – {title}";
    }
}
