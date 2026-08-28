using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Bulk multi-book properties editor (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-
/// design.md, component b) - a modal overlay, same shell + buffered discipline as
/// <see cref="BookPropertiesScreenViewModel"/>. Deliberately does NOT reuse the comic
/// <see cref="BulkIssuePropertiesScreenViewModel"/>'s registry/list-field/template machinery: the
/// book field set is six plain scalars/strings with no list fields, templates, or file write-back,
/// so hand-written per-field "apply to all" toggles are simpler and clearer.
///
/// Only ticked ("staged") fields are written. Editing a field auto-ticks it. Undo/redo records one
/// <see cref="MetadataEditTarget.Book"/> entry spanning every selected book id; series-row edits and
/// empty-series pruning stay outside that entry, same carve-out as
/// <see cref="BookPropertiesScreenViewModel"/>.
/// </summary>
public partial class BulkBookPropertiesScreenViewModel : ViewModelBase
{
    private const string MultipleWatermark = "— multiple —";

    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<string, string>? _notify;
    private readonly MetadataEditHistoryService _history;

    private List<int> _bookIds = new();
    private Dictionary<int, Dictionary<string, string?>> _beforeSnapshots = new();

    public BulkBookPropertiesScreenViewModel(Action goBack, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
        : this(goBack, PaperbunkrDb.CreateContext, notify, history)
    {
    }

    internal BulkBookPropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
        _notify = notify;
        _history = history ?? MetadataEditHistoryService.Shared;
    }

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    // Each editable field: a value + an "apply to all selected" toggle. A value edit auto-ticks.

    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private bool _applyAuthor;
    partial void OnAuthorChanged(string value) => ApplyAuthor = true;

    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _applySummary;
    partial void OnSummaryChanged(string value) => ApplySummary = true;

    [ObservableProperty] private DateTimeOffset? _publishedDate;
    [ObservableProperty] private bool _applyPublishedDate;
    partial void OnPublishedDateChanged(DateTimeOffset? value) => ApplyPublishedDate = true;

    [ObservableProperty] private string _seriesName = string.Empty;
    [ObservableProperty] private bool _applySeries;
    partial void OnSeriesNameChanged(string value)
    {
        ApplySeries = true;
        OnPropertyChanged(nameof(HasSeriesName));
    }

    public bool HasSeriesName => !string.IsNullOrWhiteSpace(SeriesName);

    [ObservableProperty] private string _seriesAuthor = string.Empty;
    partial void OnSeriesAuthorChanged(string value) => ApplySeries = true;

    [ObservableProperty] private string _seriesSortName = string.Empty;
    partial void OnSeriesSortNameChanged(string value) => ApplySeries = true;

    public string AuthorWatermark { get; private set; } = string.Empty;
    public string SummaryWatermark { get; private set; } = string.Empty;
    public string SeriesNameWatermark { get; private set; } = string.Empty;

    public void Load(IReadOnlyList<int> bookIds)
    {
        _bookIds = bookIds.ToList();

        using var context = _contextFactory();
        var books = context.Books.Include(b => b.BookSeries).Where(b => _bookIds.Contains(b.Id)).ToList();
        if (books.Count == 0)
        {
            _goBack();
            return;
        }

        HeaderLabel = books.Count == 1 ? "Editing 1 book" : $"Editing {books.Count} books";
        _beforeSnapshots = books.ToDictionary(b => b.Id, BookMetadataSnapshot.Capture);

        (Author, AuthorWatermark) = AgreedOrBlank(books.Select(b => b.Author));
        (Summary, SummaryWatermark) = AgreedOrBlank(books.Select(b => b.Summary));
        (SeriesName, SeriesNameWatermark) = AgreedOrBlank(books.Select(b => b.BookSeries?.Name));

        var dates = books.Select(b => b.PublishedDate).Distinct().ToList();
        PublishedDate = dates.Count == 1 && dates[0] is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null;

        var seriesAuthors = books.Select(b => b.BookSeries?.Author).Distinct().ToList();
        SeriesAuthor = seriesAuthors.Count == 1 ? seriesAuthors[0] ?? string.Empty : string.Empty;
        var seriesSorts = books.Select(b => b.BookSeries?.SortName).Distinct().ToList();
        SeriesSortName = seriesSorts.Count == 1 ? seriesSorts[0] ?? string.Empty : string.Empty;

        ApplyAuthor = ApplySummary = ApplyPublishedDate = ApplySeries = false;
        OnPropertyChanged(nameof(HasSeriesName));
        OnPropertyChanged(nameof(AuthorWatermark));
        OnPropertyChanged(nameof(SummaryWatermark));
        OnPropertyChanged(nameof(SeriesNameWatermark));
    }

    private static (string Value, string Watermark) AgreedOrBlank(IEnumerable<string?> values)
    {
        var distinct = values.Select(v => v ?? string.Empty).Distinct().ToList();
        return distinct.Count == 1 ? (distinct[0], string.Empty) : (string.Empty, MultipleWatermark);
    }

    public bool HasUnsavedChanges() => ApplyAuthor || ApplySummary || ApplyPublishedDate || ApplySeries;

    [RelayCommand]
    private void Save()
    {
        if (!HasUnsavedChanges())
        {
            _goBack();
            return;
        }

        using var context = _contextFactory();
        var books = context.Books.Include(b => b.BookSeries).Where(b => _bookIds.Contains(b.Id)).ToList();
        if (books.Count == 0)
        {
            _goBack();
            return;
        }

        var previousSeriesIds = books.Select(b => b.BookSeriesId).Where(id => id is not null).Distinct().ToList();

        foreach (var book in books)
        {
            if (ApplyAuthor) book.Author = NullIfEmpty(Author);
            if (ApplySummary) book.Summary = NullIfEmpty(Summary);
            if (ApplyPublishedDate) book.PublishedDate = PublishedDate?.UtcDateTime;
        }

        int? resolvedSeriesId = null;
        if (ApplySeries)
        {
            resolvedSeriesId = ResolveSeries(context, books);
        }

        context.SaveChanges();

        _history.RecordBookEdits(
            books.Count == 1 ? "Edited 1 book" : $"Edited {books.Count} books",
            _beforeSnapshots,
            books.ToDictionary(b => b.Id, BookMetadataSnapshot.Capture));

        foreach (var seriesId in previousSeriesIds.Where(id => id != resolvedSeriesId))
        {
            BookSeriesMaintenance.PruneIfEmpty(context, seriesId);
        }

        _goBack();
    }

    /// <summary>Resolve the staged series name once and attach every selected book to it (blank =
    /// detach all). Series author/sort name, if entered, write onto the resolved row. Mirrors
    /// <see cref="BookPropertiesScreenViewModel"/>'s single-book <c>ResolveSeries</c>.</summary>
    private int? ResolveSeries(PaperbunkrDbContext context, List<Book> books)
    {
        string name = SeriesName.Trim();
        if (name.Length == 0)
        {
            foreach (var book in books)
            {
                book.BookSeriesId = null;
                book.BookSeries = null;
            }

            return null;
        }

        var series = context.BookSeries.ToList()
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (series is null)
        {
            series = new BookSeries { Name = name };
            context.BookSeries.Add(series);
        }

        if (!string.IsNullOrWhiteSpace(SeriesAuthor)) series.Author = NullIfEmpty(SeriesAuthor);
        if (!string.IsNullOrWhiteSpace(SeriesSortName)) series.SortName = NullIfEmpty(SeriesSortName);

        foreach (var book in books)
        {
            book.BookSeries = series;
        }

        // Id is 0 until SaveChanges for a brand-new row; the pruning caller only compares against
        // non-null previous ids, so a transient 0 here is harmless (it never matches a real id).
        return series.Id == 0 ? null : series.Id;
    }

    [RelayCommand]
    private void Cancel() => _goBack();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
