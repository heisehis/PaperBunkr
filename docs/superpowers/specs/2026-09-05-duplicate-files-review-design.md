# Duplicate Files Review — Design

A fourth Needs Review section, "Duplicate Files," that groups issues Paperbunkr's existing
duplicate-detection logic already flags, lets the user pick which copy survives, and deletes the
rest — plus a proactive Activity Center alert when a newly-imported file turns out to duplicate one
already in the library. Closes a gap real user data surfaced: duplicate detection has existed since
the original Smart Lists work, but nothing ever turned "these look like duplicates" into an actual
cleanup action.

Date: 2026-09-05. Status: design approved (brainstorming), pending implementation plan.

---

## Context / CE-parity check (standing rule)

CE's `ComicBookDuplicateMatcher`
(`_reference/ComicRackCE/ComicRack.Engine/Metadata/ComicBook/Matcher/ComicBookDuplicateMatcher.cs`)
is a passive filter matcher, unioning two groupings — a metadata-key group (Series, Format, Count,
Number, Volume, LanguageISO, Year, Month, Day) and a FilePath group, each with `Count() > 1` — into
one "Only Duplicates" list. CE has no cleanup UI beyond letting the user browse that filtered list
and manually delete entries themselves. Paperbunkr already ported this exact algorithm
faithfully as `SmartListQueryBuilder.DuplicateIssueIds` (confirmed via direct source check,
docs/superpowers/specs/2026-08-06-smart-lists-design.md §6) and seeded it as the "Duplicate
Candidates" system Smart List (`PaperbunkrDb.SeedSystemSmartLists`) — but, matching CE, it only ever
rendered as a flat grid of flagged issues, same as every other Smart List
(docs/superpowers/specs/2026-08-09-smart-lists-results-view-design.md §3).

That same doc explicitly deferred "Duplicate Candidates' actual duplicate-group UI" to something it
called "Duplicate Finder... separate, already-scoped-for-later cleanup." Tracing that reference: it
points at the **"Duplicate Finder" plugin** built later
(docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §7) — but that plugin is a *test fixture*
exercising three Plugin API hook categories end-to-end (`Startup`, a `Library` context-menu command
comparing selected issues by series+number via `IApplication.AskQuestion`, and a `CreateBookList`
dynamic Smart List entry). It was never a real cleanup feature, and its naive selected-issues-only
comparison doesn't cover the actual case (library-wide duplicates discovered passively). **This
design supersedes that stale deferral note** — this is the first real duplicate-cleanup feature, a
deliberate Paperbunkr enhancement beyond CE, same category as the missing-files real-time watch
(docs/superpowers/specs/2026-08-23-live-folder-watch-scanning-design.md) and its proactive alert
(the missing-files Activity Center alert shipped 2026-09-05, same session as this design).

## Goals

- Turn "Duplicate Candidates" from a browse-only list into an actual cleanup workflow: group
  duplicate issues together, let the user see enough to decide, pick a copy to keep, delete the
  rest.
- Reuse everything that already exists rather than rebuilding it: `LibraryDeletionHelper.RemoveIssue`
  for the actual delete (Recycle Bin, cross-reference cleanup), the Needs Review section pattern
  (`NeedsReviewViewModel`), the Activity Center alert pattern (`IActivityService.RaiseAlert`,
  shipped this session for missing files).
- Proactively surface newly-created duplicates instead of requiring the user to remember to check
  Smart Lists.

## Non-goals (v1)

- No visual/content diff between candidate files (page-by-page comparison) — the cover thumbnail
  plus file size/date is enough signal for a personal library at this scale.
- No content-hash-based duplicate detection. Still CE's exact metadata/path dual-key; a future
  perceptual-hash pass is a separate, much larger feature.
- No changes to the existing "Duplicate Candidates" Smart List itself — it keeps working exactly as
  it does today, browsable independently of Needs Review.
