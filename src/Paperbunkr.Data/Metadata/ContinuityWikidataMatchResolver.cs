using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// One reviewable "this series looks like it belongs to this shared universe" suggestion
/// (docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 2).
/// <see cref="UniverseDescription"/> is Wikidata's own short gloss (e.g. "fictional universe in
/// Marvel Comics") - verified reliably present on every real universe item checked during design,
/// unlike a publisher claim (<c>P123</c>), which is absent on every universe item checked (Earth-616,
/// Earth-1610, Prime Earth) and not worth wiring up since it would just come back empty. Exactly one
/// of <see cref="WikidataQid"/>/<see cref="FandomKey"/> is ever set - most numbered Marvel/DC
/// alternate Earths have no Wikidata item at all (confirmed for several during design), so a
/// suggestion sourced from one of those carries a fan-wiki key instead of pretending a Wikidata QID
/// exists.
/// </summary>
public sealed record ContinuitySuggestion(Series Series, string? WikidataQid, string? FandomKey, string UniverseLabel, string? UniverseDescription, FormatSignalStrength Strength, string Reason);

/// <summary>
/// Groups <see cref="Series"/> into shared-universe <see cref="Continuity"/> suggestions by
/// resolving their most-recurring <see cref="Character"/> entities against Wikidata
/// (docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 2).
/// Series-title lookup was tried and rejected during design (0/5 hit rate on a real spot-check) -
/// this goes through characters instead, since individual characters carry Wikidata's
/// "from narrative universe" (<c>P1080</c>)/"takes place in fictional universe" (<c>P1434</c>)
/// properties far more reliably than a series item itself does. "Propose, don't assert" - creates
/// nothing; the Story Events screen turns a suggestion into a real <see cref="Continuity"/>
/// membership only on the user's explicit Accept.
/// </summary>
internal static class ContinuityWikidataMatchResolver
{
    /// <summary>Wikidata's "comics character" class - verified during design against real data (Spider-Man and the Marvel-Comics Thor both carry this P31 value; a same-named non-comics item does not).</summary>
    private const string ComicsCharacterQid = "Q1114461";

    /// <summary>DC's "Elseworlds" imprint (a real Wikidata item, P31 = "comic book imprint") - appears as a P123 (publisher) value on individual Elseworlds work items even though it's editorial, not a legal publisher. Verified on "Batman: Gotham by Gaslight" (Q2891515) during design.</summary>
    private const string ElseworldsImprintQid = "Q194712";

    /// <summary>"Alternate history comics" genre - the P136 value on the same verified Elseworlds example, used as a second, imprint-independent signal (covers non-DC alternate-timeline one-shots that carry this genre tag without the Elseworlds imprint itself).</summary>
    private const string AlternateHistoryComicsGenreQid = "Q16657177";

    /// <summary>
    /// Local, free, zero-network markers for a "one-off, non-canon alternate timeline" story
    /// (Elseworlds/What If/the classic Silver-Age "Imaginary Story" term/Amalgam) - these describe a
    /// story that deliberately does NOT belong to whatever shared universe its characters normally
    /// live in, so a series carrying one of these should never be suggested for a mainstream
    /// Continuity via the character-voting path below. Deliberately does NOT include "Ultimate" -
    /// Ultimate Marvel (Earth-1610) is itself a legitimate, reusable shared universe and a real
    /// Continuity target in this app, not a one-off aside.
    /// </summary>
    private static readonly string[] NonCanonMarkers = { "Elseworlds", "Elseworld", "What If", "Imaginary Story", "Imaginary Tale", "Amalgam" };

    /// <summary>Wikidata's "fictional universe" class (verified during design: Earth-616/Earth-1610/Earth-295 are all directly P31 this).</summary>
    private const string FictionalUniverseQid = "Q559618";

    /// <summary>Wikidata's "DC Multiverse world" class - New Earth/Prime Earth/Earth-2/Earth-3 are P31 this, which is itself P279 (subclass of) <see cref="FictionalUniverseQid"/>. Checked as a second class directly rather than walking the full P279* chain, since these are the only subclass this app has verified in practice.</summary>
    private const string DcMultiverseWorldQid = "Q92343145";

