# Scraper & Organizer C: Automation and UX

Date: 2026-10-21. Part 3 of 3 ([A reliability](2026-10-21-scraper-organizer-a-reliability-design.md),
[B speed core](2026-10-21-scraper-organizer-b-speed-core-design.md), C). Depends on A and B.

UI work here follows the `paperbunkr-design-language` skill (tokens only, shared control families, `DialogCard`, `PopupFit`, 4 px grid,
1366x768 minimum, motion roles) and the `avalonia` skill; subskills are read off disk per CLAUDE.md before XAML is written, and
`avalonia-pro-max/review-checklist` runs before UI is called done. Shortcuts are `InputActionIds` entries handled with `ScreenInput`.

## Goal
Make scraping and organizing mostly hands-off: confident matches apply on their own, doubtful ones queue for fast review, every run can be
previewed and undone, and new files get scraped, organized and cleaned without the user driving each step.

## Facts established (2026-10-21 code survey)
- `AutoChooseTopMatch` (`ScrapeSettings.cs:21`, default false) gives CE two-phase behaviour: phase 1 auto-matches, low-confidence books are
  deferred (:402-480), then a series dialog and an issue-review dialog open per leftover book. Auto-choose uses text score plus a cover-hash gate with
  fixed thresholds 0.87 and 0.77 against the runner-up. No user-tunable threshold. Existing knobs: `ConfirmIssueMatch`, `NeverIgnoreThreshold`,
  `MaxSearchResults`, year filters, per-book `ScrapePermanentlySkipped`. `ScrapeBatchSummaryDialog` follows interactive runs.
- `OrganizeCoordinator` / `LibraryOrganizerService` do Simulate / Move / Copy with a preview dialog; Move updates `Issue.FilePath` and
  removes emptied folders upward. Organize is a separate action from scrape today.
