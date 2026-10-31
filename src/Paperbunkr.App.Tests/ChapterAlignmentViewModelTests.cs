using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Library Health's "Chapter alignment" section (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 1):
/// reading the cache, "Check now" against a fake MangaDex, dismiss and restore. No network.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ChapterAlignmentViewModelTests : IDisposable
{
    private const string Aggregate = """
        {"result":"ok","volumes":{"none":{"volume":"none","chapters":{
          "1":{"chapter":"1","isUnavailable":false},"2":{"chapter":"2","isUnavailable":false},"3":{"chapter":"3","isUnavailable":false}}}}}
        """;

    private static readonly DateTime Clock = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_alignment_vm_{Guid.NewGuid():N}.db");

    public ChapterAlignmentViewModelTests()
    {
        using var context = CreateContext();
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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private int AddLinkedSeries(string name, params string[] numbers)
    {
        using var context = CreateContext();
        var series = new Series { Name = name, Issues = numbers.Select(n => new Issue { Number = n, FilePath = $"C:\\x\\{Guid.NewGuid():N}.cbz" }).ToList() };
        context.Series.Add(series);
        context.SaveChanges();
        context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = series.Id, Provider = ExternalMetadataProvider.MangaDex, ExternalId = $"md-{series.Id}" });
        context.SaveChanges();
        return series.Id;
    }

    private ChapterAlignmentViewModel CreateVm(Func<HttpRequestMessage, HttpResponseMessage>? respond = null, Action<int>? open = null) =>
        new(CreateContext, action => action(), new MangaDexAggregateClient(new HttpClient(new Stub(respond ?? (_ => Ok()))))
            , () => Clock) { OpenSeries = open };

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent(Aggregate, Encoding.UTF8, "application/json") };

    [Fact]
    public void Open_BeforeAnyCheck_ReadsTheCacheOnly_AndShowsNothing()
    {
        AddLinkedSeries("Berserk", "1");
        int calls = 0;
        var vm = CreateVm(_ => { calls++; return Ok(); });

        vm.EnsureScanned();

        Assert.Empty(vm.Rows);
        Assert.True(vm.IsClean);
        Assert.Equal(0, calls);
        Assert.Equal("· No differences", vm.HeaderDetail);
    }

    [Fact]
    public async Task CheckNow_FetchesLinkedSeries_ThenListsTheOnesThatDiffer()
    {
        AddLinkedSeries("Berserk", "1", "9");
        AddLinkedSeries("Complete", "1", "2", "3");
        var vm = CreateVm();

        await vm.CheckNowCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("Berserk", row.SeriesName);
        Assert.Equal(2, row.MissingCount);
        Assert.Equal(1, row.ExtraCount);
        Assert.Contains("2 chapters not in your library", row.Summary);
        Assert.Contains("1 not on MangaDex", row.Summary);
        Assert.Equal("Checked 2 series.", vm.StatusText);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public async Task CheckNow_WhenMangaDexRateLimits_KeepsWhatItGot_AndSaysSo()
    {
        AddLinkedSeries("Berserk", "1");
        var vm = CreateVm(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.Contains("stopped answering", vm.StatusText);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public async Task CheckNow_WithNothingToFetch_SaysEverythingIsCurrent()
    {
        var vm = CreateVm();

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal("Every linked series is already up to date.", vm.StatusText);
    }

    [Fact]
    public async Task NotAProblem_MovesTheRowToDismissed_UntilRestored_AndAChangedListBringsItBack()
    {
        int seriesId = AddLinkedSeries("Berserk", "1");
        var vm = CreateVm();
        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.NotAProblemCommand.Execute(vm.Rows[0]);

        Assert.Empty(vm.Rows);
        Assert.Single(vm.Dismissed);
        vm.ScanCommand.Execute(null);
        Assert.Empty(vm.Rows);

        vm.RestoreCommand.Execute(vm.Dismissed[0]);
        Assert.Single(vm.Rows);

        vm.NotAProblemCommand.Execute(vm.Rows[0]);
        using (var context = CreateContext())
        {
            context.Issues.Add(new Issue { SeriesId = seriesId, Number = "2", FilePath = "C:\\x\\2.cbz" });
            context.SaveChanges();
        }

        vm.ScanCommand.Execute(null);

        // Chapter 2 is no longer missing, so the series is a different finding from the dismissed one and is listed again.
        var row = Assert.Single(vm.Rows);
        Assert.Equal(1, row.MissingCount);
    }

    [Fact]
    public async Task OpenSeries_GoesToThatSeries()
    {
        int seriesId = AddLinkedSeries("Berserk", "1");
        int? opened = null;
        var vm = CreateVm(open: id => opened = id);
        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.OpenCommand.Execute(vm.Rows[0]);

        Assert.Equal(seriesId, opened);
    }

    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public Stub(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
