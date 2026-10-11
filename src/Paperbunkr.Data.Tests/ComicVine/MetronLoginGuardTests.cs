using System.Net;
using System.Net.Http;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>
/// <see cref="MetronLoginGuard"/>: Metron blocks an address for 24 hours after three 401s in five minutes (its
/// published fail2ban rules), so a rejected login must never be sent a third time inside that window. Found the
/// hard way on 2026-10-05. In the quota collection because the guard, like the quota, is process-wide.
/// </summary>
[Collection("MetronQuota")]
public sealed class MetronLoginGuardTests : IDisposable
{
    private DateTime _now = new(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc);

    public MetronLoginGuardTests()
    {
        MetronLoginGuard.Reset();
        MetronLoginGuard.Clock = () => _now;
    }

    public void Dispose()
    {
        MetronLoginGuard.Reset();
        MetronLoginGuard.Clock = () => DateTime.UtcNow;
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static MetronClient Client(Handler handler, string password = "pw") =>
        new("reader", password, ComicVineRequestPriority.High, new HttpClient(handler)) { GuardLogin = true };

    [Fact]
    public async Task ARejectedLogin_IsNotSentAgain_UntilThePauseIsOver()
    {
        var handler = new Handler(HttpStatusCode.Unauthorized, """{"detail":"Invalid username/password."}""");
        var client = Client(handler);

        var first = await Assert.ThrowsAsync<ComicVineException>(() => client.GetVolumeAsync(1, CancellationToken.None));
        var held = await Assert.ThrowsAsync<ComicVineException>(() => client.GetVolumeAsync(1, CancellationToken.None));
        await Assert.ThrowsAsync<ComicVineException>(() => client.SearchVolumesAsync("spawn", CancellationToken.None));

        Assert.Equal(1, handler.Sent);                                    // the other two never left the machine
        Assert.Equal("Metron rejected your login (Invalid username/password). Check it under Preferences → Connections.", first.Message);
        Assert.Equal(100, held.ApiStatusCode);                            // callers still treat it as a login problem
        Assert.Contains("24 hours", held.Message);

        _now += MetronLoginGuard.SameLoginPause;
        await Assert.ThrowsAsync<ComicVineException>(() => client.GetVolumeAsync(1, CancellationToken.None));
        Assert.Equal(2, handler.Sent);
    }

    [Fact]
    public async Task ACorrectedLogin_MayBeTriedOnce_ButAThirdRejectionInTheWindowIsNeverSent()
    {
        var handler = new Handler(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<ComicVineException>(() => Client(handler, "first").GetVolumeAsync(1, CancellationToken.None));
        _now += TimeSpan.FromSeconds(30);
        await Assert.ThrowsAsync<ComicVineException>(() => Client(handler, "second").GetVolumeAsync(1, CancellationToken.None));
        _now += TimeSpan.FromSeconds(30);
        await Assert.ThrowsAsync<ComicVineException>(() => Client(handler, "third").GetVolumeAsync(1, CancellationToken.None));

        Assert.Equal(2, handler.Sent);                                    // Metron's limit is three

        _now += MetronLoginGuard.Window;                                  // the first has aged out: one attempt is allowed again
        await Assert.ThrowsAsync<ComicVineException>(() => Client(handler, "third").GetVolumeAsync(1, CancellationToken.None));
        Assert.Equal(3, handler.Sent);
    }

    [Fact]
    public async Task A403_IsNotCalledAWrongLogin()
    {
        var handler = new Handler(HttpStatusCode.Forbidden, """{"detail":"You do not have permission to perform this action."}""");

        var ex = await Assert.ThrowsAsync<ComicVineException>(() => Client(handler).GetVolumeAsync(1, CancellationToken.None));

        Assert.Equal(403, ex.HttpStatus);
        Assert.StartsWith("Metron refused this request (You do not have permission to perform this action).", ex.Message);
        Assert.DoesNotContain("rejected your login", ex.Message);
    }

    [Fact]
    public async Task AWorkingLogin_IsNeverHeld()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"count":0,"next":null,"previous":null,"results":[]}""");
        var client = Client(handler);

        for (int i = 0; i < 5; i++)
        {
            await client.SearchVolumesAsync("spawn", CancellationToken.None);
        }

        Assert.Equal(5, handler.Sent);
        Assert.False(MetronLoginGuard.ShouldHold("anything", out _));
    }
}
