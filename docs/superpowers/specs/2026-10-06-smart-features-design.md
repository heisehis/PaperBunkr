# Smart features — design

Date: 2026-10-06. Source: the "Smart features pitch" in `docs/Paperbunkr-Roadmap.md` (items 1–20). Design was grilled over
three rounds with the user; every decision below was confirmed. UI layouts were chosen from full-size mockups.

## 1. Scope

One spec, five slices, built in order. Each slice gets its own implementation plan and is checked on screen before the next starts.

| Slice | Theme | Pitch items |
|---|---|---|
| S1 | Smart lists and library gaps | #1 template gallery, #8 collection gaps, #17 metadata consistency scan, series progress fields (enables #6) |
| S2 | Reading behaviour | #6 (delivered as an S1 template), #10 drop-off, #11 Up Next, #18 Wanted ranking, #19 time-left |
| S3 | Library hygiene | #4 relink suggestions, #13 duplicate keeper ranking |
| S4 | Metadata | #16 synopsis genre suggestions, #20 auto-apply confidence gate |
| S5 | Continuity and plugins | #2 continuity suggestions, #3 chronology check, #5 status event + finalize job, #7 completion hook |

**Out of scope:** #9 (dropped), #12 (dropped, already covered by Insights → Trends), #14 and #15 (need an OCR / embedding dependency; own survey later).

**Deliberate deviations from ComicRack CE** (CE source checked in `_reference/ComicRackCE`):
- CE seeds seven default smart lists and has no template picker; the gallery is new.
- CE has only a series gap count (`ComicBookSeriesStatistics.GapCount`), no unread/read counts or last-read at series level; the three progress fields are new.
- CE has no collection-wide gap view and no reading-time estimates.

## 2. Foundations

**Code placement.** Logic lives in resolvers in `Paperbunkr.Data` (next to `InsightsResolver` / `HomeFeedResolver`); view models and views in `Paperbunkr.App`.
Scans that can be slow run as Activity Center jobs. Every resolver has unit tests; every new screen/section has headless render tests and keyboard tests
in the real window (`RealWindowKeyboardTests` pattern).

**Schema changes** (one migration each, each with a migration test; the up/down/up test must not reuse a column name that a later migration drops):

| Slice | Change |
|---|---|
| S1 | New table `HealthFindingDismissal` (`Kind`, `Key`, `DismissedAt`). One dismissal model for gaps, consistency findings, continuity suggestions and list-order checks. |
| S2 | `ReadingEvent.ActiveSeconds` (nullable int, no backfill). |
| S4 | `AppSettings.AutoApplyMinConfidence` (decimal 0–1, default 0 = current behaviour). |
| S5 | `AppSettings.RefreshProviderDataOnComplete` (bool, default true); `Continuity.CompletedNotifiedAt` and `StoryEvent.CompletedNotifiedAt` (nullable datetime). Appended enum value `SeriesActivityEventKind.StatusChanged`. |

Worktrees share the dev database: any session that runs the app or `dotnet ef` from a worktree migrates it.

**Plugin API:** 4.3 → 4.4 in S5 only.

**Project rules that apply to every slice**
- All shortcuts go through `IInputService`. The only shortcut change is the Smart screen's `NewItem` handler (opens the gallery); no new action ids.
- Buttons that remove a row from the list they sit in (`Not a gap`, `This is intended`, dismiss, relink) defer the removal with `Dispatcher.UIThread.Post`.
- A new `.axaml` view is created together with its code-behind in the same step.
- Each UI slice ends with the `avalonia-pro-max/review-checklist` pass. Every user-settable option is added to the Preferences search index.
- Notifications use the Activity Center.

## 3. S1 — Smart lists and library gaps

