# Cover Thumbnail Content Verification — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-30-cover-thumbnail-content-verification-design.md*

Confirmed via `git merge-tree $(git merge-base cover/thumbnail-identity master) cover/thumbnail-identity master`:
zero conflict markers, despite `master` having moved on ~15 merges since the branch diverged. So
step 1 is a real merge, not a re-implementation from scratch.

## Step 1: Merge the identity-fix branch

**Files:** none directly — `git merge cover/thumbnail-identity` (or cherry-pick `2ebacd0`) into the
current working branch.

**What:** Brings in `CoverFingerprint`, the `{id}-{hash}.jpg` filename scheme, `CoverThumbnailPaths.EnumerateAll`/`EnumerateForIssue`, `BookCoverThumbnailPaths.EnumerateAll`/`EnumerateForBook`,
the updated `CoverImageCache`/`BookCoverImageCache`/`CoverImageConverter`, every card model's
`CoverKey`, and the startup/library-load reconcile in `MainViewModel`/`LibraryScreenViewModel`.
Everything below is written against the post-merge shape of these files.

**Depends on:** none.

**Verify:** `dotnet build`, then `dotnet test src/Paperbunkr.App.Tests` — the identity-fix's own
tests (`CoverFingerprintTests`, extended `CoverThumbnailServiceTests`/`CoverImageCacheTests`,
`BookCoverThumbnailServiceTests`) must pass unchanged. This confirms the merge didn't silently
break on any of the 29 files it touches.

## Step 2: `force` parameter on both services' `TryGenerateThumbnail`

**Files:**
- `src/Paperbunkr.App/Services/CoverThumbnailService.cs` (edit)
- `src/Paperbunkr.App/Services/BookCoverThumbnailService.cs` (edit)

**What:** Add `bool force = false` to both `TryGenerateThumbnail` signatures. The only behavior
change: `if (!force && File.Exists(destPath)) return true;` (comics currently reads
`if (File.Exists(destPath))`; books the same). Everything after that line — decode, scale, save,
`SweepStaleSiblings` — is unchanged. `ResetCover`'s internal call to `TryGenerateThumbnail` stays
`force: false` (its own presence-check right before that call already guards it).

**Depends on:** Step 1.

**Verify:** New test per service — `TryGenerateThumbnail(force: true)` overwrites a file that
already exists with freshly decoded content (assert the file's write time or bytes changed, not
just that it still exists). Existing `force: false`/default-arg tests must keep passing unmodified.

## Step 3: `VerifyAllAsync` on both services

**Files:** same two files as Step 2.

**What:** New method mirroring `GenerateAllAsync`'s structure exactly, minus the
`!File.Exists(...)` filter on candidates — every `Issue`/`Book` with a non-null `FilePath` is a
candidate, `TryGenerateThumbnail(..., force: true)` for each, same try/catch-continue per item,
same `CollectOrphans(validStems)` call at the end. `GenerateAllAsync` itself is untouched (verify
with a diff after this step — it should show zero changes to that method).

**Depends on:** Step 2.

**Verify:** New test per service — seed 2-3 issues/books with real cached covers already present
(via a prior `TryGenerateThumbnail` call), then run `VerifyAllAsync` and assert every one was
rewritten (not skipped) and orphan GC still ran. One corrupt/missing file among the candidates
doesn't stop the batch (mirrors the existing `GenerateAllAsync` corrupt-file test).

## Step 4: `AppSettings.LastCoverVerificationUtc` + migration

**Files:**
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit) — add
  `public DateTime? LastCoverVerificationUtc { get; set; }` with the XML-doc from the design's
  §"Automatic — periodic background sweep".
- New migration via `dotnet ef migrations add AddLastCoverVerificationUtc --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App`
  (creates `Migrations/<timestamp>_AddLastCoverVerificationUtc.cs` + `.Designer.cs`, updates
  `PaperbunkrDbContextModelSnapshot.cs`). Column shape:
  `migrationBuilder.AddColumn<DateTime>(name: "LastCoverVerificationUtc", table: "AppSettings", type: "TEXT", nullable: true);`
  (matches the existing `DateTime?` column convention, e.g. `AddedTime`/`OpenedTime` in
  `InitialCreate`).

