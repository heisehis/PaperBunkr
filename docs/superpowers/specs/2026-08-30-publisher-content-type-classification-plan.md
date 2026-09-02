# Publisher-based ContentType classification — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-30-publisher-content-type-classification-design.md*

Note on precedent: the design doc's periodic-sweep section was written against a "just-shipped"
cover-verification sweep (`AppSettings.LastCoverVerificationUtc`, `MainViewModel.PeriodicCoverVerificationAsync`)
that turned out to still be unmerged on its own branch (`cover-thumbnail-content-verification`),
not actually present on `master`. The pattern that *is* on `master` and serves the same purpose —
`BackupService.RunAutoBackupIfDue()`, wired from `App.axaml.cs` — is used as the real precedent
below instead. Same shape (best-effort, gated, silent, fire-and-forget via `Task.Run`), different
concrete method to mirror.

## Step 1: `PublisherContentTypeClassifier`
**Files:** `src/Paperbunkr.Data/Metadata/PublisherContentTypeClassifier.cs` (new)
**What:** Static class mirroring `src/Paperbunkr.Data/Metadata/LanguageIsoClassifier.cs`'s shape —
`public static bool TryClassify(string? publisher, out ContentType contentType, out ReadingMode readingMode)`,
backed by an ordered `(string Key, ContentType ContentType, ReadingMode ReadingMode)[]` table,
matched via `publisher.Contains(key, StringComparison.OrdinalIgnoreCase)`. Use the exact starter
list from the design doc's table (Comic/Manga/Manhwa/Manhua keys and the excluded-ambiguous list).
**Depends on:** none
**Verify:** `src/Paperbunkr.Data.Tests/PublisherContentTypeClassifierTests.cs` (new) — `[Theory]`
mirroring `LanguageIsoClassifierTests.cs` exactly: one case per category proving a real-world
variant matches (e.g. `"VIZ Media LLC"` → Manga/RightToLeft), `null`/empty/unmatched → false, and
explicit cases for the deliberately-excluded ambiguous names (`"Dark Horse Comics"`, `"Tapas"`) →
false.

## Step 2: Scan-time integration
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit)
**What:** In `ImportFiles`'s per-issue loop, the existing `isNewSeries`-guarded chain currently
reads (embedded `Manga` field) → `else if` (LanguageISO). Insert `PublisherContentTypeClassifier`
as a new `else if` branch between them, checked against `issue.Publisher` (already populated by
`CeLibraryMigrator.MapStoryFields` earlier in the same block), same `isNewSeries` guard, same
comment-documented rationale style as the two existing branches.
**Depends on:** Step 1
**Verify:** manual read-through against the exact current file (line numbers have shifted since
the design doc was written — re-read the file fresh, don't trust the design doc's cited line
numbers).

## Step 3: Scanner tests for the new step
**Files:** `src/Paperbunkr.App.Tests/LibraryFolderScannerTests.cs` (edit)
**What:** Add a new `// ===== Publisher content-type heuristic =====` section mirroring the
existing LanguageISO section's four tests exactly (same `CbzFixture`/`ComicInfo` pattern):
1. Publisher matches, no embedded `Manga`, new series → `ContentType`/`ReadingMode` set correctly.
2. Publisher matches, but series already exists → untouched (guard parity test).
3. Embedded `Manga` field present → wins over a disagreeing `Publisher` match (priority test).
4. `Publisher` matches but `LanguageISO` also present and disagrees → `Publisher` wins (priority
   test, proves publisher is checked before LanguageISO in the chain).
