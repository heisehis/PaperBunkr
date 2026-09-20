using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Paperbunkr.Daemon.Clients;

namespace Paperbunkr.Daemon.Tests.Clients;

public class TorrentHashTests
{
    private const string Hex = "c12fe1c06bba254a9dc9f519b335aa7c1367a88a";

    [Fact]
    public void FromMagnet_ReadsHexAndBase32Forms()
    {
        Assert.Equal(Hex, TorrentHash.FromMagnet($"magnet:?xt=urn:btih:{Hex.ToUpperInvariant()}&dn=Spawn+263&tr=udp%3A%2F%2Fx"));

        // Base32 of the same 20 bytes.
        var bytes = Convert.FromHexString(Hex);
        Assert.Equal(Hex, TorrentHash.FromMagnet($"magnet:?xt=urn:btih:{Base32(bytes)}"));
    }

    [Theory]
    [InlineData("http://example.com/a.torrent")]
    [InlineData("magnet:?dn=no-hash")]
    [InlineData("magnet:?xt=urn:btih:tooShort")]
    [InlineData("magnet:?xt=urn:btih:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void FromMagnet_RejectsAnythingElse(string link) => Assert.Null(TorrentHash.FromMagnet(link));

    [Fact]
    public void FromTorrentFile_HashesTheInfoDictionaryBytes()
    {
        const string info = "d6:lengthi3e4:name1:a12:piece lengthi16384e6:pieces20:AAAAAAAAAAAAAAAAAAAAe";
        var torrent = Encoding.ASCII.GetBytes($"d8:announce12:http://t/ann4:info{info}e");

        var expected = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(info))).ToLowerInvariant();

        Assert.Equal(expected, TorrentHash.FromTorrentFile(torrent));
    }

    [Fact]
    public void FromTorrentFile_FindsInfoAfterNestedValues()
    {
        const string info = "d6:lengthi1e4:name1:b12:piece lengthi16384e6:pieces20:BBBBBBBBBBBBBBBBBBBBe";
        var torrent = Encoding.ASCII.GetBytes($"d13:announce-listll5:a1234el5:b5678ee7:comment2:hi4:info{info}e");

        Assert.Equal(Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(info))).ToLowerInvariant(), TorrentHash.FromTorrentFile(torrent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a torrent")]
    [InlineData("d4:name1:ae")]                  // valid bencode, no info dictionary
    [InlineData("d4:infod6:lengthi3e")]          // truncated
    public void FromTorrentFile_ReturnsNullForNonTorrents(string text) => Assert.Null(TorrentHash.FromTorrentFile(Encoding.ASCII.GetBytes(text)));

    private static string Base32(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        return sb.ToString();
    }
}

public class QBittorrentClientTests
{
    private const string Hash = "c12fe1c06bba254a9dc9f519b335aa7c1367a88a";

    private sealed class FakeQBit : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string Body)> Calls { get; } = new();
        public Func<string, string> LoginBody { get; set; } = _ => "Ok.";
        public HttpStatusCode LoginStatus { get; set; } = HttpStatusCode.OK;
        public string InfoJson { get; set; } = "[]";
        public string AddBody { get; set; } = "Ok.";
        public int ForbidNext { get; set; }
        public Exception? Throw { get; set; }
        public bool Multipart { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw is not null) throw Throw;
            var path = request.RequestUri!.PathAndQuery;
            var body = request.Content is null ? string.Empty : await ReadAsync(request.Content);
            Calls.Add((request.Method, path, body));

            if (request.Headers.Referrer is null) return new HttpResponseMessage(HttpStatusCode.BadRequest);   // the API requires a Referer

