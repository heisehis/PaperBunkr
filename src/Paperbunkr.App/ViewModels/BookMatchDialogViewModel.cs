using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Books;

namespace Paperbunkr.App.ViewModels;

/// <summary>One catalogue hit in the match dialog's left column.</summary>
public sealed class BookMatchCandidateRow
{
    public BookMatchCandidateRow(BookMatchCandidate candidate)
    {
        Candidate = candidate;
        var r = candidate.Record;
        Title = string.IsNullOrWhiteSpace(r.Subtitle) ? r.Title : $"{r.Title}: {r.Subtitle}";
        Detail = string.Join("  ·  ", new[]
        {
            r.AuthorLine,
            r.Publisher,
            r.Year?.ToString(),
            SourceName(r.Source),
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        ScoreText = candidate.Score >= 1.0 ? "ISBN match" : $"{(int)Math.Round(candidate.Score * 100)}%";
    }

    public BookMatchCandidate Candidate { get; }

    public string Title { get; }

    public string Detail { get; }

    public string ScoreText { get; }

    internal static string SourceName(BookFieldSourceKind source) => source switch
    {
        BookFieldSourceKind.OpenLibrary => "Open Library",
        BookFieldSourceKind.GoogleBooks => "Google Books",
        _ => source.ToString(),
    };
}

/// <summary>One current-vs-proposed line with the checkbox that accepts it.</summary>
public sealed partial class BookMatchDiffRow : ObservableObject
{
    public BookMatchDiffRow(BookFieldDiff diff)
    {
        Field = diff.Field;
        Label = diff.Label;
        Current = string.IsNullOrEmpty(diff.Current) ? "(empty)" : diff.Current;
        Proposed = diff.Proposed;
        _isChecked = diff.DefaultChecked;
    }

    public string Field { get; }

    public string Label { get; }

    public string Current { get; }

    public string Proposed { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// "Match metadata…" for one book (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §5): search the enabled
/// catalogues, pick a candidate, tick the fields to take (empty ones start ticked), apply. Skip forever excludes the book from
/// every bulk run.
/// </summary>
public sealed partial class BookMatchDialogViewModel : ViewModelBase
{
    private readonly Action _close;
    private readonly Action<string, string>? _notify;
    private readonly BookMetadataMatchService _service;
    private CancellationTokenSource? _searchCts;
    private int _bookId;
    private BookProviderRecord? _full;

    public BookMatchDialogViewModel(Action close, Action<string, string>? notify = null, BookMetadataMatchService? service = null)
    {
        _close = close;
        _notify = notify;
        _service = service ?? new BookMetadataMatchService();
    }

    [ObservableProperty]
    private string _headerLabel = "Match metadata";

    [ObservableProperty]
    private string _searchTitle = string.Empty;

    [ObservableProperty]
    private string _searchAuthor = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<BookMatchCandidateRow> Candidates { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private BookMatchCandidateRow? _selectedCandidate;

    public bool HasSelection => SelectedCandidate is not null;

    public ObservableCollection<BookMatchDiffRow> DiffRows { get; } = new();

    [ObservableProperty]
    private bool _applyCover;

    [ObservableProperty]
    private bool _hasCover;

    [ObservableProperty]
    private bool _isSkipped;

    public string SkipLabel => IsSkipped ? "Include in bulk matching again" : "Never match this book";

    partial void OnIsSkippedChanged(bool value) => OnPropertyChanged(nameof(SkipLabel));

    /// <summary>Opens the dialog on a book and searches straight away.</summary>
    public async Task LoadAsync(int bookId, string title, string? author, bool skipped)
    {
        _bookId = bookId;
        HeaderLabel = $"Match “{title}”";
        SearchTitle = title;
        SearchAuthor = author ?? string.Empty;
        IsSkipped = skipped;
        Reset();
        await SearchAsync().ConfigureAwait(true);
    }

    private void Reset()
    {
        Candidates.Clear();
        DiffRows.Clear();
        SelectedCandidate = null;
        _full = null;
        ApplyCover = false;
        HasCover = false;
        Message = string.Empty;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        Reset();
        IsBusy = true;
        try
        {
            var found = await _service.SearchAsync(_bookId, SearchTitle, SearchAuthor, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var c in found)
            {
                Candidates.Add(new BookMatchCandidateRow(c));
            }

            Message = Candidates.Count == 0 ? "No matches. Try a shorter title or a different author." : string.Empty;
        }
        catch (BookProviderException ex)
        {
            Message = ex.IsRateLimited ? ex.Message : $"{ex.Message} Check your connection and try again.";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task SelectCandidateAsync(BookMatchCandidateRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedCandidate = row;
        DiffRows.Clear();
        IsBusy = true;
        try
        {
            _full = await _service.GetFullAsync(row.Candidate.Record, CancellationToken.None).ConfigureAwait(true);
            foreach (var diff in _service.BuildDiff(_bookId, _full))
            {
                DiffRows.Add(new BookMatchDiffRow(diff));
            }

            HasCover = !string.IsNullOrWhiteSpace(_full.CoverUrl);
            ApplyCover = false;
            Message = DiffRows.Count == 0 ? "This record has nothing new for the book." : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_full is null)
        {
            return;
        }

        var fields = DiffRows.Where(r => r.IsChecked).Select(r => r.Field).ToHashSet();
        if (fields.Count == 0 && !ApplyCover)
        {
            _notify?.Invoke("Nothing selected", "Tick at least one field to take.");
            return;
        }

        IsBusy = true;
        try
        {
            await _service.ApplyAsync(_bookId, _full, fields, ApplyCover, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        _close();
    }

    [RelayCommand]
    private void ToggleSkip()
    {
        IsSkipped = !IsSkipped;
        _service.SetSkipForever(_bookId, IsSkipped);
        _notify?.Invoke(IsSkipped ? "Won't be matched" : "Will be matched again", IsSkipped ? "Bulk matching will leave this book alone." : "Bulk matching can use this book again.");
    }

    [RelayCommand]
    private void Cancel()
    {
        _searchCts?.Cancel();
        _close();
    }
}
