# Cluster Library Manager — Scraper UI Redesign

*Date: 2026-09-13. Scope: redesign the ComicVine series-match dialog shipped in
`docs/superpowers/specs/2026-09-11-cluster-library-manager-design.md` §4, add the issue-match step
CE has and Paperbunkr never did, and add a persistent batch-progress header across a scrape run.
Produced via a `/grilling` pass (2 rounds) per CLAUDE.md's override of `brainstorming`'s default
one-question clarification step. Visual reference for "what CE/Omnibus actually look like" was
three screenshots the user supplied directly in this session (ComicRack CE's "Choose a Comic Book
Series"/"Choose a Comic Book Issue" dialogs, Omnibus's "Match Series" grid) — used in place of
`_reference/ComicRackCE`-equivalent source per the standing rule, since the extracted CE Python
scraper source an earlier session's grilling pass used was never checked into `_reference/` and the
screenshots are themselves the real CE UI, not a description of it.*

## 1. Goals and non-goals

**Goal:** the ComicVine series-match dialog (currently a bare text list, no covers, no grouping —
see the original design doc §4's own "no cover-thumbnail rendering in this pass" note) gets visual
parity with CE's and Omnibus's real scraper UIs: cover art, a best-match/other-results split, and
richer per-candidate metadata. Two follow-on gaps the comparison surfaced get fixed alongside it:
CE's issue-confirm step (Paperbunkr silently auto-matches the issue today, no dialog exists) and
CE's batch-progress chrome (current book cover + position + Cancel, spanning the whole run).

**Non-goal:** multiple metadata sources or sort modes. The source/sort dropdowns are included now
because the user named a concrete near-term reason (`we'll be adding more later`), but only
"ComicVine" and "Best match" are real options today — no second source is being wired in this pass.

**Non-goal:** touching non-interactive (Scheduled Task / plugin-automation-timer) scrape runs. They
already skip every modal per the original design doc §4/§9's `isInteractive: false` gate; nothing
here changes that path.

## 2. Series-match dialog

Replaces `ComicVineMatchReviewDialogView`/`ComicVineMatchReviewDialogViewModel` in
`plugins/ClusterLibraryManager/Dialogs/`. Layout is CE's list+cover-preview-pane shape (its
"Choose a Comic Book Series" dialog) with Omnibus's toolbar dropdowns added on top — chosen over a
pure cover grid because the host's native-plugin modal caps at `MaxWidth="680"`
([MainWindow.axaml:948](../../../src/Paperbunkr.App/Views/MainWindow.axaml:948)), too narrow for a
multi-column grid with legible captions; a two-pane list fits comfortably at 640px.

- **Toolbar**: source dropdown (`ComicVine`, single entry), search box (pre-filled with the series
  name, still freely editable — same rationale as today), sort dropdown (`Best match`, single
  entry), Search button. Both dropdowns are real, wired `ComboBox`es with one item each, not
  disabled placeholders — the next metadata source or sort mode is a config addition, not a UI
  rewrite.
- **Candidate list** (left column): ranked by `MatchScoreCalculator.Compute`'s existing score,
  unchanged scoring. The top-ranked candidate renders under a `Best match` label, separated from an
  `Other results` label above the rest — a rank-based split (index 0 vs. the remainder), not a
  score-threshold split, since the original design doc §4 already established CE has no confidence
  cutoff anywhere in its scoring. Each row shows name, publisher, year, and (new) issue count —
  `ComicVineVolumeSearchResult.CountOfIssues` was already fetched but never displayed.
- **Preview pane** (right column): cover art for the currently-selected row (`ComicVineVolumeSearchResult`
  already carries an image URL per the original design doc §3 — no new fetch), name/publisher/year/issue
  count repeated, and a `Show issues` link. This opens the same list+cover-pane visual as §3's issue
  dialog, but read-only: it lists issues for the selected (not-yet-confirmed) volume with a single
  `Close` button, no OK/Skip/Go Back, and never touches `Issue` state. It exists specifically for the
  `ConfirmIssueMatch = false` case (§3) — the only chance to sanity-check the auto-matched issue
  before OK on this dialog runs the whole apply with no further confirmation step.
- **Selection model**: clicking a row selects it (updates the preview pane) but does not resolve the
  dialog. A new **OK** button commits the selection; **Skip** behaves as today (resolves with
  `null`). This replaces the current click-to-resolve-immediately behavior — CE parity, and required
  by the preview pane actually being useful (there'd be nothing to preview if a click already
  committed).
- Remote cover images: the plugin has no existing remote-image loader (`AsyncCoverImage` in
  `Paperbunkr.App/Views/` is host-only, and built for local file paths, not HTTP URLs, per its own
  cache-by-`CoverFingerprint` design). A small `RemoteCoverImageLoader` ships in the plugin's own
  project — one in-memory cache keyed by URL, background `HttpClient` GET, decodes off the UI
  thread, sets a placeholder (a `PathIcon`/faint background, matching the "Empty State" treatment in
  the `avalonia-pro-max/components` recipe) while loading or on failure. This is the plugin's own
  concern, not a host change — no other native plugin currently needs remote images.

## 3. Issue-match dialog (new)

`ComicVineScrapeOrchestrator.FindIssueDetailsAsync` (original design doc, `ComicVineScrapeOrchestrator.cs`)
today matches the issue number silently — verified directly in the current source, no dialog exists
in the codebase for this step at all. This adds one, matching CE's own second dialog ("Choose a
Comic Book Issue"):

- Same visual language as §2 (list + cover preview pane) for consistency — the same `ComboBox`/list/
  preview-pane component pieces, applied to `ComicVineIssueSummary` rows (issue number, title, cover)
  instead of volumes. No source/sort toolbar here (issues within one volume don't need a source
  switch, and CE's own issue dialog doesn't have one either).
- Pre-selects whatever `FindByNumber` already auto-matched, so confirming is a single click when the
  automatic match is right (the common case).
- Buttons: **OK**, **Skip** (per-book: apply the volume-level fields already chosen, skip only the
  per-issue fields — matches `ApplyAsync`'s existing "no `details` means volume-level fields still
  apply" resilience), **Go Back** (returns to the series dialog for this same book, matching CE).
- **Gated by a new `ConfirmIssueMatch` bool on `PluginSettings`, default `true`** (§5) — shown by
  default so a fresh install gets full CE parity out of the box; the existing silent-match behavior
  is preserved as an opt-out for users who upgrade and don't want the extra click per book.
- Non-interactive and auto-choose-on runs never see this dialog, same gate as the series dialog
  (original design doc §4's `isInteractive`/`AutoChooseTopMatch` checks) — `ConfirmIssueMatch` only
  matters when both `isInteractive` is true and `AutoChooseTopMatch` is off.

## 4. Batch-progress header (new host capability)

CE's own scraper shows a small persistent window across the whole batch: the current book's own
cover, its filename, a progress bar, and a `Cancel (N remaining)` button — visible proof to the user
that the right book is being matched, not just the right series. Paperbunkr's per-book dialogs today
have no equivalent; each `ShowModalAsync` call is independent and the host's modal shell has no
concept of a header that survives across calls.

**Host change** (approved over the plugin-only alternative — see §7): `NativePluginModalHostViewModel`
([NativePluginModalHostViewModel.cs](../../../src/Paperbunkr.App/ViewModels/NativePluginModalHostViewModel.cs))
gains:
- `HeaderContent` (`Control?`, observable) — rendered above `HostedContent` inside the same
  `OverlayShell`/`Border` in `MainWindow.axaml`, not a second overlay.
- `BeginBatch(Control header) -> IDisposable` — sets `HeaderContent`, marks a batch active. Disposing
  clears `HeaderContent` and, if nothing is currently pending/queued, closes the shell (`IsOpen = false`).
- `Advance()`'s existing "close and clear `HostedContent`" branch only fires when no batch is active
  *and* the queue is empty. While a batch is active, `Advance()` leaves `IsOpen = true` (and
  `HeaderContent` untouched) between consecutive `ShowModalAsync` calls — `HostedContent` can go
  briefly null, but the shell/header never tear down and re-mount, so there's no scrim flash or
  header re-render crossing the series→issue step or the book→book step.
- The existing scrim-click/close-button `Dismiss()` keeps its current per-dialog-only semantics
  (cancels whichever modal is currently shown, same as every other native plugin dialog today) —
  stopping the *whole* batch is a distinct action, done through the header's own Cancel button (see
  below), not overloaded onto the shell's generic close affordance.

**Abstraction surface**: `BeginModalBatch(Control header) -> IDisposable` is added to
`INativePluginUiEnvironment` ([INativePluginUiEnvironment.cs](../../../src/Paperbunkr.Plugins.Abstractions.Ui/INativePluginUiEnvironment.cs)),
implemented by `PaperbunkrNativePluginEnvironment` as a direct delegation to the modal host. This is
a general primitive (any native plugin doing a per-item review loop could use it), not something
special-cased to Cluster Library Manager, which is why it belongs on the shared interface rather
than as a CLM-only side channel.

**Header content**: new `ScrapeBatchHeaderView`/`ScrapeBatchHeaderViewModel` in the plugin's
`Dialogs/` folder. Shows:
- The local book's own cover — `environment.App.GetComicThumbnail(issue)` (`IApplication`, already
  reachable from a native plugin; confirmed exposed and already used elsewhere in this plugin via
  `environment.App.GetLibraryBooks()`), decoded to a `Bitmap` directly in the plugin, no host-only
  `AsyncCoverImage` needed since this is a one-shot in-memory byte array, not a cached library grid.
- Filename/book label, `Book {index} of {total}`, a determinate progress bar.
- **Cancel** — triggers the same `CancellationTokenSource` already threaded through
  `ComicVineScrapeOrchestrator.ScrapeAsync`'s `cancellationToken` parameter
  ([ComicVineScrapeOrchestrator.cs:69](../../../plugins/ClusterLibraryManager/ComicVine/ComicVineScrapeOrchestrator.cs:69)
  already calls `ThrowIfCancellationRequested()` once per book) — no new cancellation plumbing, just
  a button wired to a token the orchestrator already checks.

**Orchestrator change**: `ScrapeAsync` gains an optional `Action<int total, int index, Issue current>?
onProgress` callback, invoked once per book at the top of the loop (before the series search) — not
per dialog-step, so the header's counter doesn't advance a second time between the series and issue
steps for the same book. `ScrapeSelectedBooksAsync`/`ScrapeSeriesAsync` in `OrganizerScraperPlugin`
construct the header view-model once per run, call `BeginModalBatch`, wire `onProgress` to update it,
and dispose the batch handle when `ScrapeAsync` returns (success, cancellation, or exception —
`try`/`finally`).

## 5. Settings

New field on `PluginSettings`: `ConfirmIssueMatch` (bool, default `true`) — surfaced in the plugin's
existing Settings screen (`SettingsView.axaml`/`SettingsViewModel.cs`) next to the existing
`AutoChooseTopMatch` toggle, since the two now form a related pair (series auto-choose, issue
confirm) the way CE's own `autochoose_series_b`/`confirm_issue_b` do.

## 6. Testing approach

- `ScrapeBatchHeaderViewModel`: unit tests for progress text (`Book {index} of {total}`) and that
  `Cancel` invokes the supplied cancellation callback exactly once.
- `RemoteCoverImageLoader`: unit tests against a fake `HttpMessageHandler` — cache hit returns
  without a second request, a failed fetch resolves to the placeholder state rather than throwing.
- `ComicVineScrapeOrchestrator`: extend existing tests for `onProgress` firing once per book (not per
  dialog step) and for the new issue-confirm branch (`ConfirmIssueMatch` true routes through a new
  interactive-issue-review delegate; false preserves today's silent `FindIssueDetailsAsync` path
  exactly).
- `ComicVineMatchReviewDialogViewModel`/new `ComicVineIssueReviewDialogViewModel`: unit tests for the
  select-then-confirm flow (`Choose` no longer resolves; a new `Confirm`/OK command does), the
  best-match/other-results split (index-based, not score-based), and `Show issues` triggering the
  peek path without resolving the series dialog.
- `NativePluginModalHostViewModel`: unit tests for `BeginBatch`/dispose lifecycle — `IsOpen` stays
  true across two sequential `ShowAsync` calls inside a batch, `HeaderContent` persists across them,
  and disposing an idle batch closes the shell.
- No new UI automation (FlaUI/UIA3) planned this pass, consistent with the original design doc §11.

## 7. Alternatives considered

**Batch header, plugin-only alternative (not chosen):** embed a small shared header control at the
top of each per-book dialog's own view, driven by a `ScrapeBatchProgress` object passed into each
dialog's constructor — no host change at all, contained entirely to the plugin. Rejected in favor of
§4's host-level `BeginBatch` after the user weighed the trade-off directly: the plugin-only version
re-renders the header on every `ShowModalAsync` call (a visible swap between the series and issue
steps for the same book), while the host-level version keeps one mounted header for the whole batch.
The cost is touching shared host code (`NativePluginModalHostViewModel`, `INativePluginUiEnvironment`)
that every native plugin depends on, rather than a change scoped to this one plugin.

**Series-match layout, alternatives not chosen:** a pure Omnibus-style cover grid (rejected — the
680px host cap leaves too little room per card for legible captions at 3 columns, and a grid doesn't
have a natural place for the preview pane the select-then-confirm model needs); the original flat
list with no cover art at all (rejected — it's the thing being replaced, no visual distinction from
plain text).

## 8. Out of scope / future work

- A second metadata source behind the now-real source dropdown.
- Additional sort modes behind the sort dropdown.
- Any change to headless/Scheduled-Task/automation-timer scrape runs.
- `BeginModalBatch` being adopted by any other native plugin — added as a general primitive, but no
  other plugin's use case is designed here.
