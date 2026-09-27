# Library organizer audit — fixes and Library Organizer 2.1.13 parity — design

*Status: ~~draft for user review, 2026-09-25.~~ **Built, uncommitted** — confirmed 2026-09-26 via
source: `Naming/TemplateEvaluator.cs`, `Naming/Sanitizer.cs`, and `Organizing/LibraryOrganizerService.cs`
all modified per the plan's Step 1. On-screen check by the user still outstanding. Audited against the real plugin source
(`Library.Organizer.2.1.13.crplugin`: `lobookmover.py`, `locommon.py`, `losettings.py`, `configureform.py`),
which the user supplied. All decisions below are recommended-and-accepted in two grilling rounds.
Earlier code comments citing the plugin were written without this source and several are wrong (listed at the end).*

## Scope of the organizer

`LibraryOrganizerService` (`src/Paperbunkr.Data/Organizing/`), `Paperbunkr.Data/Naming/*` (template engine, sanitizer),
`OrganizeCoordinator`, `ProfileManagerViewModel`, the scheduled "Organize library" task, and the tests/docs around them.
CE core has no organizer (only single-file `RenameFile` and Export Comics); the parity reference is the LO plugin.

## Phase 1 — confirmed defects (each gets a failing test first)

| # | Defect (evidence) | Fix |
|---|---|---|
| a | `Sanitizer.SanitizePath` keeps empty segments, so a missing publisher gives `\Series\x.cbz`; `Path.Combine(base, "\Series", …)` drops the base folder (file lands at the drive root); empty imprint + publisher yields a `\\Series` UNC-style path; empty middle segments give `Pub\\Series`. Values containing `/` or `\` ("Fate/Zero") become nested folders. | Strip `/` `\` from *values* at insert time (as the plugin, LO:1529), then drop empty segments after splitting. Guard empty `BaseFolder` in `PlanAsync`. |
| b | Destination == source is planned as a move; `File.Move(p,p)` does not throw on .NET 10 → counted as success, no-op undo row, queued write-back. | Skip at plan time as "already in place"; when only the case differs, rename (plugin LO:609-622). |
| c | Two issues mapping to one destination are only compared against disk; the second `File.Move` throws and is a plain failure. | Track destinations within the batch (as the plugin, LO:406/498) so the collision resolver/policy runs; also in Simulate. |
| d | Replace calls `File.Delete` (permanent) though a comment says "recycle-bin-safe"; the old `Issue` row keeps a stale path. | Send the old file to the Recycle Bin, remove/repoint the old library row. |
| e | Simulate and Copy items are enqueued for metadata write-back. | Only enqueue for real Move items. |
| f | One bad template token throws out of `RunAsync` (only cancellation is caught); the Activity job ends "Cancelled" with no reason; the whole batch is lost. | Catch per item, record the reason, continue; job finishes Failed/Succeeded-with-warnings with the reason visible. |
| g | Undo walks back through older batches on repeated presses; no-op entries; no cleanup of created folders; not re-queuing write-back; not in Activity Center. | Undo only the last batch, exclude no-op entries (fixed by b), remove empty folders the run created, re-queue write-back, log to Activity Center. |
| h | Move and DB update are not atomic; a failed `SaveChanges` leaves the file moved and the row stale with no undo entry. | Record the undo entry immediately after the move; on save failure, attempt to move back and report. |
| i | Empty file template yields a file named `.cbz`. | Reject at save time; fail the item at run time (plugin LO:280). |
| j | `Sanitizer` trims trailing dots from *files* too and describes it wrongly. | Strip leading/trailing dots on folder segments only (plugin LO:1281). |

## Phase 2 — template parity and new options

- Port the plugin's semantics where Paperbunkr differs: `manga`/`seriesComplete` `(text)` shows text only for Yes and
  `(text)(!)` only for No (unknown → empty; LO:1554-1581; **changes an existing test**, `TemplateEngineTests.cs:179-181`);
  `first(Series)` accepts the UI's friendly/field names case-insensitively (LO:1471); `pad()` handles decimals
  (7.5 → 07.5), negatives and `<number0>` auto-width (LO:1541-1551, 1973-1998); bare date token behaviour verified
  against LO:1733 rather than guessed.
- Multi-value `(sep)(issue|series)` series-union: a per-token mode chosen when the template is written (no runtime dialog),
  so every issue of a series can land in one folder (LO:1765-1962).
- `UseFolder` / `UseFileName` profile flags (rename in place, or move only).
- `EmptyFolder` (default blank, so empty segments vanish), per-field `EmptyData` fallbacks, and a
  "skip items with empty required fields" option (`FailEmptyValues`, LO:1256-1284; skipped, not failed).
- **Unknown tokens**: validated when the profile is saved (message names the token); at run time a bad item fails alone with its
  reason. This deliberately deviates from the plugin, which leaves the token as literal text and mangles the folder name.
- Windows safety: reject reserved device names and control characters; skip items whose resulting path exceeds the limit
  with a report entry (plugin prompts instead, LO:627-636). Reuse the checks already in `ImportNaming.cs:114-124`.
- Exclude rules: replace the flat AND with Paperbunkr's nested smart-list rule engine (Any/All groups) plus an
  excluded-folders list (LC:582-641).

## Phase 3 — preview and report

- Before a manual **Move**, show the computed plan: counts (moving / already in place / collisions / skipped /
  would fail) and the first N before→after paths, with Confirm / Cancel. Scheduled runs skip the preview.
- Simulate produces the same plan view and a saved report (plugin: "complete log file created").
- A run report lists every failed and skipped item with its reason (currently discarded), viewable from the Activity Center job
  and saveable as text.
- Not in this round: live preview inside the profile editor (follow-up).

## Decided deviations / non-goals

- Empty-folder cleanup stays **bounded to the base folder** (the plugin is unbounded); documented as deliberate.
- Copy mode does **not** add the copy to the library (plugin default does); stopping its write-back is in Phase 1.
- No multiple-profiles-per-run, no fileless-book thumbnail export, no plugin-profile import (design spec declined it).

## Comment/doc corrections

- `LibraryOrganizerService.cs:39` cites `loduplicate.py` for the exists rule (actually LO:406; that file is only the dialog).
- `Sanitizer.cs` header describes a trailing-period strip (plugin: leading+trailing, folders only).
- `LibraryOrganizerService.cs:417` says the " (N)" strip is `\d+` (plugin: single digit, `[0-9]`).
- `LibraryOrganizerService.cs:441` "recycle-bin-safe" (false today; true after Phase 1d).
- `FieldResolvers.cs:103-110` implies missing tokens (all 57 are present).
- `wiki/Scraping-and-Organizing.md:34` claims Simulate "shows what would happen" (true after Phase 3); spec §13
  "previews its plan" likewise.

## Testing

Each Phase 1 defect: a test that fails on current code (`LibraryOrganizerServiceTests`, `SanitizerTests`), then passes.
Phase 2: template-engine tests for each semantic change and new option; `OrganizerStoreTests` for new profile fields
(additive migration). Phase 3: view-model tests for the preview/report. Missing-test list from the audit is added
(rooted/empty segments, same-path, in-batch duplicates, case-only rename, reserved names/long paths, folder/PDF/EPUB
inputs, write-back not firing for Simulate/Copy, undo after a Rename resolution, bad-token path). On-screen verification is the user's.

## Open items (not blocking)

- Whether an uncaught exception in an async RelayCommand crashes the app, and how the scheduler treats an exception in the
  Organize task — the plan verifies both.
- Whether the folder watcher reacts to organizer moves into folders outside the watched library.
- About half of `configureform.py` was skimmed; the plan re-reads it for tooltips/defaults before Phase 2.
