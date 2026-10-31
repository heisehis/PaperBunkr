using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>A plugin's metadata source (Plugin API 4.5 §C).</summary>
public sealed record PluginMetadataProvider(
    string PluginKey,
    string DisplayName,
    Func<string, CancellationToken, Task<IReadOnlyList<PluginMetadataMatch>>> Search,
    Func<string, CancellationToken, Task<PluginSeriesMetadata?>> Get);

/// <summary>A plugin's cover source (Plugin API 4.5 §C).</summary>
public sealed record PluginCoverProvider(
    string PluginKey,
    string DisplayName,
    Func<PluginCoverRequest, CancellationToken, Task<IReadOnlyList<PluginCover>>> GetCovers);

/// <summary>A plugin's story-arc source (Plugin API 4.5 §C). <see cref="SourceKey"/> is what an imported list stores.</summary>
public sealed record PluginArcSource(
    string PluginKey,
    string DisplayName,
    bool Browsable,
    Func<string, CancellationToken, Task<IReadOnlyList<PluginArc>>> Search,
    Func<string, CancellationToken, Task<IReadOnlyList<PluginArcEntry>>> GetEntries)
{
    public string SourceKey => PluginProviderRegistry.ArcSourceKeyFor(PluginKey);
}

/// <summary>
/// Every source plugins have registered through <c>Environment.Providers</c> (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-
/// api-4-5-pitch-completion-design.md §C). One of each kind per plugin - a second registration replaces the first. The app never calls a
/// plugin's delegate directly: <see cref="CallAsync{T}"/> limits it to <see cref="Timeout"/>, turns any exception or timeout into null
/// ("source unavailable"), and writes what happened to the plugin's own log. Thread-safe.
/// </summary>
public sealed class PluginProviderRegistry
{
    public const string ArcSourcePrefix = "plugin:";

    private readonly object _gate = new();
    private readonly Dictionary<string, PluginMetadataProvider> _metadata = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginCoverProvider> _covers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginArcSource> _arcs = new(StringComparer.Ordinal);

    public PluginProviderRegistry(PluginLogFiles logFiles)
    {
        LogFiles = logFiles;
    }

    /// <summary>The app-wide registry, shared by the plugin host and the screens that offer plugin sources.</summary>
    public static PluginProviderRegistry Shared { get; } = new(PluginLogFiles.Default);

    public PluginLogFiles LogFiles { get; set; }

    /// <summary>The longest any one plugin call may take. Tests shorten it.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public static string ArcSourceKeyFor(string pluginKey) => ArcSourcePrefix + pluginKey;

    public IReadOnlyList<PluginMetadataProvider> MetadataProviders
    {
        get { lock (_gate) return _metadata.Values.OrderBy(p => p.DisplayName).ToList(); }
    }

    public IReadOnlyList<PluginCoverProvider> CoverProviders
    {
        get { lock (_gate) return _covers.Values.OrderBy(p => p.DisplayName).ToList(); }
    }

    public IReadOnlyList<PluginArcSource> ArcSources
    {
        get { lock (_gate) return _arcs.Values.OrderBy(p => p.DisplayName).ToList(); }
    }

    public PluginArcSource? FindArcSource(string sourceKey)
    {
        lock (_gate)
        {
            return sourceKey.StartsWith(ArcSourcePrefix, StringComparison.Ordinal)
                ? _arcs.GetValueOrDefault(sourceKey[ArcSourcePrefix.Length..])
                : null;
        }
    }

    /// <summary>The <see cref="IPluginProviders"/> a plugin's environment hands it: registrations land here under its key.</summary>
    public IPluginProviders ForPlugin(string pluginKey) => new Registrar(this, pluginKey);

    /// <summary>Drops everything <paramref name="pluginKey"/> registered (it was removed, disabled, or is about to re-register).</summary>
    public void Remove(string pluginKey)
    {
        lock (_gate)
        {
            _metadata.Remove(pluginKey);
            _covers.Remove(pluginKey);
            _arcs.Remove(pluginKey);
        }
    }

    /// <summary>Drops every registration whose plugin isn't in <paramref name="liveKeys"/>.</summary>
    public void RetainOnly(IEnumerable<string> liveKeys)
    {
        var keep = new HashSet<string>(liveKeys, StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (string key in _metadata.Keys.Concat(_covers.Keys).Concat(_arcs.Keys).Distinct().Where(k => !keep.Contains(k)).ToList())
            {
                _metadata.Remove(key);
                _covers.Remove(key);
                _arcs.Remove(key);
            }
        }
    }

    /// <summary>
    /// Runs one plugin call with the timeout and the exception guard. Null when it failed, timed out or was cancelled; the reason is
    /// written to the plugin's log (a cancellation by <paramref name="cancellationToken"/> isn't logged - the caller moved on).
    /// </summary>
    public async Task<T?> CallAsync<T>(string pluginKey, string what, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken = default)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            Task<T> work = Task.Run(() => call(timeout.Token), CancellationToken.None);
            Task finished = await Task.WhenAny(work, Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, timeout.Token)).ConfigureAwait(false);
            if (finished != work)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    LogFiles.Write(pluginKey, "WRN", $"{what} didn't answer within {Timeout.TotalSeconds:0} s - treated as unavailable.");
                }

                _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // observe a late failure
                return null;
            }

            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogFiles.Write(pluginKey, "ERR", $"{what} failed: {ex.Message}", ex);
            return null;
        }
    }

    private sealed class Registrar : IPluginProviders
    {
        private readonly PluginProviderRegistry _registry;
        private readonly string _pluginKey;

        public Registrar(PluginProviderRegistry registry, string pluginKey)
        {
            _registry = registry;
            _pluginKey = pluginKey;
        }

        public void RegisterMetadata(string displayName, Func<string, CancellationToken, Task<IReadOnlyList<PluginMetadataMatch>>> search, Func<string, CancellationToken, Task<PluginSeriesMetadata?>> get)
        {
            ArgumentNullException.ThrowIfNull(search);
            ArgumentNullException.ThrowIfNull(get);
            lock (_registry._gate)
            {
                _registry._metadata[_pluginKey] = new PluginMetadataProvider(_pluginKey, Name(displayName), search, get);
            }
        }

        public void RegisterCovers(string displayName, Func<PluginCoverRequest, CancellationToken, Task<IReadOnlyList<PluginCover>>> getCovers)
        {
            ArgumentNullException.ThrowIfNull(getCovers);
            lock (_registry._gate)
            {
                _registry._covers[_pluginKey] = new PluginCoverProvider(_pluginKey, Name(displayName), getCovers);
            }
        }

        public void RegisterArcSource(string displayName, bool browsable, Func<string, CancellationToken, Task<IReadOnlyList<PluginArc>>> search, Func<string, CancellationToken, Task<IReadOnlyList<PluginArcEntry>>> getEntries)
        {
            ArgumentNullException.ThrowIfNull(search);
            ArgumentNullException.ThrowIfNull(getEntries);
            lock (_registry._gate)
            {
                _registry._arcs[_pluginKey] = new PluginArcSource(_pluginKey, Name(displayName), browsable, search, getEntries);
            }
        }

        private string Name(string displayName) => string.IsNullOrWhiteSpace(displayName) ? _pluginKey : displayName.Trim();
    }
}
