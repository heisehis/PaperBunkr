using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Real adapter tests for <see cref="PaperbunkrApplication"/>'s gap-closure additions
/// (docs/superpowers/specs/2026-08-30-plugin-api-automation-gaps-design.md): AddNewBook, the icon
/// methods, and GetComicFields. Same fixture convention as <c>PluginApiV3Tests</c> - redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file rather than
/// injecting a context factory (the adapter has none, same as production).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class PaperbunkrApplicationTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public PaperbunkrApplicationTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginapp_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }

    private static int AddSeries(string name)
    {
        using var context = PaperbunkrDb.CreateContext();
        var s = new Series { Name = name };
        context.Series.Add(s);
        context.SaveChanges();
        return s.Id;
    }

    private static int AddIssue(int seriesId, string number, string? publisher = null, string? imprint = null, string? format = null, string? ageRating = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var i = new Issue { SeriesId = seriesId, Number = number, Publisher = publisher, Imprint = imprint, Format = format, AgeRating = ageRating };
        context.Issues.Add(i);
        context.SaveChanges();
        return i.Id;
    }

    // --- GetOrCreateSeriesId ---

    [Fact]
    public void GetOrCreateSeriesId_ExistingSeries_CaseInsensitive_ReturnsExistingId_NoDuplicate()
    {
        int seriesId = AddSeries("Astonishing Tales");
        var app = new PaperbunkrApplication(new MainViewModel());

        int result = app.GetOrCreateSeriesId("astonishing tales");

        Assert.Equal(seriesId, result);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.Series);
    }

    [Fact]
    public void GetOrCreateSeriesId_UnknownName_CreatesNewSeries()
    {
        var app = new PaperbunkrApplication(new MainViewModel());

        int result = app.GetOrCreateSeriesId("Brand New Series");

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Single();
        Assert.Equal(result, series.Id);
        Assert.Equal("Brand New Series", series.Name);
    }

    // --- AddNewBook ---

    [Fact]
    public void AddNewBook_NoDialog_CreatesFilelessIssueUnderSeries()
    {
        int seriesId = AddSeries("Placeholder Series");
        var app = new PaperbunkrApplication(new MainViewModel());

        var issue = app.AddNewBook(seriesId, showDialog: false);

        Assert.NotNull(issue);
        Assert.Equal(seriesId, issue!.SeriesId);
        Assert.Null(issue.FilePath);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.Issues);
    }

    [Fact]
    public void AddNewBook_ShowDialog_OpensOverlay_CancellingDeletesIt()
    {
        int seriesId = AddSeries("Cancelled Series");
        var vm = new MainViewModel();
        var app = new PaperbunkrApplication(vm);

        var issue = app.AddNewBook(seriesId, showDialog: true);

        Assert.NotNull(issue);
        Assert.True(vm.IsIssuePropertiesOverlayOpen);

        vm.IssueProperties.CancelCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.Issues);
    }

    [Fact]
    public void AddNewBook_ShowDialog_SavingKeepsIt()
    {
        int seriesId = AddSeries("Saved Series");
        var vm = new MainViewModel();
        var app = new PaperbunkrApplication(vm);

        var issue = app.AddNewBook(seriesId, showDialog: true);
        Assert.NotNull(issue);

        vm.IssueProperties.Title = "A New Title";
        vm.IssueProperties.SaveCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        var saved = Assert.Single(context.Issues);
        Assert.Equal(issue!.Id, saved.Id);
    }

    // --- Icon methods ---

    [Fact]
    public void GetComicPublisherIcon_ResolvesToARealAsset_ReturnsDecodablePng()
    {
        int seriesId = AddSeries("Marvel Series");
        int issueId = AddIssue(seriesId, "1", publisher: "Marvel");
        var app = new PaperbunkrApplication(new MainViewModel());
        var issue = app.GetBook(issueId)!;

        byte[]? bytes = app.GetComicPublisherIcon(issue);

        Assert.NotNull(bytes);
        using var bitmap = new Bitmap(new MemoryStream(bytes!));
        Assert.True(bitmap.PixelSize.Width > 0);
    }

    [Fact]
    public void GetComicPublisherIcon_ResolvesToALetterMarkOnly_ReturnsNull()
    {
        int seriesId = AddSeries("Vertigo Series");
        int issueId = AddIssue(seriesId, "1", publisher: "Vertigo");
        var app = new PaperbunkrApplication(new MainViewModel());
        var issue = app.GetBook(issueId)!;

        Assert.Null(app.GetComicPublisherIcon(issue));
    }

    [Fact]
    public void GetComicImprintIcon_ImprintDoesNotResolve_FallsBackToPublisher()
    {
        int seriesId = AddSeries("Imprint Fallback Series");
        int issueId = AddIssue(seriesId, "1", publisher: "Marvel", imprint: "Some Made Up Imprint");
        var app = new PaperbunkrApplication(new MainViewModel());
        var issue = app.GetBook(issueId)!;

        byte[]? bytes = app.GetComicImprintIcon(issue);

        Assert.NotNull(bytes); // falls back to Marvel's real asset
    }

    [Fact]
    public void GetComicAgeRatingIcon_ResolvesToARealAsset_ReturnsDecodablePng()
    {
        int seriesId = AddSeries("Mature Series");
        int issueId = AddIssue(seriesId, "1", ageRating: "Mature 17+");
        var app = new PaperbunkrApplication(new MainViewModel());
        var issue = app.GetBook(issueId)!;

        byte[]? bytes = app.GetComicAgeRatingIcon(issue);

        Assert.NotNull(bytes);
        using var bitmap = new Bitmap(new MemoryStream(bytes!));
        Assert.True(bitmap.PixelSize.Width > 0);
    }

    [Fact]
    public void GetComicAgeRatingIcon_ResolvesToALetterMarkOnly_ReturnsNull()
    {
        int seriesId = AddSeries("MA15 Series");
        int issueId = AddIssue(seriesId, "1", ageRating: "MA15+");
        var app = new PaperbunkrApplication(new MainViewModel());
        var issue = app.GetBook(issueId)!;

        Assert.Null(app.GetComicAgeRatingIcon(issue));
    }

    [Fact]
    public void GetComicFormatIcon_NeverResolvesToARealAsset_ReturnsNull()
    {
        // Format aliases resolve to Glyph/LetterMark only (MarkResolverTests confirms no SvgAsset
        // case exists for format) - this exercises the same null-safety branch the other icon
        // methods use, just with a Kind that never has a bundled image at all.
        int seriesId = AddSeries("TPB Series");
        int issueId = AddIssue(seriesId, "1", format: "Trade Paperback");
        var app = new PaperbunkrApplication(new MainViewModel());
        var issue = app.GetBook(issueId)!;

        Assert.Null(app.GetComicFormatIcon(issue));
    }

    // --- GetComicFields ---

    [Fact]
    public void GetComicFields_KeysMatchEnumNames_ValuesMatchCatalogDisplayNames()
    {
        var app = new PaperbunkrApplication(new MainViewModel());

        var fields = app.GetComicFields();

        Assert.NotEmpty(fields);
        foreach (var kv in IssueListFieldCatalog.SortFields)
        {
            Assert.Equal(kv.Value.DisplayName, fields[kv.Key.ToString()]);
        }
    }
}
