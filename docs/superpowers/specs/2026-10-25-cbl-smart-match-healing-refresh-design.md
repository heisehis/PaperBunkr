# CBL Smart Match, part 3: healing triggers and safe source refresh

Date: 2026-10-25. Status: approved design; written spec awaiting user review.

Part 3 of the October 13 Smart Match design. Part 1 (matching core) and part 2 (review tools) are complete. This delivery finishes item 1 of the current six-item sequence; items 2–6 remain separate.

## 1. Scope

Extend the existing conservative placeholder healer to run after saved changes that affect matching identity, and make refresh of arc-backed reading lists preserve local additions and explicit manual links safely.

This is a PaperBunkr-specific improvement. The ComicRack CE reference has no corresponding source-linked arc-refresh or Smart Match healing workflow. The existing CE list-import behavior does not define these policies.

In scope:

- Recheck unresolved/ambiguous placeholders after completed scrapes, identity metadata edits, series renames/reassignments, accepted identity proposals, and metadata undo/redo.
- Preserve automatic healing's existing rule: only unambiguous `AutoLink` results relink; ambiguous/missing entries remain unresolved, and links made by hand are not touched by healing.
- Make arc refresh preserve hand-added rows and source rows that the user manually linked when the provider no longer returns them.
- Reconcile current source entries by stable source-entry ID when available, otherwise strict source-reference identity.
- Apply refresh's local changes atomically after remote fetch and reject incomplete source responses before any local mutation.

Out of scope: Continuity rebuild behavior, whole-import undo, changes to the matching algorithm or network matching, new persistent source rules, review-screen changes, and backlog items 2–6.

## 2. Existing behavior and constraints

`ReadingListHealer` in `src/Paperbunkr.Data/ReadingLists/Matching/ReadingListHealer.cs` already exposes `RecheckFor`, `RecheckList`, and `RecheckAll`. Folder scanning calls `RecheckFor` after saving new issues. Arc refresh runs `RecheckList` before its current reconciliation. The healer only processes placeholders whose source method is not `User`, matches with the current `MatchEngine`, and relinks only an automatic match. It joins a caller's existing EF transaction.

`ArcReadingListBuilder.RefreshAsync` currently fetches the provider order, heals the list, materializes current source entries, and calls `ReadingListReconciler`. The reconciler is also used by Continuity rebuilds. It removes every item absent from the new order, including hand-added entries, so its default behavior must not be changed for this feature.

`ArcEntryList` carries both skipped source lines and an optional provider-reported entry count. A skipped line or a reported count greater than the returned issue count indicates that a refresh response is incomplete and must not be used to remove list items.

## 3. Approved decisions

### Healing

- Run healing after committed changes to matching identity: series identity/name or issue series assignment, issue number, volume, year, format, and provider issue IDs.
- Include successful scraping, direct and bulk metadata edits, accepted identity proposals, series rename/reassignment, and metadata undo/redo.
- Do not recheck for unrelated edits or no-op saves. For a batch, recheck once after the full operation commits.
- Continue to preserve `MatchMethod.User` links and require a unique automatic match. Ambiguous and missing results update their status and remain for later review.
- Report links and unresolved choices once in the existing operation feedback (Activity/job or edit summary); do not create a notification for a no-op recheck.
- Existing scan-triggered healing remains in place.

### Source refresh

- Fetch the source order and best-effort overview before opening the database transaction.
- If the response includes skipped entries or reports more entries than it returned, abort without local changes and report the incomplete response.
- After a complete fetch, heal, resolve, reconcile, update the list and record the list-change event in one database transaction. If local reconciliation fails, the whole local refresh rolls back.
- Match existing source-backed rows by stable source-entry ID when present. Without one, use strict source-reference identity (provider IDs first; otherwise series, issue number, year, volume, and format). Conflicting or weak identity does not guess.
- For a source entry still returned, use the current automatic match as the link target even if the row was manually linked previously. Reuse the row when identity is strict and preserve its notes, role and role-detection state, group label, and row identity.
- Remove a source-backed row absent from the provider unless its link was explicitly set by the user. Keep such manually linked rows and hand-added rows (which have no source record) after the refreshed source order, preserving their relative order and all item fields.
- If a new source entry resolves to the same issue as a hand-added row, retain only the hand-added row, with its fields and position in the retained section.
- Remove orphaned placeholders for source rows actually removed, subject to normal reference checks. Do not delete an issue still referenced elsewhere.
- Leave `ReadingListReconciler`'s default behavior untouched so Continuity rebuilds retain their current semantics.
- Summarize additions, removals, relinks, unresolved entries, and retained local rows in the existing refresh result/feedback.

