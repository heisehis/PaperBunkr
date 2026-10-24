# Provider utilization audit — 2026-10-15

Scope: the data the current Paperbunkr features can consume from linked manga/comic metadata providers, Metron, the GCD extract, pull lists, and trackers. “Fully utilized” does not mean calling every endpoint on a service: reviews, social feeds, lists and account administration are separate product features.

## Metadata

| Provider | Current path | This pass |
|---|---|---|
| AniList | Search and linked series fetch; status, description, chapters/volumes, title variants, staff, tags, relations, score, MAL id | Fixed GraphQL selection for fields that the normalizer already expected; added partial start/end dates and popularity as attributed source details. Its 18+ flag now maps to an adult age rating without claiming pornographic content. |
| MangaBaka | Search, series details, weighted tags, publisher/creator/rating, relation and cover endpoints | Retains extra upstream source identifiers, including Anime News Network, in the linked normalized snapshot. Known identifiers continue through `ExternalMediaId`. |
| MangaDex | Search, manga details and statistics | Prior pass added ratings, credits, alternate titles and tag groups. |
| ComicVine | Volume/issue search, issue details, releases | Repaired missing `field_list=` in volume search; series description now reaches blank series summary from the existing volume result. Release pagination reports incomplete responses as failures. |
| Metron | Series and issue search/details, release list, GCD/CV ids, universes | Series status/description flow was added in the prior pass. Release pagination reports incomplete responses as failures. |

Provider publication intervals, popularity and unmapped identifiers are **attributed source reports** on the shared comic/manga Details screen. They are projected from the latest normalized snapshot for the currently linked identity. They never set an issue's release date or overwrite canonical series values. This is a deliberate exception to the older audit-only snapshot design, recorded in `2026-10-15-provider-completion-design.md`.

## Other integrations

| Integration | Existing use and disposition |
|---|---|
| Wikidata | The active fetch flow was intentionally retired in favor of Metron. Re-enabling it would undo the detail-screen decision. The legacy client remains for old data and tests. |
| GCD | The installed extract covers series IDs, nonvariant issues, dates and series bonds. Full bibliographic extraction was explicitly excluded by the GCD design. The lookup budget can repeatedly visit the same unmatched Metron series; a durable retry cursor would be a separate schema change. |
| Pull lists | Metron or ComicVine release windows, issue identity/dates/covers and series enrichment. Incomplete pages now fail before cache replacement. Missing enrichment gets a backoff and later retry; prior null results no longer remain authoritative forever. |
| Trackers | Eight tracker adapters cover the current local status, chapter progress, score and finish date contract where each service supports them. Automatic push now refuses to overwrite remote progress if the read cannot be verified. Kitsu updates preserve privacy, and score/date pushes report an incomplete create+update as failed. |

## Remaining integration decisions

- `ITrackerAdapter.GetEntryAsync` still returns `null` for both absent entries and failed reads. The automatic push now chooses safety, so first-time remote entry creation requires manual sync until the contract returns a three-way result. This needs an adapter-by-adapter migration with verified 404/empty semantics and request tests.
- MAL, Shikimori and Kitsu store refresh tokens but do not use them. Their OAuth refresh flows need service-specific 401 retry and credential rotation tests. Shikimori's [OAuth guide](https://shikimori.io/oauth) confirms one-day expiry and refresh token exchange. The existing MangaDex adapter shows the local pattern. A failed refresh must show reconnect-required.
- GCD Metron lookup can starve later series under its 60-lookup cap. The pull-list cache sees Metron's GCD ID but does not persist it for the matcher. Both benefit from a durable lookup state design rather than repeated API calls.
- AniList exposes volume progress, start dates and notes, but the local tracker contract has no corresponding fields. They need an explicit user-facing tracking design. GCD stories/credits/covers and Wikidata reactivation are outside the approved integration scope.

Verification is fixture-based: the consolidated provider, manga, ComicVine, pull-list and Kitsu Data tests passed (173/173), and the detail-screen/tracker App tests passed (142/142). No authenticated live provider calls or real-library launch were run. AniList fields were checked against the [official Media reference](https://anilist.gitbook.io/anilist-apiv2-docs/docs/reference/object/media); Shikimori refresh semantics against its [OAuth guide](https://shikimori.io/oauth); MangaBaka schema stability against its [API page](https://mangabaka.org/data/api).