5. No `Publisher` match, `LanguageISO` present → still falls through to `LanguageIsoClassifier`
   (proves publisher doesn't swallow the existing fallback when it has nothing to offer).
**Depends on:** Step 2
**Verify:** `dotnet test src/Paperbunkr.App.Tests`

## Step 4: `AppSettings.LastContentTypeSweepUtc` + migration
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), new migration under
`src/Paperbunkr.Data/Migrations/`
**What:** Add `public DateTime? LastContentTypeSweepUtc { get; set; }` (doc comment: null means
"sweep never run", mirrors the sweep-gate pattern), then
`dotnet ef migrations add AddLastContentTypeSweepUtc --project src/Paperbunkr.Data` from repo root.
Working tree is clean as of this session (all prior WIP committed), so this is a normal, isolated
single-column migration — no manual snapshot surgery needed.
**Depends on:** none (independent of Steps 1-3)
**Verify:** `src/Paperbunkr.Data.Tests/` — add `AddLastContentTypeSweepUtcMigrationTests.cs`
mirroring the shape of an existing single-column migration test (e.g. `AddIssueMissingAcknowledgedMigrationTests.cs`
or similar — check what the most recent single-nullable-column migration test looks like and copy
its structure); confirm the generated migration's `Up`/`Down` are the expected single `AddColumn`/
`DropColumn` before committing.

## Step 5: Periodic sweep
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit)
**What:** Two additions:
- `public static bool ShouldRunContentTypeSweep(DateTime? lastRunUtc, DateTime nowUtc)` — pure,
  7-day interval: `lastRunUtc is null || (nowUtc - lastRunUtc.Value).TotalDays >= 7`.
- `public void RunContentTypeSweepIfDue()` — mirrors `BackupService.RunAutoBackupIfDue()`'s shape
  (try/catch swallowing all failures, best-effort). Body: open a context, check
  `ShouldRunContentTypeSweep` against `AppSettings.LastContentTypeSweepUtc`; if due, query
  `Series.Where(s => s.ContentType == ContentType.Unknown).Include(s => s.Issues)`, and for each
  series iterate its issues in existing order taking the first `Issue.Publisher` that
  `PublisherContentTypeClassifier.TryClassify` resolves, applying the result. Set
  `LastContentTypeSweepUtc = DateTime.UtcNow` and `SaveChanges()` only after the full sweep
  completes (interrupted pass retries next launch, matching the design doc).
**Depends on:** Steps 1, 4
**Verify:** covered by Step 6's tests.

## Step 6: Sweep tests
**Files:** `src/Paperbunkr.App.Tests/LibraryFolderScannerTests.cs` (edit)
**What:**
- `ShouldRunContentTypeSweep`-only tests (no DB needed — plain static calls): never run → true; run
  2 days ago → false; run 8 days ago → true.
- `RunContentTypeSweepIfDue_ClassifiesUnknownSeriesWithMatchingPublisher` — seed a `Series` at
  `ContentType.Unknown` with an `Issue.Publisher` = `"Viz Media"`, call it, assert `ContentType`/
  `ReadingMode` updated and `AppSettings.LastContentTypeSweepUtc` advanced.
- `RunContentTypeSweepIfDue_NoMatchingPublisher_LeavesSeriesUnknown` — negative case.
- `RunContentTypeSweepIfDue_NotYetDue_DoesNothing` — seed `LastContentTypeSweepUtc` to 1 day ago,
  assert an otherwise-matchable series is left untouched and the timestamp doesn't change.
**Depends on:** Step 5
**Verify:** `dotnet test src/Paperbunkr.App.Tests`

## Step 7: Wire the sweep into startup
**Files:** `src/Paperbunkr.App/App.axaml.cs` (edit)
**What:** Alongside the existing `System.Threading.Tasks.Task.Run(() => new BackupService().RunAutoBackupIfDue());`
call (~line 84), add `System.Threading.Tasks.Task.Run(() => new LibraryFolderScanner().RunContentTypeSweepIfDue());`.
Same fire-and-forget, non-blocking rationale as the existing call it sits next to.
**Depends on:** Step 5
**Verify:** no dedicated automated test for this one line (matches the existing BackupService call,
which also has none) — build the app (`dotnet build`) and confirm it launches without error; full
behavioral proof is Step 6's unit tests against the method it calls.

## Step 8: Full verification pass
**Files:** none (verification only)
**What:** `dotnet build` (watch for the AVLN2000 gotcha — not expected to trigger here since no new
`.axaml` files are added, but confirm) and `dotnet test` across the solution.
**Depends on:** Steps 1-7
**Verify:** all tests green, matches this project's existing bar for "done."