            if (path.StartsWith("/api/v2/auth/login")) return new HttpResponseMessage(LoginStatus) { Content = new StringContent(LoginBody(body)) };
            if (ForbidNext > 0) { ForbidNext--; return new HttpResponseMessage(HttpStatusCode.Forbidden); }
            if (path.StartsWith("/api/v2/app/version")) return Ok("v5.0.2");
            if (path.StartsWith("/api/v2/torrents/info")) return Ok(InfoJson);
            if (path.StartsWith("/api/v2/torrents/files")) return Ok("""[{"name":"Spawn 263.cbz","size":52428800},{"name":"","size":1}]""");
            if (path.StartsWith("/api/v2/torrents/add")) { Multipart = request.Content is MultipartFormDataContent; return Ok(AddBody); }
            if (path.StartsWith("/api/v2/torrents/delete")) return Ok("");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static async Task<string> ReadAsync(HttpContent content) => await content.ReadAsStringAsync();
        private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private sealed class FakeFetch(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request.RequestUri.ToString()));
        }
    }

    private static string Torrent(string name) => "{\"hash\":\"" + Hash + "\",\"name\":\"" + name + "\",\"category\":\"paperbunkr-comics\",\"progress\":0.5,\"state\":\"downloading\",\"size\":100,\"dlspeed\":10,\"eta\":60,\"save_path\":\"D:\\\\dl\",\"content_path\":\"D:\\\\dl\\\\x\"}";

    private static QBittorrentClient Create(FakeQBit api, FakeFetch? fetch = null) =>
        new("http://qbit.local:8080/", "admin", "secret", "paperbunkr-comics", new HttpClient(api), fetch is null ? null : new HttpClient(fetch));

    [Fact]
    public async Task TestConnection_LogsIn_AndReportsTheVersionAndCategory()
    {
        var api = new FakeQBit();

        var result = await Create(api).TestConnectionAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("v5.0.2", result.Message);
        Assert.Contains("paperbunkr-comics", result.Message);
        Assert.Contains("admin", api.Calls[0].Body);
    }

    [Fact]
    public async Task TestConnection_ExplainsBadCredentials_Bans_AndUnreachableServers()
    {
        var bad = new FakeQBit { LoginBody = _ => "Fails." };
        var r1 = await Create(bad).TestConnectionAsync(CancellationToken.None);
        Assert.False(r1.Success);
        Assert.Contains("username or password", r1.Message);

        var banned = new FakeQBit { LoginStatus = HttpStatusCode.Forbidden };
        var r2 = await Create(banned).TestConnectionAsync(CancellationToken.None);
        Assert.Contains("banned", r2.Message);

        var down = new FakeQBit { Throw = new HttpRequestException("connection refused") };
        var r3 = await Create(down).TestConnectionAsync(CancellationToken.None);
        Assert.False(r3.Success);
        Assert.Contains("Couldn't reach qBittorrent", r3.Message);
    }

    [Fact]
    public async Task GetStatus_AsksForOnlyTheCategory_AndDropsAnyTorrentFromAnother()
    {
        var api = new FakeQBit
        {
            InfoJson = "[" + Torrent("Spawn 263") + ",{\"hash\":\"ffff\",\"name\":\"My Linux ISO\",\"category\":\"other\",\"progress\":1,\"state\":\"uploading\"}]",
        };

        var found = await Create(api).GetStatusAsync(new[] { Hash.ToUpperInvariant() }, CancellationToken.None);

        var call = api.Calls.Last(c => c.PathAndQuery.StartsWith("/api/v2/torrents/info"));
        Assert.Contains("category=paperbunkr-comics", call.PathAndQuery);
        Assert.Contains("hashes=" + Hash, call.PathAndQuery);          // hashes are normalized to lower case
        var status = Assert.Single(found);                              // the other category's torrent never surfaces
        Assert.Equal("Spawn 263", status.Name);
        Assert.Equal(0.5, status.Progress);
        Assert.Equal(DownloadState.Downloading, status.State);
        Assert.Equal(TimeSpan.FromSeconds(60), status.Eta);
        Assert.Equal(10, status.BytesPerSecond);
    }

    [Theory]
    [InlineData("downloading", 0.4, DownloadState.Downloading)]
    [InlineData("stalledDL", 0.4, DownloadState.Downloading)]
    [InlineData("metaDL", 0.0, DownloadState.Downloading)]
    [InlineData("pausedDL", 0.4, DownloadState.Paused)]
    [InlineData("stoppedDL", 0.4, DownloadState.Paused)]
    [InlineData("queuedDL", 0.0, DownloadState.Queued)]
    [InlineData("uploading", 1.0, DownloadState.Completed)]
    [InlineData("stalledUP", 1.0, DownloadState.Completed)]
    [InlineData("pausedUP", 1.0, DownloadState.Completed)]
    [InlineData("stoppedUP", 1.0, DownloadState.Completed)]
    [InlineData("checkingUP", 1.0, DownloadState.Completed)]
    [InlineData("error", 0.3, DownloadState.Error)]
    [InlineData("missingFiles", 0.3, DownloadState.Missing)]
    public void States_MapAcrossQBittorrentV4AndV5(string state, double progress, DownloadState expected) =>
        Assert.Equal(expected, QBittorrentClient.MapState(state, progress));

    [Fact]
    public async Task Add_Magnet_SendsItInTheCategory_AndReturnsItsHash()
    {
        var api = new FakeQBit();

        var hash = await Create(api).AddAsync($"magnet:?xt=urn:btih:{Hash}&dn=x", CancellationToken.None);

        Assert.Equal(Hash, hash);
        Assert.True(api.Multipart);
        var add = api.Calls.Single(c => c.PathAndQuery.StartsWith("/api/v2/torrents/add"));
        Assert.Contains("paperbunkr-comics", add.Body);
        Assert.Contains($"magnet:?xt=urn:btih:{Hash}", add.Body);
    }

    [Fact]
    public async Task Add_TorrentUrl_IsFetchedAndUploadedAsAFile_SoItsHashIsKnown()
    {
        const string info = "d6:lengthi3e4:name1:a12:piece lengthi16384e6:pieces20:AAAAAAAAAAAAAAAAAAAAe";
        var bytes = Encoding.ASCII.GetBytes($"d4:info{info}e");
        var expected = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(info))).ToLowerInvariant();
        var api = new FakeQBit();
        var fetch = new FakeFetch(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        var hash = await Create(api, fetch).AddAsync("http://prowlarr.local/1/download?file=x", CancellationToken.None);

        Assert.Equal(expected, hash);
        Assert.Equal(new[] { "http://prowlarr.local/1/download?file=x" }, fetch.Urls);
        var add = api.Calls.Single(c => c.PathAndQuery.StartsWith("/api/v2/torrents/add"));
        Assert.Contains("release.torrent", add.Body);
        Assert.DoesNotContain("name=urls", add.Body);
    }

    [Fact]
    public async Task Add_FollowsARedirectThatEndsInAMagnet()
    {
        var api = new FakeQBit();
        var fetch = new FakeFetch(url => url.Contains("hop1")
            ? Redirect("http://prowlarr.local/hop2")
            : url.Contains("hop2") ? Redirect($"magnet:?xt=urn:btih:{Hash}") : new HttpResponseMessage(HttpStatusCode.NotFound));

        var hash = await Create(api, fetch).AddAsync("http://prowlarr.local/hop1", CancellationToken.None);

        Assert.Equal(Hash, hash);
        Assert.Contains($"magnet:?xt=urn:btih:{Hash}", api.Calls.Single(c => c.PathAndQuery.StartsWith("/api/v2/torrents/add")).Body);

        static HttpResponseMessage Redirect(string to) { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.TryAddWithoutValidation("Location", to); return r; }
    }

    [Fact]
    public async Task Add_RejectsWhatIsNeitherAMagnetNorATorrent_AndIndexerErrors()
    {
        var api = new FakeQBit();
        var notTorrent = new FakeFetch(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>") });
        await Assert.ThrowsAsync<DownloadClientException>(() => Create(api, notTorrent).AddAsync("http://x/y", CancellationToken.None));

        var notFound = new FakeFetch(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var ex = await Assert.ThrowsAsync<DownloadClientException>(() => Create(api, notFound).AddAsync("http://x/y", CancellationToken.None));
        Assert.Contains("404", ex.Message);
        Assert.DoesNotContain(api.Calls, c => c.PathAndQuery.StartsWith("/api/v2/torrents/add"));   // nothing was sent to the client
    }

    [Fact]
    public async Task Add_TreatsFailsAsSuccess_OnlyWhenTheTorrentIsAlreadyInOurCategory()
    {
        var already = new FakeQBit { AddBody = "Fails.", InfoJson = "[" + Torrent("Spawn 263") + "]" };
        Assert.Equal(Hash, await Create(already).AddAsync($"magnet:?xt=urn:btih:{Hash}", CancellationToken.None));

        var invalid = new FakeQBit { AddBody = "Fails.", InfoJson = "[]" };
        var ex = await Assert.ThrowsAsync<DownloadClientException>(() => Create(invalid).AddAsync($"magnet:?xt=urn:btih:{Hash}", CancellationToken.None));
        Assert.Contains("refused", ex.Message);
    }

    [Fact]
    public async Task Add_RequiresACategory()
    {
        var client = new QBittorrentClient("http://q", "a", "b", "  ", new HttpClient(new FakeQBit()));

        var ex = await Assert.ThrowsAsync<DownloadClientException>(() => client.AddAsync($"magnet:?xt=urn:btih:{Hash}", CancellationToken.None));

        Assert.Contains("category", ex.Message);
    }

    [Fact]
    public async Task AnExpiredSession_LogsInAgain_AndRetriesOnce()
    {
        var api = new FakeQBit { ForbidNext = 1, InfoJson = "[" + Torrent("Spawn 263") + "]" };
        var client = Create(api);
        await client.GetStatusAsync(null, CancellationToken.None);   // logs in, then the first info call is 403 -> log in again -> retry

        Assert.Equal(2, api.Calls.Count(c => c.PathAndQuery.StartsWith("/api/v2/auth/login")));
        Assert.Equal(2, api.Calls.Count(c => c.PathAndQuery.StartsWith("/api/v2/torrents/info")));
    }

    [Fact]
    public async Task Remove_OnlyActsOnTorrentsInOurCategory()
    {
        var foreign = new FakeQBit { InfoJson = "[]" };       // the category-locked query finds nothing for this hash
        Assert.False(await Create(foreign).RemoveAsync(Hash, deleteFiles: true, CancellationToken.None));
        Assert.DoesNotContain(foreign.Calls, c => c.PathAndQuery.StartsWith("/api/v2/torrents/delete"));

        var ours = new FakeQBit { InfoJson = "[" + Torrent("Spawn 263") + "]" };
        Assert.True(await Create(ours).RemoveAsync(Hash, deleteFiles: true, CancellationToken.None));
        var delete = ours.Calls.Single(c => c.PathAndQuery.StartsWith("/api/v2/torrents/delete"));
        Assert.Contains(Hash, delete.Body);
        Assert.Contains("deleteFiles=true", delete.Body);
    }

    [Fact]
    public async Task GetFiles_ListsOurTorrentsFiles_ButNeverAForeignOnes()
    {
        var ours = new FakeQBit { InfoJson = "[" + Torrent("Spawn 263") + "]" };
        var files = await Create(ours).GetFilesAsync(Hash, CancellationToken.None);
        Assert.Equal(new[] { "Spawn 263.cbz" }, files.Select(f => f.Path));       // the nameless entry is dropped
        Assert.Equal(52428800, files[0].SizeBytes);

        var foreign = new FakeQBit { InfoJson = "[]" };
        Assert.Empty(await Create(foreign).GetFilesAsync(Hash, CancellationToken.None));
        Assert.DoesNotContain(foreign.Calls, c => c.PathAndQuery.StartsWith("/api/v2/torrents/files"));
    }
}
