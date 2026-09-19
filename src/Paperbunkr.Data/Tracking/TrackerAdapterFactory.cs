using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Tracking.Adapters;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// The one place that knows which <see cref="TrackingService"/>s Paperbunkr can sync with, how to
/// tell whether each is connected, how to build its <see cref="ITrackerAdapter"/>, and how to reach
/// its detailed-error push (docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md
/// §5). Previously duplicated across four separate <c>switch</c>es in <c>DetailTabsViewModel</c> -
/// a service missing from any one was silently skipped (a real MangaDex omission went unreported
/// until a live "synced to MangaBaka but no mention of MangaDex" report). The automatic push/pull
/// paths must not inherit that failure mode.
/// </summary>
public static class TrackerAdapterFactory
{
    public static readonly TrackingService[] SupportedServices =
    {
        TrackingService.AniList, TrackingService.MyAnimeList, TrackingService.Shikimori, TrackingService.Bangumi, TrackingService.MangaBaka,
        TrackingService.MangaUpdates, TrackingService.Kitsu, TrackingService.MangaDex,
    };

    public static bool IsSupported(TrackingService service) => System.Array.IndexOf(SupportedServices, service) >= 0;

    /// <summary>Whether a usable credential is stored for <paramref name="service"/> (false for an
    /// unsupported service, so callers skip it rather than mis-route it).</summary>
    public static bool IsConnected(PaperbunkrDbContext context, TrackingService service) => service switch
    {
        TrackingService.AniList => CredentialStore.HasCredentials(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken),
        TrackingService.MyAnimeList => CredentialStore.HasCredentials(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken),
        TrackingService.Shikimori => CredentialStore.HasCredentials(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken),
        TrackingService.Bangumi => CredentialStore.HasCredentials(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey),
        TrackingService.MangaBaka => CredentialStore.HasCredentials(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey),
        TrackingService.MangaUpdates => CredentialStore.HasCredentials(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken),
        TrackingService.Kitsu => CredentialStore.HasCredentials(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken),
        TrackingService.MangaDex => CredentialStore.HasCredentials(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken),
        _ => false,
    };

    /// <summary>Stateless per-call adapter (matches every other provider construction site - no DI
    /// container). Null for a service Paperbunkr can't sync with (Metron, ComicVine).</summary>
    public static ITrackerAdapter? CreateAdapter(TrackingService service) => service switch
    {
        TrackingService.AniList => new AniListTrackerAdapter(AniListHttpClient.Shared),
        TrackingService.MyAnimeList => new MyAnimeListTrackerAdapter(TrackerHttpClients.MyAnimeList, clientId: null),
        TrackingService.Shikimori => new ShikimoriTrackerAdapter(TrackerHttpClients.Shikimori),
        TrackingService.Bangumi => new BangumiTrackerAdapter(TrackerHttpClients.Bangumi),
        TrackingService.MangaBaka => new MangaBakaTrackerAdapter(MangaBakaHttpClient.Shared),
        TrackingService.MangaUpdates => new MangaUpdatesTrackerAdapter(TrackerHttpClients.MangaUpdates),
        TrackingService.Kitsu => new KitsuTrackerAdapter(TrackerHttpClients.Kitsu, accessToken: null),
        TrackingService.MangaDex => new MangaDexTrackerAdapter(TrackerHttpClients.MangaDex),
        _ => null,
    };

    /// <summary>The adapter's own <see cref="ITrackerDetailedPush"/> (real error text) when it has one;
    /// any other adapter (e.g. a test fake) falls back to the bool-only
    /// <see cref="ITrackerAdapter.PushEntryAsync"/>.</summary>
    public static async Task<(bool Success, string? ErrorDetail)> PushDetailedAsync(ITrackerAdapter adapter, PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        if (adapter is ITrackerDetailedPush detailed)
        {
            return await detailed.PushEntryDetailedAsync(context, link, payload, cancellationToken).ConfigureAwait(false);
        }

        bool ok = await adapter.PushEntryAsync(context, link, payload, cancellationToken).ConfigureAwait(false);
        return (ok, ok ? null : "Push failed.");
    }
}
