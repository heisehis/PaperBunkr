using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Calibre;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Import from Calibre dialog's state machine (docs/superpowers/specs/2026-10-27-books-slice4a-calibre-import-design.md §5).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CalibreImportDialogViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_calibre_vm_{Guid.NewGuid():N}");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_calibre_vm_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    private sealed class FakePicker : IFilePickerService
    {
        public string? Folder { get; set; }

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;

        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
    }

    public CalibreImportDialogViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private void BuildLibrary()
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "metadata.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            create table books (id integer primary key, title text, sort text, timestamp text, pubdate text, series_index real, author_sort text, path text, uuid text, has_cover bool, last_modified text);
            create table authors (id integer primary key, name text, sort text, link text);
            create table books_authors_link (id integer primary key, book integer, author integer);
            create table series (id integer primary key, name text, sort text, link text);
            create table books_series_link (id integer primary key, book integer, series integer);
            create table tags (id integer primary key, name text, link text);
            create table books_tags_link (id integer primary key, book integer, tag integer);
            create table publishers (id integer primary key, name text, sort text, link text);
            create table books_publishers_link (id integer primary key, book integer, publisher integer);
            create table languages (id integer primary key, lang_code text, link text);
            create table books_languages_link (id integer primary key, book integer, lang_code integer, item_order integer);
            create table ratings (id integer primary key, rating integer, link text);
            create table books_ratings_link (id integer primary key, book integer, rating integer);
            create table comments (id integer primary key, book integer, text text);
            create table identifiers (id integer primary key, book integer, type text, val text);
            create table data (id integer primary key, book integer, format text, uncompressed_size integer, name text);
            create table custom_columns (id integer primary key, label text, name text, datatype text, mark_for_delete bool default 0, editable bool, display text, is_multiple bool, normalized bool);
            create table library_id (id integer primary key, uuid text);
            insert into library_id (uuid) values ('vm-lib');
            insert into custom_columns (id, label, name, datatype) values (1, 'read', 'Read', 'bool'), (2, 'pages', 'Pages', 'int');
            create table custom_column_1 (id integer primary key, book integer, value bool);
            insert into books (id, title, path, has_cover) values (1, 'Dune', 'Au/Dune (1)', 0);
            insert into authors (id, name) values (1, 'Frank Herbert');
            insert into books_authors_link (book, author) values (1, 1);
            insert into data (book, format, name) values (1, 'EPUB', 'Dune - Frank Herbert');
            """;
        command.ExecuteNonQuery();
        string dir = Path.Combine(_root, "Au", "Dune (1)");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Dune - Frank Herbert.epub"), "x");
    }

    private CalibreImportDialogViewModel Create(FakePicker? picker = null, Action<ToastRequest>? toast = null)
    {
        var importer = new CalibreImporter(() => new PaperbunkrDbContext(_dbOptions));
        return new CalibreImportDialogViewModel(() => { }, toast, picker ?? new FakePicker(), importer);
    }

    [Fact]
    public void FolderWithoutMetadataDb_ShowsAMessageAndCannotImport()
    {
        var vm = Create();

        vm.LibraryFolder = _root;

        Assert.False(vm.CanImport);
        Assert.Contains("metadata.db", vm.Message);
    }

    [Fact]
    public void ValidFolder_ListsOnlyUsableCustomColumns()
    {
        BuildLibrary();
        var vm = Create();

        vm.LibraryFolder = _root;

        Assert.True(vm.CanImport);
        Assert.Equal(["None", "read"], vm.StatusColumns); // the integer column is not offered
    }

    [Fact]
    public async Task Browse_FillsTheFolder()
    {
        BuildLibrary();
        var vm = Create(new FakePicker { Folder = _root });

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(_root, vm.LibraryFolder);
    }

    [Fact]
    public async Task Import_ShowsASummaryAndToasts()
    {
        BuildLibrary();
        ToastRequest? toast = null;
        var vm = Create(toast: t => toast = t);
        vm.LibraryFolder = _root;

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);
        Assert.StartsWith("1 added", vm.ResultSummary);
        Assert.False(vm.IsImporting);
        Assert.Equal("Calibre import", toast!.Title);
        using var db = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal("Dune", db.Books.Single().Title);
    }

    [Fact]
    public async Task Review_SkipAndLinkResolveTheRow()
    {
        BuildLibrary();
        using (var db = new PaperbunkrDbContext(_dbOptions))
        {
            db.Books.Add(new Book { Title = "dune", Author = "frank herbert", FilePath = "elsewhere.epub", Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
            db.SaveChanges();
        }

        var vm = Create();
        vm.LibraryFolder = _root;
        await vm.ImportCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.Review);
        Assert.True(vm.HasReview);

        await vm.LinkCommand.ExecuteAsync(row);

        Assert.True(row.IsResolved);
        Assert.Equal("Linked", row.Resolution);
        using var check = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(1, check.Books.Single().CalibreBookId);
    }

    [Fact]
    public void Summarize_OmitsZeroCounts()
    {
        var text = CalibreImportDialogViewModel.Summarize(new CalibreImportResult(3, 1, 0, 2, 0, []));

        Assert.Equal("3 added · 1 updated · 2 file not found", text);
    }
}
