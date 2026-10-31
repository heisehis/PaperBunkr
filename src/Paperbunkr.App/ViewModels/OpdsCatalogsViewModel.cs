using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Opds;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One entry of the catalog being browsed: a folder to open or a book to download.</summary>
public sealed partial class OpdsEntryRow : ObservableObject
{
    public OpdsEntryRow(OpdsEntry entry)
    {
        Entry = entry;
        FormatLabel = OpdsClient.ChooseAcquisition(entry.Acquisitions) is { } link
            ? (OpdsClient.ExtensionFor(link) ?? string.Empty).TrimStart('.').ToUpperInvariant()
            : string.Empty;
    }

    public OpdsEntry Entry { get; }

    public string Title => Entry.Title;

    public string? Author => Entry.Author;

    public string? SeriesLine => Entry.Series is null ? null : Entry.SeriesIndex is float i ? $"{Entry.Series} #{i:0.##}" : Entry.Series;

    public string? Summary => Entry.Summary;

    public bool IsBook => Entry.IsAcquisition;

    public bool IsFolder => !Entry.IsAcquisition;

    /// <summary>EPUB, PDF... of the file that would be downloaded; empty when the entry offers no readable format.</summary>
    public string FormatLabel { get; }

    public bool CanDownload => IsBook && FormatLabel.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadNow))]
    private bool _inLibrary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadNow))]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public bool CanDownloadNow => CanDownload && !InLibrary && !IsDownloading;
}

public sealed record OpdsCrumb(string Title, Uri Url);

/// <summary>
/// The OPDS catalog browser (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md §4): catalogs on the left, the current feed on the right
/// with a breadcrumb, search, paging and downloads.
/// </summary>
public sealed partial class OpdsCatalogsViewModel : ViewModelBase
{
    private readonly Action _close;
    private readonly Action<ToastRequest>? _toast;
    private readonly IFilePickerService _picker;
    private readonly OpdsCatalogStore _store;
    private readonly OpdsClient _client;
    private readonly OpdsDownloadService _downloads;
    private OpdsCredentials? _credentials;
    private OpdsFeed? _feed;
    private CancellationTokenSource? _loading;

    public OpdsCatalogsViewModel(
        Action close, Action<ToastRequest>? toast = null, IFilePickerService? picker = null,
        OpdsCatalogStore? store = null, OpdsClient? client = null, OpdsDownloadService? downloads = null)
    {
        _close = close;
        _toast = toast;
        _picker = picker ?? new FilePickerService();
        _store = store ?? new OpdsCatalogStore();
        _client = client ?? new OpdsClient();
        _downloads = downloads ?? new OpdsDownloadService(client: _client);
    }

    /// <summary>Tests turn this off; the real app loads cover thumbnails as entries appear.</summary>
    internal bool LoadThumbnails { get; set; } = true;

    /// <summary>Test seam for the download folder (the real one is the saved setting, asked for once).</summary>
    internal Func<Task<string?>>? DownloadFolderProvider { get; set; }

    public ObservableCollection<OpdsCatalog> Catalogs { get; } = new();

    public ObservableCollection<OpdsEntryRow> Entries { get; } = new();