**Depends on:** Step 1 (needs current `AppSettings` shape/migration chain, unaffected by 1-3 but
sequenced after so the migration is generated against the final schema state, not an intermediate
one from a half-finished branch).

**Verify:** `dotnet ef database update` (or the test suite's own `EnsureCreated`) applies cleanly;
existing `PreferencesScreenViewModelTests` (which construct `AppSettings` rows) still pass — new
nullable column with no default doesn't break any existing row construction.

## Step 5: Periodic background sweep in `MainViewModel`

**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)

**What:**
1. A pure, testable gate function (static, no I/O):
   ```csharp
   internal static bool ShouldRunCoverVerification(DateTime? lastRunUtc, DateTime nowUtc) =>
       lastRunUtc is not DateTime last || nowUtc - last >= TimeSpan.FromDays(7);
   ```
2. `PeriodicCoverVerificationAsync()` — reads `AppSettings.LastCoverVerificationUtc`, calls the
   gate function, and if true runs `VerifyAllAsync` on both services (fire-and-forget, `noProgress`
   sink like `ReconcileCoverCachesAsync`), then opens a **second** context to write
   `LastCoverVerificationUtc = DateTime.UtcNow` only after both calls complete without throwing —
   mirrors the design's "only advances on completion" requirement and the existing
   `ReconcileCoverCachesAsync` best-effort try/catch style.
3. Constructor gains `_ = PeriodicCoverVerificationAsync();` alongside the existing
   `_ = ReconcileCoverCachesAsync();` call.

**Depends on:** Step 3 (`VerifyAllAsync`), Step 4 (the column to gate on).

**Verify:** New `MainViewModelTests` (or a small standalone test class) covering
`ShouldRunCoverVerification` directly — null, 3-days-ago (false), 8-days-ago (true) — no real
elapsed time or full `MainViewModel` construction needed for this part. Separately, one integration
test constructs `MainViewModel` against a seeded temp DB with `LastCoverVerificationUtc` already
recent and confirms `VerifyAllAsync` does not fire (avoids needing to assert the fire-and-forget
task's completion for the "should run" case in a unit test).

## Step 6: "Verify & Repair Covers" command

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)

**What:** `[ObservableProperty] private bool _isVerifyingCovers;` and a `[RelayCommand] private
async Task VerifyCovers()` placed right after the existing `GenerateCovers` command (~line 1205),
following its exact toast/guard-flag shape. Per the design's fixed version: two separate
`Progress<(int Done, int Total)>` instances (one for comics, one for books) that both write into
shared `comicTotal`/`bookTotal` locals so the toast's `Done`/`Total` accumulate across both calls
instead of the second call's smaller total overwriting the first's progress. Final toast message:
`$"Re-checked {comicTotal + bookTotal} cover{...}."`.

**Depends on:** Step 3.

**Verify:** If `PreferencesScreenViewModelTests` already exercises `GenerateCoversCommand` end to
end (check first), mirror that test for `VerifyCoversCommand`. Otherwise a direct call to
`vm.VerifyCoversCommand.ExecuteAsync(null)` against a temp DB seeded with a couple of issues/books,
asserting the cache files exist afterward and `IsVerifyingCovers` returns to `false`.

## Step 7: "Clear Cover Cache" — two `TwoStepConfirm` actions

**Files:** same as Step 6.

**What:**
- `public TwoStepConfirm ClearComicCoverCacheConfirm { get; }` and
  `public TwoStepConfirm ClearBookCoverCacheConfirm { get; }`, constructed in the VM constructor
  with `idleLabel`/`armedLabel` per the design (`"Clear Comic Cover Cache"` /
  `"Clear Book Cover Cache"`, `"Confirm clear?"` for both).
