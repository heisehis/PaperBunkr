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
/// <see cref="SelectComics"/> is a documented no-op: Paperbunkr's Library grid doesn't yet expose a
/// selection API a plugin can drive (deferred, see the spec's follow-on notes).
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

    public void SelectComics(IEnumerable<Issue> books)
    {
        // No-op - see class doc comment.
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
