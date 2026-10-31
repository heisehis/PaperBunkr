namespace Paperbunkr.Plugins;

/// <summary>One search hit from a plugin metadata provider.</summary>
public sealed record PluginMetadataMatch(string Id, string Title, string? Url = null, string? Detail = null);

/// <summary>
/// Series metadata from a plugin provider. Every field is optional; only the ones set become proposals the user reviews -
/// nothing is written without their approval.
/// </summary>
public sealed record PluginSeriesMetadata
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    /// <summary><c>Ongoing</c>, <c>Completed</c>, <c>Cancelled</c> or <c>Hiatus</c> (case-insensitive); anything else is ignored.</summary>
    public string? Status { get; init; }

    public string? Publisher { get; init; }
    public int? StartYear { get; init; }
    public string? CoverUrl { get; init; }
    public string? Url { get; init; }
}

/// <summary>What the cover picker is choosing a cover for.</summary>
public sealed record PluginCoverRequest(string SeriesName, string? Number, int? Year, int? IssueId, int? SeriesId);

/// <summary>One cover a plugin offers: a small image for the picker grid and the full image applied when chosen.</summary>
public sealed record PluginCover(string ThumbnailUrl, string FullUrl, string? Label = null);

/// <summary>One story arc or reading list a plugin source can import.</summary>
public sealed record PluginArc(string Id, string Name, string? Publisher = null, int? Year = null);

/// <summary>One entry of an arc, in reading order.</summary>
public sealed record PluginArcEntry(string Series, string Number, string? Volume = null, int? Year = null);

/// <summary>
/// Lets a plugin add sources to the app (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §C).
/// Register from a <c>Startup</c> command (script plugins) or <c>RegisterCommands</c> (native plugins); a registration lasts until the
/// plugin is reloaded or disabled, and registering the same kind again replaces the earlier one. Every call the app makes is limited to
/// 30 seconds - a timeout or exception shows as "source unavailable" and is written to the plugin's log, never as a hang.
/// </summary>
public interface IPluginProviders
{
    /// <summary>Adds a series metadata source to the Detail screen's metadata search. Results become proposals the user approves.</summary>
    void RegisterMetadata(
        string displayName,
        Func<string, CancellationToken, Task<IReadOnlyList<PluginMetadataMatch>>> search,
        Func<string, CancellationToken, Task<PluginSeriesMetadata?>> get);

    /// <summary>Adds a group of covers to the cover picker.</summary>
    void RegisterCovers(string displayName, Func<PluginCoverRequest, CancellationToken, Task<IReadOnlyList<PluginCover>>> getCovers);

    /// <summary>
    /// Adds a story-arc / reading-list source to the CBL source list. <paramref name="browsable"/>: an empty search lists the whole
    /// catalog (a small fixed list) rather than nothing.
    /// </summary>
    void RegisterArcSource(
        string displayName,
        bool browsable,
        Func<string, CancellationToken, Task<IReadOnlyList<PluginArc>>> search,
        Func<string, CancellationToken, Task<IReadOnlyList<PluginArcEntry>>> getEntries);
}

/// <summary>What an environment without provider support offers: registrations are ignored.</summary>
public sealed class UnavailablePluginProviders : IPluginProviders
{
    public static UnavailablePluginProviders Instance { get; } = new();

    public void RegisterMetadata(string displayName, Func<string, CancellationToken, Task<IReadOnlyList<PluginMetadataMatch>>> search, Func<string, CancellationToken, Task<PluginSeriesMetadata?>> get)
    {
    }

    public void RegisterCovers(string displayName, Func<PluginCoverRequest, CancellationToken, Task<IReadOnlyList<PluginCover>>> getCovers)
    {
    }

    public void RegisterArcSource(string displayName, bool browsable, Func<string, CancellationToken, Task<IReadOnlyList<PluginArc>>> search, Func<string, CancellationToken, Task<IReadOnlyList<PluginArcEntry>>> getEntries)
    {
    }
}
