using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Calibre;

namespace Paperbunkr.App.ViewModels;

/// <summary>One "looks like a book you already have" row of the Calibre import result.</summary>
public sealed partial class CalibreReviewRow : ObservableObject
{
    public CalibreReviewRow(CalibreReviewItem item) => Item = item;

    public CalibreReviewItem Item { get; }

    public string Title => $"{Item.Title}" + (string.IsNullOrWhiteSpace(Item.Author) ? string.Empty : $" · {Item.Author}");

    public string Detail => $"{Item.Reason}: your book \"{Item.ExistingTitle}\"";

    [ObservableProperty]
    private bool _isResolved;

    [ObservableProperty]
    private string _resolution = string.Empty;
}

/// <summary>
/// "Import from Calibre…" (docs/superpowers/specs/2026-10-27-books-slice4a-calibre-import-design.md §5): pick a Calibre library folder, optionally a
/// read-status column, import, then settle the books that look like ones already in the library.
/// </summary>
public sealed partial class CalibreImportDialogViewModel : ViewModelBase
{
    private const string NoColumn = "None";

    private readonly Action _close;
    private readonly Action<ToastRequest>? _toast;
    private readonly IFilePickerService _picker;
    private readonly CalibreImporter _importer;

    public CalibreImportDialogViewModel(Action close, Action<ToastRequest>? toast = null, IFilePickerService? picker = null, CalibreImporter? importer = null)
    {
        _close = close;
        _toast = toast;
        _picker = picker ?? new FilePickerService();
        _importer = importer ?? new CalibreImporter(PaperbunkrDb.CreateContext, (id, path) => new BookCoverThumbnailService().TrySetCustomCover(id, path));
        StatusColumns.Add(NoColumn);
        SelectedStatusColumn = NoColumn;
    }

    public ObservableCollection<string> StatusColumns { get; } = new();

    public ObservableCollection<CalibreReviewRow> Review { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private string _libraryFolder = string.Empty;

    [ObservableProperty]
    private string _selectedStatusColumn = NoColumn;

    [ObservableProperty]
    private bool _refreshExisting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _isImporting;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public bool HasReview => Review.Count > 0;

    public bool CanImport => !IsImporting && CalibreLibraryReader.IsCalibreLibrary(LibraryFolder);

    /// <summary>Pre-fills the folder with Calibre's default location when it exists.</summary>
    public void Prepare()
    {
        HasResult = false;
        Review.Clear();
        OnPropertyChanged(nameof(HasReview));
        Message = string.Empty;
        if (string.IsNullOrWhiteSpace(LibraryFolder))
        {
            string guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Calibre Library");
            if (CalibreLibraryReader.IsCalibreLibrary(guess))
            {
                LibraryFolder = guess;
            }
        }
    }

    partial void OnLibraryFolderChanged(string value)
    {
        StatusColumns.Clear();
        StatusColumns.Add(NoColumn);
        SelectedStatusColumn = NoColumn;
        Message = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!CalibreLibraryReader.IsCalibreLibrary(value))
        {
            Message = "That folder has no metadata.db, so it does not look like a Calibre library.";
            return;
        }

        try
        {
            using var reader = CalibreLibraryReader.Open(value);
            foreach (var column in reader.CustomColumns().Where(c => c.DataType is "bool" or "text" or "enumeration"))
            {
                StatusColumns.Add(column.Label);
            }
        }
        catch (Exception ex)
        {
            Message = $"Could not read the Calibre library: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _picker.PickFolderAsync("Choose your Calibre library folder");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            LibraryFolder = folder;
        }
    }

    private CalibreImportOptions Options() =>
        new(SelectedStatusColumn == NoColumn ? null : SelectedStatusColumn, RefreshExisting);

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanImport)
        {
            return;
        }

        IsImporting = true;
        HasResult = false;
        Review.Clear();
        OnPropertyChanged(nameof(HasReview));
        Message = string.Empty;
        ProgressFraction = 0;
        string folder = LibraryFolder;
        var options = Options();
        var progress = new Progress<(int Done, int Total)>(p => ProgressFraction = p.Total == 0 ? 0 : (double)p.Done / p.Total);
        try
        {
            var result = await Task.Run(() => _importer.Import(folder, options, progress));
            ResultSummary = Summarize(result);
            foreach (var item in result.Review)
            {
                Review.Add(new CalibreReviewRow(item));
            }

            OnPropertyChanged(nameof(HasReview));
            HasResult = true;
            _toast?.Invoke(new ToastRequest("Calibre import", $"{result.Added} added, {result.Updated} updated", ToastSeverity.Success));
        }
        catch (Exception ex)
        {
            Message = $"The import stopped: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    internal static string Summarize(CalibreImportResult result)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            $"{result.Added} added",
            $"{result.Updated} updated",
        };
        if (result.NeedsReview > 0) parts.Add($"{result.NeedsReview} to review");
        if (result.FileNotFound > 0) parts.Add($"{result.FileNotFound} file not found");
        if (result.Skipped > 0) parts.Add($"{result.Skipped} skipped (no supported format)");
        if (result.NoLongerInCalibre > 0) parts.Add($"{result.NoLongerInCalibre} no longer in Calibre");
        return string.Join(" · ", parts);
    }

    [RelayCommand]
    private async Task LinkAsync(CalibreReviewRow? row)
    {
        if (row is null || row.IsResolved)
        {
            return;
        }

        string folder = LibraryFolder;
        var options = Options();
        await Task.Run(() => _importer.Link(folder, row.Item, options));
        row.IsResolved = true;
        row.Resolution = "Linked";
    }

    [RelayCommand]
    private void Skip(CalibreReviewRow? row)
    {
        if (row is null || row.IsResolved)
        {
            return;
        }

        row.IsResolved = true;
        row.Resolution = "Skipped";
    }

    [RelayCommand]
    private void Close() => _close();
}