    /// <summary>
    /// One <see cref="LocationUniverseWhitelist"/> entry - exactly one of <see cref="Qid"/>/
    /// <see cref="FandomKey"/> is set. Most numbered Marvel/DC alternate Earths simply have no
    /// Wikidata item (confirmed for several during design, e.g. Earth-928, Earth-58163), so most
    /// entries here carry a fan-wiki key instead of a real QID; <see cref="Description"/> is a short
    /// hand-written gloss for those (no Wikidata item to pull one from), left null for real-QID
    /// entries since <see cref="ResolveUniverseDetailsAsync"/> fetches Wikidata's own description
    /// for those instead.
    /// </summary>
    private sealed record LocationWhitelistEntry(string? Qid, string? FandomKey, string Label, string? Description = null);

    /// <summary>
    /// A comic tagged with a <see cref="Issue.Locations"/> value naming a specific parallel-earth
    /// designation (e.g. "Earth-616") is telling you its universe directly - more explicit than
    /// inferring one from character voting (user's own real-world observation prompted this: some
    /// taggers/scrapers do put these in the Location field). Sourced from the Marvel Database/DC
    /// Database fan wikis, cross-checked against Wikidata where a real item exists. Deliberately
    /// exact-match, case-insensitive, no fuzzy variants attempted yet ("Earth 616" without the
    /// hyphen, bare "616", etc.) - add aliases here if real library data turns up a variant this
    /// misses. "Earth-2" covers both the Golden-Age and New-52 "Earth 2" - Wikidata itself doesn't
    /// split them into separate items, so a location tag alone can't disambiguate which era it means.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, LocationWhitelistEntry> LocationUniverseWhitelist = new Dictionary<string, LocationWhitelistEntry>(StringComparer.OrdinalIgnoreCase)
    {
        // Real Wikidata QIDs, verified during design.
        ["Earth-616"] = new(Qid: "Q2246088", FandomKey: null, Label: "Earth-616"),
        ["Earth-1610"] = new(Qid: "Q116671834", FandomKey: null, Label: "Earth-1610"),
        ["Earth-295"] = new(Qid: "Q111976149", FandomKey: null, Label: "Earth-295"),
        ["New Earth"] = new(Qid: "Q28195263", FandomKey: null, Label: "New Earth"),
        ["Prime Earth"] = new(Qid: "Q113001257", FandomKey: null, Label: "Prime Earth"),
        ["Earth-2"] = new(Qid: "Q1752718", FandomKey: null, Label: "Earth-Two"),
        ["Earth-Two"] = new(Qid: "Q1752718", FandomKey: null, Label: "Earth-Two"),
        ["Earth-3"] = new(Qid: "Q3984524", FandomKey: null, Label: "Earth-Three"),
        ["Earth-Three"] = new(Qid: "Q3984524", FandomKey: null, Label: "Earth-Three"),

        // Fan-wiki-sourced (marvel.fandom.com/dc.fandom.com), verified live against those wikis -
        // none of these were independently re-checked against Wikidata itself (unlike the block
        // above), so they're all fandom-key entries even though a few might turn out to also have a
        // real Wikidata item; that would just mean a missed opportunity to reuse Wikidata's own
        // description text, not an incorrect match. Two Marvel/DC "Age of X"-style numbers from an
        // earlier, unverified guess turned out wrong on fetch (Earth-90214 is Marvel Noir, not Age of
        // Ultron; Earth-11326 is Age of X, not Mangaverse) - corrected here to the real numbers.
        // Left out as too low-confidence for real tagging: DC's Earth-13 (wiki's own description is
        // vague/contested) and Earth-16 (wiki flags contradictory sourcing across appearances).
        ["Earth-928"] = new(Qid: null, FandomKey: "Earth-928", Label: "Earth-928 (2099)", Description: "Marvel's futuristic 2099 timeline, home of Spider-Man 2099."),
        ["Earth-58163"] = new(Qid: null, FandomKey: "Earth-58163", Label: "Earth-58163 (House of M)", Description: "The reality created by Scarlet Witch's 2005 House of M event."),
        ["Earth-2149"] = new(Qid: null, FandomKey: "Earth-2149", Label: "Earth-2149 (Marvel Zombies)", Description: "The zombie-infected reality of the Marvel Zombies imprint."),
        ["Earth-Z"] = new(Qid: null, FandomKey: "Earth-2149", Label: "Earth-2149 (Marvel Zombies)", Description: "The zombie-infected reality of the Marvel Zombies imprint."),
        ["Earth-807128"] = new(Qid: null, FandomKey: "Earth-807128", Label: "Earth-807128 (Old Man Logan)", Description: "The devastated future reality of the Old Man Logan storyline."),
        ["Earth-90210"] = new(Qid: null, FandomKey: "Earth-807128", Label: "Earth-807128 (Old Man Logan)", Description: "The devastated future reality of the Old Man Logan storyline."),
        ["Earth-61112"] = new(Qid: null, FandomKey: "Earth-61112", Label: "Earth-61112 (Age of Ultron)", Description: "The reality reshaped by Ultron's conquest in the 2013 Age of Ultron event."),
        ["Earth-2301"] = new(Qid: null, FandomKey: "Earth-2301", Label: "Earth-2301 (Mangaverse)", Description: "Marvel's manga/anime-styled alternate reality."),
        ["Earth-65"] = new(Qid: null, FandomKey: "Earth-65", Label: "Earth-65 (Spider-Gwen)", Description: "Home reality of Ghost-Spider/Spider-Gwen."),
        ["Earth-14211"] = new(Qid: null, FandomKey: "Earth-65", Label: "Earth-65 (Spider-Gwen)", Description: "Home reality of Ghost-Spider/Spider-Gwen."),

        ["Earth-1"] = new(Qid: null, FandomKey: "Earth-1", Label: "Earth-1 (Earth One)", Description: "DC's standalone \"Earth One\" original-graphic-novel line, distinct from Prime Earth."),
        ["Earth-4"] = new(Qid: null, FandomKey: "Earth-4", Label: "Earth-4", Description: "Home of DC's Charlton-derived characters, the basis for Watchmen."),
        ["Earth-5"] = new(Qid: null, FandomKey: "Earth-5", Label: "Earth-5 (Marvel Family)", Description: "Home of the classic Shazam/Marvel Family cast."),
        ["Earth-8"] = new(Qid: null, FandomKey: "Earth-8", Label: "Earth-8 (Meta Militia)", Description: "Home of DC's explicit Marvel Comics analogue heroes."),
        ["Earth-10"] = new(Qid: null, FandomKey: "Earth-10", Label: "Earth-10 (Overman)", Description: "The reality where the Nazis won World War II."),
        ["Earth-11"] = new(Qid: null, FandomKey: "Earth-11", Label: "Earth-11", Description: "The reality where DC's major heroes are gender-flipped."),
        ["Earth-12"] = new(Qid: null, FandomKey: "Earth-12", Label: "Earth-12 (Batman Beyond)", Description: "The future reality of Batman Beyond."),
        ["Earth-19"] = new(Qid: null, FandomKey: "Earth-19", Label: "Earth-19 (Gaslight)", Description: "DC's Victorian-era reality, setting of Batman: Gotham by Gaslight."),
        ["Earth-1889"] = new(Qid: null, FandomKey: "Earth-19", Label: "Earth-19 (Gaslight)", Description: "DC's Victorian-era reality, setting of Batman: Gotham by Gaslight."),
        ["Earth-20"] = new(Qid: null, FandomKey: "Earth-20", Label: "Earth-20", Description: "DC's pulp-era reality."),
        ["Earth-21"] = new(Qid: null, FandomKey: "Earth-21", Label: "Earth-21 (New Frontier)", Description: "The reality of DC: The New Frontier."),
        ["Earth-22"] = new(Qid: null, FandomKey: "Earth-22", Label: "Earth-22 (Kingdom Come)", Description: "The near-future reality of Kingdom Come."),
        ["Earth-96"] = new(Qid: null, FandomKey: "Earth-22", Label: "Earth-22 (Kingdom Come)", Description: "The near-future reality of Kingdom Come."),
        ["Earth-23"] = new(Qid: null, FandomKey: "Earth-23", Label: "Earth-23 (President Superman)", Description: "Home of Calvin Ellis, the Superman who is also President."),
        ["Earth-30"] = new(Qid: null, FandomKey: "Earth-30", Label: "Earth-30 (Red Son)", Description: "The reality of Superman: Red Son."),
        ["Earth-1598"] = new(Qid: null, FandomKey: "Earth-30", Label: "Earth-30 (Red Son)", Description: "The reality of Superman: Red Son."),
    };

