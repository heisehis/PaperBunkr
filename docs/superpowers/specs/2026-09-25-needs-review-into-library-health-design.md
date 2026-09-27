# Needs Review → Library Health

**Date:** 2026-09-25
**Status:** Approved (grilling round 2, all recommendations accepted)

## Problem

The five review queues — Content Type, Duplicate Files, Series Conflicts, Metadata Proposals, Ad
Pages — live in the ComicRack CE Migration overlay's "Needs Review" tab. That's a fringe place: they
are ongoing library-maintenance queues fed by scans, the filename parser, providers and the reader,
not migration steps. Library Health (Preferences → Library) is where maintenance already lives
(Missing Files moved there on 2026-09-06, `2026-09-06-missing-files-library-health-design.md`).

## Decisions

1. **Rehost, don't split.** `NeedsReviewViewModel` stays one unit (it is already a self-contained VM
   for exactly these five queues). Ownership moves from `MigrationOverlayViewModel` to
   `MainViewModel`, which constructs it once and passes the same instance to both
   `MigrationOverlayViewModel` (only so migration completion can call `Refresh()`) and
   `PreferencesScreenViewModel` (exposed as `NeedsReview`). No queue logic changes except §3.
2. **Order inside the Library Health card** (each subsection keeps the existing divider + "✓ Nothing
   here" empty-state pattern):
   1. Missing Files
   2. Duplicate Files (with "Keep Largest in All Groups")
   3. Series Conflicts (with "Keep All Separate" / "Merge All Above 90%")
   4. Content Type (row click still opens Series Detail)
   5. Metadata Proposals
   6. Ad Pages
   7. Reported pages
   8. Find Similar Series
   9. Empty Rows
   10. Recently Removed

   Card subtitle becomes "Keep your library clean: missing files, duplicates, conflicts and things to
   review."
3. **Metadata Proposals: Pending first, Applied collapsed.** Split `MetadataProposalItems` into
   `PendingProposalItems` (shown) and `AppliedProposalItems` (behind a collapsed "Applied · N" toggle,
   same shape as Recently Removed). Both keep Accept/Reject. `HasPendingItems` counts only Pending
   proposals — Accepted ones are audit history and must not keep the review badge lit forever.
4. **Migration overlay loses its Needs Review tab.** The tab strip is removed; the overlay is just the
   Migrate flow. `MigrationOverlayMode`, `IsReviewMode`, `SwitchToReviewCommand` and `Open()`'s
   auto-jump-to-review go away (`Open()` always resets to Locate). Results screen: "View Needs Review"
   → **"Review in Library Health"**, which closes the overlay, navigates to Preferences and calls the
   existing `GoLibraryHealth` (scroll + pulse `library.health`). Conflicts-step copy: "it lands in
   Needs Review" → "it lands in Library Health".
5. **Pending indicator.** The dot beside "Migrate…" is removed. Library Health's group header gets a
   count chip "Needs review · N" (`PbRadiusChip` squircle, never a pill; visible only when N > 0),
   and the Preferences sidebar **Library** nav item gets a small dot bound to
   `NeedsReview.HasPendingItems`. N = `NeedsReview.PendingCount`, a new sum of the five queues'
   pending counts (Ad Pages counted per page, Duplicates per group, Proposals Pending only).
6. **Ad Pages is rehosted only.** It is reader slice B work, uncommitted as of today; its logic and
   row VMs are untouched. Check `git status` before editing shared files.

## Components touched

- `ViewModels/NeedsReviewViewModel.cs` — proposal split (§3), `PendingCount`, notify it.
- `ViewModels/MigrationOverlayViewModel.cs` — takes a `NeedsReviewViewModel` + an
  `onReviewInLibraryHealth` callback instead of constructing one; drop mode machinery.
- `ViewModels/MainViewModel.cs` — owns `NeedsReview`; existing refresh points (folder-watch handler,
  `GoPreferences`) retarget to it; implements the Review-in-Library-Health callback.
- `ViewModels/PreferencesScreenViewModel.cs` — exposes `NeedsReview`; update the `Migration` doc
  comment.
- `Views/MigrationOverlay.axaml` — remove tab strip, Review panel and the now-unused row templates.
- `Views/Preferences/LibrarySection.axaml` — receive the row templates (Conflict, DuplicateGroup,
  MetadataProposal, AdPageGroup) and the five subsections; header chip; subtitle; remove Migrate dot.
- `Views/PreferencesScreen.axaml` — sidebar Library dot.

Row templates bind commands through `$parent[ItemsControl]` casts to `MigrationOverlayViewModel`;
these become `PreferencesScreenViewModel` casts (`...DataContext).NeedsReview.X`).

## Errors / edge cases

- Refresh timing is unchanged: the queues already refresh on `GoPreferences`, on migration
  completion and on the folder-watch handler, so opening Preferences costs nothing new.
- Row actions that remove rows keep their existing deferral (`Dispatcher.UIThread.Post`) per the
  CLAUDE.md routed-event rule; moving hosts doesn't change that. The Content Type row now navigates
  away from Preferences — acceptable, same as any deep-link.

## Testing

- `NeedsReviewViewModelTests`: retarget to `PendingProposalItems`/`AppliedProposalItems`; add: an
  Accepted-only proposal set gives `HasPendingItems == false` and one Applied row; `PendingCount`
  sums across queues.
- `DuplicateFilesReviewTests`: unchanged (same VM).
- `MigrationOverlayViewModel` tests (if any reference review mode): drop/replace; add: "Review in
  Library Health" invokes the callback.
- `PreferencesScreenViewModelTests`: `NeedsReview` is the same instance MainViewModel owns.
- Build with the Avalonia weave check (CLAUDE.md), then on-screen: Library Health shows all five
  sections, empty states, chip + sidebar dot, Applied toggle; Migration overlay has no tab strip.
- Run `avalonia-pro-max/review-checklist` before calling UI done.
