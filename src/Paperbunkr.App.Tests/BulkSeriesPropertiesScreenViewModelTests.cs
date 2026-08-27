using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// docs/superpowers/specs/2026-08-24-library-multiselect-slice3-design.md - same edit-buffer Save/
/// Cancel discipline as <see cref="BulkIssuePropertiesScreenViewModelTests"/>, over
/// <see cref="Series"/> instead of <see cref="Issue"/>.
/// </summary>
public class BulkSeriesPropertiesScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public BulkSeriesPropertiesScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulkseriesvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private BulkSeriesPropertiesScreenViewModel CreateViewModel(Action? goBack = null) =>
        new(goBack ?? (() => { }), () => new PaperbunkrDbContext(_dbOptions));

    private int CreateSeries(string name, string? publisher = null, ContentType contentType = ContentType.Comic)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = name, Publisher = publisher, ContentType = contentType };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void Load_SingleSeries_PopulatesEveryFieldFromRealData()
    {
        int seriesId = CreateSeries("Alpha One", publisher: "Acme");
        var vm = CreateViewModel();

        vm.Load(new[] { seriesId });

        var nameField = vm.Fields.Single(f => f.Label == "Name");
        var publisherField = vm.Fields.Single(f => f.Label == "Publisher");
        Assert.Equal("Alpha One", nameField.Value);
        Assert.Equal("Acme", publisherField.Value);
        Assert.All(vm.Fields, f => Assert.False(f.IsStaged));
    }

    [Fact]
    public void Load_MultipleSeriesWithDifferingValues_BlanksTheField()
    {
        int seriesA = CreateSeries("Alpha One", publisher: "Acme");
        int seriesB = CreateSeries("Bravo Two", publisher: "Globex");
        var vm = CreateViewModel();

        vm.Load(new[] { seriesA, seriesB });

        var publisherField = vm.Fields.Single(f => f.Label == "Publisher");
        Assert.Equal(string.Empty, publisherField.Value);
    }

    [Fact]
    public void Load_MultipleSeriesWithSameValue_ShowsTheSharedValue()
    {
        int seriesA = CreateSeries("Alpha One", contentType: ContentType.Manga);
        int seriesB = CreateSeries("Bravo Two", contentType: ContentType.Manga);
        var vm = CreateViewModel();

        vm.Load(new[] { seriesA, seriesB });

        var contentTypeField = vm.Fields.Single(f => f.Label == "Content Type");
        Assert.Equal(nameof(ContentType.Manga), contentTypeField.Value);
    }

    [Fact]
    public void Save_OnlyWritesStagedFields()
    {
        int seriesId = CreateSeries("Alpha One", publisher: "Acme");
        var vm = CreateViewModel();
        vm.Load(new[] { seriesId });

        var nameField = vm.Fields.Single(f => f.Label == "Name");
        nameField.Value = "Renamed Series";
        // Publisher deliberately left untouched/unstaged.

        vm.SaveCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = context.Series.Single(s => s.Id == seriesId);
        Assert.Equal("Renamed Series", series.Name);
        Assert.Equal("Acme", series.Publisher);
    }

    [Fact]
    public void Save_AppliesStagedFieldToEverySelectedSeries()
    {
        int seriesA = CreateSeries("Alpha One");
        int seriesB = CreateSeries("Bravo Two");
        var vm = CreateViewModel();
        vm.Load(new[] { seriesA, seriesB });

        vm.Fields.Single(f => f.Label == "Status").Value = nameof(SeriesStatus.Completed);

        vm.SaveCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.All(context.Series, s => Assert.Equal(SeriesStatus.Completed, s.Status));
    }

    [Fact]
    public void Save_ClearsIsStagedAndCallsGoBack()
    {
        int seriesId = CreateSeries("Alpha One");
        bool wentBack = false;
        var vm = CreateViewModel(() => wentBack = true);
        vm.Load(new[] { seriesId });
        var nameField = vm.Fields.Single(f => f.Label == "Name");
        nameField.Value = "Renamed";

        vm.SaveCommand.Execute(null);

        Assert.False(nameField.IsStaged);
        Assert.False(vm.HasUnsavedChanges());
        Assert.True(wentBack);
    }

    [Fact]
    public void Cancel_NeverTouchesTheDatabase()
    {
        int seriesId = CreateSeries("Alpha One", publisher: "Acme");
        bool wentBack = false;
        var vm = CreateViewModel(() => wentBack = true);
        vm.Load(new[] { seriesId });
        vm.Fields.Single(f => f.Label == "Publisher").Value = "Changed";

        vm.CancelCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal("Acme", context.Series.Single(s => s.Id == seriesId).Publisher);
        Assert.True(wentBack);
    }

    [Fact]
    public void HasUnsavedChanges_TrueOnlyAfterStagingAField()
    {
        int seriesId = CreateSeries("Alpha One");
        var vm = CreateViewModel();
        vm.Load(new[] { seriesId });

        Assert.False(vm.HasUnsavedChanges());

        vm.Fields.Single(f => f.Label == "Name").Value = "Renamed";

        Assert.True(vm.HasUnsavedChanges());
    }
}
