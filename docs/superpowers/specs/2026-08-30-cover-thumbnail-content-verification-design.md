# Cover Thumbnail Content Verification — design

2026-08-30

## Problem

`2ebacd0` ("Fingerprint cover thumbnails by file identity, not just id", branch
`cover/thumbnail-identity`, not yet merged to `master`) fixed one specific failure mode: a cache
file surviving a primary-key reassignment and getting served to the wrong entity. It fingerprints
by **normalized path + file size** and trusts a cache file whenever both match the current
`Issue`/`Book` row — but it never looks at the file's actual bytes. If the cached JPEG was simply
*wrong from the moment it was written* — a bad decode during a bulk scan, a corrupt archive read,
whatever produced an incorrect page-1 image while path and size were always correct — the identity
fingerprint matches every time and the mismatch is trusted forever.

This is the reported symptom: a 2000+ comic library scan left a real, currently-unknown number of
issues with covers that don't match their files, and nothing in the existing (or even the
unmerged) pipeline ever re-examines a cache file once its identity checks out.

## Goal

1. A way to fix the *existing* backlog of wrong covers across the whole library, on demand.
2. An ongoing, unattended mechanism that periodically re-derives covers from source so a future
   instance of this bug can't silently persist the way this one did.
3. A manual, unconditional escape hatch — wipe the cache outright — independent of both, for when
   the user wants a guaranteed-clean rebuild without relying on any detection logic at all.

Both (1) and (2) apply to comics and books, mirroring how `2ebacd0` already treats
`CoverThumbnailService`/`BookCoverThumbnailService` as a matched pair.

## Non-goals

- Whether "page 0" is even the right definition of front cover (e.g. respecting a ComicInfo.xml
  `Page Type="FrontCover"` tag). Out of scope for this patch — a separate concern from cache
  correctness. Confirmed page-0 extraction already goes through the real ported ComicRack CE
  engine (`Providers.Readers.CreateSourceProvider` → `ImageProvider`), not custom sort logic, so
  it's not the suspected cause here.
- A content hash / stored fingerprint of page-0 bytes, considered and dropped — see "Rejected
  approach" below.
- Any change to the identity fingerprint (`CoverFingerprint.Stem`) itself; this builds on it
  unchanged.

## Rejected approach: content hash

The obvious-looking design is to hash page 0's raw bytes at generation time, store the hash, and
compare on a periodic check — regenerate only on a mismatch. Two problems killed it:

- **Where to store the hash breaks the hot read path.** Every card in a library grid resolves its
  cover through `CoverFingerprint.Stem(id, filePath, fileSize)` — computed from fields already in
  memory, zero I/O. If the hash lived in the cache filename (a third segment), the read path would
  need to know that hash to find the file, which means opening the archive and reading page 0 just
  to *display* a cover — an archive-open per card, at 2000+-library scale, is a real perf
  regression. A sidecar file avoids that (reads stay filename-only) but adds a second file to write
  and garbage-collect per issue for no benefit over the alternative below.
- **No baseline for currently-wrong covers.** A hash comparison only detects *drift* — the first
  time it runs against a pre-existing cache file, there is nothing to compare against except the
  cache file itself (already wrong). The obvious fallback — "treat the current file as the
  baseline, write the hash, don't regenerate" — would permanently launder the exact backlog this
  feature exists to fix.

Unconditional regeneration (below) sidesteps both: no new lookup key, no baseline problem, because
it never asks "did this change" — it always re-derives the truth and overwrites.

## Mechanism: forced regeneration

`TryGenerateThumbnail` gains a `force` parameter, default `false`:

```csharp
public bool TryGenerateThumbnail(int issueId, string filePath, long? fileSize = null, bool force = false)
{
    string stem = CoverFingerprint.Stem(issueId, filePath, fileSize);
    string destPath = CoverThumbnailPaths.GetCachePath(stem);
    if (!force && File.Exists(destPath))
    {
        return true;
    }
    // decode + scale + save destPath, exactly as today; SweepStaleSiblings unchanged
}
```

`force: true` skips only the presence shortcut — decode failures still return `false` without
throwing, same as today, so one bad archive doesn't stop a batch. Mirrored on
`BookCoverThumbnailService.TryGenerateThumbnail`.

### `VerifyAllAsync`

New method on both services, alongside the existing `GenerateAllAsync`:

```csharp
public async Task VerifyAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
{
    // Same candidate query as GenerateAllAsync (every Issue/Book with a FilePath), minus the
    // "cache file already exists" filter - every linked entity is a candidate.
    // TryGenerateThumbnail(..., force: true) for each; same try/catch-and-continue per item.
    // CollectOrphans afterward, same as GenerateAllAsync.
}
```

