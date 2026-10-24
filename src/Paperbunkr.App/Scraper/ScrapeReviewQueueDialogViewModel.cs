using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.App.Scraper;

/// <summary>One library series' comics waiting for review.</summary>
public sealed class ScrapeReviewSeriesGroup(string seriesName, IReadOnlyList<ScrapeReviewItem> items)
{
    public string SeriesName { get; } = seriesName;

    public IReadOnlyList<ScrapeReviewItem> Items { get; } = items;

    public IReadOnlyList<int> IssueIds => Items.Select(i => i.IssueId).ToList();

    public string CountText => Items.Count == 1 ? "1 comic" : $"{Items.Count} comics";

    /// <summary>The first few labels, so the row says which comics without listing fifty.</summary>
    public string Preview =>
        string.Join(", ", Items.Take(3).Select(i => i.BookLabel)) + (Items.Count > 3 ? $" and {Items.Count - 3} more" : string.Empty);

    public string? Reason => Items.Select(i => i.Reason).FirstOrDefault(r => !string.IsNullOrEmpty(r));
}

/// <summary>
/// The review list (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 2): what unattended scrapes could
/// not confirm, a library series per row. "Review" closes this dialog and runs an interactive scrape of that series (series-first, so its
/// match is asked once); "Skip" takes it off the list; "Never" marks its comics never to be scraped again (CE's skip-forever).
/// </summary>
public sealed partial class ScrapeReviewQueueDialogViewModel : ObservableObject
{
    private readonly ScrapeReviewQueue _queue;
    private readonly Func<PaperbunkrDbContext> _createContext;
    private readonly Action<IReadOnlyList<int>?> _resolve;

    public ScrapeReviewQueueDialogViewModel(ScrapeReviewQueue queue, Func<PaperbunkrDbContext> createContext, Action<IReadOnlyList<int>?> resolve)
    {
        _queue = queue;
        _createContext = createContext;
        _resolve = resolve;
        Reload();
    }

    public ObservableCollection<ScrapeReviewSeriesGroup> Groups { get; } = new();

    /// <summary>The focused series (Enter reviews it, Delete skips it).</summary>
    [ObservableProperty]
    private ScrapeReviewSeriesGroup? _selectedGroup;

    public bool IsEmpty => Groups.Count == 0;

    public string Subtitle => Groups.Count == 0
        ? "Nothing is waiting."
        : $"{Groups.Sum(g => g.Items.Count)} comics in {Groups.Count} series. Review asks you to pick each series' match once.";

    public void Reload()
    {
        var items = _queue.Load();
        var ids = items.Select(i => i.IssueId).ToList();
        Dictionary<int, string> seriesOf;
        using (var context = _createContext())
        {
            seriesOf = context.Issues.AsNoTracking().Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, Name = i.Series != null ? i.Series.Name : string.Empty })
                .ToDictionary(x => x.Id, x => x.Name);
        }

        // A comic no longer in the library has nothing left to review.
        var gone = ids.Where(id => !seriesOf.ContainsKey(id)).ToList();
        if (gone.Count > 0)
        {
            _queue.Remove(gone);
        }

        Groups.Clear();
        foreach (var group in items.Where(i => seriesOf.ContainsKey(i.IssueId))
                     .GroupBy(i => seriesOf[i.IssueId], StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new ScrapeReviewSeriesGroup(string.IsNullOrEmpty(group.Key) ? "(no series)" : group.Key, group.ToList()));
        }

        SelectedGroup = Groups.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Subtitle));
    }

    [RelayCommand]
    private void Review(ScrapeReviewSeriesGroup? group)
    {
        if (group is not null)
        {
            _resolve(group.IssueIds);
        }
    }

    [RelayCommand]
    private void ReviewAll() => _resolve(Groups.SelectMany(g => g.IssueIds).ToList());

    [RelayCommand]
    private void Skip(ScrapeReviewSeriesGroup? group)
    {
        if (group is null)
        {
            return;
        }

        _queue.Remove(group.IssueIds);
        RemoveDeferred(group);
    }

    [RelayCommand]
    private void Never(ScrapeReviewSeriesGroup? group)
    {
        if (group is null)
        {
            return;
        }

        var ids = group.IssueIds;
        using (var context = _createContext())
        {
            foreach (var issue in context.Issues.Where(i => ids.Contains(i.Id)))
            {
                issue.ScrapePermanentlySkipped = true;
            }

            context.SaveChanges();
        }

        _queue.Remove(ids);
        RemoveDeferred(group);
    }

    [RelayCommand]
    private void Close() => _resolve(null);

    // The clicked button is inside the row being removed: wait for its Click to finish routing (CLAUDE.md, routed-event detach).
    private void RemoveDeferred(ScrapeReviewSeriesGroup group) => Dispatcher.UIThread.Post(() =>
    {
        int index = Groups.IndexOf(group);
        Groups.Remove(group);
        if (SelectedGroup is null || SelectedGroup == group)
        {
            SelectedGroup = Groups.Count == 0 ? null : Groups[System.Math.Clamp(index, 0, Groups.Count - 1)]; // keep the keyboard on the next series
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Subtitle));
    });
}
