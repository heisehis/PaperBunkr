# ComicVine Scraper fidelity — Design

**Status:** ~~design, not yet planned/implemented~~ **built, uncommitted** — confirmed 2026-09-26 via
source: `ScrapeBatchResult`/`CoverPerceptualHash`/`ScrapeOrchestrator` changes present and untracked
(the follow-up `2026-09-24-scraper-review-tables-and-batch-summary-design.md` already documents this
as shipped, on-screen, by the user). On-screen check of this base pass specifically still outstanding.
**Motivation:** the earlier Cluster Library Manager (CLM) port was audited internally against its own
design doc's claims and looked complete. That audit was insufficient — it never checked those claims
against the actual CE plugin. The user supplied the real plugin bundle
(`ComicVineScraper-1.0.102.crplugin`, extracted to
`C:\Users\DeeDee\AppData\Local\Temp\claude\C--Users-DeeDee-PaperBunkr\...\scratchpad\cvscraper\`), and a
direct line-by-line comparison against it found real correctness bugs, a missing safety mechanism, and
a long tail of smaller fidelity gaps. This design fixes them, phased by severity.

## Facts this design is grounded on

Every claim below was verified by reading the real IronPython source directly (file:line citations in
the two research passes earlier in this session), not inferred from any prior design doc. Key facts
that shape this design specifically:

- CE's `matchscore.py` sums six terms (namescore/priorscore/publisherscore/bookscore/yearscore/
  recency_score) — Paperbunkr's `MatchScoreCalculator.cs` already has the same six terms and mostly
  matches, but diverges in tokenization, fractional-number handling, and year sanity-checking.
- CE's `automatcher.py` never trusts `MatchScoreCalculator` alone for unattended auto-choose — it
  requires the book's local cover to perceptually match the candidate's remote cover above 0.87
  similarity, plus a second check that the top match isn't ambiguously close (>0.77) to a runner-up.
  Paperbunkr's `AutoChooseTopMatch` path has no such gate at all.
- CE's `cvdb.py`/`comicbook.py` only ever writes `Imprint` when the raw ComicVine publisher string is a
  recognized imprint; Paperbunkr's `ScrapeOrchestrator.cs:355-372` writes `Imprint = volume.Publisher`
  unconditionally, every scrape.
- CE stores issue number as a plain string (`issue_num_s`) everywhere except one narrow float-parse
  inside `matchscore.py` itself — confirmed directly in `bookdata.py`/`dbmodels.py`/`comicbook.py`.
  Paperbunkr's `Issue.Number` is already a `string?`, matching this exactly; only the scoring-time
  parse needs fixing.
- CE hardcodes `ignoreblanks=True` for Series and Issue Number regardless of the user's global
  "ignore blank values" setting (`comicbook.py:259-268`) — Paperbunkr has no such carve-out.
- CE's `cvconnection.py` retries a failed request exactly once, after a flat 2.5s sleep, on any
  failure (network error, empty body, parse failure, bad status code) — Paperbunkr has no retry at
  all beyond `ComicVineRateLimitHandler`'s reactive 429/420 cool-off.
- CE's `pluginbookdata.py` persists `comicvine_issue`/`comicvine_volume` identity on every scraped
  book — Paperbunkr has no equivalent on a regular `Issue`/`Series`, so "already scraped" is inferred
  heuristically (`MetadataSource != null && Volume non-empty`) rather than known precisely.
- Full inventories of settings, credit-role mapping, throttling, and batch/review UX gaps are recorded
  in this session's transcript (two research passes) and are not repeated verbatim here — this design
  references them by the numbered findings already presented to and confirmed by the user.

## Scope and phasing

Four phases, in the order the user chose (severity-ordered, not file-ordered):

1. **Correctness bugs** — silently wrong data today.
2. **Safety/quality mechanisms** — missing behavior with real match-quality or reliability consequences.
3. **Settings** — CE toggles/knobs with no Paperbunkr equivalent, mostly gating Phase 1/2 behavior.
4. **Batch/review UX** — cover cycling, permanent-skip, natural sort, batch summary.

Explicitly out of scope (confirmed non-issues during the audit, not chased here): fileless-book
thumbnail handling, folder-grouping toggle, per-dialog window-geometry persistence, inline field
editing, "remember last search," double-click-to-confirm — none of these have a real CE behavior to
port, or don't apply to Paperbunkr's architecture.

## Phase 1 — Correctness bugs

### 1.1 Imprint/Publisher write bug
`ScrapeOrchestrator.ApplyVolumeAndIssueAsync` stops unconditionally writing
`tracked.Imprint = volume.Publisher`. New rule, matching CE (`comicbook.py:444-493`): resolve
`ComicVineImprints.FindParentPublisher(volume.Publisher, overrides)` first; if it returns a different
value than the raw string (i.e. the raw publisher *was* a recognized imprint), write the **resolved**
value to `Publisher` and the **raw** value to `Imprint`; otherwise write the raw value to `Publisher`
and leave `Imprint` untouched (CE's "publisher == imprint → blank it" case). This already respects the
existing `ScrapeField.Publisher`/`ScrapeField.Imprint` toggles and `ShouldWrite` policy — no new toggle
needed for the write direction itself (the on/off switch for *whether imprint resolution happens at
all* is `convert_imprints_b`, Phase 3).

### 1.2 Ignore-blanks exemption for Series/Number
`IssueDetailsApplier`'s `Text()` calls for `ScrapeField.Number` and `ScrapeOrchestrator`'s Series-name
write both bypass the global `IgnoreBlankValues` policy — they use a fixed `ignoreBlankValues: true`
regardless of `ScrapeFieldPolicy.IgnoreBlankValues`, matching CE's hardcoded exemption exactly. Every
other field keeps respecting the global toggle unchanged.

### 1.3 Community-rating default
`ScrapeSettings.EnabledScrapeFields`'s default construction currently includes every `ScrapeField`
value. Change the default to exclude `ScrapeField.CommunityRating`, matching CE's own default-off
(`configuration.py:62`, `update_rating_b = False`) — it costs an extra API call CE never made by
default. This changes the default for a **new** settings row only; existing persisted rows are
untouched (single-user app, per prior CLM decisions — no migration needed).

## Phase 2 — Safety/quality mechanisms

### 2.1 Cover-hash auto-match safety gate
New `CoverPerceptualHash` static class in `Paperbunkr.Data.ComicVine.Scraping`, using
`CoenM.ImageSharp.ImageHash` (new NuGet dependency) and its `AverageHash` algorithm specifically (not
`PerceptualHash`/`DifferenceHash`) — this keeps computed similarity values comparable to CE's verified
0.87/0.77 thresholds, since CE's own algorithm *is* an average hash (confirmed directly from
`imagehash.py`: resize 8×8, luma-grayscale, bit-per-pixel-above-mean, Hamming-distance similarity).
Covers already decode as `Avalonia.Media.Imaging.Bitmap` (`CoverThumbnailService.cs`); a small adapter
converts that to `SixLabors.ImageSharp.Image<Rgba32>` for the library call.

**Where it gates:** only the `AutoChooseTopMatch` path (`ScrapeOrchestrator.cs`'s auto-choose branch),
matching CE exactly — interactive review (the modal-per-book flow) is unaffected, since a human is
already looking at the cover there. Logic, mirroring `automatcher.py` precisely:
1. Hash the book's own local cover (already-decoded first page/cover image).
2. Hash the top-ranked candidate's cover (fetch if not already cached from the search response).
3. If similarity ≤ 0.87, don't auto-apply — fall through to the same path a low-confidence match
   already takes (interactive: show the review modal; non-interactive: skip-and-log, per the existing
   `isInteractive` gate).
4. If similarity > 0.87, additionally check the 2nd/3rd-ranked candidates' covers: if either is *also*
   similar to the book's cover above 0.77, treat the match as ambiguous and fall through the same way
   (CE's TPB-vs-issue-#1 collision guard).
5. Only when both checks pass does the top match auto-apply without a modal.

A missing/undecoded local cover (can't hash) skips the gate entirely and falls through to review/skip
— never silently auto-applies without the check it can't perform.

### 2.2 Retry-once on transient failure
Both `ComicVineClient.GetAsync` and `MetronClient`'s equivalent request path gain a single retry: on
`HttpRequestException`, a JSON parse failure, or a non-success status code that isn't already handled
by the rate-limit cool-off path, wait 2500ms (CE's exact constant) and retry the request exactly once;
a second failure propagates as today. This sits *underneath* `ComicVineRateLimitHandler`'s existing
420/429 cool-off (which stays as-is, unrelated concern) — the two don't conflict since one handles
"the server says slow down" and this handles "the request itself failed."

### 2.3 Namescore tokenizer fixes
`MatchScoreCalculator.Tokenize` changes to match `matchscore.py:44-48` exactly: strip apostrophes
before splitting (not split on them — `"don't"` → `"dont"`, one word), split on `\W+`-equivalent
(any non-alphanumeric, not the current curated separator list — notably this also stops splitting on
`_`, which CE's `\W+` treats as a word character), and canonicalize `giant[- ]*sized?`→`"giant size"`,
`king[- ]*sized?`→`"king size"`, `one[- ]*shot`→`"one shot"` before tokenizing.

### 2.4 Fractional issue numbers in bookscore
New narrow helper (e.g. `ParseIssueNumberForScoring`) used only inside `MatchScoreCalculator`'s
bookscore term — regex-strips non-digit/period/minus characters and parses as `double?`, matching
`matchscore.py:87-92` exactly. The existing `ParseIssueNumber`/`int?` used elsewhere in
`ScrapeOrchestrator` is untouched (confirmed scoped fix, per Q3 — CE itself only ever floats this
value for scoring, nowhere else).

### 2.5 Year sanity check
`MatchScoreCalculator.YearScore` gains CE's `is_valid_year_b` range check (`1900 < y <= currentYear+1`)
before applying the -100/-500 logic — a book or series year outside that range is now treated as
absent rather than as a valid (and possibly wildly wrong) signal.

### 2.6 Person-role credit mapping
`ComicVineClient`'s role-parsing loop changes to match `cvdb.py:663-691`: `"artist"` maps to **both**
Penciller and Inker (not Penciller only), and a person's **every** comma-separated role is applied
(not just the first recognized one) — matching CE's `ROLE_DICT` fan-out and its per-role loop exactly.

### 2.7 Persisted ComicVine identity on Issue/Series
Per Q2: reuse `ComicMetadataExternalId` rather than new columns. `ComicMetadataEntityKind` gains two
new values, `Issue` and `Series`. Unlike the five existing kinds (which go through a resolver's
`GetOrCreate`), Issue/Series already *are* the row being identified — a direct upsert after a
successful scrape writes `{EntityKind: Issue, EntityId: issue.Id, Provider, ExternalId: comicVineIssueId}`
and the equivalent for the matched series/volume. This also lets `ComicVineMatchMemory`'s priorscore
read the persisted volume id directly for "was this exact series previously scraped" checks, closer to
CE's global per-series-key memory than today's narrower search-key-plus-volume-id keying — though full
convergence with CE's global scope is not attempted here (CE's own scope, a flat cross-session set, is
itself a coarser signal than Paperbunkr's per-search-key memory; this phase only fixes the *missing
identity*, not memory's exact scope, which is a separate, smaller design question if revisited later).

### 2.8 Filename-guessed number/year available to scoring under manual-review policy
`ScrapeOrchestrator.ScrapeAsync`'s call into `MatchScoreCalculator.Compute` changes its `bookYear`/
`bookIssueNumber` inputs: when `Issue.EffectiveYear()`/`EffectiveNumber()` return null because a
`MetadataProposal` is still `Pending` (manual-review policy), read the **pending proposal's own value**
directly for this scoring call only — never write it, never treat it as accepted, never bypass the
review gate. This only affects what data reaches the match-scoring formula for one scrape attempt;
the field itself stays exactly as gated as it already is everywhere else in the app.

## Phase 3 — Settings

New `ScrapeSettings` properties, each gating exactly the Phase 1/2 behavior it corresponds to (so nothing
in Phase 1/2 needs to wait on this phase to be correct — these are on/off switches for already-correct
default behavior, matching CE's own defaults):

- `ConvertImprints` (default `true`, matches CE §1.2's `convert_imprints_b`) — when `false`, §1.1's
  imprint resolution is skipped entirely and the raw ComicVine publisher string writes straight to
  `Publisher` with `Imprint` left alone (CE's literal off-behavior, `comicbook.py:464-466`: the
  imprint name would otherwise land in the Publisher field with conversion off — reproduce this exact
  alternate behavior, not just "do nothing").
- `ForceSeriesArt` (default `true`, matches CE's always-on-in-practice behavior found during audit) —
  exposed as a real toggle for the first time; `false` means the match-review dialog's cover pane falls
  back to the issue's own cover instead of always forcing the series/volume art.
- `ShowCovers` (default `true`) — `false` hides cover thumbnails in both review dialogs (a real,
  useful "scrape faster on a slow connection" toggle CE had and Paperbunkr didn't).
- `ScrapeDelayMs` (default 1000, matching CE's `scrape_delay_n` default, clamp 2000-3,600,000ms per
  CE's own 2-3600 **second** range) — a new proactive inter-book delay in `ScrapeOrchestrator`'s batch
  loop, distinct from and in addition to the existing per-HTTP-request 1100ms spacing.
- `PublisherAliases` (`Dictionary<string,string>`, default empty) — CE's `publisher_aliases_sm`,
  applied to both the resolved publisher and imprint strings after §1.1's resolution, distinct from
  `ImprintOverrides` (which is CE's separate `IMPRINT=` setting, already implemented).

Lower-priority CE settings noted in the audit (`alt_search_regex_s`, `note_scrape_date_b`,
`rescrape_notes_b`/`rescrape_tags_b`) are not added here — each was a minor, narrowly-scoped advanced
setting in CE with no corresponding gap found in Phase 1/2's behavior; adding UI for them without a
concrete behavior they'd control would be speculative. Revisit if a specific need surfaces.

All new settings surface in the existing Preferences → Organize & Scrape section, next to the current
`OverwriteExisting`/`IgnoreBlankValues`/`AutoChooseTopMatch`/`ConfirmIssueMatch` toggles — no new
Preferences section.

## Phase 4 — Batch/review UX

### 4.1 Batch summary via Activity Center (not a modal)
Per Q6: the existing single "applied to N of M" job-result string is replaced with a real breakdown —
scraped / skipped-by-user / no-match-found / failed counts — still surfaced through the existing
`IActivityService`/`IActivityJobHandle` job-result pattern every other batch operation in this app
already uses. No new modal dialog.

### 4.2 Cover-image cycling + alt-cover memory
Both review dialogs (`ComicVineMatchReviewDialogViewModel`/`ComicVineIssueCandidateViewModel`) gain
next/prev navigation across every cover URL a candidate has (CE's `issuecoverpanel.py` alt-cover
list), with the user's non-default pick remembered for the remainder of that scrape session (CE's
`session_data_map`, in-memory only, not persisted across app restarts — matching CE's own scope).
ComicVine's issue-detail response would need to actually expose alternate cover URLs for this to have
data to cycle through — confirm during implementation whether `cvdb.py`'s alt-cover source (verify the
exact field) is already fetched by `ComicVineClient` or needs adding to the field_list.

### 4.3 Ctrl-held "permanent skip"
Both review dialogs' Skip action checks for the Ctrl modifier at click time (CE's pattern across
`seriesform.py`/`issueform.py`/`searchform.py`); a Ctrl-held skip writes a durable "never auto-match
this book again" marker (reusing Phase 2.7's new `ComicMetadataExternalId`-adjacent identity, or a
simple boolean column on `Issue` — decide the exact shape during planning, since CE's own mechanism is
a tag/custom-value on the book, and Paperbunkr has no direct equivalent slot) versus a plain skip,
which only skips this one run. The button's label changes while Ctrl is held, matching CE's own visual
affordance.

### 4.4 Natural sort for the issue review list
`ComicVineIssueReviewDialogViewModel`'s candidate list sorts by natural/numeric issue-number order
(handling fractional and unicode-fraction numbers per CE's `natural_compare`), instead of relying on
whatever order the ComicVine API happened to return.

## Testing

- Phase 1: unit tests for the imprint/publisher write branching (recognized-imprint vs not), the
  Series/Number ignore-blanks exemption, and the new `EnabledScrapeFields` default.
- Phase 2: `CoverPerceptualHash` gets table-driven similarity tests against known-similar/known-different
  fixture image pairs; `MatchScoreCalculator` gets new cases for apostrophe/giant-sized/king-sized/
  one-shot tokenization, fractional bookscore, and out-of-range yearscore; a live-network-free retry
  test using the existing `FakeHandler` pattern (fail once, succeed on retry, assert exactly one retry
  happens); a test confirming the manual-review-pending-proposal scoring path reads without writing.
- Phase 3: settings default/round-trip tests, same shape as existing `ScrapeSettings` tests.
- Phase 4: headless view-construct tests for any new XAML per this project's convention; cover-cycling
  and Ctrl-skip get ViewModel-level tests (no real UI automation, per this session's standing
  permission constraints).
