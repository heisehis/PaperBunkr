# Series identity scan fixes — design

2026-08-31

## Problem

Two real, user-reported data-quality cases where `LibraryFolderScanner`'s series
find-or-create logic (`ImportFiles`, keyed purely on exact `Series.Name` match) produces the
wrong `Series` grouping:

1. **TPB over-splitting.** A trade-paperback collection's embedded `ComicInfo.Series` field
   often carries collection wording an ongoing single-issue series' `Series` field doesn't
   ("Batman: Court of Owls", "Batman Vol. 1") — an exact-name miss against the existing
   "Batman" series, so the scanner creates a second, spurious series for what is really just
   a collected volume of the same story.
2. **Anthology/imprint under-splitting.** Confirmed via a live Detail-screen check on the
   user's real library: their "Warhammer 40" series holds 51 issues, but the real story
   identity for each issue lives on its own `Issue.Title` ("Damnation Crusade", "Dawn of War
   III", "Forge of War", ...) — `Series.Name` is a shared imprint label, not a series
   identity. One `Series` row structurally can't represent ~15 unrelated stories; the
   book-centric Library redesign (2026-08-18) fixed *browsing* this (per-issue tiles, not
   series cards) but the underlying `Series` row is still one row for all 51 issues, which
   still affects Series Detail, Smart Lists, Continuity, and every other series-scoped
   feature.

Both are the same root shape — "is this the same series?" needs to be smarter than exact
`Series.Name` equality — but they pull in opposite directions (fold vs. split), so they're
two independent fixes sharing one integration point (`LibraryFolderScanner`) and one shared
primitive (moving an issue to a different series).

## 1. TPB folds into its base series

**Where:** `LibraryFolderScanner.ImportFiles`, around the `seriesName`/`seriesByName` lookup
(`src/Paperbunkr.App/Services/LibraryFolderScanner.cs:162-170`).

**TPB detection.** `embeddedInfo?.Format` (the raw `ComicInfo.Format` string — filename-parsed
`ComicNameInfo.Format` never produces "TPB", it only matches annual/preview/B&W/etc., so it's
not a source here) is checked against the same alias set `format-aliases.tsv`'s "Trade Paper
Back" row already defines: `trade paperback`, `tpb`, `trade` (trimmed, case-insensitive). A new
`private static bool IsTradePaperback(string? format)` helper in `LibraryFolderScanner` holds
this — small and self-contained enough not to justify routing through `MarkResolver`'s
alias-table/SVG-asset machinery, which exists to resolve UI badges, not to gate scan logic. A
comment on the helper notes it mirrors that tsv row so the two don't silently drift.

**Stripping.** When `IsTradePaperback` is true, a new `private static string StripCollectionWording(string seriesName)` produces a base-name candidate:

1. Truncate at the first `:` (drop the subtitle) — `"Batman: Court of Owls"` → `"Batman"`.
2. Strip a trailing `Vol.` / `Volume` + number — `"Batman Vol. 1"` → `"Batman"`.
3. Strip a trailing `(N)` or `#N` — `"Batman (1)"` → `"Batman"`.
4. Trim whitespace/trailing punctuation left over from the above.

**Fold logic.** The existing flow is unchanged for every non-TPB issue and for any TPB whose
raw `seriesName` already matches an existing series exactly (today's exact-match lookup runs
first, as it does now). Only on a miss, and only when `IsTradePaperback` is true, does the
scanner also try `StripCollectionWording(seriesName)` against the same `seriesByName`
dictionary:

- **Stripped name matches an existing series** → the issue attaches to that existing series
  (fold). No new series is created. The issue's own fields (`Title`, `Number`, `Volume`, etc.)
  are unaffected — only which `Series` it's attached to changes.
- **Stripped name still misses** (no existing "Batman" to fold into — e.g. a standalone TPB-only
  work like "Batman: White Knight" with no ongoing single-issue counterpart) → falls back to
  today's unchanged behavior: create a new series using the raw, un-stripped `seriesName`. The
  stripped name is **never** used to name a newly-created series — folding only ever attaches to
  something that already exists, so a standalone TPB's own distinct title is preserved rather
  than being merged into a same-prefixed but unrelated series.

This keeps the change conservative: it only ever *prevents* a spurious new series when a clear
existing match is available; it never invents a new grouping.

## 2. Automatic series-split (anthology/imprint case)

**Detection rule.** Within one `Series`, group its `Issue`s by `Title` (trimmed,
case-insensitive), excluding blank titles and any title equal to the series' own `Name`
(case-insensitive — that's just an issue restating its series, not a distinct story). Any
group with **2 or more issues** is a split candidate: those issues move to a new `Series` whose
`Name` is that title. `ContentType`/`ReadingMode` are copied from the original series (a
better default than leaving a fresh `Unknown`/`LeftToRight`, since the whole anthology already
carries one classification). Groups of exactly 1 (a one-off subtitle on an otherwise normal
issue — common in ongoing single-issue series) are left alone; requiring ≥2 keeps that the
common case, not the exception.

**Known false-positive risk, accepted deliberately:** a genuine two-part story arc inside an
otherwise normal ongoing series (two issues sharing one arc title) will also get split out,
since the rule can't distinguish "anthology imprint" from "ongoing series with a two-parter." Per
your explicit choice of automatic/no-review over a manual confirm step, this is an accepted
trade-off, not an oversight — worth knowing about if a split looks wrong later.

**Move primitive (shared, not reinvented).** `SeriesReassignmentResolver.Apply(context,
proposal)` (`src/Paperbunkr.Data/Metadata/SeriesReassignmentResolver.cs`) already does exactly
this move correctly — target find-or-create by name, both-sides EF navigation fixup (needed or
the reassignment silently no-ops, per its own comment), two-phase `SaveChanges` so "does the
source series now have zero issues" reads real persisted state, and pruning the source series
once it's empty. It's reused, not duplicated: its body is extracted into a new
`internal static void MoveIssueToSeries(PaperbunkrDbContext context, Issue issue, string
targetSeriesName)`, and `Apply` becomes a thin wrapper that validates the proposal/issue/
`ProposedValue` and calls it. The new split detector calls the same shared method.

**New `SeriesSplitDetector`** (`Paperbunkr.Data.Metadata`, alongside `SeriesReassignmentResolver`):

- `SplitResult? DetectAndSplit(PaperbunkrDbContext context, int seriesId)` — runs the detection
  rule above for one series; returns `null` if no group qualifies, otherwise a
  `record SplitResult(string OriginalSeriesName, IReadOnlyList<(string NewSeriesName, int
  IssueCount)> SplitGroups)` describing what moved (for the toast/summary — see below). Calls
  `MoveIssueToSeries` once per issue in each qualifying group; the first call in a group creates
  the new `Series` row (subsequent calls in the same group find it by name, now persisted).
- `IReadOnlyList<SplitResult> DetectAndSplitAll(PaperbunkrDbContext context)` — runs the above
  over every series currently in the context; used by the on-demand command below.

**Trigger (a) — every scan.** `LibraryFolderScanner.ImportFiles` already tracks
`seriesTouched` (currently a `HashSet<string>` of names, used only for the result's count). It's
changed to a `HashSet<Series>` of the actual touched entity references instead — their `Id`
only becomes valid after `SaveChanges` (same ordering constraint the existing
`autoAcceptedSeriesProposals` post-save pass already has), so after that pass runs,
`SeriesSplitDetector.DetectAndSplit` is called once per distinct `.Id` across the touched set.
This catches the split going forward as new issues land on a series, without needing a full
rescan.

**Trigger (b) — on-demand, whole-library.** Trigger (a) alone won't fix the user's *existing*
51-issue Warhammer 40 series (it's not "touched" unless rescanned). A new command,
**"Detect & Split Mismatched Series,"** is added to Preferences → Libraries, next to the
existing "Scan Now"/"Sync Metadata" buttons (`PreferencesScreenViewModel.cs:1130` region) —
that's where whole-library maintenance actions already live, not the Library screen toolbar.
Same `IsScanning`-style busy-flag/status-text/`_showToast` pattern `ScanNow` already uses
(`PreferencesScreenViewModel.cs:1156-1161`): runs `SeriesSplitDetector.DetectAndSplitAll`
on a background thread, then shows a toast summarizing every split that happened, e.g. "Split 3
series: Warhammer 40 → 15 series, ...". No confirmation dialog before running, matching the
"automatic, no review step" choice — the toast is purely a post-hoc summary, not a gate.

## Testing

- `LibraryFolderScannerTests`: TPB with an existing base series folds (no new series, issue
  attaches to the existing one); TPB with no matching base series falls back to creating a new
  series under the raw name (unchanged behavior); a non-TPB issue whose name would strip to an
  existing series is *not* folded (folding is TPB-gated); an exact raw-name hit short-circuits
  before any stripping is attempted.
- New `SeriesSplitDetectorTests` (`Paperbunkr.Data.Tests`, alongside
  `SeriesReassignmentResolverTests`): a series with two 2+-issue title groups splits into two new
  series plus itself pruned if it ends up empty; a series where every issue shares one title
  (a normal ongoing series) never splits; a lone repeated-title pair inside an otherwise
  single-titled series splits only that pair out, leaving the rest on the original series;
  `ContentType`/`ReadingMode` land on the new series copied from the original.
- `SeriesReassignmentResolverTests`: unchanged behavior after the `MoveIssueToSeries` extraction
  (pure refactor, existing tests should pass unmodified — a regression here would mean the
  extraction wasn't behavior-preserving).
- `LibraryFolderScanner` integration: a scan that both creates a fresh anthology-style series
  and immediately splits it within the same run (trigger (a) firing on a just-touched series).
- `PreferencesScreenViewModelTests`: new command's busy-flag/status/toast wiring, mirroring
  existing `ScanNow`/`ScanBooksNow` tests.

## Out of scope

- No UI to preview or undo a split before it happens (explicit choice: automatic, no review).
- No manual "move this issue to a different series" editor — not needed since the detector
  handles the known case end-to-end; flagged here only so it's not assumed to exist.
- No change to how `Format` itself is displayed/badged (`MarkResolver`) — this only reads it.
