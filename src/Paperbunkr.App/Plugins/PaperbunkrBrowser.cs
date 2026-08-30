using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IBrowser"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §4), self-contained against the whole library (ordered by Series/Number) relative to whatever
/// the Reader currently has open - doesn't depend on the Library screen's own selection model.
/// <see cref="SelectComics"/> drives Library's real selection model (docs/superpowers/specs/
/// 2026-08-30-plugin-api-automation-gaps-design.md) - it was a documented no-op until multiselect
/// shipped.
/// </summary>
public sealed class PaperbunkrBrowser : IBrowser
{
    private readonly MainViewModel _main;
    private static readonly Random Rng = new();

    public PaperbunkrBrowser(MainViewModel main) => _main = main;

    public bool OpenNextComic() => OpenRelative(offset: 1);

    public bool OpenPrevComic() => OpenRelative(offset: -1);

    public bool OpenRandomComic()
    {
        using var context = PaperbunkrDb.CreateContext();
        var ids = context.Issues.Select(i => i.Id).ToList();
        if (ids.Count == 0)
        {
            return false;
        }

        _main.OpenReaderForPlugin(ids[Rng.Next(ids.Count)]);
        return true;
    }

    /// <summary>
    /// Only issues actually present in Library's currently-loaded <c>IssueList.Rows</c> get
    /// selected - doesn't force-clear an active search/filter to make everything visible, matching
    /// CE's own behavior of only ever operating on the currently-visible list. Navigates to Library
    /// first so the selection is immediately visible, not set invisibly on a screen nobody's
    /// looking at.
    /// </summary>
    public void SelectComics(IEnumerable<Issue> books)
    {
        var targetIds = books.Select(b => b.Id).ToHashSet();
        var matchingRows = _main.Library.IssueList.Rows.Where(r => targetIds.Contains(r.Id)).ToList();

        _main.Library.Selection.Clear();
        _main.Library.Selection.SelectAll(matchingRows);
        _main.GoLibraryCommand.Execute(null);
    }

    private bool OpenRelative(int offset)
    {
        int? currentId = _main.Reader.LoadedIssue?.Id;
        using var context = PaperbunkrDb.CreateContext();
        var ordered = context.Issues
            .OrderBy(i => i.SeriesId)
            .ThenBy(i => i.Number)
            .Select(i => i.Id)
            .ToList();

        if (ordered.Count == 0)
        {
            return false;
        }

        int index = currentId is int id ? ordered.IndexOf(id) : -1;
        int nextIndex = index < 0
            ? 0
            : Math.Clamp(index + offset, 0, ordered.Count - 1);

        _main.OpenReaderForPlugin(ordered[nextIndex]);
        return true;
    }
}
