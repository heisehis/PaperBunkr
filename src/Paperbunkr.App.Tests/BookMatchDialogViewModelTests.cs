using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Books;

namespace Paperbunkr.App.Tests;

public class BookMatchDialogViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public BookMatchDialogViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookmatch_vm_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private sealed class Fake : IBookMetadataProvider
    {
        public Func<IReadOnlyList<BookProviderRecord>> Results { get; set; } = () => Array.Empty<BookProviderRecord>();

        public BookFieldSourceKind Kind => BookFieldSourceKind.OpenLibrary;

        public string DisplayName => "Fake";

        public Task<IReadOnlyList<BookProviderRecord>> SearchAsync(string? isbn, string title, string? author, CancellationToken ct) =>
            Task.FromResult(Results());

        public Task<BookProviderRecord?> GetAsync(string id, CancellationToken ct) => Task.FromResult<BookProviderRecord?>(null);
    }

    private static BookProviderRecord Rec(string id, string title) =>
        new(BookFieldSourceKind.OpenLibrary, id, title, null, new[] { "Frank Herbert" }, "Ace", 1965, "en", "9780441172719",
            new[] { "Science Fiction" }, null, null, null, null, null, null);

    private (BookMatchDialogViewModel Vm, int BookId, List<string> Toasts, Func<bool> Closed) Create(Fake provider)
    {
        var book = new Book { Title = "Dune", Author = "Frank Herbert", Format = BookFormat.Epub, FilePath = @"C:\b\dune.epub", AddedTime = DateTime.UtcNow };
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Books.Add(book);
            context.SaveChanges();
        }

        var service = new BookMetadataMatchService(() => new PaperbunkrDbContext(_options), () => new IBookMetadataProvider[] { provider });
        bool closed = false;
        var toasts = new List<string>();
        var vm = new BookMatchDialogViewModel(() => closed = true, (t, _) => toasts.Add(t), service);
        return (vm, book.Id, toasts, () => closed);
    }

    [Fact]
    public async Task Load_SearchesStraightAway_AndListsCandidates()
    {
        var (vm, id, _, _) = Create(new Fake { Results = () => new[] { Rec("a", "Dune") } });

        await vm.LoadAsync(id, "Dune", "Frank Herbert", skipped: false);

        Assert.Equal("Match “Dune”", vm.HeaderLabel);
        var row = Assert.Single(vm.Candidates);
        Assert.Equal("Dune", row.Title);
        Assert.Contains("Frank Herbert", row.Detail);
        Assert.Contains("Open Library", row.Detail);
        Assert.False(vm.HasMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task NoResults_AndProviderFailure_ShowAMessageNotAnEmptyList()
    {
        var empty = Create(new Fake());
        await empty.Vm.LoadAsync(empty.BookId, "Dune", null, false);
        Assert.Contains("No matches", empty.Vm.Message);

        var down = Create(new Fake { Results = () => throw new BookProviderException("Open Library could not be reached.") });
        await down.Vm.LoadAsync(down.BookId, "Dune", null, false);
        Assert.Contains("could not be reached", down.Vm.Message);
        Assert.Empty(down.Vm.Candidates);
    }

    [Fact]
    public async Task SelectingACandidate_ShowsTheDiff_ApplyWritesTickedFields_AndCloses()
    {
        var (vm, id, _, closed) = Create(new Fake { Results = () => new[] { Rec("a", "Dune") } });
        await vm.LoadAsync(id, "Dune", "Frank Herbert", false);

        await vm.SelectCandidateCommand.ExecuteAsync(vm.Candidates[0]);

        Assert.True(vm.HasSelection);
        var fields = vm.DiffRows.ToDictionary(r => r.Field);
        Assert.True(fields[BookFields.Publisher].IsChecked); // empty on the book: pre-ticked
        fields[BookFields.Isbn].IsChecked = false;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(closed());
        using var context = new PaperbunkrDbContext(_options);
        var book = context.Books.Include(b => b.FieldSources).Single();
        Assert.Equal("Ace", book.Publisher);
        Assert.Null(book.ISBN);
        Assert.Equal(BookFieldSourceKind.OpenLibrary, book.FieldSources.Single(s => s.Field == BookFields.Publisher).Source);
    }

    [Fact]
    public async Task ApplyWithNothingTicked_NotifiesAndStaysOpen()
    {
        var (vm, id, toasts, closed) = Create(new Fake { Results = () => new[] { Rec("a", "Dune") } });
        await vm.LoadAsync(id, "Dune", null, false);
        await vm.SelectCandidateCommand.ExecuteAsync(vm.Candidates[0]);
        foreach (var row in vm.DiffRows)
        {
            row.IsChecked = false;
        }

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.False(closed());
        Assert.Contains("Nothing selected", toasts);
    }

    [Fact]
    public async Task ToggleSkip_PersistsAndFlipsTheLabel()
    {
        var (vm, id, _, _) = Create(new Fake());
        await vm.LoadAsync(id, "Dune", null, false);
        Assert.Equal("Never match this book", vm.SkipLabel);

        vm.ToggleSkipCommand.Execute(null);

        Assert.Equal("Include in bulk matching again", vm.SkipLabel);
        using var context = new PaperbunkrDbContext(_options);
        Assert.True(context.Books.Single().SkipMetadataMatch);
    }
}
