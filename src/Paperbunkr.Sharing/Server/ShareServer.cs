using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Paperbunkr.Sharing.Protocol;

namespace Paperbunkr.Sharing.Server;

public sealed class ShareServerOptions
{
    /// <summary>0 picks a free port (tests); the app uses the user's configured port (default 7614).</summary>
    public int Port { get; set; } = 7614;

    /// <summary>Loopback until the host enables sharing beyond this machine.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;

    public string DisplayName { get; set; } = Environment.MachineName;

    public string InstanceId { get; set; } = Guid.NewGuid().ToString("D");

    /// <summary>A <see cref="PasswordHasher"/> string. The server refuses to start without one.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Off by default: clients outside loopback/private/VPN ranges are turned away (spec §1).</summary>
    public bool AllowPublicClients { get; set; }

    public int MaxFailedAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(1);

    public int MaxPageSize { get; set; } = 500;

    public int MaxImageWidth { get; set; } = 4096;

    /// <summary>Server-side diagnostics only. Never sent to a client.</summary>
    public Action<string>? Log { get; set; }
}

/// <summary>
/// The sharing server (spec §3 Approach A, §5): Kestrel over HTTPS, JSON for the catalog, raw bytes
/// for pages and covers. Everything except <c>/v1/hello</c> needs a session token from
/// <c>/v1/session</c>; that endpoint is rate-limited per client. Errors reach clients as a generic
/// body - detail goes to <see cref="ShareServerOptions.Log"/> only.
/// </summary>
public sealed class ShareServer : IAsyncDisposable
{
    private readonly ShareServerOptions _options;
    private readonly IShareCatalogSource _catalog;
    private readonly ISharePageSource _pages;
    private readonly X509Certificate2 _certificate;
    private readonly TimeProvider _time;

    private WebApplication? _app;

    public ShareServer(
        ShareServerOptions options,
        IShareCatalogSource catalog,
        ISharePageSource pages,
        X509Certificate2 certificate,
        TimeProvider? time = null)
    {
        _options = options;
        _catalog = catalog;
        _pages = pages;
        _certificate = certificate;
        _time = time ?? TimeProvider.System;

        Sessions = new SessionStore(options.SessionLifetime, _time);
        Limiter = new FailedAuthLimiter(options.MaxFailedAttempts, baseLockout: options.LockoutDuration, time: _time);
    }

    public SessionStore Sessions { get; }

    public FailedAuthLimiter Limiter { get; }

    /// <summary>The port actually bound (resolves <c>Port = 0</c>). Valid after <see cref="StartAsync"/>.</summary>
    public int Port { get; private set; }

    /// <summary>Client key (its address) each time a password attempt fails.</summary>
    public event Action<string>? AuthFailed;

