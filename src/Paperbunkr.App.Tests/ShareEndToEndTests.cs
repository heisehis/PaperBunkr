using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Sharing;
using Paperbunkr.Sharing;
using Paperbunkr.Sharing.Protocol;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The host stack end to end (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md P1):
/// a real .cbz on disk, the EF-backed catalog, the archive page source, and the real HTTPS server on
/// an ephemeral loopback port. Runs under <see cref="AvaloniaTestCollection"/> because page
/// downscaling needs Avalonia's render interface.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class ShareEndToEndTests : IAsyncLifetime
{
    private const string Password = "hunter2-hunter2";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_share_e2e_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly string _cbzPath;
    private int _issueId;
    private ArchivePageSource _pages = null!;
    private ShareServer _server = null!;
    private HttpClient _http = null!;

    public ShareEndToEndTests()
    {
        Directory.CreateDirectory(_root);
        _cbzPath = Path.Combine(_root, "Saga 001.cbz");
        CbzFixture.Create(_cbzPath, pageCount: 3, pageSize: _ => new System.Drawing.Size(800, 1200));

        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
        var series = new Series { Name = "Saga" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Title = "One", FilePath = _cbzPath, Rating = 5 };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
    }

    public async Task InitializeAsync()
    {
        var catalog = new DbShareCatalogSource(() => new PaperbunkrDbContext(_options), () => new ShareScope { Mode = ShareMode.All });
        // No pre-generated cover on the host: forces the "decode page 1" cover path.
        _pages = new ArchivePageSource(() => new PaperbunkrDbContext(_options), effectiveCoverPath: _ => null);
        var cert = new CertificateManager(Path.Combine(_root, "cert")).GetOrCreate();
        _server = new ShareServer(
            new ShareServerOptions { Port = 0, PasswordHash = PasswordHasher.Hash(Password, 1_000) },
            catalog, _pages, cert);
        await _server.StartAsync();

        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        _http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{_server.Port}") };
        var session = await (await _http.PostAsJsonAsync("/v1/session", new SessionRequest(Password)))
            .Content.ReadFromJsonAsync<SessionResponse>();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        _pages.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Catalog_ListsTheIssue_WithoutPathOrPersonalData()
    {
        string json = await _http.GetStringAsync("/v1/catalog");

        Assert.Contains("\"title\":\"One\"", json);
        Assert.DoesNotContain("Saga 001.cbz", json);
        Assert.DoesNotContain(_root, json);
        Assert.DoesNotContain("\"rating\":", json, StringComparison.OrdinalIgnoreCase); // personal rating (communityRating/ageRating are fine)
    }

    [Fact]
    public async Task Pages_ReportsThePageCount()
    {
        var pages = await _http.GetFromJsonAsync<PagesResponse>($"/v1/issues/{_issueId}/pages");

        Assert.Equal(3, pages!.PageCount);
    }

    [Fact]
    public async Task Page_Original_IsTheExactPngFromTheArchive()
    {
        var response = await _http.GetAsync($"/v1/issues/{_issueId}/pages/0");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        using var bitmap = new Bitmap(new MemoryStream(bytes));
        Assert.Equal(800, bitmap.PixelSize.Width);
        Assert.Equal(1200, bitmap.PixelSize.Height);
    }

    [Fact]
    public async Task Page_WithWidth_IsADownscaledJpeg_KeepingAspectRatio()
    {
        var response = await _http.GetAsync($"/v1/issues/{_issueId}/pages/1?w=200");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, bytes[..2]);
        using var bitmap = new Bitmap(new MemoryStream(bytes));
        Assert.Equal(200, bitmap.PixelSize.Width);
        Assert.Equal(300, bitmap.PixelSize.Height);
    }

    [Fact]
    public async Task Page_WithWidthLargerThanTheSource_IsNotUpscaled()
    {
        var response = await _http.GetAsync($"/v1/issues/{_issueId}/pages/0?w=3000");
        using var bitmap = new Bitmap(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));

        Assert.Equal(800, bitmap.PixelSize.Width);
    }

    [Fact]
    public async Task Cover_FallsBackToTheFirstPage_Downscaled()
    {
        var response = await _http.GetAsync($"/v1/issues/{_issueId}/cover?w=100");
        using var bitmap = new Bitmap(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(100, bitmap.PixelSize.Width);
    }

    [Fact]
    public async Task OutOfRangePage_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/v1/issues/{_issueId}/pages/3")).StatusCode);
    }

    [Fact]
    public async Task RepeatedRequests_ReuseOneOpenArchive()
    {
        for (int i = 0; i < 3; i++)
        {
            await _http.GetAsync($"/v1/issues/{_issueId}/pages/{i}");
        }

        Assert.Equal(1, _pages.OpenArchiveCount);
    }

    [Fact]
    public async Task IdleArchives_AreClosedAfterTheTimeout()
    {
        _pages.IdleTimeout = TimeSpan.FromMilliseconds(1);
        await _http.GetAsync($"/v1/issues/{_issueId}/pages/0");
        Assert.Equal(1, _pages.OpenArchiveCount);

        await Task.Delay(50);
        await _http.GetAsync($"/v1/issues/{_issueId}/pages/0"); // Acquire() evicts idle entries first, then reopens

        Assert.Equal(1, _pages.OpenArchiveCount);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 1, 2, 3, 4 }, "application/octet-stream")]
    public void Sniff_IdentifiesFormatsByMagicBytes(byte[] bytes, string expected)
    {
        Assert.Equal(expected, ArchivePageSource.Sniff(bytes));
    }
}