- `private async Task ClearComicCoverCacheAsync()` / `ClearBookCoverCacheAsync()`: delete every
  file from `CoverThumbnailPaths.EnumerateAll()` / `BookCoverThumbnailPaths.EnumerateAll()`
  (swallow `IOException` per-file, matching every other cache-delete call site in this codebase),
  then run the existing plain `GenerateAllAsync` (not `VerifyAllAsync`) with the same
  toast/`IsGeneratingCovers` (comic) — book side needs its own guard flag,
  `IsClearingBookCovers`/reuse of `IsScanningBooks`'s toast pattern, since `IsGeneratingCovers` is
  comic-only today and there's no pre-existing "generating book covers" flag to reuse cleanly. Add
  `_isClearingBookCoverCache` as its own `[ObservableProperty]`.

**Depends on:** Step 1 only (uses `EnumerateAll`/`GenerateAllAsync`, both already present
post-merge) — independent of Steps 2-6, can be implemented in parallel with them.

**Verify:** New tests — first `TriggerCommand` execute arms without deleting anything; second
execute within the window deletes every existing cache file and repopulates via `GenerateAllAsync`
(assert file count returns to matching the seeded issue/book count). Mirror
`TwoStepConfirmTests`' pattern of not waiting out the real revert timer.

## Step 8: Wire buttons into `LibrarySection.axaml`

**Files:** `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit)

**What:**
- In the "Comic Library Folders" group's action row (after the existing Sync Metadata button,
  ~line 85): a `headerAction ghost` button bound to `VerifyCoversCommand`/`!IsVerifyingCovers`
  (icon `Symbol="Search"` or similar "check" glyph — pick whatever's already used elsewhere for a
  verify/scan action, don't invent a new visual language), and a second button bound to
  `ClearComicCoverCacheConfirm.TriggerCommand`, `Content="{Binding ClearComicCoverCacheConfirm.Label}"`
  — text-content button, matching the `DeleteConfirm.Label`/`TriggerCommand` pattern already used
  throughout `MainWindow.axaml` (not the icon-toggle variant, since this needs a visible label
  change on arm, and there's no existing per-row icon slot to reuse here).
- In the "Book Folders" group's action row (after Scan Now, ~line 147): a button bound to
  `ClearBookCoverCacheConfirm.Label`/`TriggerCommand`, same pattern.

**Depends on:** Steps 6 and 7 (bindings must exist on the VM first).

**Verify:** `dotnet build` (AVLN2000 is a build-time XAML-compile error — this is a binding-only
edit to an already-compiled view, not a new View, so the CLAUDE.md new-View gotcha doesn't apply,
but build regardless). Manual on-screen check per Step 9.

## Step 9: Full verification pass

**Verify:**
- `dotnet test` — full solution (`src/Paperbunkr.App.Tests`, `src/Paperbunkr.Data.Tests`), not
  just the new tests, to catch any regression in the merge or the shared services.
- Manual on-screen check via `dotnet run`: open Preferences → Libraries, confirm all three new
  buttons render, "Verify & Repair Covers" runs to completion on a small real/test library and the
  toast shows a sane combined count, and both Clear-Cache buttons show the label change on first
  click and actually clear+rebuild on the second.
- Confirm the periodic sweep is silent as designed: with `LastCoverVerificationUtc` unset, launch
  the app once and verify (via a breakpoint/log, not a UI signal — there isn't one by design) that
  `PeriodicCoverVerificationAsync` actually ran and set the timestamp, then relaunch and confirm it
  does *not* re-run.

## Notes for whoever picks this up

- Steps 2+3 (comics) and their book-service mirrors are best done together per file, not
  comic-then-book-later — the two services are meant to stay in lockstep, per the existing
  `2ebacd0` doc comments' own rationale for treating them as a matched pair.
- Step 7 is the one step with no hard dependency on 2-6 — if time-boxing, it can be built and
  merged independently of the verification-sweep work.
- No cache-file format change anywhere in this plan — every step writes the same
  `{id}-{identityHash}.jpg` filenames the merged branch already produces.