    /// <summary>Client key each time a session is issued.</summary>
    public event Action<string>? SessionStarted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PasswordHash))
        {
            throw new InvalidOperationException("A sharing password must be set before the server can start.");
        }

        if (_app is not null)
        {
            throw new InvalidOperationException("The server is already running.");
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(_options.BindAddress, _options.Port, listen => listen.UseHttps(_certificate));
        });

        WebApplication app = builder.Build();
        MapPipeline(app);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        _app = app;
        Port = ResolveBoundPort(app);
    }

    public async Task StopAsync()
    {
        WebApplication? app = _app;
        _app = null;
        Sessions.Clear();
        if (app is not null)
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static int ResolveBoundPort(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        return new Uri(addresses.First().Replace("[::]", "localhost").Replace("0.0.0.0", "localhost")).Port;
    }

    private void MapPipeline(WebApplication app)
    {
        // Outermost: any unhandled failure becomes a generic body; the detail stays on the host.
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers[ProtocolVersion.HeaderName] = ProtocolVersion.Current.ToString();
            try
            {
                await next().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // Client went away mid-request - nothing to report.
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"Unhandled error on {ctx.Request.Method} {ctx.Request.Path}: {ex}");
                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.Clear();
                    ctx.Response.Headers[ProtocolVersion.HeaderName] = ProtocolVersion.Current.ToString();
                    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await ctx.Response.WriteAsJsonAsync(new ErrorResponse("internal_error")).ConfigureAwait(false);
                }
            }
        });

        // LAN/VPN only unless the host opted out.
        app.Use(async (ctx, next) =>
        {
            if (!_options.AllowPublicClients && !PrivateNetwork.IsPrivateOrLoopback(ctx.Connection.RemoteIpAddress))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("forbidden")).ConfigureAwait(false);
                return;
            }

            await next().ConfigureAwait(false);
        });

        // Session gate for everything under /v1 except hello and session.
        app.Use(async (ctx, next) =>
        {
            PathString path = ctx.Request.Path;
            bool open = path.StartsWithSegments("/v1/hello") || path.StartsWithSegments("/v1/session");
            if (path.StartsWithSegments("/v1") && !open && !Sessions.TryValidate(ReadBearerToken(ctx)))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("unauthorized")).ConfigureAwait(false);
                return;
            }

            await next().ConfigureAwait(false);
        });

        app.MapGet("/v1/hello", () => Results.Json(new HelloResponse(
            ProtocolVersion.Current, _options.InstanceId, _options.DisplayName, RequiresPassword: true)));

        // Typed as Func<..., Task<IResult>> on purpose: a method group or lambda returning Task<IResult>
        // also converts to RequestDelegate (Task), and the compiler prefers that overload - which binds
        // and then silently discards the IResult, answering 200 with an empty body.
        Func<HttpContext, Task<IResult>> session = HandleSessionAsync;
        app.MapPost("/v1/session", session);

        app.MapGet("/v1/lists", async (CancellationToken ct) => Results.Json(await _catalog.GetListsAsync(ct).ConfigureAwait(false)));

        app.MapGet("/v1/catalog", HandleCatalogAsync);

        app.MapGet("/v1/issues/{id:int}/pages", async (int id, CancellationToken ct) =>
        {
            if (!await _catalog.IsIssueSharedAsync(id, ct).ConfigureAwait(false))
            {
                return NotFound();
            }

            PagesResponse? pages = await _pages.GetPagesAsync(id, ct).ConfigureAwait(false);
            return pages is null ? NotFound() : Results.Json(pages);
        });

        app.MapGet("/v1/issues/{id:int}/pages/{index:int}", async (int id, int index, int? w, CancellationToken ct) =>
        {
            if (index < 0 || !await _catalog.IsIssueSharedAsync(id, ct).ConfigureAwait(false))
            {
                return NotFound();
            }

            return ToResult(await _pages.GetPageAsync(id, index, ClampWidth(w), ct).ConfigureAwait(false));
        });

        app.MapGet("/v1/issues/{id:int}/cover", async (int id, int? w, CancellationToken ct) =>
        {
            if (!await _catalog.IsIssueSharedAsync(id, ct).ConfigureAwait(false))
            {
                return NotFound();
            }

            return ToResult(await _pages.GetCoverAsync(id, ClampWidth(w), ct).ConfigureAwait(false));
        });
    }

    private async Task<IResult> HandleSessionAsync(HttpContext ctx)
    {
        string client = ClientKey(ctx);

        if (Limiter.IsBlocked(client, out TimeSpan retryAfter))
        {
            ctx.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString("0");
            return Results.Json(new ErrorResponse("too_many_attempts"), statusCode: StatusCodes.Status429TooManyRequests);
        }

        SessionRequest? body = null;
        try
        {
            body = await ctx.Request.ReadFromJsonAsync<SessionRequest>(ctx.RequestAborted).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
        }

        if (body is null || !PasswordHasher.Verify(body.Password, _options.PasswordHash))
        {
            Limiter.RecordFailure(client);
            AuthFailed?.Invoke(client);
            return Results.Json(new ErrorResponse("invalid_credentials"), statusCode: StatusCodes.Status401Unauthorized);
        }

        Limiter.RecordSuccess(client);
        var (token, expiresAt) = Sessions.Create();
        SessionStarted?.Invoke(client);
        return Results.Json(new SessionResponse(token, expiresAt));
    }

    private async Task<IResult> HandleCatalogAsync(HttpContext ctx, string? cursor, int? pageSize, CancellationToken ct)
    {
        string version = await _catalog.GetCatalogVersionAsync(ct).ConfigureAwait(false);
        string etag = $"\"{version}\"";

        // Only the first page is conditional: a client that already holds this version skips the whole pull.
        if (cursor is null && ctx.Request.Headers.IfNoneMatch.Any(v => v == etag))
        {
            ctx.Response.Headers.ETag = etag;
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        int size = Math.Clamp(pageSize ?? 200, 1, _options.MaxPageSize);
        CatalogPage page = await _catalog.GetCatalogPageAsync(cursor, size, ct).ConfigureAwait(false);

        ctx.Response.Headers.ETag = etag;
        return Results.Json(page with { CatalogVersion = version });
    }

    private int? ClampWidth(int? width) => width is null ? null : Math.Clamp(width.Value, 16, _options.MaxImageWidth);

    private static IResult NotFound() => Results.Json(new ErrorResponse("not_found"), statusCode: StatusCodes.Status404NotFound);

    private static IResult ToResult(PageContent? content)
    {
        if (content is null)
        {
            return NotFound();
        }

        // Range needs a seekable stream; fall back to a full response otherwise.
        return Results.Stream(content.Content, content.ContentType, enableRangeProcessing: content.Content.CanSeek);
    }

    private static string? ReadBearerToken(HttpContext ctx)
    {
        string? header = ctx.Request.Headers.Authorization.FirstOrDefault();
        const string prefix = "Bearer ";
        return header is not null && header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static string ClientKey(HttpContext ctx)
    {
        IPAddress? address = ctx.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        return address?.ToString() ?? "unknown";
    }
}
