using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>What a row of the drawer's NOTES list is.</summary>
public enum NoteItemKind
{
    Note,
    Clip,
    BookmarkNote,
}

/// <summary>One row of the drawer's NOTES list: a page note, a region clip, or (read-only) the note of a named bookmark.</summary>
public sealed class NoteListItem
{
    public NoteItemKind Kind { get; init; }

    /// <summary>0-based page the row belongs to.</summary>
    public int PageIndex { get; init; }

    public string PageLabel => $"Page {PageIndex + 1}";

    /// <summary>The note text, the clip's caption, or the bookmark's note.</summary>
    public string? Text { get; set; }

    /// <summary>The clip's row id (0 for a note or bookmark note).</summary>
    public int ClipId { get; init; }

    public string? ImagePath { get; init; }

    public Bitmap? Thumbnail { get; init; }

    public bool IsClip => Kind == NoteItemKind.Clip;

    public bool IsBookmarkNote => Kind == NoteItemKind.BookmarkNote;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public string KindLabel => Kind switch
    {
        NoteItemKind.Clip => "Clip",
        NoteItemKind.BookmarkNote => "Bookmark note",
        _ => "Note",
    };
}

/// <summary>
/// Page notes on the reader view model (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #7): one note per page, edited in the tools drawer's NOTES section, a marker on the page dots, and a list of
/// every note (and later clip) of the issue that jumps to its page. A bookmark's own note is listed read-only beside them so nothing is lost or duplicated. Kept in its own file so the reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    private readonly Dictionary<int, PageNote> _pageNotes = new();
    private readonly HashSet<int> _notedPages = new();
    private List<(int Page, string Note)> _bookmarkNotes = new();

    /// <summary>Every note, clip and bookmark note of the open issue, by page.</summary>
    public ObservableCollection<NoteListItem> NoteItems { get; } = new();

    /// <summary>The text box for the current page's note.</summary>
    [ObservableProperty]
    private string _noteDraft = string.Empty;

    /// <summary>The current page already has a saved note (the Delete button shows).</summary>
    [ObservableProperty]
    private bool _hasCurrentNote;

    [ObservableProperty]
    private bool _isNotesSectionExpanded;

    public bool HasNoteItems => NoteItems.Count > 0;

    /// <summary>Raised when the note box should take keyboard focus (the palette's and page menu's "Note on this page").</summary>
    public event Action? NoteFocusRequested;

    private void HookNotes() => CurrentPageIndexChanged += _ => RefreshNoteDraft();

    /// <summary>Reads this issue's notes and clips (called from <c>Load</c>, before the thumbnails are built so the dots know which pages have a note).</summary>
    private void LoadNotes(PaperbunkrDbContext context, int issueId)
    {
        _pageNotes.Clear();
        _notedPages.Clear();
        foreach (var note in context.PageNotes.Where(n => n.IssueId == issueId))
        {
            _pageNotes[note.PageNumber] = note;
            _notedPages.Add(note.PageNumber);
        }

        _bookmarkNotes = context.IssueBookmarks
            .Where(b => b.IssueId == issueId && b.Note != null && b.Note != string.Empty)
            .OrderBy(b => b.PageNumber)
            .AsEnumerable()
            .Select(b => (b.PageNumber, b.Note!))
            .ToList();
        LoadClips(context, issueId);
        RefreshNoteDraft();
        RebuildNoteItems();
    }

    /// <summary>Puts the current page's saved note (or nothing) in the text box.</summary>
    private void RefreshNoteDraft()
    {
        _pageNotes.TryGetValue(_currentPageIndex, out var note);
        NoteDraft = note?.Text ?? string.Empty;
        HasCurrentNote = note is not null;
    }

    private void RebuildNoteItems()
    {
        NoteItems.Clear();
        var rows = new List<NoteListItem>();
        foreach (var note in _pageNotes.Values)
        {
            rows.Add(new NoteListItem { Kind = NoteItemKind.Note, PageIndex = note.PageNumber, Text = note.Text });
        }

        foreach (var (page, note) in _bookmarkNotes)
        {
            rows.Add(new NoteListItem { Kind = NoteItemKind.BookmarkNote, PageIndex = page, Text = note });
        }

        AddClipRows(rows);
        foreach (var row in rows.OrderBy(r => r.PageIndex).ThenBy(r => r.Kind))
        {
            NoteItems.Add(row);
        }

        OnPropertyChanged(nameof(HasNoteItems));
    }

    /// <summary>Saves the text box as this page's note; an empty box deletes the note.</summary>
    [RelayCommand]
    private void SaveNote()
    {
        if (_loadedIssueId is not int issueId)
        {
            return;
        }

        string text = (NoteDraft ?? string.Empty).Trim();
        if (text.Length > PageNote.MaxTextLength)
        {
            text = text[..PageNote.MaxTextLength];
        }

        if (text.Length == 0)
        {
            DeleteNote();
            return;
        }

        int page = _currentPageIndex;
        using (var context = PaperbunkrDb.CreateContext(includeRemote: true))
        {
            var row = context.PageNotes.FirstOrDefault(n => n.IssueId == issueId && n.PageNumber == page);
            if (row is null)
            {
                row = new PageNote { IssueId = issueId, PageNumber = page, CreatedTime = DateTime.UtcNow };
                context.PageNotes.Add(row);
            }

            row.Text = text;
            row.ModifiedTime = DateTime.UtcNow;
            context.SaveChanges();
            _pageNotes[page] = row;
        }

        SetNoteMarker(page, true);
        NoteDraft = text;
        HasCurrentNote = true;
        RebuildNoteItems();
        ToastRequested?.Invoke(new ToastRequest("Note saved", $"Page {page + 1}"));
    }

    [RelayCommand]
    private void DeleteNote()
    {
        if (_loadedIssueId is not int issueId)
        {
            return;
        }

        int page = _currentPageIndex;
        using (var context = PaperbunkrDb.CreateContext(includeRemote: true))
        {
            var row = context.PageNotes.FirstOrDefault(n => n.IssueId == issueId && n.PageNumber == page);
            if (row is not null)
            {
                context.PageNotes.Remove(row);
                context.SaveChanges();
            }
        }

        _pageNotes.Remove(page);
        SetNoteMarker(page, false);
        NoteDraft = string.Empty;
        HasCurrentNote = false;
        RebuildNoteItems();
    }

    /// <summary>Jumps to the page of a row in the NOTES list.</summary>
    [RelayCommand]
    private void GoToNoteItem(NoteListItem? item)
    {
        if (item is not null)
        {
            GoToPage(item.PageIndex);
        }
    }

    /// <summary>Palette and page menu: open the drawer at the NOTES section and put the cursor in the note box.</summary>
    [RelayCommand]
    private void NoteOnThisPage()
    {
        IsDrawerOpen = true;
        IsNotesSectionExpanded = true;
        NoteFocusRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleNotesSection()
    {
        IsNotesSectionExpanded = !IsNotesSectionExpanded;
        if (IsNotesSectionExpanded)
        {
            RebuildNoteItems();
        }
    }

    /// <summary>Sets one page dot's note marker in place (a full thumbnail rebuild is not needed).</summary>
    private void SetNoteMarker(int page, bool hasNote)
    {
        if (hasNote)
        {
            _notedPages.Add(page);
        }
        else
        {
            _notedPages.Remove(page);
        }

        if (page < 0 || page >= Thumbnails.Count)
        {
            return;
        }

        var existing = Thumbnails[page];
        Thumbnails[page] = new ReaderThumbnailSample
        {
            CoverBrush = CoverBrush, CoverImage = existing.CoverImage, IsSelected = existing.IsSelected, IsBookmarked = existing.IsBookmarked,
            PageType = existing.PageType, IsRotated = existing.IsRotated, SpreadHint = existing.SpreadHint, HasNote = hasNote,
        };
    }
}
