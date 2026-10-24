using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary><see cref="MetronAuth"/> and <see cref="MetronAuthProbe"/> (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md Part 1).</summary>
public class MetronAuthTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public MetronAuthTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_metronauth_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Nothing_saved_is_not_configured()
    {
        Assert.Null(MetronAuth.Load(_context));
        Assert.False(MetronAuth.IsConfigured(_context));
    }

    [Fact]
    public void A_legacy_login_still_loads_as_basic_auth()
    {
        CredentialStore.Set(_context, "Metron", CredentialKind.Username, "reader");
        CredentialStore.Set(_context, "Metron", CredentialKind.Password, "secret");

        var auth = MetronAuth.Load(_context);

        Assert.NotNull(auth);
        Assert.False(auth!.IsToken);
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal(Convert.ToBase64String("reader:secret"u8.ToArray()), auth.Parameter);
        Assert.True(MetronAuth.IsConfigured(_context));
    }

    [Fact]
    public void A_token_wins_over_a_legacy_login_and_defaults_to_the_Bearer_scheme()
    {
        CredentialStore.Set(_context, "Metron", CredentialKind.Username, "reader");
        CredentialStore.Set(_context, "Metron", CredentialKind.Password, "secret");
        CredentialStore.Set(_context, "Metron", CredentialKind.ApiToken, "  abc123  ");

        var auth = MetronAuth.Load(_context)!;

        Assert.True(auth.IsToken);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("abc123", auth.Parameter);
        Assert.Equal("Bearer abc123", auth.Header().ToString());
    }

    [Fact]
    public void The_learned_scheme_is_used()
    {
        CredentialStore.Set(_context, "Metron", CredentialKind.ApiToken, "abc123");
        CredentialStore.Set(_context, "Metron", CredentialKind.AuthScheme, "token");

        Assert.Equal("Token", MetronAuth.Load(_context)!.Scheme);
    }

    [Fact]
    public void The_new_credential_kinds_are_appended_after_every_existing_one()
    {
        Assert.Equal(7, (int)CredentialKind.ApiToken);
        Assert.Equal(8, (int)CredentialKind.AuthScheme);
    }

    [Fact]
    public void Switching_scheme_or_credential_changes_the_guard_key()
    {
        var token = MetronAuth.Token("abc");
        Assert.NotEqual(token.Key, token.WithOtherScheme().Key);
        Assert.NotEqual(token.Key, MetronAuth.Basic("u", "p").Key);
    }

    [Fact]
    public async Task The_client_sends_the_token_header()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = new MetronClient(MetronAuth.Token("abc123"), http: new HttpClient(handler));

        await client.VerifyAsync(CancellationToken.None);

        Assert.Equal("Bearer abc123", handler.LastAuthorization);
    }

    [Fact]
    public async Task Probe_accepts_the_saved_scheme_without_a_second_request()
    {
        var tried = new List<string>();
        var result = await MetronAuthProbe.ProbeAsync(MetronAuth.Token("abc"), (a, _) => { tried.Add(a.Scheme); return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("Bearer", result.Working!.Scheme);
        Assert.Equal(new[] { "Bearer" }, tried);
    }

    [Fact]
    public async Task Probe_tries_the_other_scheme_after_a_401_and_reports_it_as_the_working_scheme()
    {
        var tried = new List<string>();
        var result = await MetronAuthProbe.ProbeAsync(MetronAuth.Token("abc"), (a, _) =>
        {
            tried.Add(a.Scheme);
            return a.Scheme == "Bearer" ? throw new ComicVineException("rejected", 100) { HttpStatus = 401 } : Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("Token", result.Working!.Scheme);
        Assert.Equal(new[] { "Bearer", "Token" }, tried);
    }

    [Fact]
    public async Task Probe_gives_up_after_both_schemes_are_refused()
    {
        int calls = 0;
        var result = await MetronAuthProbe.ProbeAsync(MetronAuth.Token("abc"), (_, _) =>
        {
            calls++;
            throw new ComicVineException("rejected", 100) { HttpStatus = 401 };
        }, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(2, calls);
        Assert.Contains("both", result.Message);
    }

    [Fact]
    public async Task Probe_does_not_retry_a_legacy_login_or_a_non_401_failure()
    {
        int calls = 0;
        Task Fail(MetronAuth _, CancellationToken __)
        {
            calls++;
            throw new ComicVineException("down") { HttpStatus = 503 };
        }

        var login = await MetronAuthProbe.ProbeAsync(MetronAuth.Basic("u", "p"), Fail, CancellationToken.None);
        var token = await MetronAuthProbe.ProbeAsync(MetronAuth.Token("abc"), Fail, CancellationToken.None);

        Assert.False(login.Accepted);
        Assert.False(token.Accepted);
        Assert.Equal(2, calls);
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"results\":[]}") });
        }
    }
}