`GenerateAllAsync` itself is untouched — it stays the cheap, presence-based "fill gaps for newly
scanned comics" path, still used by `LibraryFolderScanner`'s post-scan step and the identity-fix's
startup/library-load reconcile. `VerifyAllAsync` is strictly heavier (every linked entity gets a
real decode+scale+encode+write) and is only ever invoked by the two paths below, never
automatically on every startup/navigation the way `GenerateAllAsync`'s reconcile is.

## Two entry points

### 1. Manual — "Verify & Repair Covers"

New `PreferencesScreenViewModel` command, `VerifyCovers`, alongside the existing `GenerateCovers`
(left as-is — this is deliberately a separate button, not a behavior change to the existing one,
so nothing about today's "fill gaps" semantics or its tests changes):

```csharp
[ObservableProperty] private bool _isVerifyingCovers;

[RelayCommand]
private async Task VerifyCovers()
{
    if (IsVerifyingCovers) return;
    IsVerifyingCovers = true;
    var toast = new ToastProgressViewModel("Verifying covers…");
    _showProgressToast(toast);
    // Two sequential passes sharing one toast - accumulate rather than assign directly, or the
    // second pass's smaller Total would make the bar jump backward and undercount the summary.
    int comicTotal = 0;
    int bookTotal = 0;
    try
    {
        var comicProgress = new Progress<(int Done, int Total)>(p =>
        {
            comicTotal = p.Total;
            toast.Done = p.Done;
            toast.Total = comicTotal;
        });
        await new CoverThumbnailService(_contextFactory).VerifyAllAsync(comicProgress);

        var bookProgress = new Progress<(int Done, int Total)>(p =>
        {
            bookTotal = p.Total;
            toast.Done = comicTotal + p.Done;
            toast.Total = comicTotal + bookTotal;
        });
        await new BookCoverThumbnailService(_contextFactory).VerifyAllAsync(bookProgress);
    }
    finally
    {
        IsVerifyingCovers = false;
        _closeProgressToast(toast);
        int total = comicTotal + bookTotal;
        _showToast("Covers verified", $"Re-checked {total} cover{(total == 1 ? "" : "s")}.");
    }
}
```

Placed in Preferences → Libraries, next to Generate Covers / Sync Metadata / Book Folders. This is
what fixes the current 2000+ backlog: one run walks every linked issue and book and rewrites every
cover from source.

### 2. Automatic — periodic background sweep

`AppSettings` gains one nullable column:

```csharp
/// <summary>
/// UTC timestamp of the last completed full cover-content verification pass (docs/superpowers/
/// specs/2026-08-30-cover-thumbnail-content-verification-design.md). Null means never run. Only
/// set on successful completion - an interrupted pass (app closed mid-run) retries next launch
/// rather than being marked done early.
/// </summary>
public DateTime? LastCoverVerificationUtc { get; set; }
```

New EF migration `AddLastCoverVerificationUtc`, same pattern as the recent `AddSmartCollections`/
`MediaRelationCollectionNodes` migrations.

`MainViewModel`'s existing startup reconcile block (added by `2ebacd0`) grows a second,
independent fire-and-forget task:

```csharp
_ = ReconcileCoverCachesAsync();       // existing - GenerateAllAsync, every launch
_ = PeriodicCoverVerificationAsync();  // new
```

```csharp
private static async Task PeriodicCoverVerificationAsync()
{
    using var context = PaperbunkrDb.CreateContext();
    var settings = context.GetOrCreateAppSettings();
    if (settings.LastCoverVerificationUtc is DateTime last && DateTime.UtcNow - last < TimeSpan.FromDays(7))
    {
        return;
    }

    var noProgress = new Progress<(int Done, int Total)>();
    try
    {
        await new CoverThumbnailService().VerifyAllAsync(noProgress);
        await new BookCoverThumbnailService().VerifyAllAsync(noProgress);

        using var writeContext = PaperbunkrDb.CreateContext();
        var toUpdate = writeContext.GetOrCreateAppSettings();
        toUpdate.LastCoverVerificationUtc = DateTime.UtcNow;
        writeContext.SaveChanges();
    }
    catch
    {
        // Best-effort, same rationale as ReconcileCoverCachesAsync - retried next launch either
        // way since the timestamp only advances on full completion.
    }
}
```

Silent by design (confirmed preference: auto-fix, no notification) — this runs fire-and-forget,
low-priority, on whatever cadence the user happens to launch the app, and simply corrects the
cache in place. No toast, no log surfaced in the UI. 7 days is a fixed constant, not
user-configurable — no UI request for tuning it, and it's a maintenance sweep, not a
user-facing setting.

The 7-day gate is a plain `DateTime` comparison, so it's trivially testable by constructing
`AppSettings` with a known `LastCoverVerificationUtc` rather than needing real elapsed time.

## Clear Cover Cache — manual escape hatch

Two independent destructive actions (per confirmed preference — separate comic/book buttons, not
one combined action), Preferences → Libraries, each guarded by the app's existing
`TwoStepConfirm` inline affordance (docs/superpowers/specs/2026-08-22-delete-functionality-design.md)
— no modal dialog, matching every other destructive action in this codebase:

```csharp
public TwoStepConfirm ClearComicCoverCacheConfirm { get; }

// constructor:
ClearComicCoverCacheConfirm = new TwoStepConfirm(
    onConfirmed: () => _ = ClearComicCoverCacheAsync(),
    idleLabel: "Clear Comic Cover Cache",
    armedLabel: "Confirm clear?");

private async Task ClearComicCoverCacheAsync()
{
    foreach (string path in CoverThumbnailPaths.EnumerateAll())
    {
        try { File.Delete(path); } catch (IOException) { }
    }

    // Directory is now empty, so the existing cheap presence-based path regenerates everything -
    // no need for VerifyAllAsync/force here.
    IsGeneratingCovers = true;
    var toast = new ToastProgressViewModel("Rebuilding comic covers…");
    _showProgressToast(toast);
    try
    {
        var progress = new Progress<(int Done, int Total)>(p => { toast.Done = p.Done; toast.Total = p.Total; });
        await new CoverThumbnailService(_contextFactory).GenerateAllAsync(progress);
    }
    finally
    {
        IsGeneratingCovers = false;
        _closeProgressToast(toast);
        _showToast("Comic covers rebuilt", $"Regenerated {toast.Total} cover{(toast.Total == 1 ? "" : "s")}.");
    }
}
```

`ClearBookCoverCacheAsync`/`ClearBookCoverCacheConfirm` mirror this exactly against
`BookCoverThumbnailPaths`/`BookCoverThumbnailService`. Both reuse the plain `GenerateAllAsync`
(not `VerifyAllAsync`) since
an emptied directory makes the presence check and the forced check equivalent — no reason to pay
for the heavier method. `CoverThumbnailPaths.EnumerateAll()`/`BookCoverThumbnailPaths.EnumerateAll()`
already exist (added by `2ebacd0`) — no new path-helper surface needed.

## Testing

Extend `CoverThumbnailServiceTests` / `BookCoverThumbnailServiceTests`:

- `TryGenerateThumbnail(force: true)` overwrites an existing cache file with freshly decoded
  content even though one was already present (the presence-check-skip that `force: false`
  exercises today must not fire).
- `VerifyAllAsync` regenerates every candidate regardless of prior cache state, and still performs
  orphan GC afterward (same as `GenerateAllAsync`).
- One bad file (corrupt archive) doesn't stop `VerifyAllAsync` partway through the batch.

New test for the periodic gate (`MainViewModelTests` or a small standalone test around the gate
logic extracted for testability):

- `LastCoverVerificationUtc == null` → sweep runs.
- `LastCoverVerificationUtc` 3 days ago → sweep does not run.
- `LastCoverVerificationUtc` 8 days ago → sweep runs and the timestamp advances to "now".
- A `VerifyAllAsync` that throws leaves `LastCoverVerificationUtc` unchanged.

New tests for `ClearComicCoverCacheAsync` / `ClearBookCoverCacheAsync`:

- The first `TwoStepConfirm.Trigger()` (arming) deletes nothing - only the second, within the
  confirm window, invokes `onConfirmed`.
- Letting the confirm window lapse (per `TwoStepConfirmTests`' existing timer-based pattern)
  reverts without deleting anything.
- `onConfirmed` firing deletes every existing cache file and repopulates the directory via
  `GenerateAllAsync`.

## Migration / rollout

No cache-file format change — `VerifyAllAsync` writes the same `{id}-{identityHash}.jpg` files
`2ebacd0` already produces, just unconditionally. Sequencing: this patch is written against
`2ebacd0`'s already-unmerged branch state (per the earlier decision to extend it before merging
rather than merge-then-repatch), so both land together. First manual "Verify & Repair Covers" run
after this ships is the actual fix for the reported 2000+-library backlog; the periodic sweep
is prevention going forward, not a substitute for running it once.
