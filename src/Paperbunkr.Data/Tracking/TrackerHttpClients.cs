using System;
using System.Net.Http;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Shared <see cref="HttpClient"/> instances for the new tracker adapters (docs/superpowers/specs/
/// 2026-08-23-tracker-write-back-sync-design.md) - one per service, same rationale as
/// <c>AniListHttpClient</c> (no DI container/<c>IHttpClientFactory</c> in this app, and this app's
/// single-user scale doesn't need one). Kept separate from <c>AniListHttpClient</c> itself since each
/// targets a different host.
/// </summary>
public static class TrackerHttpClients
{
    public static readonly HttpClient MyAnimeList = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static readonly HttpClient Shikimori = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static readonly HttpClient Bangumi = new() { Timeout = TimeSpan.FromSeconds(15) };
}
