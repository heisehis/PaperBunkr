using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ReadingLists.Sources;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// A plugin's story-arc source as an <see cref="IReadingListSource"/> (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-
/// pitch-completion-design.md §C), so the CBL source picker, list creation and Refresh treat it like any built-in source. Every call goes
/// through <see cref="PluginProviderRegistry.CallAsync{T}"/>; a failure or timeout surfaces as the <see cref="ReadingListSourceException"/>
/// the existing flows already show as a status line. <see cref="Unavailable"/> stands in for a source whose plugin is gone, so a list
/// imported from it still opens and its Refresh says why it can't.
/// </summary>
public sealed class PluginReadingListSource : IReadingListSource
{
    private readonly PluginProviderRegistry _registry;
    private readonly PluginArcSource _source;

    public PluginReadingListSource(PluginProviderRegistry registry, PluginArcSource source)
    {
        _registry = registry;
        _source = source;
    }

    public string SourceKey => _source.SourceKey;

    public string DisplayName => _source.DisplayName;

    public bool RequiresCredentials => false;

    public bool HasBrowsableCatalog => _source.Browsable;

    public async Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var arcs = await _registry.CallAsync(_source.PluginKey, $"{DisplayName} search", ct => _source.Search(query, ct), cancellationToken).ConfigureAwait(false)
            ?? throw Unanswered();
        return arcs.Select(a => new ArcSearchResult(a.Id, a.Name, a.Year?.ToString(), a.Publisher, 0)).ToList();
    }

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken)
    {
        var entries = await _registry.CallAsync(_source.PluginKey, $"{DisplayName} arc entries", ct => _source.GetEntries(arcId, ct), cancellationToken).ConfigureAwait(false)
            ?? throw Unanswered();
        return entries.Select(e => new ArcIssue(e.Series, e.Number, e.Year ?? 0, CoverImageUrl: null)).ToList();
    }

    public Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken) => Task.FromResult<ArcOverviewInfo?>(null);

    private ReadingListSourceException Unanswered() =>
        new(DisplayName, $"{DisplayName} didn't answer (the plugin's log says why).");

    /// <summary>What a <c>plugin:</c> key resolves to when its plugin isn't registered: every call says so.</summary>
    public sealed class Unavailable : IReadingListSource
    {
        public Unavailable(string sourceKey) => SourceKey = sourceKey;

        public string SourceKey { get; }

        public string DisplayName => SourceKey[PluginProviderRegistry.ArcSourcePrefix.Length..];

        public bool RequiresCredentials => false;

        public bool HasBrowsableCatalog => false;

        private ReadingListSourceException Gone() =>
            new(DisplayName, $"The \"{DisplayName}\" plugin isn't available (removed or disabled), so this list can't be refreshed from it.");

        public Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) => throw Gone();

        public Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken) => throw Gone();

        public Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken) => Task.FromResult<ArcOverviewInfo?>(null);
    }

    /// <summary>Wires <see cref="ReadingListSourceRegistry"/>'s extension hooks to <paramref name="registry"/>. Called once by the host.</summary>
    public static void Install(PluginProviderRegistry registry)
    {
        ReadingListSourceRegistry.ExtraSources = () => registry.ArcSources
            .Select(s => (s.SourceKey, s.DisplayName, false, s.Browsable))
            .ToList();
        ReadingListSourceRegistry.ResolveExtra = key =>
            !key.StartsWith(PluginProviderRegistry.ArcSourcePrefix, StringComparison.Ordinal)
                ? null
                : registry.FindArcSource(key) is { } source
                    ? new PluginReadingListSource(registry, source)
                    : new Unavailable(key);
    }
}