## 4. Implementation shape

Use an arc-refresh-specific reconciliation planner around `ArcReadingListBuilder.RefreshAsync`. It pairs incoming source references with existing list items by the approved source identity, computes the final source order followed by retained local rows, and stages link/source changes without losing item annotations. It handles a source entry changing its automatic link separately from source identity, preventing issue-ID-based reconciliation from discarding row metadata. Duplicate local/source targets collapse to the retained hand-added row as approved.

Leave `ReadingListReconciler`'s normal contract unchanged for Continuity. The arc planner owns source-row removal, retained-row ordering, duplicate accounting, and orphan placeholder cleanup for this refresh path only.

Keep healing logic in `ReadingListHealer`. Add post-commit calls at the operations that save identity changes, with shared helper code for collecting changed issue IDs and formatting one result per operation. A series rename or reassignment supplies the affected issues from that series. Metadata undo/redo compares the before/after snapshots and rechecks only issues whose matching identity actually changed. Any provider-ID-only update is included.

For a refresh, the database context is opened for the operation, current list state is reloaded after the remote fetch, and a single EF transaction wraps healing, resolution/materialization, arc-specific reconciliation, list-change recording, and final save. The transaction does not include network requests. Verify that any placeholder ID needed for reconciliation is saved within the transaction before dependent rows are constructed.

## 5. Failure handling and safety

- Provider/network errors and incomplete responses leave the list, placeholders, and reading-list history untouched.
- An unavailable/deleted/retargeted list after the network fetch aborts the refresh rather than applying data to stale source settings.
- SQLite/EF failures roll back all local refresh work. Surface a failure summary through the existing status/job route.
- Match ambiguity never becomes an automatic link. Strict source identity ambiguity does not merge rows.
- Duplicate target issues never create duplicate list rows; report the duplicate outcome while preserving the approved hand-added row.
- Healing stays local and makes no provider/network request.
- No behavior change to Continuity reconciliation, manual review batches, or the global metadata undo model beyond invoking a conservative recheck after identity changes.

## 6. Verification

Data tests cover post-change healing candidates, manual-link protection, ambiguous/no-match outcomes, provider identity changes, series rename/reassignment, and no-op identity updates. Arc refresh tests cover stable-ID and strict metadata identity reuse, source entry relinking while preserving row fields, hand-added rows/order, removed source rows, manually linked removed rows, duplicate incoming/local targets, orphan placeholder cleanup, incomplete and failed fetches with no mutations, and transaction rollback. Continuity reconciliation tests confirm its existing remove/reorder behavior is unchanged.

App tests cover the scrape, edit, proposal-acceptance, series-change and undo/redo trigger points, once-per-operation reporting, and no duplicate rechecks. Finish with targeted Data/App tests, the main `Paperbunkr.App` Debug build, and `git diff --check`. Native on-screen review is recorded separately if interaction or visual layout changes are introduced.

## 7. Self-review

- Scope is limited to the remaining Smart Match part 3; backlog items 2–6 remain untouched.
- `ReadingListReconciler` remains behaviorally unchanged for Continuity.
- Q4's explicit preference (use the new automatic match for a source entry that still exists) controls over the earlier bundled Q8 phrasing; source identity reuses the row and its metadata, not its old link target.
- Incomplete provider responses are detected from the existing `SkippedLines` and `ReportedCount` fields before mutation.
- The hand-added duplicate case retains one hand-added row, preventing a duplicate without silently replacing its annotations.
