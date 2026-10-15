# Faster automatic missing-file verification after Scan Now

**Date:** 2026-10-15  
**Status:** Approved in conversation; written spec awaiting user review  
**Builds on:** `2026-09-06-scan-missing-file-handling-design.md`, `2026-10-15-library-scan-diagnostics-design.md`

## Problem

When `AutoRemoveMissingOnScan` is enabled, `PreferencesScreenViewModel.ScanNow` runs the complete
Library Health verifier after `LibraryFolderScanner.ScanAllAsync`. For every tracked issue, that
verifier calls `File.Exists`; for every present file, it also opens the archive through
`PageDecodeCore.TryOpenProvider` to refresh the empty/unreadable-file state. The user reports this
second pass takes long enough to dominate scans over a library of 5,500+ comics.

The setting is specifically about automatically removing confirmed-missing files. Empty or
unreadable archive detection is a separate Library Health feature and can remain part of the
explicit **Verify Now** action. ComicRack CE's `ComicScanner.ScanFilesOrFolders` likewise checks
linked comic paths for existence as part of its full scan (`_reference/ComicRackCE/ComicRack.Engine/ComicScanner.cs`)
and does not open every archive to perform the separate content-health check Paperbunkr added.

## Goals

- Remove the redundant second existence check for tracked comics under watched folders that Scan
  Now has successfully enumerated.
- Keep automatic cleanup's existing two-consecutive-check threshold, missing flags/events, and
  reachable-root guard.
- Keep full empty/unreadable-archive detection in explicit **Verify Now**.
- Preserve verification coverage for library entries outside successfully enumerated watched roots.
- Keep scan observations transient; do not persist a file index or paths in Activity history.

## Non-goals

- Do not change the meaning or default of `AutoRemoveMissingOnScan`.
- Do not change manual **Verify Now**, manual bulk removal, or the content-empty queue.
- Do not add a persistent incremental file index or alter directory-enumeration behavior.
- Do not treat an unavailable watched folder as a successfully enumerated folder.

## Design

### 1. Transient scan presence snapshot

Extend the full-scan result with a transient presence snapshot containing:

- Canonical paths of supported comic files observed during the completed enumeration, including
  candidate comics imported by that scan (so the snapshot grows with comic files, not unrelated
  files under watched roots).
- The watched roots whose enumerations completed successfully.

The snapshot is produced in the existing `ScanAll` directory walk. It adds no extra filesystem
walk or archive reads and is not included in the `LibraryScanCompletedEvent`, Activity summary, or
database. A root is included only if it existed and its recursive enumeration completed; roots
that were unavailable remain outside the snapshot. A failed or cancelled scan does not proceed to
the post-scan cleanup step, as today.

Path identity uses canonical full paths with Windows case-insensitive comparison, consistent with
the app's Windows library behavior. The scanner canonicalizes observed comic paths so equivalent
slash/case forms cannot be mistaken for a missing comic. An issue whose extension is no longer
supported by the scanner is not covered by the snapshot and uses the fallback check below.
Invalid or uncanonicalizable paths are also not trusted as snapshot matches and use the fallback
check below.

### 2. Missing-only auto verification

Add a Library Health verification path specifically for automatic post-scan cleanup. For each
tracked, non-placeholder issue with a file path:

1. If its canonical path is under a successfully enumerated watched root, determine presence by
   membership in the snapshot.
2. Otherwise, use `File.Exists`, preserving current coverage for files outside those roots and
   roots that were unavailable during the scan.
3. Apply the existing missing-state behavior: present files clear missing state/count and
   `MissingAcknowledged`; absent files increment the consecutive verification count, clear
   `IsContentEmpty`, and become eligible for removal only at the configured threshold and when not
   acknowledged. Raise `MissingFileDetected` only when the threshold is crossed, as today.
4. Do not open present archives and do not change `IsContentEmpty` or `EmptyRowAcknowledged` for
   present files in this mode.

The automatic path continues to use the existing reachable-root guard before removing a confirmed
missing issue, and removal still records Recently Removed / Removed File Path state through the
existing deletion helper. This verification mode is used only when `AutoRemoveMissingOnScan` is
enabled after a successful **Scan Now**.

### 3. Full manual verification stays unchanged

`VerifyLibraryHealthNowCommand` continues using the full verifier: `File.Exists` plus archive-open
health detection for every present issue, including the current empty-row acknowledgement reset
behavior. Its progress, counts, and user-visible behavior remain unchanged.

## Errors and edge cases

- A missing tracked path within a successfully enumerated watched root is absent from the snapshot
  and receives a missing strike.
- A present tracked path within a successfully enumerated root is present in the snapshot and does
  not require `File.Exists` or an archive open.
- An issue outside those roots, or under an unavailable watched root, uses `File.Exists`; the
  existing root-reachability guard still prevents auto-removal while its root is disconnected.
- Overlapping watched roots and duplicate issue rows for one path produce the same presence result
  for every issue at that path.
- Snapshot path normalization errors fall back to `File.Exists`; they do not mark a file missing
  based on an invalid comparison.
- The full verifier remains the only path that refreshes empty/unreadable-file state.

## Testing and verification

- `LibraryFolderScannerTests`: the scan result reports observed supported-comic paths (including
  files imported in the same pass) and only completed, available watched roots; unavailable roots
  are excluded; the existing counts and import behavior remain unchanged.
- `LibraryHealthServiceTests`: missing-only verification uses snapshot membership inside covered
  roots, falls back to `File.Exists` outside coverage, preserves the two-strike/event/acknowledgment
  semantics, and does not open archives or mutate content-empty state for present issues.
- `PreferencesScreenViewModelTests`: automatic post-scan cleanup passes the completed scan snapshot
  into missing-only verification; manual **Verify Now** still detects empty/unreadable files; the
  unreachable-root auto-removal test remains valid.
- Compare the Activity Center's before/after `verification` duration on the user's library. No
  performance target is claimed until a real-library result is available.

## Scope

Expected implementation surfaces are `LibraryFolderScanner.cs`, `LibraryHealthService.cs`,
`PreferencesScreenViewModel.cs`, and their existing scanner, service, and view-model test files.
There is no schema migration or XAML change.
