using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.ViewModels;

/// <summary>One row in the reader command palette's result list.</summary>
public sealed partial class ReaderPaletteRow : ObservableObject
{
    private readonly Action<ReaderPaletteRow>? _execute;

    public ReaderPaletteRow(ReaderPaletteEntry entry, Action<ReaderPaletteRow>? execute = null)
    {
        Entry = entry;
        _execute = execute;
    }

    /// <summary>Runs this row (a click); the palette closes first.</summary>
    [RelayCommand]
    private void Execute() => _execute?.Invoke(this);

    public ReaderPaletteEntry Entry { get; }

    public string Title => Entry.Title;

    public string Group => Entry.Group;

    public string? Shortcut => Entry.Shortcut;

    public bool HasShortcut => !string.IsNullOrEmpty(Entry.Shortcut);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The reader's Ctrl+K command palette and Ctrl+G go-to-page prompt (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 5). A small state machine
/// owned by <see cref="ReaderScreenViewModel"/>: the reader supplies the entries and the page bounds, the overlay in <c>ReaderScreen.axaml</c> binds to this, and the key
/// handling (Up/Down/Enter/Esc) lives in the screen's code-behind like the bad-page picker's. Choosing an entry closes the palette first and runs the entry one dispatcher tick
/// later, so the reader is back in front (with focus on the canvas) before the command acts.
/// </summary>
public sealed partial class ReaderCommandPaletteViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<ReaderPaletteEntry>> _entries;
    private readonly Func<int> _pageCount;
    private readonly Action<int> _goToPage;
    private readonly Action<Action> _post;
    private IReadOnlyList<ReaderPaletteEntry> _snapshot = [];

    /// <param name="entries">Builds the catalog each time the palette opens (shortcuts and availability can change between openings).</param>
    /// <param name="pageCount">Pages in the loaded issue (0 when none).</param>
    /// <param name="goToPage">Jumps to a zero-based page index through the reader's jump path, so a big jump shows the "Back to page N" chip.</param>
    /// <param name="post">Test seam for the one-tick deferral (default: the UI dispatcher).</param>
    public ReaderCommandPaletteViewModel(Func<IReadOnlyList<ReaderPaletteEntry>> entries, Func<int> pageCount, Action<int> goToPage, Action<Action>? post = null)
    {
        _entries = entries;
        _pageCount = pageCount;
        _goToPage = goToPage;
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
    }

    public ObservableCollection<ReaderPaletteRow> Results { get; } = new();

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>True when opened by Ctrl+G: only the go-to-page row is offered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    private bool _isGoToPageMode;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public string Placeholder => IsGoToPageMode
        ? _pageCount() > 0 ? $"Page number (1-{_pageCount()})" : "Page number"
        : "Type a command, or a page number…";

    public bool HasResults => Results.Count > 0;

    /// <summary>Opens the palette listing every command (the catalog order until something is typed).</summary>
    public void Open() => Show(goToPage: false);

    /// <summary>Opens the palette as a go-to-page prompt (Ctrl+G).</summary>
    public void OpenGoToPage() => Show(goToPage: true);

    /// <summary>Ctrl+K while it is already open closes it.</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Close()
    {
        IsOpen = false;
    }

    private void Show(bool goToPage)
    {
        _snapshot = goToPage ? [] : _entries();
        IsGoToPageMode = goToPage;
        Query = string.Empty;
        Refresh();
        IsOpen = true;
    }

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnSelectedIndexChanged(int value)
    {
        for (int i = 0; i < Results.Count; i++)
        {
            Results[i].IsSelected = i == value;
        }
    }

    private void Refresh()
    {
        var rows = new List<ReaderPaletteEntry>();

        // A page number always gets its own row first, in either mode ("Go to page 40 of 120").
        if (ReaderPaletteCatalog.TryParsePageNumber(Query, out int page) && _pageCount() is int count and > 0)
        {
            int clamped = Math.Clamp(page, 1, count);
            rows.Add(new ReaderPaletteEntry($"Go to page {clamped} of {count}", "Navigate", null, () => _goToPage(clamped - 1)));
        }

        if (!IsGoToPageMode)
        {
            rows.AddRange(ReaderPaletteCatalog.Rank(_snapshot, Query));
        }

        Results.Clear();
        foreach (var entry in rows)
        {
            Results.Add(new ReaderPaletteRow(entry, Execute));
        }

        OnPropertyChanged(nameof(HasResults));
        SelectedIndex = Results.Count > 0 ? 0 : -1;
        OnSelectedIndexChanged(SelectedIndex);
    }

    /// <summary>Moves the highlight by <paramref name="delta"/> rows, wrapping at both ends.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = (SelectedIndex + delta + Results.Count) % Results.Count;
    }

    /// <summary>Runs the highlighted row (Enter).</summary>
    [RelayCommand]
    private void ExecuteSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            Execute(Results[SelectedIndex]);
        }
    }

    /// <summary>Runs <paramref name="row"/>: closes the palette, then runs the entry one dispatcher tick later.</summary>
    internal void Execute(ReaderPaletteRow? row)
    {
        if (row is null)
        {
            return;
        }

        Close();
        var run = row.Entry.Run;
        _post(run);
    }
}