    public ObservableCollection<OpdsCrumb> Breadcrumb { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCatalog))]
    private OpdsCatalog? _selectedCatalog;

    public bool HasSelectedCatalog => SelectedCatalog is not null;

    public bool HasNoCatalogs => Catalogs.Count == 0;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _canSearch;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private string _feedTitle = string.Empty;

    // --- add / edit form ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    public bool IsNotEditing => !IsEditing;

    private int? _editingId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHttpWarning))]
    private string _editUrl = string.Empty;

    [ObservableProperty]
    private string _editUsername = string.Empty;

    [ObservableProperty]
    private string _editPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditMessage))]
    private string _editMessage = string.Empty;

    public bool HasEditMessage => !string.IsNullOrWhiteSpace(EditMessage);

    /// <summary>A plain http:// address is allowed (many home servers use it) but its password travels unencrypted.</summary>
    public bool ShowHttpWarning => EditUrl.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public void Prepare()
    {
        Reload();
        Message = string.Empty;
        IsEditing = false;
        if (Catalogs.Count == 1 && SelectedCatalog is null)
        {
            _ = SelectCatalogAsync(Catalogs[0]);
        }
    }

    private void ReloadCatalogs()
    {
        var selectedId = SelectedCatalog?.Id;
        Catalogs.Clear();
        foreach (var catalog in _store.List())
        {
            Catalogs.Add(catalog);
        }

        SelectedCatalog = Catalogs.FirstOrDefault(c => c.Id == selectedId);
        OnPropertyChanged(nameof(HasNoCatalogs));
    }

    private void Reload() => ReloadCatalogs();

    // ===================== catalogs =====================

    [RelayCommand]
    private void AddCatalog()
    {
        _editingId = null;
        EditName = string.Empty;
        EditUrl = string.Empty;
        EditUsername = string.Empty;
        EditPassword = string.Empty;
        EditMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditCatalog(OpdsCatalog? catalog)
    {
        if (catalog is null)
        {
            return;
        }

        _editingId = catalog.Id;
        EditName = catalog.Name;
        EditUrl = catalog.Url;
        EditUsername = catalog.Username ?? string.Empty;
        EditPassword = string.Empty; // never shown; leaving it empty keeps the saved one
        EditMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveCatalog()
    {
        try
        {
            var saved = _store.Save(_editingId, EditName, EditUrl, EditUsername, EditPassword);
            IsEditing = false;
            ReloadCatalogs();
            _ = SelectCatalogAsync(Catalogs.First(c => c.Id == saved.Id));
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            EditMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void RemoveCatalog(OpdsCatalog? catalog)
    {
        if (catalog is null)
        {
            return;
        }

        _store.Remove(catalog.Id);
        if (SelectedCatalog?.Id == catalog.Id)
        {
            SelectedCatalog = null;
            Entries.Clear();
            Breadcrumb.Clear();
            _feed = null;
            CanSearch = false;
            HasMore = false;
            FeedTitle = string.Empty;
        }

        ReloadCatalogs();
    }

    // ===================== browsing =====================

    [RelayCommand]
    private async Task SelectCatalogAsync(OpdsCatalog? catalog)
    {
        if (catalog is null)
        {
            return;
        }

        SelectedCatalog = catalog;
        _credentials = OpdsCatalogStore.CredentialsOf(catalog);
        Breadcrumb.Clear();
        SearchText = string.Empty;
        await OpenAsync(new Uri(catalog.Url), catalog.Name, replaceCrumbsAfter: -1);
    }

    /// <param name="replaceCrumbsAfter">-1 to start the trail with this feed; otherwise the trail is cut after that index and this feed added.</param>
    private async Task OpenAsync(Uri url, string title, int replaceCrumbsAfter)
    {
        if (SelectedCatalog is null)
        {
            return;
        }

        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;
        IsLoading = true;
        Message = string.Empty;
        try
        {
            var feed = await _client.GetFeedAsync(url, _credentials, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            while (Breadcrumb.Count > replaceCrumbsAfter + 1)
            {
                Breadcrumb.RemoveAt(Breadcrumb.Count - 1);
            }

            Breadcrumb.Add(new OpdsCrumb(string.IsNullOrWhiteSpace(feed.Title) ? title : feed.Title, url));
            _feed = feed;
            FeedTitle = Breadcrumb[^1].Title;
            Entries.Clear();
            AppendEntries(feed);
            if (feed.Entries.Count == 0)
            {
                Message = "Nothing here.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Message = Describe(ex);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void AppendEntries(OpdsFeed feed)
    {
        var inLibrary = SelectedCatalog is null ? new HashSet<string>() : _downloads.InLibrary(SelectedCatalog.Id, feed.Entries);
        foreach (var entry in feed.Entries)
        {
            var row = new OpdsEntryRow(entry) { InLibrary = inLibrary.Contains(entry.Id) };
            if (row.InLibrary)
            {
                row.Status = "In library";
            }

            Entries.Add(row);
            if (LoadThumbnails && entry.Thumbnail is { } thumb)
            {
                _ = LoadThumbnailAsync(row, thumb);
            }
        }

        CanSearch = feed.Search is not null;
        HasMore = feed.Next is not null;
    }

    private async Task LoadThumbnailAsync(OpdsEntryRow row, OpdsLink link)
    {
        try
        {
            byte[] bytes = await _client.GetBytesAsync(new Uri(link.Href), _credentials);
            using var stream = new MemoryStream(bytes);
            row.Thumbnail = new Bitmap(stream);
        }
        catch (Exception)
        {
            // A missing thumbnail only means the card shows no picture.
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "The catalog needs a username and password, or rejected the saved ones. Edit the catalog to change them.",
        HttpRequestException h => $"Could not reach the catalog: {h.Message}",
        FormatException f => f.Message,
        System.Xml.XmlException => "That address did not return an OPDS catalog.",
        _ => $"Something went wrong: {ex.Message}",
    };

    [RelayCommand]
    private async Task OpenEntryAsync(OpdsEntryRow? row)
    {
        if (row?.Entry.NavigationLink is not { } link)
        {
            return;
        }

        await OpenAsync(new Uri(link.Href), row.Title, Breadcrumb.Count - 1);
    }

    [RelayCommand]
    private async Task GoToCrumbAsync(OpdsCrumb? crumb)
    {
        int index = crumb is null ? -1 : Breadcrumb.IndexOf(crumb);
        if (index < 0)
        {
            return;
        }

        await OpenAsync(crumb!.Url, crumb.Title, index - 1);
    }

    [RelayCommand]
    private async Task UpAsync()
    {
        if (Breadcrumb.Count > 1)
        {
            await GoToCrumbAsync(Breadcrumb[^2]);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Breadcrumb.Count > 0)
        {
            await GoToCrumbAsync(Breadcrumb[^1]);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_feed is null || SelectedCatalog is null || string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        try
        {
            var url = await _client.ResolveSearchAsync(_feed, SearchText, _credentials);
            if (url is null)
            {
                Message = "This catalog does not offer search.";
                return;
            }

            await OpenAsync(url, $"Search: {SearchText.Trim()}", Breadcrumb.Count - 1);
        }
        catch (Exception ex)
        {
            Message = Describe(ex);
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_feed?.Next is not { } next)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var more = await _client.GetFeedAsync(next, _credentials);
            _feed = more with { Search = _feed.Search ?? more.Search, SearchIsTemplate = _feed.Search is not null ? _feed.SearchIsTemplate : more.SearchIsTemplate };
            AppendEntries(_feed);
        }
        catch (Exception ex)
        {
            Message = Describe(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ===================== downloads =====================

    [RelayCommand]
    private async Task DownloadAsync(OpdsEntryRow? row)
    {
        if (row is null || SelectedCatalog is null || !row.CanDownloadNow)
        {
            return;
        }

        string? folder = await GetDownloadFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        row.IsDownloading = true;
        row.Progress = 0;
        row.Status = "Downloading…";
        var progress = new Progress<double>(p => row.Progress = p);
        try
        {
            var (_, bookId) = await _downloads.DownloadAsync(SelectedCatalog, row.Entry, folder, progress);
            row.InLibrary = true;
            row.Status = bookId is null ? "Downloaded" : "In library";
            _toast?.Invoke(new ToastRequest("Downloaded", row.Title, ToastSeverity.Success));
        }
        catch (Exception ex)
        {
            row.Status = ex is UnauthorizedAccessException ? "Sign-in needed" : $"Failed: {ex.Message}";
        }
        finally
        {
            row.IsDownloading = false;
        }
    }

    private async Task<string?> GetDownloadFolderAsync()
    {
        if (DownloadFolderProvider is { } provider)
        {
            return await provider();
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            string? saved = context.GetOrCreateAppSettings().OpdsDownloadFolder;
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved;
            }
        }

        string? chosen = await _picker.PickFolderAsync("Choose where downloaded books are saved");
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            using var context = PaperbunkrDb.CreateContext();
            context.GetOrCreateAppSettings().OpdsDownloadFolder = chosen;
            context.SaveChanges();
        }

        return chosen;
    }

    [RelayCommand]
    private void Close() => _close();
}
