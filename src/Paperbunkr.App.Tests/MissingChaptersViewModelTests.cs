using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>The manga Detail "Missing chapters" card (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 1). No network.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MissingChaptersViewModelTests : IDisposable
{
    private const string Aggregate = """
        {"result":"ok","volumes":{
          "none":{"volume":"none","chapters":{"9":{"chapter":"9","isUnavailable":false}}},
          "1":{"volume":"1","chapters":{"1":{"chapter":"1","isUnavailable":false},"2":{"chapter":"2","isUnavailable":false},"3":{"chapter":"3","isUnavailable":false}}}}}
        """;

    private static readonly DateTime Clock = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_missing_chapters_{Guid.NewGuid():N}.db");

    public MissingChaptersViewModelTests()
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

    private int AddSeries(bool linked, params string[] numbers)
    {
        using var context = CreateContext();
        var series = new Series { Name = "Berserk", Issues = numbers.Select(n => new Issue { Number = n, FilePath = $"C:\\x\\{Guid.NewGuid():N}.cbz" }).ToList() };
        context.Series.Add(series);
        context.SaveChanges();
        if (linked)
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = series.Id, Provider = ExternalMetadataProvider.MangaDex, ExternalId = "md-1" });
            context.SaveChanges();
        }

        return series.Id;
    }

    private MissingChaptersViewModel CreateVm(Func<HttpRequestMessage, HttpResponseMessage>? respond = null, Action? onCall = null) =>
        new(CreateContext, new MangaDexAggregateClient(new HttpClient(new Stub(r =>
        {
            onCall?.Invoke();
            return respond?.Invoke(r) ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Aggregate, Encoding.UTF8, "application/json") };
        }))), () => Clock);

    [Fact]
    public void Load_SeriesNotLinkedToMangaDex_HidesTheCard()
    {
        int id = AddSeries(linked: false, "1");
        var vm = CreateVm();

        vm.Load(id);

        Assert.False(vm.IsVisible);
        Assert.Empty(vm.Groups);
    }

    [Fact]
    public void Load_LinkedButNeverChecked_ShowsThePromptAndMakesNoRequest()
    {
        int id = AddSeries(linked: true, "1");
        int calls = 0;
        var vm = CreateVm(onCall: () => calls++);

        vm.Load(id);

        Assert.True(vm.IsVisible);
        Assert.True(vm.NeverChecked);
        Assert.Equal("Not checked against MangaDex yet.", vm.Summary);
        Assert.Equal(string.Empty, vm.Badge);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Check_FillsTheGroupsByVolume_AndTheHaveOverTotalBadge()
    {
        int id = AddSeries(linked: true, "1", "3");
        var vm = CreateVm();
        vm.Load(id);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal("2/4", vm.Badge);
        Assert.Equal(2, vm.MissingCount);
        Assert.Equal(new[] { "Volume 1", "No volume" }, vm.Groups.Select(g => g.Label));
        Assert.Equal("2", vm.Groups[0].Numbers);
        Assert.Equal("9", vm.Groups[1].Numbers);
        Assert.Equal("2 chapters on MangaDex aren't in your library.", vm.Summary);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public async Task Check_WhenEverythingIsOwned_SaysSo()
    {
        int id = AddSeries(linked: true, "1", "2", "3", "9");
        var vm = CreateVm();
        vm.Load(id);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.True(vm.IsComplete);
        Assert.Equal("4/4", vm.Badge);
        Assert.Equal("You have every chapter MangaDex lists.", vm.Summary);
    }

    [Fact]
    public async Task Load_AfterACheck_ReadsTheCacheWithoutRequesting()
    {
        int id = AddSeries(linked: true, "1");
        int calls = 0;
        var first = CreateVm(onCall: () => calls++);
        first.Load(id);
        await first.CheckCommand.ExecuteAsync(null);

        var second = CreateVm(onCall: () => calls++);
        second.Load(id);

        Assert.Equal(1, calls);
        Assert.True(second.HasReport);
        Assert.Equal(3, second.MissingCount);
    }

    [Fact]
    public async Task Check_WhenMangaDexIsDown_KeepsTheCardAndExplains()
    {
        int id = AddSeries(linked: true, "1");
        var vm = CreateVm(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        vm.Load(id);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.True(vm.IsVisible);
        Assert.False(vm.HasReport);
        Assert.Contains("isn't answering", vm.StatusText);
        Assert.False(vm.IsChecking);
    }

    private const string VolumeAggregate = """
        {"result":"ok","volumes":{
          "1":{"volume":"1","chapters":{"11":{"chapter":"11"},"12":{"chapter":"12"}}},
          "2":{"volume":"2","chapters":{"21":{"chapter":"21"},"22":{"chapter":"22"}}},
          "3":{"volume":"3","chapters":{"31":{"chapter":"31"}}}}}
        """;

    private MissingChaptersViewModel CreateVolumeVm() =>
        new(CreateContext, new MangaDexAggregateClient(new HttpClient(new Stub(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(VolumeAggregate, Encoding.UTF8, "application/json") }))), () => Clock);

    [Fact]
    public async Task AVolumeLibrary_OffersToMapVolumesToChapters_ThenReadsAsChapters()
    {
        int id = AddSeries(linked: true, "1", "2");
        var vm = CreateVolumeVm();
        vm.Load(id);
        await vm.CheckCommand.ExecuteAsync(null);

        Assert.True(vm.CanMapVolumes);
        Assert.Equal("1 volume on MangaDex isn't in your library.", vm.Summary);

        await vm.MapVolumesCommand.ExecuteAsync(null);

        Assert.False(vm.CanMapVolumes);
        Assert.True(vm.HasMappings);
        Assert.Equal(AlignmentUnit.Chapters, vm.Unit);
        Assert.Equal(1, vm.MissingCount); // volume 3's chapter 31
        Assert.Equal("5 chapters listed", $"{vm.RemoteCount} chapters listed");
        Assert.Contains("Mapped 2 volumes to chapters", vm.StatusText);
    }

    [Fact]
    public async Task ClearMapping_PutsTheVolumeLibraryBack()
    {
        int id = AddSeries(linked: true, "1", "2");
        var vm = CreateVolumeVm();
        vm.Load(id);
        await vm.CheckCommand.ExecuteAsync(null);
        await vm.MapVolumesCommand.ExecuteAsync(null);

        vm.ClearMappingsCommand.Execute(null);

        Assert.False(vm.HasMappings);
        Assert.True(vm.CanMapVolumes);
        Assert.Contains("Cleared 2 mapped ranges", vm.StatusText);
    }

    [Fact]
    public async Task MappingWhenNoVolumeMatches_SaysSo()
    {
        int id = AddSeries(linked: true, "7", "8");
        var vm = new MissingChaptersViewModel(CreateContext, new MangaDexAggregateClient(new HttpClient(new Stub(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Aggregate, Encoding.UTF8, "application/json") }))), () => Clock);
        vm.Load(id);

        await vm.MapVolumesCommand.ExecuteAsync(null);

        Assert.False(vm.HasMappings);
        Assert.Contains("nothing was mapped", vm.StatusText);
    }

    [Fact]
    public async Task WhenMangaDexListsNothing_TheCardSaysThereIsNothingToCompare_NotThatYouHaveEverything()
    {
        int id = AddSeries(linked: true, "1", "2");
        var vm = new MissingChaptersViewModel(CreateContext, new MangaDexAggregateClient(new HttpClient(new Stub(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":\"ok\",\"volumes\":[]}", Encoding.UTF8, "application/json") }))), () => Clock);
        vm.Load(id);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.Badge);
        Assert.False(vm.IsComplete);
        Assert.StartsWith("MangaDex lists no chapters", vm.Summary);
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
