# ComicVine Scraper Fidelity — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md*

Survey surfaced real signature-level decisions the design doc didn't pin down; each is resolved here
explicitly rather than guessed at implementation time:

1. **`MatchScoreCalculator.Compute`'s `bookIssueNumber` is already `int?`**, pre-parsed by
   `ScrapeOrchestrator.ParseIssueNumber` *before* the call (`ScrapeOrchestrator.cs:153`). The design's
   "scoped fractional parse inside bookscore only" requires `Compute`'s parameter to become the raw
   `string?` instead, with the double-parse moved inside `BookScore` itself — a real signature change,
   not just a new private helper alongside the existing one. `ScrapeOrchestrator.ParseIssueNumber`
   itself is untouched (used elsewhere for filtering by full-volume-issue-count comparisons — confirm
   at Step 6 whether anything else calls it before deciding whether it can be deleted).
2. **`PersonRoleMap` is `Dictionary<string,string>`** — one value per key. "Artist" fanning into
   Penciller *and* Inker needs the map retyped to `Dictionary<string,string[]>`, and the parsing loop
   (currently one `ComicVineCredit` per person, first-matched-role-wins) rewritten to emit one credit
   per *resolved field*, not per person.
3. **Layering**: `ScrapeOrchestrator` lives in `Paperbunkr.Data`; `CoverThumbnailService` (needed for
   the cover-hash gate's "book's own cover") lives in `Paperbunkr.App`. `Data` cannot reference `App`.
   Fix: thread a `Func<int, string?> getCoverPath` delegate into `ScrapeOrchestrator`'s constructor,
   supplied by `ScrapeCoordinator` (`Paperbunkr.App`) via `CoverThumbnailService.GetEffectiveCoverPath`.
   The new `CoverPerceptualHash` class (and its `SixLabors.ImageSharp`/`CoenM.ImageSharp.ImageHash`
   dependency) lives entirely in `Paperbunkr.Data` and never needs Avalonia — it decodes straight from
   the file path.
4. **`AutoChooseTopMatch`'s branch is a mutually-exclusive `if/else if`** against the interactive-review
   branch today (`ScrapeOrchestrator.cs:167-177`). The cover-hash gate needs it restructured into a
   fallthrough: compute `chosen` from the top-ranked candidate, run the gate, and on failure treat the
   book as if `AutoChooseTopMatch` were off *for that one book* (falls into whichever of
   interactive-review/skip-and-log the `isInteractive` flag already selects).
5. **No alt-cover data exists anywhere in the type system** — not just missing UI. `ComicVineIssueDetails`
   and `ComicVineIssueSummary`/`ComicVineVolumeSearchResult` all carry exactly one `ImageUrl`. Cover
   cycling (design §4.2) needs a new ComicVine field request (`associated_images` — unconfirmed against
   a live response, same caution as the design doc) and new DTO members before any ViewModel work.
6. **`ScrapeAsync` returns a single `int applied`** — no scraped/skipped/no-match/failed breakdown
   exists anywhere to build §4.1's summary from. This needs a richer return type, which also changes
   `ScrapeCoordinator.cs`'s consumption of it.
7. **Ctrl-modifier detection has no path through `[RelayCommand]`** — both review dialogs' Skip buttons
   bind `Command=`, which carries no modifier-key state. §4.3 needs either a code-behind `Click` handler
   reading `PointerEventArgs`/`KeyModifiers` or an `InputElement.KeyDown` binding, not a plain command.

---

## Phase 1 — Correctness bugs

### Step 1: Imprint/Publisher write fix
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs` (`ApplyVolumeAndIssueAsync`,
around lines 355-372)
**What:** Replace the unconditional `tracked.Imprint = volume.Publisher` write. Call
`ComicVineImprints.FindParentPublisher(volume.Publisher, _settings.ImprintOverrides)` first; if the
result differs from the raw `volume.Publisher` (i.e. it *was* a recognized imprint), write the resolved
value through `ScrapeField.Publisher`'s existing `ShouldWrite` check to `tracked.Publisher`, and the raw
`volume.Publisher` through `ScrapeField.Imprint`'s check to `tracked.Imprint`. If unresolved (result ==
raw), write the raw value to `Publisher` only; leave `Imprint` alone (don't blank an existing value the
user or a prior scrape set).
**Depends on:** none
**Verify:** new tests in `ScrapeOrchestratorTests.cs` (or wherever its integration tests live — locate
via `Glob` at implementation time) covering: recognized imprint → Publisher gets resolved value, Imprint
gets raw value; unrecognized publisher → Publisher gets raw value, Imprint untouched.

### Step 2: Ignore-blanks exemption for Series/Number
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/IssueDetailsApplier.cs` (the `Text(ScrapeField.Number, ...)`
call), `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs` (the Series-name write in
`ApplyVolumeAndIssueAsync`, `ShouldWrite(ScrapeField.Series, ...)` call)
**What:** Both call sites bypass the policy's global `IgnoreBlankValues` for these two fields
specifically — either by calling `ShouldWrite` with a hardcoded `ignoreBlankValues: true` override (if
`ScrapeFieldPolicy.ShouldWrite`'s signature allows a per-call override; check at implementation time —
if not, add one) or by constructing a one-off policy variant for just this check. Every other field's
`ShouldWrite` call is untouched.
**Depends on:** none
**Verify:** test that a global `IgnoreBlankValues = false` still lets an empty ComicVine Series/Number
value through unchanged (current buggy behavior would blank it), while every other field still respects
`IgnoreBlankValues = false` normally (regression check).

### Step 3: Community-rating default-off
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeSettings.cs`
**What:** `EnabledScrapeFields`'s default initializer changes from `new(Enum.GetValues<ScrapeField>())`
to that set minus `ScrapeField.CommunityRating`.
**Depends on:** none
**Verify:** a one-line test asserting a fresh `ScrapeSettings()`'s `EnabledScrapeFields` excludes
`CommunityRating` but includes everything else.

---

## Phase 2 — Safety/quality mechanisms

### Step 4: Retry-once on transient failure (both providers)
**Files:** edit `src/Paperbunkr.Data/ComicVine/ComicVineClient.cs` (`GetAsync`, ~line 330),
`src/Paperbunkr.Data/ComicVine/MetronClient.cs` (`GetAsync`, ~line 412)
**What:** In each `GetAsync`, wrap the HTTP-send + JSON-parse portion (not the post-parse `status_code`
check — a well-formed non-1/non-success API response is NOT a retry trigger, only
`HttpRequestException`, a timeout-shaped `OperationCanceledException`, and JSON parse failure are) in a
loop that retries exactly once after `Task.Delay(2500, cancellationToken)` on those specific failure
modes, then propagates on a second failure. Note (from survey): a retry re-enters
`ComicVineRateLimitHandler`/Metron's own rate limiter and spends a second slot — this is expected and
matches CE's own behavior (the plugin's throttle applies before every attempt too), not a bug to avoid.
**Depends on:** none
**Verify:** new tests in `ComicVineClientTests.cs`/`MetronClientTests.cs` using a stateful `FakeHandler`
(closure-captured call counter — a new pattern in these fixtures per the survey, not yet used) that
fails on the first call and succeeds on the second; assert exactly 2 requests were sent and the call
succeeds. A second test: fails twice, asserts the original exception propagates and exactly 2 requests
were sent (no third attempt).

### Step 5: Namescore tokenizer fixes
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/MatchScoreCalculator.cs` (`Tokenize`, `WordSeparators`)
**What:** Remove `_` from `WordSeparators` (currently included; CE's `\W+` treats it as a word
character). Before splitting, strip apostrophes (`'` → nothing, not a split point — remove `'` from
`WordSeparators` too and instead `.Replace("'", "")` the input first). Add the CE canonicalizations
before tokenizing: `giant[- ]*sized?` → `"giant size"`, `king[- ]*sized?` → `"king size"`,
`one[- ]*shot` → `"one shot"` (case-insensitive regex replacements). Keep `Tokenize`'s existing two-arg
`(text, extra)` signature — both `NameScore` call sites stay as-is.
**Depends on:** none
**Verify:** new `MatchScoreCalculatorTests.cs` cases: `"Don't"` tokenizes as one word not two;
`"Giant-Sized X-Men"` vs `"Giant Size X-Men"` namescore-match at full weight; `"King Sized"` and
`"One-Shot"` equivalents; a word containing `_` no longer splits.

### Step 6: Fractional issue numbers in bookscore
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/MatchScoreCalculator.cs` (`Compute`, `BookScore`),
`src/Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs` (the call site at ~line 153, and confirm
whether `ParseIssueNumber` at line 604 has any other caller before deciding whether to keep it as a
separate utility or fold it away)
**What:** Per survey finding #1: change `Compute`'s `int? bookIssueNumber` parameter to `string?
bookIssueNumberRaw`. Add a private `ParseIssueNumberForScoring(string?) : double?` inside
`MatchScoreCalculator` — regex-strip non-digit/period/minus characters, `double.TryParse` — matching
`matchscore.py:87-92`. `BookScore` uses this instead of the old `int?` parameter. Update the call site
in `ScrapeOrchestrator.cs:153` to pass `issue.EffectiveNumber()` directly (raw string) instead of
`ParseIssueNumber(issue.EffectiveNumber())`. Leave `ParseIssueNumber` itself in place if anything else
calls it (check first); if `Compute` was its only caller, delete it.
**Depends on:** none (independent of Step 5, touches the same file — sequence after Step 5 to avoid a
merge headache within one session, not a real dependency)
**Verify:** new test: a book with issue number `"5.5"` against a series with `count_of_issues=10` scores
via the ±100 step function correctly (previously fell back to the neutral 100 because `int.TryParse`
failed); existing integer-issue-number tests still pass unchanged.

### Step 7: Year sanity check
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/MatchScoreCalculator.cs` (`YearScore`, ~line 122-135)
**What:** Add CE's `1900 < y <= currentYear+1` validity check; a year outside that range is treated as
absent (same as null) for the -100/-500 logic, not as a valid signal.
**Depends on:** none
**Verify:** new test: `bookYear = 31337` (or any out-of-range value) scores identically to `bookYear =
null`, not as a mismatch.

### Step 8: Person-role credit mapping
**Files:** edit `src/Paperbunkr.Data/ComicVine/ComicVineClient.cs` (`PersonRoleMap`, the credit-parsing
loop in `ParseIssueDetails`, ~lines 201-263)
**What:** Retype `PersonRoleMap` from `Dictionary<string,string>` to `Dictionary<string,string[]>` —
`"artist"` maps to `["Penciller", "Inker"]`; every other existing key keeps its single-element array.
Rewrite the parsing loop: for each person, split `role` on `,` as today, but for **every** recognized
token (not just the first), resolve to its field array and add each resulting field's `ComicVineCredit`
for that person (dedup if the same field appears twice for one person, e.g. two role tokens both mapping
to Penciller). A person with zero recognized roles keeps today's fallback (`ComicVineCredit(name, null)`).
**Depends on:** none
**Verify:** existing `ComicVineClientTests.cs` credit-mapping test extended: a person credited as
`"artist"` produces both a Penciller and an Inker `ComicVineCredit`; a person credited as `"writer,
artist"` produces Writer + Penciller + Inker (three credits, not the old single first-match).

### Step 9: Persisted ComicVine issue/series identity
**Files:** edit `src/Paperbunkr.Data/Entities/ComicMetadataEntityKind.cs` (add `Issue`, `Series`), edit
`src/Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs` (`ApplyVolumeAndIssueAsync` and the
issue-apply path) to upsert a `ComicMetadataExternalId` row per successful scrape: `{EntityKind: Issue,
EntityId: tracked.Id, Provider, ExternalId: comicVineIssueId}` and `{EntityKind: Series, EntityId:
tracked.SeriesId, Provider, ExternalId: chosenVolumeId}` (reuse the upsert-by-unique-index pattern
already established in `ComicMetadataExternalIdSync.Attach` from the Metron work, or call that helper
directly if its signature fits without contorting it for a non-resolver-backed kind)
**What:** No migration needed — `ComicMetadataEntityKind` is a plain `int`-backed enum column already;
adding two enum values doesn't change the schema, only what values legitimately appear in it.
**Depends on:** none
**Verify:** new test: after a successful scrape, `ComicMetadataExternalIds` has an `Issue`-kind row for
the scraped issue and a `Series`-kind row for its series, both with the correct external ids; re-scraping
the same issue updates rather than duplicates (idempotency, matching the existing sync pattern's tests).

### Step 10: Filename-guessed number/year available to scoring under manual review
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs` (the `SearchAndRank`
closure / `MatchScoreCalculator.Compute` call site, ~lines 150-157)
**What:** When `issue.EffectiveNumber()`/`EffectiveYear()` return null, look up the issue's own
`MetadataProposals` (already a loaded/loadable navigation) for a `Pending` proposal on the corresponding
field and use its raw value for this scoring call only — never write it, never mark it resolved. Locate
the exact `MetadataProposal`/`MetadataProposalField` shape (`src/Paperbunkr.Data/Entities/` — already
referenced elsewhere in this codebase per the design doc's citation) before writing this, to match its
real field/status enum names rather than guessing them.
**Depends on:** Step 6 (both touch the same `Compute` call site — sequence together to avoid a rebase
conflict within the session)
**Verify:** new test: an issue with `EffectiveNumber() == null` but a `Pending` filename-derived number
proposal scores as if that number were present; the proposal itself remains `Pending` after scoring
(not silently accepted).

### Step 11: Cover-hash auto-match safety gate
**Files:** new `src/Paperbunkr.Data/ComicVine/Scraping/CoverPerceptualHash.cs`; edit
`Paperbunkr.Data.csproj` (add `CoenM.ImageSharp.ImageHash` package reference, which pulls in
`SixLabors.ImageSharp` transitively — first use of ImageSharp in the solution, per survey); edit
`src/Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs` (constructor gains `Func<int, string?>
getCoverPath`, and the `AutoChooseTopMatch` branch at ~lines 167-177 restructures per survey finding #4);
edit `src/Paperbunkr.App/Scraper/ScrapeCoordinator.cs` (wherever `ScrapeOrchestrator` is constructed,
pass `CoverThumbnailService.GetEffectiveCoverPath` as the new delegate)
**What:**
- `CoverPerceptualHash.Hash(string filePath) : ulong?` — decode via `SixLabors.ImageSharp.Image.Load`,
  compute via `CoenM.ImageHash`'s `AverageHash` algorithm; return null if the file can't be decoded.
- `CoverPerceptualHash.Similarity(ulong hash1, ulong hash2) : double` — `CoenM.ImageHash`'s
  `CompareHash.Similarity` (or equivalent Hamming-distance helper the library exposes).
- In `ScrapeAsync`'s `AutoChooseTopMatch` branch: after `chosen = ranked[0].Volume`, if
  `getCoverPath(issue.Id)` and the candidate's own cover URL both hash successfully, check similarity
  > 0.87; if it passes, additionally hash the 2nd/3rd-ranked candidates (if any) and reject as
  ambiguous if either exceeds 0.77 similarity to the book's cover. On any gate failure (below threshold,
  ambiguous, or either cover unhashable), don't auto-apply — fall through to the same branch this book
  would take with `AutoChooseTopMatch` off (interactive review if `isInteractive` and a review callback
  is available, otherwise skip-and-log).
- Candidate cover download: reuse whatever HTTP path already fetches `Volume.ImageUrl` for display (if
  one exists) or add a minimal one-off `HttpClient.GetByteArrayAsync` — check `ComicVineClient`/
  `ScrapeOrchestrator` for an existing image-fetch helper before adding a new one.
**Depends on:** none structurally, but do last in Phase 2 since it's the largest, riskiest item and
benefits from Steps 4-10's test patterns already being established in the same session.
**Verify:** `CoverPerceptualHash` gets table-driven tests against small fixture image pairs (reuse
`CbzFixture.cs`'s existing test-image-generation helpers if they produce comparable images, or add
minimal new fixtures — two near-identical images should score >0.87, two very different ones <0.87).
`ScrapeOrchestrator` integration test: auto-choose with a matching cover applies without triggering
interactive review; auto-choose with a mismatched cover falls through to skip-and-log in a
non-interactive run. On-screen verification of the interactive fallback path is out of scope for this
session per the same standing constraints as the Metron work (no UI automation without permission,
shared-DB launch risk) — flag as an outstanding manual check.

---

## Phase 3 — Settings

### Step 12: New ScrapeSettings properties
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeSettings.cs`
**What:** Add, following the exact existing property style:
```csharp
public bool ConvertImprints { get; set; } = true;
public bool ForceSeriesArt { get; set; } = true;
public bool ShowCovers { get; set; } = true;
public int ScrapeDelayMs { get; set; } = 1000;   // clamp 2000-3_600_000 at the point of use, per CE's 2-3600s range
public Dictionary<string, string> PublisherAliases { get; set; } = new();
```
Wire `ConvertImprints`: when `false`, Step 1's resolution is skipped and CE's off-behavior reproduces —
the raw imprint name lands in `Publisher` (not the parent publisher), `Imprint` stays untouched (matches
`comicbook.py:464-466`'s alternate branch, not just "do nothing"). Wire `ForceSeriesArt`: `false` means
the match-review dialog's cover pane falls back to the candidate issue's own cover instead of the
series/volume art (locate the current always-on cover-selection logic in the review ViewModel/View
first). Wire `ShowCovers`: `false` hides the cover `Image` element in both review dialogs' `.axaml`
(an `IsVisible` binding, not a data-fetch skip — simplest correct implementation). Wire `ScrapeDelayMs`:
a new `await Task.Delay(clampedMs, cancellationToken)` between books in `ScrapeOrchestrator.ScrapeAsync`'s
main loop, distinct from the existing per-HTTP-request 1100ms spacing. Wire `PublisherAliases`: applied
to both the resolved publisher and imprint strings after Step 1's resolution, same lookup shape as
`ImprintOverrides`.
**Depends on:** Step 1 (ConvertImprints/PublisherAliases wire into that code path), Step 11 (ForceSeriesArt/ShowCovers
touch the same review-dialog cover code Step 11's fallback logic may also touch — sequence after)
**Verify:** settings round-trip tests for each new property, same shape as existing `ScrapeSettings`
tests. `ConvertImprints=false` test: an unresolved-vs-resolved imprint name both land in `Publisher`, `Imprint` stays
null, distinct from Step 1's on-behavior.

### Step 13: Preferences UI for the new settings
**Files:** edit `src/Paperbunkr.App/ViewModels/OrganizeScrapeSettingsViewModel.cs` (`Load`/`Save`/new
observable properties, following the exact pattern already there for `AutoChooseTopMatch`/
`MaxSearchResults`/`ImprintOverrides`), locate and edit the corresponding `.axaml` view (not found in
the survey pass — `Glob` for it at implementation time, likely `src/Paperbunkr.App/Views/Preferences/OrganizeScrapeSection.axaml`
per the naming convention of sibling Preferences sections)
**What:** One new observable bool property per boolean setting (`ConvertImprints`, `ForceSeriesArt`,
`ShowCovers`), a string-backed numeric field for `ScrapeDelayMs` matching `MaxSearchResults`'s existing
parse pattern, and a newline-delimited `"X --> Y"` text block for `PublisherAliases` reusing the exact
parsing already at `OrganizeScrapeSettingsViewModel.cs:138-148` for `ImprintOverrides`. Add corresponding
toggles/fields to the `.axaml`, in the existing section, no new Preferences page.
**Depends on:** Step 12
**Verify:** headless view-construct test (XAML weave, per this project's convention) for the updated
view; VM-level `Load`/`Save` round-trip test for each new field, mirroring existing `OrganizeScrapeSettingsViewModel`
tests.

---

## Phase 4 — Batch/review UX

### Step 14: Batch summary breakdown
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs` (`ScrapeAsync`'s return
type, currently `Task<int>`), `src/Paperbunkr.App/Scraper/ScrapeCoordinator.cs` (the consumer at
~lines 98/135)
**What:** New small result record, e.g. `ScrapeBatchResult(int Applied, int SkippedByUser, int NoMatchFound,
int Failed)`, replacing the bare `int`. `ScrapeAsync`'s existing control flow already distinguishes these
cases internally (explicit `skip`/`break`/exception-catch branches per the survey) — thread a counter
increment into each existing branch rather than restructuring the loop. `ScrapeCoordinator.cs` builds the
summary string from all four counts and passes the same breakdown into `itemsProcessed`/`itemsFailed` on
the existing `Succeed(...)` call — no new modal, no new job-result mechanism.
**Depends on:** Step 11 (auto-match-gate fallthroughs need their own bucket — "gate failed, fell through
to skip" should count toward the right bucket, so sequence after that branch exists)
**Verify:** `ScrapeCoordinatorTests`/`ScrapeOrchestratorTests` updated to assert the right counts land in
the right buckets for: a normal apply, a user skip, a no-match batch, and an exception-caught failure.

### Step 15: Natural sort for issue review list
**Files:** edit `src/Paperbunkr.App/Scraper/ComicVineIssueReviewDialogViewModel.cs`
**What:** Sort `Candidates` by natural/numeric issue-number order before populating (handle fractional
numbers per Step 6's parser if reusable, and unicode-fraction characters per CE's `natural_compare` if a
real example is found — otherwise plain numeric-then-string fallback for unparseable numbers).
**Depends on:** none
**Verify:** test with issue numbers in non-sequential API order (`"3"`, `"1"`, `"2"`, `"Annual 1"`)
producing correctly ordered `Candidates`.

### Step 16: Ctrl-held "permanent skip"
**Files:** edit `src/Paperbunkr.App/Scraper/ComicVineMatchReviewDialogView.axaml`/`.axaml.cs` and
`ComicVineIssueReviewDialogView.axaml`/`.axaml.cs` (Skip button gains a code-behind `Click` handler
reading `PointerPressedEventArgs`/current `KeyModifiers` instead of a plain `Command=` binding, per
survey finding #7), the corresponding ViewModels (new `SkipPermanently()` method alongside existing
`Skip()`)
**What:** Ctrl-held click calls `SkipPermanently()`, which — beyond the normal skip resolve — writes a
durable "never auto-match this book again" marker. Storage shape: reuse Step 9's `ComicMetadataExternalId`
machinery is a poor fit (that table identifies *matched* entities, not *excluded* ones) — add a small,
separate boolean-ish marker instead, e.g. a new `Issue.ScrapePermanentlySkipped` column (simplest,
mirrors CE's own tag-on-the-book shape most directly) — confirm this doesn't collide with anything in
the acquisition pipeline's own skip/ignore concepts before adding a same-named column. Button label
changes while Ctrl is held (a `PointerMoved`/`KeyDown`+`KeyUp` pair toggling a bound label string),
matching CE's visual affordance.
**Depends on:** none
**Verify:** ViewModel-level test for `SkipPermanently()` setting the marker; a scrape run test asserting
a permanently-skipped issue is excluded from future auto-match/search entirely, not just skipped once.
New migration for the `Issue` column (follow this project's established `dotnet ef migrations add`
convention).

### Step 17: Cover-image cycling with alt-cover memory
**Files:** edit `src/Paperbunkr.Data/ComicVine/ComicVineClient.cs` (request `associated_images` in
`IssueDetailFields`, parse into a new DTO field — confirm the real field name against a live response
first, per the design doc's own caution), `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeComicVine.cs`
(`ComicVineIssueSummary`/`ComicVineVolumeSearchResult` gain an alt-cover URL list, or a follow-up detail
fetch supplies it — decide based on what the confirmed field actually returns and at which endpoint),
`src/Paperbunkr.App/Scraper/ComicVineMatchReviewDialogViewModel.cs` and
`ComicVineIssueReviewDialogViewModel.cs` (candidate ViewModels gain `NextCover`/`PreviousCover` commands
and an index into the alt-cover list; a per-dialog-session `Dictionary<candidateKey, int>` remembers a
non-default pick, matching CE's in-memory-only `session_data_map` scope), the `.axaml` views (next/prev
buttons around the cover `Image`)
**What:** Largest remaining item — genuinely new data (server field), new DTO surface, and new
interactive UI. Confirm the ComicVine field name and response shape against a live call or ComicVine's
public API docs before writing the parser (this is exactly the kind of external-API fact this project's
standing rule says to verify, not assume).
**Depends on:** none, but do last — most likely to need a follow-up correction once the real field shape
is confirmed.
**Verify:** DTO parsing test once the field is confirmed; ViewModel test for cycling forward/backward and
wraparound; session-memory test (pick a non-default cover, reload the same candidate within the same
dialog session, confirm the pick persisted; a new dialog session starts fresh).

---

## Suggested execution order for this session

Phases 1-2 (Steps 1-11) are the highest-value, most load-bearing work — correctness bugs and the
match-quality/safety fixes — and share heavy context (all touch `ScrapeOrchestrator.cs`/
`MatchScoreCalculator.cs`/`ComicVineClient.cs`), so do them first and together. Phase 3 (Steps 12-13)
is comparatively mechanical once Phase 1-2 land. Phase 4 (Steps 14-17) is genuinely separable UX work;
Step 17 specifically should not block the rest of the session on confirming a live API field shape.
