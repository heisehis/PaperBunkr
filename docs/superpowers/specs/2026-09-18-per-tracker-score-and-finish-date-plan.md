# Per-Tracker Score & Finish-Date — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-18-per-tracker-score-and-finish-date-design.md*

Current shapes confirmed by direct read this session: `ITrackerAdapter.cs` (`TrackerPushPayload`/
`TrackerRemoteEntry`, Status+ChapterProgress only), all 8 adapters in
`src/Paperbunkr.Data/Tracking/Adapters/`, `Series.cs`/`Issue.cs` (`Issue.Rating` float? 0-5),
`DetailTabsViewModel.cs` (`RefreshTrackerLinks`, `SyncTrackersAsync`, tracker-link chip state),
`DetailTabs.axaml` (chip `ItemsControl` at line ~837), `TrackerLinkSample.cs`.

## Step 1: Extend the push/pull contract
**Files:** `src/Paperbunkr.Data/Tracking/ITrackerAdapter.cs` (edit)
**What:** Add `decimal? Score` and `DateOnly? FinishDate` to both `TrackerPushPayload` and
`TrackerRemoteEntry`, both trailing/optional so every existing call site keeps compiling.
**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.Data`.

## Step 2: `Series.Rating` + migration
**Files:** `src/Paperbunkr.Data/Entities/Series.cs` (edit), new EF migration under
`src/Paperbunkr.Data/Migrations/`
**What:** `public float? Rating { get; set; }` on `Series`, doc comment noting it's the average of
rated `Issue.Rating` values, distinct from any single issue's own rating. No Fluent config needed
(plain nullable scalar, same as `Issue.Rating`). `dotnet ef migrations add AddSeriesRating` from
`src/Paperbunkr.Data` (matches this session's own earlier `AddSeriesCreatorAndExternalMediaRelation`
precedent).
**Depends on:** none
**Verify:** `dotnet ef migrations has-pending-model-changes` reports none; `dotnet build`.

## Step 3: `SeriesRatingResolver`
**Files:** `src/Paperbunkr.Data/Metadata/SeriesRatingResolver.cs` (new)
**What:** `public static float? Recompute(Series series)` - average of `series.Issues.Select(i =>
i.Rating).Where(r => r.HasValue)`, null when no issue has a rating (never 0). Caller's
responsibility to assign the result to `series.Rating` and `SaveChanges()` - this is a pure
function, matching `ProviderRelationTypeMapper`'s own "static, no context" shape from earlier this
session.
**Depends on:** Step 2
**Verify:** unit test - empty series → null; mixed rated/unrated issues → average of the rated
ones only; all-null issues → null, not 0.

## Step 4-11: Per-adapter Score/FinishDate wiring

Each step adds a `PushEntryDetailedAsync(... ) : Task<(bool Success, string? ErrorDetail)>`
mirroring the pattern already shipped this session for `MangaBakaTrackerAdapter`/
`MangaDexTrackerAdapter` (`PushEntryAsync` becomes a one-line delegate to it, same as those two),
threads `Score`/`FinishDate` through the same request where the tracker's real API supports it
(confirmed field names/scales are in the design doc's own capability table - use those, not
re-derived guesses), and extends `GetEntryAsync` to populate the new `TrackerRemoteEntry` fields
where the tracker supports reading them back.

### Step 4: AniList
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/AniListTrackerAdapter.cs` (edit)
**What:** `SaveMediaListEntryMutation` gains `$score: Float`/`$completedAt: FuzzyDateInput`
variables; `GetMediaListEntryQuery` gains `score`/`completedAt { year month day }`. New
`AniListScoreFormatMapper`: fetches `Viewer.mediaListOptions.scoreFormat` once per push/pull call
(`query { Viewer { mediaListOptions { scoreFormat } } }`, needs the bearer token already in hand)
and converts `Series.Rating` (0-5) into that exact format (`POINT_100` → ×20, `POINT_10_DECIMAL`/
`POINT_10` → ×2, `POINT_5` → as-is, `POINT_3` → round to 1/2/3) and back on pull. `FuzzyDate` →
`DateOnly` conversion: null when any of year/month/day is missing (a partial date can't become a
real `DateOnly`).
**Depends on:** Step 1
**Verify:** unit tests (fake `HttpMessageHandler`, existing `AniListTrackerAdapterTests.cs`
pattern) - score format fetched and applied for at least `POINT_100` and `POINT_10`; FuzzyDate
round-trip; partial FuzzyDate → null, not a crash.