- Write-back runs through `MetadataWriteBackQueue` after a batch; there is no ordering between it and the Organizer (see Spec A's race test).
- Provider order and cache come from Spec B; job persistence (`ScrapeJob`, `ScrapeJobItem`) comes from Spec B.

## Design

### 1. Strictness and decision explanations
- One setting `ScrapeStrictness` (Lenient / Balanced / Strict, default Balanced) replaces the fixed thresholds' role; it maps to text-score and
  cover-gate cut-offs in one `ConfidencePolicy` class. `AutoChooseTopMatch` becomes on by default when Metron is connected.
- Each decision records a `MatchExplanation` (title score, year, issue number, publisher, cover similarity, provider) stored on the job item
  and shown as a compact "Why" row in the review queue and the preview.
- Live preview in Preferences > Organize & Scrape: "On your last 500 scraped books this setting would auto-apply N, review M, skip K",
  computed from stored job items (hidden until at least 50 exist).

### 2. Review queue (`ScrapeReviewScreen`)
- A screen reached from the Activity Center ("12 books need review") and the Library toolbar, listing `NeedsReview` items grouped by series.
- Per series: candidate volume(s) with covers beside the library cover; "Accept series" accepts the volume for all its issues, per issue
  accept/skip/never-again (`ScrapePermanentlySkipped`); "Skip series".
- Keyboard-first: arrows move rows, Enter accepts, S skips, N never, Ctrl+Z undoes the last decision; shortcuts via `InputActionIds` and
  `ScreenInput.Attach`, tab strip classes for any tabs. Cards are not focusable unless the keyboard needs them.
- Replaces the per-book dialogs for non-interactive and scrape-on-import runs; interactive runs may still show the series dialog once per
  series (Spec B), and "Send the rest to review" is offered instead of answering dozens of dialogs.

### 3. Dry-run diff and undo
- Preview dialog (`DialogCard`, Large) before applying a batch: per-field before/after for the selected book, a count by outcome, and
  "Apply" / "Review first". Skippable via a "Don't preview, I trust Strictness" setting.
- `ScrapeBatchSnapshot` (table: Id, JobId, CreatedAt; `ScrapeBatchSnapshotItem`: IssueId, previous values JSON) written in the Apply stage in
  the same transaction as the metadata change. "Undo this batch" in the Activity Center and the batch summary restores every field from the
  snapshot (and re-queues write-back). Snapshots pruned after 30 days at startup.

### 4. Provider waterfall and field-level merge
- Order from Spec B. `FieldMergePolicy`: per-field provider priority table (default Metron > others > ComicVine; editable in Preferences >
  Organize & Scrape as a compact table of field rows with provider chips). A field the user edited by hand is never overwritten (uses the
  existing manual-edit/provenance markers; if none exist for a field it is treated as unlocked).
- ComicVine is consulted for a book only when Metron lacks a confident match or lacks a field the policy asks for and the user enabled
  "fill gaps from other providers".

### 5. Filename and folder intelligence
Extend the existing filename parser (series, volume vs year, annual, TPB, "v2 #14", scanner tags like `(2023-)`, `_` as space, e.g.
`Batman_Beyond_Neo-Gothic_(2023-)_#1`) to produce a `ParsedHint` (series, volume/year, issue, format) that seeds the group key and the
search before any API call. Folder names may supply series/year when the filename lacks them. Pure functions, table-driven tests from
real library names.

### 6. Cover hash tie-breaker
Keep the gate (`PassesCoverHashGateAsync`) but read/write hashes through Spec B's cache so each cover is computed once; used as the tie-
breaker when two candidates score within the runner-up margin.

### 7. Chained job: Scrape, Organize, Clean
- "Scrape and organize" as one job in the Library bulk-actions catalogue: Plan -> Match -> Apply -> WriteBack -> (per file, after its
  write-back settles) Organize -> sweep leftovers. The Organizer awaits `FileWriteBackCoordinator`'s per-path settled signal (Spec A) so
  it never moves a file mid-write. Organize alone still works.
- The summary dialog reports scraped/applied/review/skipped, files organized, leftovers cleaned, with Undo for the scrape part.

### 8. Organizer rules
Rename/move templates from metadata tokens (existing token system), shown in the preview with collision detection (existing
`OrganizerSafety`), plus "Organize after scrape" as a profile option feeding the chained job. No new template language.

### 9. Scrape on import
New files from drag-and-drop, watched folders and the daemon are queued as a low-priority `ScrapeJob` (kind Import). Default on when a Metron
token is set; it never blocks the import. Confident matches apply, the rest become `NeedsReview`; an Activity Center line "Scraped 8, 3 to
review". Setting: Preferences > Organize & Scrape > "Scrape new files automatically" (+ "and organize them" off by default).

### 10. Scheduled health pass
A scheduled task (Automation tab) "Scrape health": retries `Failed` items with backoff, re-checks `NeedsReview` items older than 30 days
against refreshed provider data, fills missing fields for already-matched books if "fill gaps" is on, runs the leftover sweep. Off by
default, daily, Low priority, non-interactive.

## Testing
- `ConfidencePolicyTests` (each strictness vs fixed score fixtures), `FieldMergePolicyTests` (priority, manual edit protected),
  `FilenameHintParserTests` (real names), snapshot/undo round trip, chained-job ordering test (Organizer never runs before write-back
  settles), scrape-on-import enqueue tests with a fake watcher, review-queue view-model tests.
- `RealWindowKeyboardTests`-style keyboard test for the review queue (arrows/Enter/S/N/Ctrl+Z) in the real `MainWindow`.
- Lint suite (`XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`) and renders in dark, Daylight and
  Matrix at 1366x768; user on-screen check before calling UI done.

## Out of scope
New providers; machine-learning matching; changing CE's scoring formula; auto-deleting duplicates found during organize.

## As built (2026-10-21)

Built: Strictness (moves the cover thresholds; the setting shows a one-line description instead of a live preview), the review list (a
dialog in the scraper's modal host, grouped by series, Review / Skip / Never; Review runs an interactive scrape of that series), batch undo
(`scrape-undo/`, the comic's own fields only, 30 days), "Scrape and organize…" in the issue and series menus, scrape on import (after a
library scan; watched-folder and drag-and-drop imports that don't end in a scan are not covered), and the "Scrape health check" task.

Second pass (2026-10-21, later):
- **Preview** (`ScrapeOrchestrator.DryRun` + `ScrapeProposal`): an interactive multi-comic scrape works every match out without writing, then
  shows each comic's field-by-field before/after (`ScrapePreviewDialogView`); only kept comics are applied, with the decisions already made.
  Setting "Show the changes before applying" (on).
- **Formats and filename hints** (`ComicFormatHints`, asked for by the user): the format comes from the Format field, else the file name, else
  the folder (Annual, TPB, HC/Deluxe, Omnibus, One-Shot, GN, Special, FCBD); a candidate series of the same format scores +15, a mismatch
  -15 (-5 for a collected edition named like its series). Added on top of CE's scorer, which is unchanged. Series-first reuse is keyed by
  format, so an annual filed under the regular series asks for its own match. File-style names are cleaned for the query and give a year.
- **Fill blanks from ComicVine** (off by default: one ComicVine request per comic): Metron's `cv_id` is now read; fields Metron left empty
  are filled from that ComicVine issue, never replacing a value.
- **Review list keyboard**: a ListBox; arrows move by series, Enter reviews, Delete skips.

Third pass (2026-10-21, later still):
- **Which source wins** (`ScrapeSettings.FieldSourcePreference`, Preferences > Organize & Scrape > Scrape): per issue-level field, Matched source /
  Metron / ComicVine. The other source's copy of the issue is found through the shared id (Metron's `cv_id`; a ComicVine issue looked up on
  Metron) and wins the chosen fields where it has a value. Series-level fields (series, publisher, volume, count) and the number stay with the match.
- **Strictness preview**: every automatic cover check is recorded as measurements only (`ScrapeDecisionHistory`, last 500, `scrape-decisions.json`);
  with 20 or more, the Strictness description says how many each level would have applied. The gate now measures both checks before deciding
  (`ConfidencePolicy.Passes`), same verdict. Fixed while testing: the runner-up limit moved the wrong way with Strictness (Strict was looser
  than Lenient); it is now Lenient 0.82 / Balanced 0.77 (CE) / Strict 0.72.
- **`.crswap`**: not Paperbunkr. `.crswap` is the scratch file Chromium (Chrome, Edge, WebView2) writes for a web page saving through the File
  System Access API; Paperbunkr never uses that API (its only WebView2 is the EPUB reader, which writes no comic files). The sweeper stays.

Not built: "Organize after scrape" as a profile option (the "Scrape and organize…" action covers it).