### 3.1 Series progress fields
New series-target smart-list fields in `SeriesSmartListCatalog` (+ query builder): `UnreadCount`, `ReadCount`, `DaysSinceLastRead`.
- *Read* = existing rule (`ReadThresholdPercent` 95). *Unread* = not read (in-progress counts as unread). Placeholders excluded.
- *Last read* = newest `OpenedTime` / `ReadingEvent` timestamp across the series' issues. A series never opened has no value; `<` comparisons do not match it.
- The series query builder includes related data only when a rule needs it; the progress fields gate an include of the series' issues the same way.
- One new issue-target field, `Has pending proposal` (exists-check against `MetadataProposal` with `Status = Pending` and the issue's id).

### 3.2 Template catalog and gallery dialog
`SmartListTemplateCatalog` — code-defined; each entry: id, name, kind (Issue / Series / Novel), one-line description, rule builder that produces the same condition-group graph a hand-built list has.

| Kind | Templates |
|---|---|
| Issues | Unread manga · Recently added, ongoing · Needs-review flagged · Highly rated, unread · Missing metadata · Never opened (90+ days) |
| Series | Behind on ongoing series (≥3 unread, idle 30+ days) · Mid-run (≥1 read, ≥1 unread, status Reading) · Almost finished (1–3 unread, ≥1 read) · Stalled (idle 21+ days, in progress) |
| Novels | In progress |

A guard test asserts every template only uses fields that exist in its kind's catalog and that every field has an operator valid for its data type.

**UI (chosen: dialog with a card grid).** "+ New Smart List" opens a dialog: kind tabs (Issues / Series / Novels), a two-column card grid (first card is "Blank list"),
each card shows name, description and rule chips rendered from the rule graph (so chips cannot drift from the real rules), footer `Cancel` / `Create from "<name>"`.
Create produces an ordinary editable list (no link back to the template) and opens the editor. `Ctrl+N` (`NewItem`) on the Smart screen opens the dialog. Esc closes.
Not in scope: user-saved templates, live match counts on cards.

### 3.3 Collection gaps
New section "Collection gaps" on the Library Health **Review** tab (compact table: Series / Owned / Missing / actions).
- `CollectionGapResolver` wraps `InsightsResolver.ComputeGaps` with different options: ≥2 numeric issues, no ownership floor, no cap on missing numbers. The Insights card keeps its stricter defaults and uses the same code.
- Missing numbers collapse to ranges (`#7–8, #10`). Fractional numbers, annuals, placeholders and remote-source issues are excluded. Issues whose files are missing still count as owned (they are already reported in the Files tab).
- Row actions: **Open series** (navigate to Series Detail) and **Not a gap**. A dismissal is keyed on the series plus its set of missing numbers, so a new gap in the same series resurfaces it. Dismissed rows sit in a collapsed sub-group with Restore (existing dismissed-rows pattern). Row removal deferred one tick.
- Recomputed when the section opens and on **Rescan**; no stored cache, no background job.

### 3.4 Metadata consistency scan
New section "Metadata consistency" on the Review tab. `MetadataConsistencyResolver`, per series:

| Check | Flag when | Fix |
|---|---|---|
| Year outlier | ≥4 issues have a year, ≥70% within 10 years of the median, and this issue is >10 years from the median | Pending issue-scoped `Year` `MetadataProposal` (source `Other`, confidence 0.7) proposing the median year |
| Publisher mismatch | ≥4 issues have a publisher and ≥70% agree on one value; this issue differs | Report only; "Open in editor" |
| Age-rating mismatch | same threshold as publisher | Report only; "Open in editor" |

`MetadataProposalField` has no Publisher or AgeRating member; extending it means changing the accept/revert code, so v1 does not. "Duplicate issue numbers" is intentionally not a check — the Duplicates section already groups by series + number.
Results are held in memory for the session and recomputed on **Rescan**. **This is intended** stores a dismissal per series + check (`consistency:{seriesId}:{check}`), same Restore pattern as 3.3.

### 3.5 Tests (S1)
Resolver unit tests (field evaluation incl. never-opened, gaps with ranges/fractions/dismissal resurfacing, consistency thresholds); template guard test; headless render of the dialog and both sections; real-window keyboard tests (Ctrl+N, arrows, Esc, Enter); migration test for `HealthFindingDismissal`.

## 4. S2 — Reading behaviour

### 4.1 Recording active time
- `ReadingEvent.ActiveSeconds` is written at session teardown next to the existing page-delta update; `IReadingEventRecorder.UpdateSessionPages` gets an overload taking seconds.
- Comic reader: `ReadingSessionClock` already tracks active time (2-minute idle cutoff). At `EndReadingSession` write `clock.ActiveTime − activeAtIssueStart`, read **before** `EndComfortVisit` resets the clock. Switching issues in `Load()` ends the first session and takes a new baseline.
- Novel and PDF readers get a small `ReaderActivityTracker` wrapping the same clock (10-second timer + reader input). The comic reader keeps its current wiring.
- Old events keep null and are ignored by pace.

### 4.2 Pace and time-left
`ReadingPaceResolver`: median seconds/page over the last 30 qualifying sessions (≥3 pages and ≥30 s), per item type (comic, novel). Fewer than 5 qualifying sessions → no pace → nothing is shown.
Estimate = (page count − last page read) × median, formatted `~25 min`, `<5 min`, `~1 h 20 min` (rounded to 5 min). Series Detail shows the sum over unread issues ("~3 h to finish") only when ≥3 unread issues.
Shown on: Detail issue panel, Library inspector, Home Continue Reading cards, Up Next rows. Never in the reader.

### 4.3 Drop-off watch (#10)
`DropOffResolver`: stalled series = not Completed/Dropped, ≥1 read issue, last read >21 days ago (existing `StalledDays`); stall point = issues read. Needs ≥5 stalled series. Cliff = most common stall point (ties → lower). At-risk = active series one issue short of the cliff.
Shown as a card on Insights → Today ("You often stop after about 3 issues. 4 series are at issue 2.") — nudge wording; hidden when there is no cliff or nothing at risk. No Home module.

### 4.4 Up Next (#11)
`UpNextResolver` beside `HomeFeedResolver`. Per series, the next unread issue in reading order (`NumberSortKey`). Eligible: series with ≥1 read issue, or in a reading list you have started; excludes Dropped and Paused series, placeholders and missing files, and any series with an issue in progress (those are in Continue Reading).
Score is additive with constants in one place (recency of last read; +25 for 1–3 unread left; +20 next in reading list; bonus for stalled so it resurfaces); an ordering test pins it. The score is never shown; each row shows one reason: *Almost done*, *Next in your list*, *Recent*, *Stalled N days*.
**UI (chosen: ranked list).** New Home section key `upNext`, shown by default right after Continue Reading, 5 rows: rank number, small cover, title + reason line, time-left on the right. Section hidden when empty. `HomeLayout.Resolve` inserts the new key at its default index for existing layouts. Row activation matches the Continue Reading cards.

### 4.5 Wanted sort (#18)
`WantedAffinityScorer`: 50% finish ratio of the linked local series, 30% recency of last read (decays over 90 days), 20% publisher share of your finished issues. Unlinked series use the publisher share only. A fourth Wanted sort **"Most likely to read"**; default stays *Attention*. Tooltip shows the breakdown.

### 4.6 Tests (S2)
Resolver tests (pace median/thresholds, estimate formatting, up-next ordering/exclusions, drop-off thresholds, affinity); headless tests (Home section default position, empty hiding, keyboard navigation, Insights card); migration test for `ActiveSeconds`.

## 5. S3 — Library hygiene

### 5.1 Relink suggestions (#4)
`MissingFileMatchResolver` finds candidates among issues whose file is present, in two tiers: **Exact** (same file size and page count) and **Probable** (same format and issue number, series name matches via `TitleNormalizer.NamesMatch`).
Each Missing Files row shows "Likely match: <path> (Exact|Probable)" and **Relink to this**. Relink keeps the *old* issue (it carries read history): point it at the new file, clear the missing flags as manual relink does, regenerate the cover, delete the duplicate row. The duplicate removal is not recorded as a removed path (the path now belongs to the relinked issue).
**Relink all exact matches** (two-step confirm) handles the safe cases in bulk; probable matches are one at a time. Row removal deferred one tick. The scanner's TPB-folding helpers are not extracted.

### 5.2 Duplicate keeper ranking (#13)
`DuplicateKeeperRanker` (pure, `Paperbunkr.Data`): order by (1) file present and not content-empty, (2) higher page count, (3) higher bytes per page, (4) format CBZ > CBR > CB7 > PDF > other, (5) older added date. Issues carry no pixel resolution, so it is not used.
The best candidate gets a "Recommended" badge with a one-line reason (e.g. "most pages: 28 vs 24") in Library Health → Duplicates. It does not change the current selection and never deletes. The Duplicate Finder sample plugin is untouched; no Plugin API change.

### 5.3 Tests (S3)
Matcher tiers and the relink data flow (history kept, duplicate gone, cover regenerated), ranker ordering, headless test of the Missing Files row.

## 6. S4 — Metadata

### 6.1 Confidence gate (#20)
- `AppSettings.AutoApplyMinConfidence` gates the Automatic policy: in `LibraryFolderScanner` (series-mismatch auto-accept and `AddFilenameProposal`) a proposal is Accepted only if its confidence ≥ threshold, otherwise Pending. At 0 nothing changes. Filename proposals have a fixed confidence of 0.6, so a threshold above 60% sends them to review. Provider proposals stay at 1.0 and always apply.
- Preferences (Library area, searchable) gets the two controls it lacks today: the Automatic/Prompt policy and the confidence slider.
- The proposals queue gets **Accept all at or above X%** using the setting; hidden at 0.
- Undo is the existing Applied list. Fix: `RevertSeriesField` gets a `Creator` case so rejecting a Creator proposal restores the previous value.

### 6.2 Synopsis genre suggestions (#16)
`SynopsisGenreInferrer`: curated table of ~30 genres → keywords. A genre is proposed when ≥2 distinct keywords match; confidence 0.6–0.8 by hit count. Candidates: series with an empty genre, no provider tags and a non-empty synopsis. No content warnings.
Output: series-scoped `Genre` `MetadataProposal`s, source `Other`, `ProviderKey = "synopsis-keywords"`, always Pending (ignore the auto-apply policy). A series that already has a proposal for that field in any status (including Rejected) is skipped, so no new column is needed.
Runs as new scheduled task `synopsis-genre-suggest` (off by default, daily, budget 200 series per run, Run now on the Automation tab). Reviewed in the existing Proposals review queue.

### 6.3 Tests (S4)
Threshold gating at 0 / 0.6 / 1, Accept-all-at-or-above, the Creator revert, inferrer hits/misses, skip-if-proposed, headless test of the Preferences controls.

## 7. S5 — Continuity and plugins

### 7.1 Continuity suggestions (#2)
- Add-to-continuity flows (manual add, search result, bulk add, Detail's add) show "also in: X, Y" per series row via `ContinuityResolver.GetContinuities`.
- A new source in the Suggestions tab lists series that share ≥3 characters with a member series and are not members (`GetSeriesIdsSharingCharacterWith`). Row: "Shares Spider-Man, Venom, Carnage with Amazing Spider-Man"; Accept → `AddSeriesToContinuity`; Dismiss → `HealthFindingDismissal` keyed on continuity + series. Computed when the tab opens; per-continuity candidate count is capped.

### 7.2 Reading order vs chronology (#3)
Works on any reading list (event-linked lists order issues *within* one event, so a linked-only check would have almost nothing to check). Each item's rank = its earliest event under `EventChronology` (directional relations Prequel / Sequel / Continuation); items with no events are ignored. Out-of-order count = ranked items − longest correctly ordered run (same measure as `CanonicalDiff.OutOfOrderCount`).
Skips lists marked PublicationOrder. The existing checks panel shows the count and the first few inverted pairs; **Reorder to chronology** moves only the ranked items within the slots they already occupy; **Order is intended** is a per-list dismissal (`list-order:{listId}`).
`EventChronology` resolves ordering cycles silently today; add `FindCycles`. The panel reports a loop ("A → B → A") and offers no reorder for lists touching it.

### 7.3 Status event and finalize job (#5)
- `SeriesStatusWriter.Set(context, series, newStatus, source)`: no-op if unchanged; otherwise writes the status, records a `StatusChanged` activity entry and raises `LibraryEvents.SeriesStatusChanged`. Callers: `MetadataLinkResolver.ProposeAndApply`, series bulk edit, the Library edit, the review-queue revert. CE migration and imports do not use it.
- When a series becomes Completed, `RefreshProviderDataOnComplete` is on and the series is linked to a provider: an Activity Center job refreshes relations (`RefreshRelationsAsync`) and the provider cover candidates. Deduplicated per series; a failure is reported once and not retried.
- Preferences > Behavior toggle "Refresh provider data when a series is marked completed" (on by default, searchable).
- **Non-goal:** no scheduled provider status refresh. Status only changes on manual actions today; the event is the seam for a future scheduled refresh.

### 7.4 Completion hook (#7)
A collection (continuity or story event) is complete when every non-placeholder issue in it is read. `ReadingFinished` triggers an evaluation of the continuities and events containing the finished issue. On transition to complete with `CompletedNotifiedAt` empty: stamp it, raise the hook, post an Activity Center notification "You finished <name>" with an Open action.
Re-arming: `CompletedNotifiedAt` is cleared by the post-scan pass (`LibraryScanCompleted`) when the collection is incomplete again, and when a series is added to a continuity.
Hook `ContinuityCompleted`: notification-only; globals = kind (`continuity` | `event`), id, name, issue count, completed-at; fire-and-forget through `DomainHookDispatcher`. Plugin API 4.4 touches: hook constant, `DomainEventHooks`, `ValidHooks`, globals class, `PluginGlobalsTypeMap`, the `PluginHostService` handler, `PluginApiCompatibilityTests`, `wiki/Plugins.md`.

### 7.5 Tests (S5)
Resolver tests (suggestion thresholds, inversions and cycles, completion + re-arm sequences); status writer (unchanged, raised once, excluded callers); hook dispatch + compatibility tests; headless tests for the nudge, suggestion rows and check panel; migration test for the two `CompletedNotifiedAt` columns and the new settings.

## 8. Implementation notes

Built 2026-10-06, all five slices, in one session. **Nothing here has been seen in the running app** - everything below was verified by
unit tests and headless render tests only. Plan: `2026-10-06-smart-features-plan.md`.

**Where the build differs from the sections above**

- **§2 schema.** Three migrations, not one per change: `AddHealthFindingDismissals` (S1), `AddReadingEventActiveSeconds` (S2) and
  `AddSmartFeaturesSettings` (S4 + S5: `AutoApplyMinConfidence`, `RefreshProviderDataOnComplete`, both `CompletedNotifiedAt` columns).
  `HealthFindingDismissal` also has a `Label` column, so a dismissed row can be named in the "Dismissed" sub-group after its finding can
  no longer be computed. The smart-list fields need no migration (`SmartListCondition.Field` is stored by name).
  `AddSmartFeaturesSettings.Down()` drops the two `CompletedNotifiedAt` columns but deliberately leaves the two `AppSettings` columns:
  the project's standing rule (a `DropColumn` rebuilds `AppSettings` on SQLite and strands the three legacy `Library*` columns older
  rollbacks need). Forgetting that rule first broke seven older migration tests; they pass again.
- **§3.1.** `SeriesStatus` was also added to the *issue* catalog; the "Recently added, ongoing" template needs it.
- **§3.2.** The three sidebar "+" buttons (Issues / Series / Novels) each open the gallery on their own kind's tab; Ctrl+N opens Issues.
  Esc is routed through `MainViewModel.Escape` like every other overlay. Double-clicking a card creates from it.
- **§3.4 - the year check is report-only too.** Accepting a `Year` proposal never changes an issue that already has a stored year
  (`EffectiveYear` is `issue.Year ?? acceptedProposal`), so a proposal could not correct an outlier. All three checks now offer "Open in
  editor" per outlier and create no proposals.
- **§4.1.** The novel and PDF readers have no window-activation signal, so they count as present while a book is open; the two-minute
  idle cutoff is what stops their clock.
- **§4.2.** Estimates are comics only for now. Shown on: Detail (appended to the issue and series meta lines), the Library inspector
  ("Time left" row), Home Continue Reading cards and Up Next rows. Not yet on the Manga detail screen or for novels.
- **§4.4.** A row's reason is the most specific thing true of the series (almost done, then next in list, then stalled, then recent),
  not the largest score component - with the largest component nearly every row read "You read this recently".
- **§4.5.** The Wanted queue lists one group per watched series, so the sort ranks series, and the "why" is the tooltip on a group's
  summary line.
- **§5.1.** No explicit thumbnail call was needed beyond a best-effort regenerate: the cover cache is invalidated for the kept entry.
  Present issues are loaded without their proposals, so the probable tier compares stored numbers.
- **§6.1.** The two controls sit at the top of Library Health > Review > Metadata Proposals (always visible), not on a separate
  Preferences page. Series-scoped proposals can now be Pending, which the review code did not expect: accepting one writes the Series
  field (it used to only flip the status), and rejecting one that was never applied no longer "reverts" the field to its snapshot.
- **§6.2.** A suggestion writes `Series.Genre`, as provider proposals do; it does not tag individual issues. A series nothing could be
  read from has no marker row and is looked at again on the next run (the check is a handful of regexes).
- **§7.1.** The "Also in: X, Y" nudge is on the continuity page's series search results (manual, search and bulk add). Detail's own
  add-to-continuity flow already lists the series' continuities and was left alone.
- **§7.2.** Entries are only compared within one connected chain of directionally related events, so two unrelated chains can never
  produce a false inversion. The reorder writes `SortOrder` directly and does not raise `ReadingListChanged`.
- **§7.3 - no `SeriesStatusWriter`.** Two of the status writers are bulk-editor field setters with no context to call a helper with, so
  the change is detected in `PaperbunkrDbContext.SaveChanges` instead (the same backstop pattern as the reading-list one): any modified
  series whose `Status` differs gets a `StatusChanged` activity entry in that save and a `SeriesStatusChanged` event after it. Creating
  a series with a status is not a change. The finalize job refreshes provider *relations* (AniList, MangaBaka, MangaDex links); cover
  candidates have no per-series store to refresh. The toggle is in Preferences > Connections > Tracking behavior, beside the other
  provider behaviour switches.
- **§7.4.** The reaction lives in `SmartLibraryReactions` (started in `App.axaml.cs`, after the plugin host). The issue just finished is
  counted as read even if the debounced position save has not landed. The notification's action opens the Continuity screen, not the
  specific continuity or event.