### Step 5: MyAnimeList
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/MyAnimeListTrackerAdapter.cs` (edit)
**What:** `PushEntryAsync`'s form gains `score` (0-10 int, `Series.Rating` × 2 rounded) and
`finish_date` (`YYYY-MM-DD`) fields; `GetEntryAsync`'s `fields=` selector gains `score,finish_date`,
`MalListItemStatusDto` gains matching properties.
**Depends on:** Step 1
**Verify:** unit tests - score/date present in the PUT form body; absent/null local rating sends
no `score` field at all (not `0`, matching this tracker's own "0 = unscored" semantics).

### Step 6: MangaBaka
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/MangaBakaTrackerAdapter.cs` (edit)
**What:** The existing PUT/POST body gains `rating` (0-100 int, `Series.Rating` × 20) and
`finish_date` (`YYYY-MM-DD`). Reuses the PUT-then-POST-on-404 fallback already shipped this
session - no new fallback logic needed, just more fields on the same body. `GetEntryAsync`/
`MangaBakaLibraryEntryDto` gain `rating`/`finish_date`.
**Depends on:** Step 1 (this adapter's detailed-error path already exists from earlier this
session, so this step only adds fields, no new error-handling shape)
**Verify:** unit tests - rating/finish_date present in both the PUT and the POST-fallback body.

### Step 7: Kitsu
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/KitsuTrackerAdapter.cs` (edit)
**What:** `rating` (2-20 int, `Series.Rating` × 4 rounded, clamped 2-20; null/0 local rating sends
`rating: null` to clear rather than an invalid 0) and `finishedAt`/`startedAt` (ISO-8601) added to
the **update** mutation only - confirmed not accepted on create. `PushEntryAsync` (→
`PushEntryDetailedAsync`, new for this adapter) must therefore always create-then-update on a
series' first-ever push to Kitsu, mirroring `MangaUpdatesTrackerAdapter`'s existing add-then-update
precedent, not a new shape. `findLibManga`'s query gains `rating startedAt finishedAt`.
**Depends on:** Step 1
**Verify:** unit tests - first push (no existing library entry) issues create-then-update in that
order with score/date only on the second call; a push to an *existing* entry updates directly, one
call.

### Step 8: MangaDex
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/MangaDexTrackerAdapter.cs` (edit)
**What:** New `PushRatingAsync`/similar private helper hitting `POST /rating/{mangaId}` (body
`{ "rating": <1-10 int> }`, `Series.Rating` × 2 rounded and clamped 1-10) or `DELETE
/rating/{mangaId}` when the local rating is null/0 - separate call from the existing
`/manga/{id}/status` push, both fired from `PushEntryDetailedAsync`, same expired-token
refresh-and-retry wrapper this adapter already has (reuse `TryRefreshAccessTokenAsync`, don't
duplicate it). `GetEntryAsync` adds a `GET /rating?manga[]={id}` call to read the score back. No
finish-date wiring - confirmed absent from MangaDex's real API this session.
**Depends on:** Step 1
**Verify:** unit tests - score>0 posts to `/rating/{id}`; score cleared issues `DELETE`; a 401 on
the rating call also triggers the existing refresh-and-retry path.

### Step 9: MangaUpdates
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/MangaUpdatesTrackerAdapter.cs` (edit)
**What:** New `PushEntryDetailedAsync` (this adapter doesn't have one yet) wrapping the existing
add-then-update push plus a new `PUT /v1/series/{id}/rating` (body `{"rating": <0.1-10.0 decimal>}`,
`Series.Rating` × 2 rounded to 1 decimal) or `DELETE /v1/series/{id}/rating` when cleared - same
separate-endpoint/DELETE-on-clear shape as MangaDex, real API confirmed this session. `GetEntryAsync`
adds a `GET /v1/series/{id}/rating` call. No finish-date wiring - confirmed absent.
**Depends on:** Step 1
**Verify:** unit tests mirroring Step 8's shape for this service's own endpoint/decimal scale.

### Step 10: Shikimori
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/ShikimoriTrackerAdapter.cs` (edit)
**What:** New `PushEntryDetailedAsync`. The existing `user_rates` PUT/POST body gains `score`
(0-10 int, `Series.Rating` × 2 rounded) - same request as status/progress, no new endpoint.
`GetEntryAsync`/`ShikimoriUserRateDto` gain `score`. No finish-date wiring - confirmed absent from
the real fetched docs this session.
**Depends on:** Step 1
**Verify:** unit tests - score present in both the PUT and POST body shapes.

### Step 11: Bangumi
**Files:** `src/Paperbunkr.Data/Tracking/Adapters/BangumiTrackerAdapter.cs` (edit)
**What:** New `PushEntryDetailedAsync`. The existing collection-update body gains `rate` (0-10 int,
`Series.Rating` × 2 rounded) - same request as status/progress. `GetEntryAsync`/
`BangumiCollectionResponseDto` gain `rate`. No finish-date wiring - confirmed absent from the real
OpenAPI spec this session.
**Depends on:** Step 1
**Verify:** unit tests - `rate` present in the collection-update body.

