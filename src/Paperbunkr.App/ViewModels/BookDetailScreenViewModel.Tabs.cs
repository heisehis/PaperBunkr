using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Book Detail's tabs, reading dashboard, notes hub and author link (docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md). A separate file so
/// the screen's main view model keeps to what it already does; <see cref="LoadTabData"/> is the one hook it calls once a book (and its parsed chapters) is known.
/// </summary>
public partial class BookDetailScreenViewModel
{
    // --- tabs (§2) ---

    private int _tabBookId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTab), nameof(IsChaptersTab), nameof(IsNotesTab), nameof(IsYourReadingTab), nameof(ShowStats))]
    private BookDetailTab _activeTab = BookDetailTab.Overview;

    public bool IsOverviewTab => ActiveTab == BookDetailTab.Overview;

    public bool IsChaptersTab => ActiveTab == BookDetailTab.Chapters;

    public bool IsNotesTab => ActiveTab == BookDetailTab.Notes;

    /// <summary>The reading dashboard's own tab (books redesign §4.1).</summary>
    public bool IsYourReadingTab => ActiveTab == BookDetailTab.YourReading;

    /// <summary>A PDF has no chapters, so it has no Chapters tab.</summary>
    [ObservableProperty]
    private bool _showChaptersTab;

    /// <summary>Shown once there is anything to list; a PDF always shows it (its captured regions live there).</summary>
    [ObservableProperty]
    private bool _showNotesTab;

    [RelayCommand]
    private void SetTab(BookDetailTab tab) => ActiveTab = tab;

    // --- reading dashboard (§4) ---

    public ObservableCollection<BookStatTile> StatTiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStats))]
    private bool _hasStats;

    /// <summary>The dashboard shows on the Your reading tab, and that tab only exists when there is something to say (books redesign §4.1).</summary>
    public bool ShowStats => HasStats && IsYourReadingTab;

    // --- notes hub (§6) ---

    private IReadOnlyList<BookNoteEntry> _allNotes = Array.Empty<BookNoteEntry>();

    public ObservableCollection<BookNoteEntry> Notes { get; } = new();

    [ObservableProperty]
    private BookNoteKindFilter _noteKindFilter = BookNoteKindFilter.All;

    [ObservableProperty]
    private BookHighlightColor? _noteColorFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoNotes))]
    private bool _hasNotes;

    public bool ShowNoNotes => !HasNotes;

    public bool HasNoteMatches => Notes.Count > 0;

    public bool ShowNoNoteMatches => HasNotes && Notes.Count == 0;

    public bool HasHighlights => _allNotes.Any(n => n.Kind == BookNoteKind.Highlight);

    partial void OnNoteKindFilterChanged(BookNoteKindFilter value)
    {
        OnPropertyChanged(nameof(IsKindAll));
        OnPropertyChanged(nameof(IsKindHighlights));
        OnPropertyChanged(nameof(IsKindBookmarks));
        OnPropertyChanged(nameof(IsKindRegions));
        RefreshNotes();
    }

    partial void OnNoteColorFilterChanged(BookHighlightColor? value)
    {
        OnPropertyChanged(nameof(IsColorYellow));
        OnPropertyChanged(nameof(IsColorGreen));
        OnPropertyChanged(nameof(IsColorBlue));
        OnPropertyChanged(nameof(IsColorPink));
        RefreshNotes();
    }

    public bool IsKindAll => NoteKindFilter == BookNoteKindFilter.All;
    public bool IsKindHighlights => NoteKindFilter == BookNoteKindFilter.Highlights;
    public bool IsKindBookmarks => NoteKindFilter == BookNoteKindFilter.Bookmarks;
    public bool IsKindRegions => NoteKindFilter == BookNoteKindFilter.Regions;
    public bool IsColorYellow => NoteColorFilter == BookHighlightColor.Yellow;
    public bool IsColorGreen => NoteColorFilter == BookHighlightColor.Green;
    public bool IsColorBlue => NoteColorFilter == BookHighlightColor.Blue;
    public bool IsColorPink => NoteColorFilter == BookHighlightColor.Pink;

    [RelayCommand]
    private void SetNoteKindFilter(BookNoteKindFilter filter) => NoteKindFilter = filter;

    /// <summary>Clicking the active colour clears the colour filter.</summary>
    [RelayCommand]
    private void ToggleNoteColor(BookHighlightColor color) => NoteColorFilter = NoteColorFilter == color ? null : color;

    private void RefreshNotes()
    {
        Notes.Clear();
        foreach (var note in BookNotesBuilder.Filter(_allNotes, NoteKindFilter, NoteColorFilter))
        {
            Notes.Add(note);
        }

        OnPropertyChanged(nameof(HasNoteMatches));
        OnPropertyChanged(nameof(ShowNoNoteMatches));
    }

    /// <summary>Opens the reader at the note's place. A highlight or bookmark carries its block; a PDF region only its page, which the page reader does not take yet, so it opens at the saved position.</summary>
    [RelayCommand]
    private void OpenNote(BookNoteEntry? note)
    {
        if (note is null)
        {
            return;
        }

        _goReaderForBook(_bookId, _format, note.Kind == BookNoteKind.Region ? null : new BookPosition(note.ChapterIndex, note.BlockId));
    }

    /// <summary>Deletes the note from the database and, one dispatcher tick later (its own button is still raising the click), from the list.</summary>
    [RelayCommand]
    private void DeleteNote(BookNoteEntry? note)
    {
        if (note is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            switch (note.Kind)
            {
                case BookNoteKind.Highlight:
                    context.BookHighlights.Where(h => h.Id == note.Id).ExecuteDelete();
                    break;
                case BookNoteKind.Bookmark:
                    context.BookBookmarks.Where(b => b.Id == note.Id).ExecuteDelete();
                    break;
                default:
                    context.BookAnnotationImages.Where(r => r.Id == note.Id).ExecuteDelete();
                    break;
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            _allNotes = _allNotes.Where(n => !(n.Kind == note.Kind && n.Id == note.Id)).ToList();
            Bookmarks.Remove(Bookmarks.FirstOrDefault(b => note.Kind == BookNoteKind.Bookmark && b.Id == note.Id)!);
            HasBookmarks = Bookmarks.Count > 0;
            HasNotes = _allNotes.Count > 0;
            ShowNotesTab = HasNotes || _format == BookFormat.Pdf;
            OnPropertyChanged(nameof(HasHighlights));
            RefreshNotes();
        });
    }

    // --- author link (§7) ---

    /// <summary>Set by MainViewModel: opens the Books grid filtered to one author.</summary>
    public Action<string>? GoBooksForAuthor { get; set; }

    [RelayCommand]
    private void OpenAuthor()
    {
        if (HasAuthor)
        {
            GoBooksForAuthor?.Invoke(Author.Trim());
        }
    }

    // --- the hook the main view model calls ---

    /// <summary>Fills the tabs once the book and its chapter titles are known. Opening a different book returns to Overview; reloading the same one keeps the tab.</summary>
    private void LoadTabData(Book book, IReadOnlyList<string> chapterTitles)
    {
        if (book.Id != _tabBookId)
        {
            _tabBookId = book.Id;
            ActiveTab = BookDetailTab.Overview;
            NoteKindFilter = BookNoteKindFilter.All;
            NoteColorFilter = null;
        }

        _allNotes = BookNotesBuilder.Build(book.Highlights, book.Bookmarks, book.AnnotationImages, chapterTitles);
        HasNotes = _allNotes.Count > 0;
        ShowNotesTab = HasNotes || book.Format == BookFormat.Pdf;
        ShowChaptersTab = book.Format != BookFormat.Pdf && HasChapters;
        OnPropertyChanged(nameof(HasHighlights));
        RefreshNotes();

        // A tab that is no longer offered (a deleted last note, a PDF) cannot stay selected.
        if ((IsNotesTab && !ShowNotesTab) || (IsChaptersTab && !ShowChaptersTab) || (IsYourReadingTab && !HasStats))
        {
            ActiveTab = BookDetailTab.Overview;
        }

        LoadStats(book);
        LoadJacket(book);
    }

    private void LoadStats(Book book)
    {
        StatTiles.Clear();

        List<ReadingEvent> events;
        using (var context = PaperbunkrDb.CreateContext())
        {
            events = context.ReadingEvents
                .AsNoTracking()
                .Where(e => e.ItemType == ReadingItemType.Novel && e.ItemId == book.Id)
                .ToList();
        }

        var stats = BookReadingStatsResolver.Compute(events, book.CharacterCount, BookProgress.Compute(book), book.Status == BookReadingStatus.Finished);
        _timeLeft = stats.TimeLeft;
        if (stats.HasAny)
        {
            StatTiles.Add(new BookStatTile("Sessions", stats.Sessions.ToString("N0", CultureInfo.CurrentCulture)));
            if (stats.ActiveTime > TimeSpan.Zero)
            {
                StatTiles.Add(new BookStatTile("Time read", FormatDuration(stats.ActiveTime)));
            }

            if (stats.LastReadUtc is { } last)
            {
                StatTiles.Add(new BookStatTile("Last read", last.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture)));
            }

            if (stats.FirstReadUtc is { } first && stats.Sessions > 1)
            {
                StatTiles.Add(new BookStatTile("First read", first.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture)));
            }

            if (stats.CharsPerMinute is { } pace)
            {
                StatTiles.Add(new BookStatTile("Pace", $"{pace * 60d / ReadingPageMathCharsPerPage:0} pages/hour"));
            }

            if (stats.TimeLeft is { } left)
            {
                StatTiles.Add(new BookStatTile("Time left", $"About {FormatDuration(left)}"));
            }
        }

        HasStats = StatTiles.Count > 0;
    }

    private const double ReadingPageMathCharsPerPage = Paperbunkr.Data.Metadata.ReadingPageMath.EpubCharsPerPage;

    /// <summary>"3h 20m", "45m", "2h".</summary>
    internal static string FormatDuration(TimeSpan span)
    {
        int minutes = (int)Math.Round(span.TotalMinutes);
        if (minutes < 60)
        {
            return $"{Math.Max(minutes, 1)}m";
        }

        int hours = minutes / 60;
        int rest = minutes % 60;
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }
}
