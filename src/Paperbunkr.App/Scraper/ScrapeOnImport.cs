using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// Scrape on import (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 9): comics a library scan just added
/// are scraped in the background, unattended, when "Scrape new files automatically" is on and Metron is connected. It never blocks the import:
/// confident matches apply, the rest wait in the review list. Manga is left to its own sources.
/// </summary>
public sealed class ScrapeOnImport : IDisposable
{
    private readonly LibraryEvents _events;
    private readonly Func<PaperbunkrDbContext> _createContext;
    private readonly Func<IReadOnlyList<int>, Task<string>> _scrape;
    private readonly Func<Func<Task>, Task> _run;

    public ScrapeOnImport(LibraryEvents events, Func<PaperbunkrDbContext> createContext, Func<IReadOnlyList<int>, Task<string>> scrape, Func<Func<Task>, Task>? run = null)
    {
        _events = events;
        _createContext = createContext;
        _scrape = scrape;
        _run = run ?? (work => Task.Run(work));
        _events.LibraryScanCompleted += OnScanCompleted;
    }

    public void Dispose() => _events.LibraryScanCompleted -= OnScanCompleted;

    /// <summary>The comics of <paramref name="addedIds"/> this feature should scrape now (empty when it is off or Metron isn't connected).</summary>
    public IReadOnlyList<int> Eligible(IReadOnlyList<int> addedIds)
    {
        if (addedIds.Count == 0)
        {
            return Array.Empty<int>();
        }

        using var context = _createContext();
        if (!ScrapeSettings.Load(context).ScrapeNewFiles || !MetronAuth.IsConfigured(context))
        {
            return Array.Empty<int>();
        }

        return context.Issues.AsNoTracking()
            .Where(i => addedIds.Contains(i.Id) && i.MetadataSource == null && i.FilePath != null && !i.ScrapePermanentlySkipped
                        && i.Series != null
                        && i.Series.ContentType != ContentType.Manga && i.Series.ContentType != ContentType.Manhua && i.Series.ContentType != ContentType.Manhwa)
            .Select(i => i.Id)
            .ToList();
    }

    private void OnScanCompleted(LibraryScanCompletedEvent e)
    {
        var added = e.AddedItemIds;
        _ = _run(async () =>
        {
            try
            {
                var ids = Eligible(added);
                if (ids.Count > 0)
                {
                    await _scrape(ids).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Best effort: the comics stay unmatched, and "Scrape all unmatched" or the scheduled scrape picks them up.
            }
        });
    }
}
