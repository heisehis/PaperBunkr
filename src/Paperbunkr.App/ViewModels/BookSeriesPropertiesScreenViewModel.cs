using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// BookSeries properties overlay (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-
/// design.md, component c) - edits a <see cref="BookSeries"/> row: Name (renamed in place - the
/// deliberate opposite of <see cref="BookPropertiesScreenViewModel"/>'s "the name box never renames"
/// rule, because this *is* the series editor), Sort name, Author. Buffered Save/Cancel like the
/// other book overlays.
///
/// Not undoable: series-row edits sit outside the per-book <see cref="MetadataEditHistoryService"/>
/// model, same call the Book Properties editor made for series Author/SortName. Renaming to collide
/// with a different existing series is blocked (merging series is out of scope).
/// </summary>
public partial class BookSeriesPropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<string, string>? _notify;

    private int _seriesId;
    private string _loadSignature = string.Empty;

    public BookSeriesPropertiesScreenViewModel(Action goBack, Action<string, string>? notify = null)
        : this(goBack, PaperbunkrDb.CreateContext, notify)
    {
    }

    internal BookSeriesPropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<string, string>? notify = null)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
        _notify = notify;
    }

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sortName = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    public void Load(int bookSeriesId)
    {
        _seriesId = bookSeriesId;

        using var context = _contextFactory();
        var series = context.BookSeries.FirstOrDefault(s => s.Id == bookSeriesId);
        if (series is null)
        {
            _goBack();
            return;
        }

        HeaderLabel = $"Edit series “{series.Name}”";
        Name = series.Name;
        SortName = series.SortName ?? string.Empty;
        Author = series.Author ?? string.Empty;
        _loadSignature = Signature();
    }

    public bool HasUnsavedChanges() => Signature() != _loadSignature;

    private string Signature() => string.Join('␟', Name, SortName, Author);

    [RelayCommand]
    private void Save()
    {
        string name = Name.Trim();
        if (name.Length == 0)
        {
            _notify?.Invoke("Can't save", "Series name can't be empty.");
            return;
        }

        using var context = _contextFactory();
        var series = context.BookSeries.FirstOrDefault(s => s.Id == _seriesId);
        if (series is null)
        {
            _goBack();
            return;
        }

        bool collides = context.BookSeries.ToList()
            .Any(s => s.Id != _seriesId && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (collides)
        {
            _notify?.Invoke("Can't rename", $"A series called “{name}” already exists.");
            return;
        }

        series.Name = name;
        series.SortName = NullIfEmpty(SortName);
        series.Author = NullIfEmpty(Author);
        context.SaveChanges();

        _goBack();
    }

    [RelayCommand]
    private void Cancel() => _goBack();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
