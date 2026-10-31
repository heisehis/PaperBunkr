using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The reader's Notes panel (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.3): one floating panel for highlights and
/// bookmarks, with the same All / Highlights / Bookmarks chips as Book Detail's Notes tab, and the only Export in the reader. The old
/// Bookmarks and Highlights drawer flags stay (input actions and tests use them); either one opens this panel with its own filter.
/// </summary>
public partial class BookReaderScreenViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotesAll), nameof(IsNotesHighlights), nameof(IsNotesBookmarks), nameof(ShowNoteHighlights), nameof(ShowNoteBookmarks))]
    private BookNoteKindFilter _notesFilter = BookNoteKindFilter.All;

    /// <summary>The panel is open when either of the old drawers would have been.</summary>
    public bool IsNotesOpen
    {
        get => IsHighlightsOpen || IsBookmarksOpen;
        set
        {
            if (!value)
            {
                IsHighlightsOpen = false;
                IsBookmarksOpen = false;
            }
        }
    }

    public bool IsNotesAll => NotesFilter == BookNoteKindFilter.All;

    public bool IsNotesHighlights => NotesFilter == BookNoteKindFilter.Highlights;

    public bool IsNotesBookmarks => NotesFilter == BookNoteKindFilter.Bookmarks;

    public bool ShowNoteHighlights => NotesFilter is BookNoteKindFilter.All or BookNoteKindFilter.Highlights;

    public bool ShowNoteBookmarks => NotesFilter is BookNoteKindFilter.All or BookNoteKindFilter.Bookmarks;

    partial void OnIsHighlightsOpenChanged(bool value) => OnPropertyChanged(nameof(IsNotesOpen));

    partial void OnIsBookmarksOpenChanged(bool value)
    {
        if (value)
        {
            NotesFilter = BookNoteKindFilter.Bookmarks;
        }

        OnPropertyChanged(nameof(IsNotesOpen));
    }

    /// <summary>The Actions cluster's Notes button: everything, highlights first.</summary>
    [RelayCommand]
    private void OpenNotes()
    {
        NotesFilter = BookNoteKindFilter.All;
        OpenHighlightsCommand.Execute(null);
    }

    [RelayCommand]
    private void SetNotesFilter(BookNoteKindFilter filter) => NotesFilter = filter;
}
