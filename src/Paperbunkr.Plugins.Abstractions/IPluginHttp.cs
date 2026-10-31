namespace Paperbunkr.Plugins;

/// <summary>A request from <see cref="IPluginHttp"/> to a host the plugin didn't declare in its <c>&lt;Network&gt;</c>.</summary>
public sealed class PluginNetworkException : Exception
{
    public PluginNetworkException(string message) : base(message)
    {
    }
}

/// <summary>
/// HTTP for a plugin, limited to the hosts its <c>plugin.xml</c> declares (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-
/// 4-5-pitch-completion-design.md §F): <c>&lt;Network&gt;&lt;Host name="api.example.com"/&gt;&lt;/Network&gt;</c>, or <c>*.example.com</c>
/// for a domain and its subdomains. A request anywhere else throws <see cref="PluginNetworkException"/> and is written to the plugin's log.
/// The declared hosts are what the user sees when installing the plugin.
/// </summary>
public interface IPluginHttp
{
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default);

    Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}

/// <summary>What an environment without HTTP support offers: every request is refused.</summary>
public sealed class UnavailablePluginHttp : IPluginHttp
{
    public static UnavailablePluginHttp Instance { get; } = new();

    private static PluginNetworkException Refused() => new("Environment.Http needs Plugin API 4.5.");

    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default) => Task.FromException<string>(Refused());

    public Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default) => Task.FromException<byte[]>(Refused());

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) => Task.FromException<HttpResponseMessage>(Refused());
}
