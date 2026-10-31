using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using cYo.Common.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One file of a duplicate book in Library Health, with its two-step delete.</summary>
public sealed partial class BookDuplicateEntryRow : ObservableObject
{
    public BookDuplicateEntryRow(BookDuplicateEntry entry, long? sizeBytes)
    {
        Entry = entry;
        SizeLabel = sizeBytes is long bytes ? FormatSize(bytes) : "file missing";
    }

    public BookDuplicateEntry Entry { get; }

    public int BookId => Entry.BookId;

    public string FormatLabel => Entry.Format switch
    {
        BookFormat.Epub => "EPUB",
        BookFormat.Fb2 => "FB2",
        BookFormat.Mobi => "MOBI",
        _ => "PDF",
    };

    public string Path => Entry.FilePath;

    public string SizeLabel { get; }

    /// <summary>The first click on Delete only asks; the second (Yes) moves the file to the Recycle Bin.</summary>
    [ObservableProperty]
    private bool _isConfirmingDelete;

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}

public sealed class BookDuplicateGroupRow
{
    public BookDuplicateGroupRow(BookDuplicateGroup group, IEnumerable<BookDuplicateEntryRow> entries)
    {
        Label = group.Label;
        Reason = group.MixedFormats ? $"{group.Reason} · different formats" : $"{group.Reason} · same format";
        Entries = new ObservableCollection<BookDuplicateEntryRow>(entries);
    }

    public string Label { get; }

    public string Reason { get; }

    public ObservableCollection<BookDuplicateEntryRow> Entries { get; }
}

/// <summary>
/// Library Health's "Duplicate books" section (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §8): the same book held as several files,
/// with Reveal and a two-step Delete (Recycle Bin). Report-only otherwise - no "primary copy" and no dismiss list. Scanned when the section opens and on Rescan.
/// </summary>
public sealed partial class BookDuplicatesViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<Action> _defer;
    private readonly Action<string> _reveal;
    private readonly Action<string> _recycle;

    /// <param name="defer">How a row removal is postponed until the click that asked for it has finished routing (tests pass an immediate runner).</param>
    public BookDuplicatesViewModel(
        Func<PaperbunkrDbContext>? contextFactory = null,
        Action<Action>? defer = null,
        Action<string>? reveal = null,
        Action<string>? recycle = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _defer = defer ?? (action => Dispatcher.UIThread.Post(action));
        _reveal = reveal ?? new Action<string>(path => { if (File.Exists(path)) { FileExplorer.OpenFolderAndSelect(path); } });
        _recycle = recycle ?? (path => RecycleBinHelper.SendToRecycleBin(path));
    }

    public ObservableCollection<BookDuplicateGroupRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDetail), nameof(IsClean))]
    private bool _isScanned;

    public bool HasRows => Rows.Count > 0;

    public bool IsClean => IsScanned && Rows.Count == 0;

    public string HeaderDetail => !IsScanned
        ? "· Not scanned yet"
        : Rows.Count == 0 ? "· No duplicates" : $"· {Rows.Count:N0} book{(Rows.Count == 1 ? string.Empty : "s")}";

    public void EnsureScanned()
    {
        if (!IsScanned)
        {
            Scan();
        }
    }

    [RelayCommand]
    private void Scan()
    {
        using var context = _contextFactory();
        var groups = BookDuplicateFinder.Find(context.Books.AsEnumerable().ToList());

        Rows.Clear();
        foreach (var group in groups)
        {
            Rows.Add(new BookDuplicateGroupRow(group, group.Entries.Select(e => new BookDuplicateEntryRow(e, SizeOf(e.FilePath)))));
        }

        IsScanned = true;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(HeaderDetail));
    }

    private static long? SizeOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    [RelayCommand]
    private void Reveal(BookDuplicateEntryRow? entry)
    {
        if (entry is not null)
        {
            _reveal(entry.Path);
        }
    }

    /// <summary>First click: show "Delete this file?". Any other row asking is reset so only one question is open at a time.</summary>
    [RelayCommand]
    private void AskDelete(BookDuplicateEntryRow? entry)
    {
        foreach (var row in Rows.SelectMany(r => r.Entries))
        {
            row.IsConfirmingDelete = ReferenceEquals(row, entry);
        }
    }

    [RelayCommand]
    private void CancelDelete(BookDuplicateEntryRow? entry)
    {
        if (entry is not null)
        {
            entry.IsConfirmingDelete = false;
        }
    }

    /// <summary>Second click: the file goes to the Recycle Bin, the book row goes, an emptied series is pruned, and the list is rebuilt one tick later (the Yes button is still raising this click).</summary>
    [RelayCommand]
    private void ConfirmDelete(BookDuplicateEntryRow? entry)
    {
        if (entry is null)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            var book = context.Books.FirstOrDefault(b => b.Id == entry.BookId);
            if (book is not null)
            {
                int? seriesId = book.BookSeriesId;
                if (File.Exists(book.FilePath))
                {
                    _recycle(book.FilePath);
                }

                context.Books.Remove(book);
                context.SaveChanges();
                BookCoverImageCache.Invalidate(entry.BookId);
                BookSeriesMaintenance.PruneIfEmpty(context, seriesId);
            }
        }

        _defer(Scan);
    }
}