- No "merge metadata from both copies before deleting" — Resolve always keeps one copy's row as-is
  and deletes the others. If the discarded copy had better metadata, that's a separate manual edit.

---

## Data model

One new column, mirroring `Issue.MissingAcknowledged` exactly (same migration shape as
`20260806152003_AddIssueMissingAcknowledged`):

```csharp
public bool DuplicateAcknowledged { get; set; }
```

New migration `AddIssueDuplicateAcknowledged`. No other schema changes — grouping is computed live
over existing fields, the same "in-memory over the fully-loaded library" architecture every Smart
List condition already uses (docs/superpowers/specs/2026-08-06-smart-lists-design.md).

## Grouping algorithm

New method next to the existing one in `SmartListQueryBuilder`:

```csharp
public static List<List<Issue>> BuildDuplicateGroups(IReadOnlyCollection<Issue> issues)
```

`DuplicateIssueIds` stays untouched (still backs the Smart List and the `Duplicate` field) — this is
a second entry point over the same two keys, needed because Needs Review must show *which* issues
belong together, not just a flat set of flagged ids.

Implementation: union-find over `issues`. Two issues merge into the same cluster if they share
*either* key:

- **Metadata key**: `(SeriesId, Format, Count, EffectiveNumber(), EffectiveVolume(), LanguageISO,
  EffectiveYear(), Month, Day)` — identical to `DuplicateIssueIds`'s metadata grouping, same
  Effective\* accessors so filename-inferred values count.
- **Path key**: identical non-empty `FilePath`.

Using union-find (rather than the two independent GroupBys `DuplicateIssueIds` does, then
concatenating) means an issue linked to two different partners via two different keys still lands
in one cluster, not two overlapping group cards showing the same pair twice.

