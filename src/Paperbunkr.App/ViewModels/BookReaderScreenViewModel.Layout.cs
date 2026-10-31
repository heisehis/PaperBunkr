using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reading units, paged/scroll layout and book pages (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md). A unit is one
/// contents entry's slice of a file; <see cref="_position"/> still stores the file index and a block id, so every saved position, bookmark and
/// highlight keeps working, and the unit is derived from it.
/// </summary>
public partial class BookReaderScreenViewModel
{
    /// <summary>Passed to <see cref="PositionRestoreRequested"/> to land on the last page of the unit (going back into the previous chapter).</summary>
    public const string LastPageAnchor = "pb:last";

    private BookUnitSet _units = new(Array.Empty<BookUnit>(), Array.Empty<int>());
    private string?[] _injectedFiles = Array.Empty<string?>();
    private int _unitIndex;
    private BookPageMap? _pageMap;
    private IReadOnlyList<(int Unit, int Offset, string Label, int File, string? Fragment)> _printMarks = Array.Empty<(int, int, string, int, string?)>();
    private int _pageMapGeneration;
    private bool _seedingLayout;

    /// <summary>Completes when the book page map for the open book is ready (tests wait on it, then run the dispatcher).</summary>
    public Task PageMapReady { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaged))]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private BookReaderLayout _layout = BookReaderLayout.Paged;

    public bool IsPaged => Layout == BookReaderLayout.Paged;

    /// <summary>The screen page within the unit (0-based) and how many there are, reported by the page in paged mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private int _screenPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private int _screenPageCount;

    [ObservableProperty]
    private string _goToPageText = string.Empty;

    [ObservableProperty]
    private string _goToPageError = string.Empty;

    /// <summary>"of 640" beside the Go to page field; empty until the page map is ready.</summary>
    public string GoToPageTotalText => TotalPagesLabel is { } total ? $"of {total}" : string.Empty;

    /// <summary>The unit on screen (index into every unit of the book).</summary>
    public int CurrentUnitIndex => _unitIndex;

    public int UnitCount => _units.Units.Count;

    /// <summary>The block at the top of the screen (or the element a jump landed on), so a layout switch can come back to it.</summary>
    public string? CurrentBlockId => _position.BlockId;

    private void SeedLayout(AppSettings appSettings)
    {
        _seedingLayout = true;
        Layout = appSettings.BookReaderLayout;
        _seedingLayout = false;
    }

    partial void OnLayoutChanged(BookReaderLayout value)
    {
        if (value == BookReaderLayout.Paged)
        {
            IsAutoScrolling = false; // auto-scroll is a scroll-mode aid (spec §5.5)
        }

        ScreenPage = 0;
        ScreenPageCount = 0;
        if (!_seedingLayout)
        {
            SaveAidSetting(a => a.BookReaderLayout = value);
        }
    }

    [RelayCommand]
    private void SetLayout(BookReaderLayout layout) => Layout = layout;

    /// <summary>The page reports which screen page of the unit is showing (paged mode).</summary>
    public void OnScreenPage(int page, int count)
    {
        ScreenPageCount = Math.Max(0, count);
        ScreenPage = Math.Clamp(page, 0, Math.Max(0, count - 1));
    }

    /// <summary>Cuts every file into units and prepares the print page marks; called once per book, right after the source opens.</summary>
    private void BuildUnits(IBookTextSource source)
    {
        _injectedFiles = source.Chapters.Select(c => c.Html is null ? null : BlockIdInjector.Inject(c.Html)).ToArray();
        _units = BookUnitSlicer.Build(_tocEntries, _injectedFiles);
        _unitIndex = 0;

        var marks = new List<(int, int, string, int, string?)>();
        foreach (var page in source.PrintPages)
        {
            if (page.ChapterIndex < 0 || page.ChapterIndex >= _injectedFiles.Length)
            {
                continue;
            }

            string? html = _injectedFiles[page.ChapterIndex];
            int offset = page.Fragment is null || html is null ? 0 : EpubBookSource.OffsetOf(html, page.Fragment);
            if (offset >= 0)
            {
                marks.Add((_units.UnitOf(page.ChapterIndex, html, page.Fragment), offset, page.Label, page.ChapterIndex, page.Fragment));
            }
        }

        _printMarks = marks.OrderBy(m => m.Item1).ThenBy(m => m.Item2).ToList();
    }

    /// <summary>Builds the word-count page map off the UI thread; the footer shows unit and screen page until it lands.</summary>
    private void StartPageMap(int minWords, int maxWords)
    {
        _pageMap = null;
        int generation = ++_pageMapGeneration;
        var units = _units.Units;
        var files = _injectedFiles;
        PageMapReady = Task.Run(() =>
        {
            var blocks = units.Select(u => BookBlockReader.Read(u.Slice(files[u.FileIndex]))).ToList();
            return BookPageMap.Build(blocks, minWords, maxWords);
        }).ContinueWith(task =>
        {
            if (task.Status != TaskStatus.RanToCompletion)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _pageMapGeneration)
                {
                    return;
                }

                _pageMap = task.Result;
                RefreshFooter();
            });
        }, TaskScheduler.Default);
    }

    /// <summary>The unit a (file, block) position is in.</summary>
    private int UnitOfPosition(BookPosition position) =>
        position.ChapterIndex >= 0 && position.ChapterIndex < _injectedFiles.Length
            ? _units.UnitOf(position.ChapterIndex, _injectedFiles[position.ChapterIndex], position.BlockId)
            : 0;

    /// <summary>The current unit's markup, cut out of its file.</summary>
    private string? CurrentUnitHtml() =>
        _unitIndex < _units.Units.Count ? _units.Units[_unitIndex].Slice(_injectedFiles[_units.Units[_unitIndex].FileIndex]) : null;

    /// <summary>Where a unit opens, as a position (its file and the element it starts with).</summary>
    private BookPosition StartOf(int unitIndex) => new(_units.Units[unitIndex].FileIndex, _units.Units[unitIndex].Fragment);

    private bool HasPrintPages => _printMarks.Count > 0;

    /// <summary>Book page number for the top of the screen: the print label when the file has a page list, else the word-count page (1-based).</summary>
    private string? CurrentPageLabel
    {
        get
        {
            if (HasPrintPages)
            {
                string? html = _position.ChapterIndex < _injectedFiles.Length ? _injectedFiles[_position.ChapterIndex] : null;
                int offset = html is not null && !string.IsNullOrEmpty(_position.BlockId) ? EpubBookSource.OffsetOf(html, _position.BlockId) : -1;
                if (offset < 0 && _unitIndex < _units.Units.Count)
                {
                    offset = _units.Units[_unitIndex].Start;
                }

                string? label = null;
                foreach (var mark in _printMarks)
                {
                    if (mark.Unit < _unitIndex || (mark.Unit == _unitIndex && mark.Offset <= offset))
                    {
                        label = mark.Label;
                    }
                    else
                    {
                        break;
                    }
                }

                return label ?? _printMarks[0].Label;
            }

            int page = _pageMap?.PageOf(_unitIndex, _position.BlockId) ?? -1;
            return page >= 0 ? (page + 1).ToString(CultureInfo.InvariantCulture) : null;
        }
    }

    private string? TotalPagesLabel =>
        HasPrintPages ? _printMarks[^1].Label : _pageMap is { Count: > 0 } map ? map.Count.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>Bottom-bar label (spec §6.3): "p. 214 of 640 · Nine · 3 / 12" paged, "p. 214 of 640 · Nine · 37%" scroll.</summary>
    public string ProgressLabel
    {
        get
        {
            string? page = CurrentPageLabel is { } current && TotalPagesLabel is { } total ? $"p. {current} of {total}" : null;
            string? where = IsPaged
                ? ScreenPageCount > 0 ? $"{ScreenPage + 1} / {ScreenPageCount}" : null
                : $"{ProgressPercent:F0}%";
            return string.Join(" · ", new[] { page, ChapterTitle, where }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    /// <summary>The thin progress bar: book page over total once the page map is ready, else the old within-unit fraction.</summary>
    public double ProgressFraction
    {
        get
        {
            if (_pageMap is { Count: > 0 } map)
            {
                int page = map.PageOf(_unitIndex, _position.BlockId);
                return page < 0 ? 0 : (double)(page + 1) / map.Count;
            }

            return ProgressPercent / 100.0;
        }
    }

    private void RefreshFooter()
    {
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(GoToPageTotalText));
        OnPropertyChanged(nameof(ClusterLeftLabel));
        OnPropertyChanged(nameof(ClusterRightLabel));
        OnPropertyChanged(nameof(FolioLabel));
        OnPropertyChanged(nameof(PartTicks));
    }

    // --- books redesign §5.1-5.2: the page cluster and the folio ---

    /// <summary>Left of the page cluster's progress line: "p. 214", or "37%" before the page map is ready.</summary>
    public string ClusterLeftLabel => CurrentPageLabel is { } page ? $"p. {page}" : $"{ProgressPercent:F0}%";

    /// <summary>Right of the progress line: "of 640", plus the screen page of this unit in paged mode.</summary>
    public string ClusterRightLabel
    {
        get
        {
            string? total = TotalPagesLabel is { } t ? $"of {t}" : null;
            string? screen = IsPaged && ScreenPageCount > 0 ? $"{ScreenPage + 1} / {ScreenPageCount}" : null;
            return string.Join(" · ", new[] { total, screen }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    /// <summary>The faint page number at the foot of the paper.</summary>
    public string? FolioLabel => CurrentPageLabel;

    /// <summary>The file the reader is in (highlights store it), for the margin notes.</summary>
    public int CurrentFileIndex => _position.ChapterIndex;

    /// <summary>Where each part begins, as a fraction of the book's files, for the page cluster's ticks.</summary>
    public IReadOnlyList<double> PartTicks
    {
        get
        {
            int files = _source?.Chapters.Count ?? 0;
            if (files <= 1)
            {
                return Array.Empty<double>();
            }

            return TocGroups.Where(g => !string.IsNullOrWhiteSpace(g.PartTitle) && g.Chapters.Count > 0)
                .Select(g => (double)g.Chapters[0].Index / files)
                .Where(f => f > 0 && f < 1)
                .ToArray();
        }
    }

    /// <summary>Go to page (spec §6.5): a print label when the book has a page list, else a word-count page number.</summary>
    [RelayCommand]
    private void GoToPage()
    {
        string text = GoToPageText.Trim();
        GoToPageError = string.Empty;
        if (_source is null || text.Length == 0)
        {
            return;
        }

        BookPosition? target = null;
        if (HasPrintPages)
        {
            foreach (var mark in _printMarks)
            {
                if (string.Equals(mark.Label, text, StringComparison.OrdinalIgnoreCase))
                {
                    target = new BookPosition(mark.File, mark.Fragment);
                    break;
                }
            }
        }
        else if (_pageMap is { } map && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= map.Count)
        {
            var start = map.Pages[number - 1];
            target = string.IsNullOrEmpty(start.BlockId) ? StartOf(start.UnitIndex) : new BookPosition(_units.Units[start.UnitIndex].FileIndex, start.BlockId);
        }

        if (target is not { } position)
        {
            GoToPageError = $"No page {text}";
            return;
        }

        _history.Push(_position);
        _position = position;
        NoteJump(_history.Peek(), _position);
        // Deferred: this runs from the field's Enter inside the contents drawer (routed-event rule, CLAUDE.md).
        Dispatcher.UIThread.Post(() => IsTocOpen = false);
        RecomputeCurrentPage();
        PersistPosition();
        PositionRestoreRequested?.Invoke(position.BlockId);
    }
}
