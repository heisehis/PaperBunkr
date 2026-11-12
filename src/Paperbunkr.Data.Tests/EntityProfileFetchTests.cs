using System.Net;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

public class EntityProfileFetchTests
{
    private sealed class Handler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }

    [Theory]
    [InlineData(ComicMetadataEntityKind.Character, "character/4005-17")]
    [InlineData(ComicMetadataEntityKind.Team, "team/4060-17")]
    [InlineData(ComicMetadataEntityKind.Creator, "person/4040-17")]
    public async Task ComicVine_UsesKindSpecificResource_AndRequestsTheProfileFields(ComicMetadataEntityKind kind, string path)
    {
        using var handler = new Handler(HttpStatusCode.OK, """{"status_code":1,"results":{"name":"Name","deck":"A mutant who controls the weather.","aliases":"Alias","image":{"original_url":"https://image"}}}""");
        using var http = new HttpClient(handler);
        var client = new ComicVineClient("fake-key", http: http);
        var profile = await client.GetEntityProfileAsync(kind, "17", default);
        Assert.Contains(path, handler.Requested!.AbsolutePath);
        Assert.Contains("field_list=", handler.Requested.Query);
        Assert.Contains("aliases", handler.Requested.Query);
        Assert.Equal("A mutant who controls the weather.", profile!.Description);
        Assert.Equal(new[] { "Alias" }, profile.Aliases);
    }

    [Theory]
    [InlineData(ComicMetadataEntityKind.Character, "character")]
    [InlineData(ComicMetadataEntityKind.Team, "team")]
    [InlineData(ComicMetadataEntityKind.Creator, "creator")]
    public async Task Metron_UsesKindSpecificResource(ComicMetadataEntityKind kind, string path)
    {
        using var handler = new Handler(HttpStatusCode.OK, """{"id":17,"name":"Name","desc":"Bio","alias":["Alias"],"image":"https://image"}""");
        using var http = new HttpClient(handler);
        var client = new MetronClient("fake-user", "fake-password", http: http);
        var profile = await client.GetEntityProfileAsync(kind, "17", default);
        Assert.Equal($"/api/{path}/17/", handler.Requested!.AbsolutePath);
        Assert.Equal("https://image", profile!.ImageUrl);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NotFound_ReturnsNull(bool metron)
    {
        using var handler = new Handler(HttpStatusCode.NotFound, """{"detail":"Not found."}""");
        using var http = new HttpClient(handler);
        IEntityProfileSource client = metron ? new MetronClient("fake-user", "fake-password", http: http) : new ComicVineClient("fake-key", http: http);
        Assert.Null(await client.GetEntityProfileAsync(ComicMetadataEntityKind.Character, "17", default));
    }
}
