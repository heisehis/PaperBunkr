using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// <see cref="IPluginHttp"/> for one plugin (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §F):
/// a request goes out only when its host is one the plugin's <c>&lt;Network&gt;</c> declares; anything else throws
/// <see cref="PluginNetworkException"/> and is written to the plugin's log. Uses one shared <see cref="HttpClient"/> (with the app's
/// user agent) for every plugin, so a plugin can't exhaust sockets by creating its own per call.
/// </summary>
public sealed class PluginHttp : IPluginHttp
{
    private static readonly Lazy<HttpClient> SharedClient = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr-Plugin/" + PluginApi.Current);
        return client;
    });

    private readonly IReadOnlyList<string> _declared;
    private readonly IPluginLogger _log;
    private readonly HttpMessageInvoker _client;

    public PluginHttp(IReadOnlyList<string> declared, string pluginKey, IPluginLogger log, HttpMessageInvoker? client = null)
    {
        _declared = declared;
        _log = log;
        _client = client ?? SharedClient.Value;
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        Uri? uri = request.RequestUri;
        if (uri is null || !uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw Refuse($"'{uri}' isn't an http(s) address.");
        }

        if (!PluginNetworkHosts.Allows(_declared, uri.Host))
        {
            throw Refuse($"{uri.Host} isn't declared in plugin.xml's <Network> ({(_declared.Count == 0 ? "no hosts are declared" : string.Join(", ", _declared))}).");
        }

        return _client.SendAsync(request, cancellationToken);
    }

    private PluginNetworkException Refuse(string message)
    {
        _log.Warn("Environment.Http refused a request: " + message);
        return new PluginNetworkException(message);
    }
}