## Step 12: `TrackerLinkSample` capability + value fields
**Files:** `src/Paperbunkr.App/Models/TrackerLinkSample.cs` (edit)
**What:** Add `bool SupportsScore`/`bool SupportsFinishDate` (computed from `Service`, per the
design doc's capability table - Score: all 8 true; FinishDate: AniList/MyAnimeList/MangaBaka/Kitsu
only), plus mutable `Score`/`FinishDate`/local-editing-buffer properties the expanded panel binds
to (`ObservableObject`, since this model now needs to support live inline edits, unlike its current
plain-record shape).
**Depends on:** none
**Verify:** unit test - `SupportsFinishDate` true only for the 4 confirmed services.

## Step 13: Expand-on-click panel state + per-field push
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit)
**What:**
- `[ObservableProperty] private TrackerLinkSample? _selectedTrackerLink;` + `[RelayCommand]
  ToggleTrackerLinkDetails(TrackerLinkSample link)` (toggles selection, clears if re-clicking the
  already-open one) - same shape as this file's existing `SelectedConnectionProvider`-style
  toggle precedents.
- On selecting a link, populate its `Score`/`FinishDate`/current `Status`/`ChapterProgress` from a
  fresh `GetEntryAsync` call (lazy, matches this session's own established lazy-fetch precedent -
  only fetched when the panel actually opens).
- New `[RelayCommand] PushTrackerFieldAsync(TrackerLinkSample link)` - builds a
  `TrackerPushPayload` from the panel's current (possibly just-edited) values and calls that one
  tracker's `PushEntryDetailedAsync` immediately, per the design's "immediate single-tracker push
  on edit" decision. Resolve the concrete adapter via the same `GetTrackerAdapter(link.Service)`
  switch this file already has (Step 4-11 already added `PushEntryDetailedAsync` to every case).
- New `[RelayCommand] UseTrackerScoreAsync(TrackerLinkSample link)` - the explicit pull action:
  re-fetches that tracker's remote score via `GetEntryAsync`, converts to the local 0-5 scale, sets
  `series.Rating` directly (bypassing `SeriesRatingResolver` - this is an explicit override, not a
  recompute), `SaveChanges()`.
- Unsupported fields (`!SupportsScore`/`!SupportsFinishDate`) never attempt a fetch/push for that
  field - the panel just doesn't render an editable control for it (see Step 14).
**Depends on:** Steps 4-11 (needs every adapter's `PushEntryDetailedAsync`), Step 12
**Verify:** ViewModel test - selecting a link populates its panel; editing Score and calling
`PushTrackerFieldAsync` invokes that tracker's push with the new value; `UseTrackerScoreAsync`
writes `series.Rating`, not the resolver-computed value.

## Step 14: XAML - expand-on-click panel
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit)
**What:** Each chip in the existing `ItemsControl` (line ~837) gets its `Border` wrapped in a
`Button`-like click handler (`Command="{Binding #Root.((vm:DetailTabsViewModel)DataContext).
ToggleTrackerLinkDetailsCommand}"` `CommandParameter="{Binding}"`) alongside the existing unlink
`✕`. Below the chip `ItemsControl`, a new `Border` (`IsVisible="{Binding SelectedTrackerLink,
Converter={x:Static ObjectConverters.IsNotNull}}"`) shows the selected link's Status/Progress/
Score/Finish-date rows - editable `TextBox`/`NumericUpDown`/`CalendarDatePicker` per field,
`IsVisible="{Binding SupportsScore}"`/`SupportsFinishDate}"` gating each row, a plain "Not
supported by this tracker" label in the else case (matches the design's "n/a, not hidden"
precedent). A "Use this score" button next to the Score row, bound to `UseTrackerScoreAsync`.
**Depends on:** Step 13
**Verify:** on-screen check (this project's UI-automation harness / manual) - click a chip, confirm
the panel expands with real pulled values; edit Score, confirm it posts to the real tracker (same
verification standard that caught the MangaBaka/MangaDex bugs).

## Testing summary

- Unit tests per adapter (Steps 4-11), fake `HttpMessageHandler`, extending each adapter's existing
  test file (`AniListTrackerAdapterTests.cs`, etc.) rather than new files - matches this session's
  own established per-adapter test file convention.
- `SeriesRatingResolverTests.cs` (new) for Step 3's average/null logic.
- `TrackerLinkSampleTests` (or inline in an existing App.Tests file) for Step 12's capability flags.
- `DetailTabsViewModelTests.cs` additions for Step 13's panel-open/push/use-score flow.
- On-screen verification for Step 14 - the only way to confirm a push actually lands correctly on
  each real service, same lesson this session already learned twice.
