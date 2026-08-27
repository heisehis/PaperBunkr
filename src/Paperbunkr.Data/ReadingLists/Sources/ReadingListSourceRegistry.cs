using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// Single place that knows how to list and construct <see cref="IReadingListSource"/> instances
/// (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §3) - UI code only ever talks
/// to <see cref="IReadingListSource"/>, adding a new source means adding one case here plus the
/// adapter class itself. <c>Comic Book Herald</c>, <c>CMRO</c>, <c>Grand Comics Database</c>,
/// <c>League of Comic Geeks</c>, and <c>MyComicList</c> are deliberately absent - confirmed
/// nonviable (Cloudflare-blocked, ToS-prohibited, or nonexistent) by the reference project's own
/// investigation.
/// </summary>
public static class ReadingListSourceRegistry
{
    public static readonly IReadOnlyList<(string Key, string DisplayName, bool RequiresCredentials, bool HasBrowsableCatalog)> All = new[]
    {
        ("ComicVine", "ComicVine", true, false),
        ("Metron", "Metron", true, false),
        ("ComicBookReadingOrders", "Comic Book Reading Orders", false, true),
        ("ComicArc", "ComicArc", false, true),
        ("ReadingOrdersNet", "ReadingOrders.com", false, true),
        ("ReadThingsRight", "ReadThingsRight", false, true),
    };

    /// <summary>Human-readable name for a stored <c>ReadingList.Source</c> key, for display - falls back to the raw key itself if it's ever unrecognized (e.g. a source removed in a future version) rather than showing nothing.</summary>
    public static string GetDisplayName(string sourceKey) =>
        All.FirstOrDefault(s => s.Key == sourceKey).DisplayName ?? sourceKey;

    /// <summary>Returns null when the source key is unknown, or a credentialed source is missing its required credentials.</summary>
    public static IReadingListSource? Get(PaperbunkrDbContext context, string sourceKey)
    {
        switch (sourceKey)
        {
            case "ComicVine":
                string? apiKey = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
                return string.IsNullOrEmpty(apiKey) ? null : new ComicVineSource(apiKey);

            case "Metron":
                string? username = CredentialStore.Get(context, "Metron", CredentialKind.Username);
                string? password = CredentialStore.Get(context, "Metron", CredentialKind.Password);
                return string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
                    ? null
                    : new MetronSource(username, password);

            case "ComicBookReadingOrders":
                return new ComicBookReadingOrdersSource();

            case "ComicArc":
                return new ComicArcSource();

            case "ReadingOrdersNet":
                return new ReadingOrdersNetSource();

            case "ReadThingsRight":
                return new ReadThingsRightSource();

            default:
                return null;
        }
    }
}