Only clusters with `Count >= 2` are returned. Each cluster is sorted deterministically —
`FileIsMissing` ascending first (a missing copy is never the default keep when a present one exists
in the same cluster — this can genuinely happen: the same issue can surface in both the Missing
Files and Duplicate Files sections at once, which is expected, not a bug), then `FileSize`
descending (nulls last, since a null size can't be "largest"), then `AddedTime` ascending, then `Id`
ascending as a final tie-break — so the best candidate is always first and the order never flickers
between refreshes when sizes are equal or unknown.

## Needs Review — "Duplicate Files" section

Fourth section in `NeedsReviewViewModel`, following the exact pattern the other three already use
(`RefreshDuplicateFileItems` alongside `RefreshMissingFileItems`, its own `HasDuplicateFileItems` /
`ObservableCollection`, folded into `HasPendingItems`).

**Refresh**: load every non-placeholder issue, run `BuildDuplicateGroups`, keep only clusters that
contain at least one issue with `DuplicateAcknowledged == false`. (A cluster where every current
member was previously dismissed stays hidden — but if a *new* file later joins that same cluster, it
reappears showing every member, acknowledged or not, so the user has full context again rather than
a confusing partial view.)

**Per-group row** (`DuplicateGroupRowViewModel`, new — holds a list of `DuplicateCandidateViewModel`,
one per issue in the cluster):

- Group header: series/issue label (e.g. "Kilo Station #012") + candidate count, matching the
  existing section's label conventions.
- Each candidate: small cover thumbnail (`CoverThumbnailPaths`/`AsyncCoverImage`, same source Library
  tiles use), file name, file size, date added — one compact row per candidate (visual option **C**
  from the brainstorming mockup: same row density as Missing Files today, plus a small cover swatch
  so the file-size heuristic isn't the only signal before deleting something).
- Selection: radio-style, one candidate marked "keep" per group. Defaults to the largest file
  (cluster is pre-sorted that way, so default = first candidate).
- Two actions, same button classes (`rowAction`/`pillAction`) the other three sections already use:
  - **Resolve** — calls `LibraryDeletionHelper.RemoveIssue` for every non-selected candidate, then
    `SaveChanges` + `Refresh()`. No `TwoStepConfirm` gate (unlike Missing Files' single-issue Remove)
    — deleting file copies you already have a keeper for is the entire point of this screen, and CE
    parity doesn't apply here since CE had no equivalent action at all. Recycle Bin already gives the
    safety net `RemoveIssue` provides everywhere else.
  - **Dismiss** — sets `DuplicateAcknowledged = true` on every issue currently in the cluster (not
    just the non-kept ones — dismissing means "these aren't actually duplicates I want flagged,"
    covering the whole group).

**Bulk action**: "Keep largest in all groups" button above the list (same placement/style as Series
Conflicts' `MergeAllAboveNinety`) — runs Resolve against every currently-visible group using its
pre-computed default (largest-file) selection, in one pass.

## Proactive alert

After an import batch completes — both `LibraryFolderScanner`'s manual "Scan Now" path and
`LiveFolderWatchService.ImportCreatedFilesAsync` — recompute `BuildDuplicateGroups` over the whole
library and check whether any cluster contains one of the issue ids from *this* batch. If so, raise
one alert via `IActivityService.RaiseAlert`:

```csharp
new ActivityAlert
{
    Severity = ActivityAlertSeverity.Warning,
    Title = "Possible duplicates found",
    Detail = $"{n} newly-added file{(n == 1 ? "" : "s")} may duplicate something already in your library.",
    ActionLabel = "Review",
    ActionLink = new ActivityLink(ActivityLinkKind.MigrationReview),
    DedupeKey = "duplicate-files",
}
```

Same `MigrationReview` link the missing-files alert uses — `MigrationOverlayViewModel.Open()`
already lands on the Review tab whenever `NeedsReview.HasPendingItems` is true, so no new
`ActivityLinkKind` is needed. Same dedupe-by-key behavior: a second batch of duplicate-producing
imports before the first alert is reviewed refreshes the existing alert's timestamp rather than
stacking a second one.

Two call sites need this check wired in:
- `LiveFolderWatchService.ImportCreatedFilesAsync` (already has an `onLibraryChanged`-style callback
  seam from the missing-files work this session — extend the same `onFilesMissing`-shaped pattern
  rather than inventing a new one).
- Wherever `LibraryFolderScanner.ImportNewFilesAsync`'s manual "Scan Now" result is consumed
  (Preferences → Library section's scan action) — the implementation plan should locate the exact
  call site and thread the same check through.

## Testing

- `SmartListQueryBuilderTests` (new cases alongside the existing `Duplicate` field tests):
  `BuildDuplicateGroups_UnionsIssuesLinkedByEitherKey_IntoOneCluster`,
  `BuildDuplicateGroups_ExcludesSingletons`,
  `BuildDuplicateGroups_SortsDescendingByFileSize`.
- `NeedsReviewViewModelTests` (new, alongside existing Missing Files tests):
  `Refresh_GroupsDuplicateIssues_ExcludingFullyAcknowledgedClusters`,
  `Resolve_DeletesNonKeptCandidates_KeepsSelected`,
  `Dismiss_AcknowledgesEveryClusterMember`,
  `KeepLargestInAllGroups_ResolvesEveryVisibleGroup`.
- `LiveFolderWatchServiceTests`: extend the missing-files-alert test pattern
  (`Deleted_WatchedFile_RaisesOnFilesMissing_WithCount`) with
  `Created_FileDuplicatingExisting_RaisesOnDuplicatesDetected`.
- Migration test mirroring `MissingAcknowledgedTests` for the new column.
- Manual: import a file that duplicates an existing library entry via a watched folder, confirm the
  Activity Center alert appears and links to a Needs Review queue showing the new "Duplicate Files"
  section with both copies, cover thumbnails, and the larger file pre-selected; Resolve and confirm
  the smaller copy is gone (Library + Recycle Bin) and the alert/section clear.