    private const int MaxCharactersPerSeries = 5;

    /// <summary>Same 30-day expiry rationale as <see cref="ArcExternalVerificationService"/>'s negative cache.</summary>
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(30);

    /// <summary>
    /// Caps an unattended full-library scan (<paramref name="onlySeriesId"/> null,
    /// <paramref name="capToBoundedRun"/> true) to a bounded run time - each series can cost up to 5
    /// characters x 2 Wikidata calls x the client's 1-second courtesy throttle, so an uncapped scan
    /// over a large library could run for the length of the whole app session with no feedback (real
    /// bug reported against v1: it did exactly this, before real progress reporting existed). Now
    /// that a caller can show real "N / Total series" progress (the manual "Check Wikidata" button
    /// does via Activity Center), an explicit, watched click can run uncapped instead of making the
    /// user re-click it repeatedly to cover a large library - see <paramref name="capToBoundedRun"/>.
    /// </summary>
    private const int MaxSeriesPerFullScan = 20;

    /// <param name="onlySeriesId">When set, scopes the scan to a single series - the per-series manual trigger's shape (docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md) rather than the full-library scan.</param>
    /// <param name="progress">
    /// Reported after each series finishes (done, total) - each series can take several seconds
    /// (up to 5 characters x 2 Wikidata calls x the client's 1-second throttle), and with no signal
    /// during the run the scheduled task's Activity Center row looked stuck for its whole duration
    /// (real feedback after this shipped without it).
    /// </param>
    /// <param name="capToBoundedRun">
    /// True (default) caps a full-library scan at <see cref="MaxSeriesPerFullScan"/> - the right
    /// choice for the unattended weekly scheduled task, which shouldn't tie up the network/DB for an
    /// unbounded time with nobody watching. False runs every eligible series in one call - the right
    /// choice for an explicit, interactive trigger with visible progress (the manual "Check Wikidata"
    /// button), where repeatedly re-clicking to cover a large library is the actual thing being
    /// fixed. Ignored when <paramref name="onlySeriesId"/> is set - that path is already scoped to
    /// one series.
    /// </param>
    /// <param name="onSuggestionFound">
    /// Invoked immediately when a suggestion survives dismissal filtering, before the scan of the
    /// remaining series continues - lets an interactive caller (the manual "Check Wikidata" button)
    /// show each result the moment it's found instead of making the user wait for every eligible
    /// series to finish before seeing anything (real complaint: an uncapped scan over a large
    /// library still takes a while end-to-end even though it no longer drops results across
    /// separate runs). The final return value still carries the complete list either way.
    /// </param>
    public static async Task<IReadOnlyList<ContinuitySuggestion>> GetSuggestionsAsync(
        PaperbunkrDbContext context,
        WikidataClient client,
        CancellationToken cancellationToken,
        int? onlySeriesId = null,
        IProgress<(int Done, int Total)>? progress = null,
        bool capToBoundedRun = true,
        Action<ContinuitySuggestion>? onSuggestionFound = null)
    {
        var alreadyInContinuity = context.ContinuityMemberships.Select(m => m.SeriesId).ToHashSet();

        var dismissed = context.ContinuitySuggestionDismissals
            .Select(d => new { d.SeriesId, d.WikidataQid })
            .AsEnumerable()
            .Select(d => (d.SeriesId, d.WikidataQid))
            .ToHashSet();

        var fandomDismissed = context.ContinuityFandomSuggestionDismissals
            .Select(d => new { d.SeriesId, d.FandomKey })
            .AsEnumerable()
            .Select(d => (d.SeriesId, d.FandomKey))
            .ToHashSet();

        DateTime cacheHorizon = DateTime.UtcNow - NegativeCacheTtl;
        var negativeCachedCharacterIds = context.ContinuityCharacterLookupNegativeCaches
            .Where(c => c.CheckedAt >= cacheHorizon)
            .Select(c => c.CharacterId)
            .ToHashSet();

        var eligibleSeries = context.Series
            .Where(s => !alreadyInContinuity.Contains(s.Id) && (onlySeriesId == null || s.Id == onlySeriesId))
            .ToList();

        // Shuffled, not Id-ordered, when capped - so a bounded run doesn't check the exact same
        // first N series every single time (a series with no Wikidata match burns through the
        // budget just as fast as one that matches, so without rotation the tail of a large library
        // would never get sampled). This shuffle-and-cap combination is also what caused a real,
        // reported bug: a user re-clicking the manual button to cover more of a large library saw
        // previously-surfaced suggestions silently vanish, because each click cleared the visible
        // list and repopulated it from a fresh random 20 that didn't always include the same series
        // twice. Uncapped (capToBoundedRun: false) avoids that entirely by covering every eligible
        // series in one pass, so there's no batch boundary for anything to fall across.
        var candidateSeries = onlySeriesId is not null || !capToBoundedRun
            ? eligibleSeries
            : eligibleSeries.OrderBy(_ => Guid.NewGuid()).Take(MaxSeriesPerFullScan).ToList();

        var universeLabelCache = new Dictionary<string, (string Label, string? Description)>();
        var suggestions = new List<ContinuitySuggestion>();
        int seriesIndex = 0;

        foreach (var series in candidateSeries)
        {
            // Reported once per series, up front - covers every exit path below (no characters, no
            // votes, dismissed) without needing a report call at each individual `continue`.
            progress?.Report((++seriesIndex, candidateSeries.Count));

            // Free, zero-network first pass: a series explicitly labeled as a one-off non-canon
            // story (Elseworlds/What If/etc.) never belongs in a mainstream Continuity no matter
            // which characters it uses, and skipping here also saves every character lookup below.
            if (IsLocallyMarkedNonCanon(context, series))
            {
                continue;
            }

            // A Location tag directly naming a universe is more explicit than inferring one from
            // characters - check it first, and if it hits, skip character-voting for this series
            // entirely rather than treating it as just one more vote.
            var locationMatch = await TryResolveLocationUniverseAsync(context, client, series, cancellationToken).ConfigureAwait(false);
            if (locationMatch is not null)
            {
                var (locationText, qid, fandomKey, locationLabel, locationDescription) = locationMatch.Value;
                bool isDismissed = qid is not null
                    ? dismissed.Contains((series.Id, qid))
                    : fandomDismissed.Contains((series.Id, fandomKey!));

                if (!isDismissed)
                {
                    // A real QID gets its description fetched from Wikidata (consistent with the
                    // character-vote path below); a fandom-only key has no Wikidata item to fetch
                    // from, so it keeps whatever hand-written gloss the whitelist entry carries.
                    string? resolvedDescription = locationDescription;
                    if (qid is not null)
                    {
                        (_, resolvedDescription) = await ResolveUniverseDetailsAsync(client, qid, universeLabelCache, cancellationToken).ConfigureAwait(false);
                    }

                    string locationReason = $"Location tag \"{locationText}\" names \"{locationLabel}\" directly";
                    var locationSuggestion = new ContinuitySuggestion(series, qid, fandomKey, locationLabel, resolvedDescription, FormatSignalStrength.Strong, locationReason);
                    suggestions.Add(locationSuggestion);
                    onSuggestionFound?.Invoke(locationSuggestion);
                }

                continue;
            }

            var characters = CharacterResolver.GetTopCharactersForSeries(context, series.Id, MaxCharactersPerSeries);
            if (characters.Count == 0)
            {
                continue;
            }

            var votes = new Dictionary<string, int>();
            foreach (var character in characters)
            {
                if (negativeCachedCharacterIds.Contains(character.Id))
                {
                    continue;
                }

                string? universeQid = await ResolveCharacterUniverseAsync(context, client, character, cancellationToken).ConfigureAwait(false);
                if (universeQid is not null)
                {
                    votes[universeQid] = votes.GetValueOrDefault(universeQid) + 1;
                }
            }

            if (votes.Count == 0)
            {
                continue;
            }

            var top = votes.OrderByDescending(kv => kv.Value).First();
            if (dismissed.Contains((series.Id, top.Key)))
            {
                continue;
            }

            // Second, network-based pass - only spent on a series that would otherwise actually
            // produce a suggestion, not on every series scanned. Catches an unlabeled Elseworlds-
            // style one-shot the local marker check above missed, by checking the WORK's own
            // Wikidata item (not the character's) for the Elseworlds imprint or an alternate-
            // history genre tag. Series-title search has a known low hit rate for ordinary series
            // (0/5 in a design-time spot-check) - a miss here just means "no extra signal found,"
            // never a reason to exclude on its own.
            if (await IsWikidataFlaggedNonCanonAsync(client, series.Name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var (label, description) = await ResolveUniverseDetailsAsync(client, top.Key, universeLabelCache, cancellationToken).ConfigureAwait(false);
            var strength = top.Value >= 2 ? FormatSignalStrength.Strong : FormatSignalStrength.Weak;
            string reason = $"{top.Value} of {characters.Count} recurring characters (by appearance count) link to \"{label}\" on Wikidata";

            var characterSuggestion = new ContinuitySuggestion(series, top.Key, null, label, description, strength, reason);
            suggestions.Add(characterSuggestion);
            onSuggestionFound?.Invoke(characterSuggestion);
        }

        return suggestions;
    }

    /// <summary>Checks <see cref="Series.Name"/> and every member issue's <c>SeriesGroup</c>/<c>Format</c> for a literal <see cref="NonCanonMarkers"/> word.</summary>
    private static bool IsLocallyMarkedNonCanon(PaperbunkrDbContext context, Series series)
    {
        if (ContainsNonCanonMarker(series.Name))
        {
            return true;
        }

        var issueFields = context.Issues
            .Where(i => i.SeriesId == series.Id)
            .Select(i => new { i.SeriesGroup, i.Format })
            .ToList();

        return issueFields.Any(f => ContainsNonCanonMarker(f.SeriesGroup) || ContainsNonCanonMarker(f.Format));
    }

    private static bool ContainsNonCanonMarker(string? text) =>
        !string.IsNullOrEmpty(text) && NonCanonMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static readonly char[] LocationSeparators = { ',', ';' };

    /// <summary>Splits a CE <c>Locations</c> string the same way <see cref="CharacterResolver.ParseNames"/> splits <c>Characters</c> - same CSV convention, different field.</summary>
    private static IEnumerable<string> ParseLocations(string? locationsText) =>
        (locationsText ?? string.Empty)
            .Split(LocationSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Checks every distinct <see cref="Issue.Locations"/> value across <paramref name="series"/>'s
    /// issues against <see cref="LocationUniverseWhitelist"/> first (free), then falls back to a
    /// live Wikidata search for any value not already known, checking whether a result's own
    /// <c>P31</c> is <see cref="FictionalUniverseQid"/> or <see cref="DcMultiverseWorldQid"/>. Stops
    /// at the first match; a location tag naming a universe is treated as authoritative, so there's
    /// no reason to keep checking once one hits.
    /// </summary>
    private static async Task<(string LocationText, string? Qid, string? FandomKey, string Label, string? Description)?> TryResolveLocationUniverseAsync(PaperbunkrDbContext context, WikidataClient client, Series series, CancellationToken cancellationToken)
    {
        var locations = context.Issues
            .Where(i => i.SeriesId == series.Id)
            .Select(i => i.Locations)
            .ToList()
            .SelectMany(ParseLocations)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string location in locations)
        {
            if (LocationUniverseWhitelist.TryGetValue(location, out var known))
            {
                return (location, known.Qid, known.FandomKey, known.Label, known.Description);
            }
        }

        // The static whitelist missed every tagged location - fall back to a live search. This can
        // only ever produce a real Qid (never a FandomKey), since it's reading live Wikidata data.
        foreach (string location in locations)
        {
            var results = await client.SearchEntitiesAsync(location, cancellationToken).ConfigureAwait(false);
            foreach (var result in results.Take(3))
            {
                var entity = await client.GetEntityAsync(result.Qid, cancellationToken).ConfigureAwait(false);
                if (entity is not null && (entity.InstanceOfQids.Contains(FictionalUniverseQid) || entity.InstanceOfQids.Contains(DcMultiverseWorldQid)))
                {
                    return (location, entity.Qid, null, entity.Label, entity.Description);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Searches Wikidata for the series title itself (not a character) and checks the top few
    /// results' own <c>P123</c>/<c>P136</c> claims for the Elseworlds imprint or an alternate-
    /// history genre tag - the signal <see cref="IsLocallyMarkedNonCanon"/> can't see because it
    /// only exists on the work's Wikidata item, not in local ComicInfo fields. Checks up to 3
    /// results since series-title search is inherently ambiguous; stops at the first match.
    /// </summary>
    private static async Task<bool> IsWikidataFlaggedNonCanonAsync(WikidataClient client, string seriesName, CancellationToken cancellationToken)
    {
        var results = await client.SearchEntitiesAsync(seriesName, cancellationToken).ConfigureAwait(false);
        foreach (var result in results.Take(3))
        {
            var entity = await client.GetEntityAsync(result.Qid, cancellationToken).ConfigureAwait(false);
            if (entity is not null && (entity.PublisherQids.Contains(ElseworldsImprintQid) || entity.GenreQids.Contains(AlternateHistoryComicsGenreQid)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Searches Wikidata for <paramref name="character"/>'s name, filters results to those whose
    /// <c>P31</c> includes <see cref="ComicsCharacterQid"/> (excludes same-named mythological/
    /// historical figures - e.g. searching "Thor" without this filter would risk matching the Norse
    /// god), and reads the first match's universe property. A character with no matching result gets
    /// a persisted negative-cache row so it isn't re-queried by every weekly run.
    /// </summary>
    private static async Task<string?> ResolveCharacterUniverseAsync(PaperbunkrDbContext context, WikidataClient client, Character character, CancellationToken cancellationToken)
    {
        var results = await client.SearchEntitiesAsync(character.Name, cancellationToken).ConfigureAwait(false);
        foreach (var result in results)
        {
            var entity = await client.GetEntityAsync(result.Qid, cancellationToken).ConfigureAwait(false);
            if (entity is not null && entity.InstanceOfQids.Contains(ComicsCharacterQid))
            {
                string? universeQid = entity.FromNarrativeUniverseQid ?? entity.TakesPlaceInFictionalUniverseQid;
                if (universeQid is not null)
                {
                    return universeQid;
                }

                // A real comics-character match with no universe property of its own - not a
                // "character not found" case, so no negative cache; just nothing to vote with.
                return null;
            }
        }

        RecordNegativeCache(context, character.Id);
        return null;
    }

    private static async Task<(string Label, string? Description)> ResolveUniverseDetailsAsync(WikidataClient client, string qid, Dictionary<string, (string Label, string? Description)> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(qid, out var cached))
        {
            return cached;
        }

        var entity = await client.GetEntityAsync(qid, cancellationToken).ConfigureAwait(false);
        var details = (Label: entity?.Label ?? qid, Description: entity?.Description);
        cache[qid] = details;
        return details;
    }

    private static void RecordNegativeCache(PaperbunkrDbContext context, int characterId)
    {
        bool already = context.ContinuityCharacterLookupNegativeCaches.Any(c => c.CharacterId == characterId);
        if (already)
        {
            return;
        }

        context.ContinuityCharacterLookupNegativeCaches.Add(new ContinuityCharacterLookupNegativeCache { CharacterId = characterId });
        context.SaveChanges();
    }

    /// <summary>Persists "never suggest this universe for this series again" - idempotent.</summary>
    public static void Dismiss(PaperbunkrDbContext context, int seriesId, string wikidataQid)
    {
        bool already = context.ContinuitySuggestionDismissals.Any(d => d.SeriesId == seriesId && d.WikidataQid == wikidataQid);
        if (already)
        {
            return;
        }

        context.ContinuitySuggestionDismissals.Add(new ContinuitySuggestionDismissal { SeriesId = seriesId, WikidataQid = wikidataQid });
        context.SaveChanges();
    }

    /// <summary>Clears a persisted dismissal so the universe can be suggested again for this series - a no-op if it isn't dismissed.</summary>
    public static void Restore(PaperbunkrDbContext context, int seriesId, string wikidataQid)
    {
        var row = context.ContinuitySuggestionDismissals.FirstOrDefault(d => d.SeriesId == seriesId && d.WikidataQid == wikidataQid);
        if (row is not null)
        {
            context.ContinuitySuggestionDismissals.Remove(row);
            context.SaveChanges();
        }
    }

    /// <summary>Same as <see cref="Dismiss"/>, for a <see cref="ContinuitySuggestion.FandomKey"/>-sourced suggestion (no real Wikidata QID).</summary>
    public static void DismissFandom(PaperbunkrDbContext context, int seriesId, string fandomKey)
    {
        bool already = context.ContinuityFandomSuggestionDismissals.Any(d => d.SeriesId == seriesId && d.FandomKey == fandomKey);
        if (already)
        {
            return;
        }

        context.ContinuityFandomSuggestionDismissals.Add(new ContinuityFandomSuggestionDismissal { SeriesId = seriesId, FandomKey = fandomKey });
        context.SaveChanges();
    }

    /// <summary>Same as <see cref="Restore"/>, for a <see cref="ContinuitySuggestion.FandomKey"/>-sourced suggestion.</summary>
    public static void RestoreFandom(PaperbunkrDbContext context, int seriesId, string fandomKey)
    {
        var row = context.ContinuityFandomSuggestionDismissals.FirstOrDefault(d => d.SeriesId == seriesId && d.FandomKey == fandomKey);
        if (row is not null)
        {
            context.ContinuityFandomSuggestionDismissals.Remove(row);
            context.SaveChanges();
        }
    }
}
