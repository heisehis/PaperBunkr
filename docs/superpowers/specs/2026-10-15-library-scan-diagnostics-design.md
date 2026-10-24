# Library scan diagnostics — design

Date: 2026-10-15. Status: implemented; focused verification passed on 2026-10-15. Source changes are uncommitted.

## Problem

The user reports that comic-library scans are slow and wants them faster. Today, `ScanAll` recursively
enumerates every file below each watched folder, filters supported extensions and known file paths,
then imports candidates. The Preferences **Scan Now** flow may also generate cover thumbnails for
newly imported issues and, when configured, run a full Library Health verification. These stages
share one Activity Center job and can make the overall operation appear to be one undifferentiated
scan.

The existing performance-and-memory design (`2026-10-07-performance-and-memory-design.md`) is
focused on memory, job concurrency, and broad performance scenarios; it does not measure comic
scan throughput by stage. Choosing an enumeration strategy or file-processing optimization before
measuring risks optimizing a stage that is not the bottleneck.

## Goals

- Measure the elapsed time and useful counts for the full watched-folder scan without changing
  which files are discovered, imported, or updated.
- Separate folder enumeration/filtering, import processing, post-scan cover generation, and the
  optional automatic verification stage.
- Make one run's measurements visible in the existing Activity Center result/history so the user
  can report the slow stage without inspecting logs.
- Keep diagnostics low-overhead and avoid storing file names or paths in activity history.
- Use measurements from the user's library to choose a later, targeted scan optimization.

## Approaches

1. **Measure the current pipeline first (chosen).** Add stage durations and counts to the scan
   result/activity summary; leave discovery and import behavior alone. Low risk and directly answers
   which stage needs work.
2. **Immediately enumerate only supported extensions.** This might reduce managed filtering work,
   but could require multiple directory passes and change behavior around extension casing,
   provider extensions, and access errors. It is not selected without measurements.
3. **Build a persistent incremental file index.** This could avoid repeated full-tree walks, but
   introduces invalidation and recovery concerns and is premature before knowing whether enumeration
   dominates.

## Design

### Scanner measurements

Extend the completed full-scan result with a diagnostics value that contains:

- Number of watched folders that were available and enumerated.
- Total file paths enumerated under those folders.
- Supported comic files encountered.
- Paths already represented in the library and therefore skipped.
- New candidate files passed to import.
- Issues successfully imported.
- Elapsed time for folder enumeration and filtering.
- Elapsed time for candidate processing/import and its post-save work within `ImportFiles`.
- Total `ScanAll` elapsed time.

Measure enumeration/filtering and import with `Stopwatch` around their existing work. Count paths as
they are visited in the existing recursive walk, so this instrumentation does not add another tree
walk or archive reads. Keep the existing `LibraryScanCompletedEvent` overall duration and result
counts compatible; diagnostics extend rather than replace them.

### Scan Now stages

`PreferencesScreenViewModel.ScanNow` adds separate elapsed-time measurements around:

- Cover generation when new issues were added.
- Automatic Library Health verification when that setting is enabled.

On success, append a compact diagnostic suffix to the existing `job.Succeed` result summary, using
counts and durations only. The Activity Center already displays and persists result summaries, so no
new XAML screen or database schema is required. Example shape (exact copy may be refined in
implementation): “Scan: 12,400 paths / 318 supported / 27 new; enumerate 8.2 s, import 14.6 s;
covers 9.1 s.” When automatic verification runs, include its duration separately. The human-facing
summary of added issues remains present.

If a scan fails or is cancelled, existing failure/cancellation handling stays in effect. Diagnostics
are best-effort, in-memory values; a partial or interrupted run is not reported as a successful
measurement. No file path, comic title, or other identifying data is included in the summary.

### Follow-up optimization

This implementation deliberately stops after collecting comparable measurements. After the user has run Scan
Now on the real library and reviewed the stage breakdown, a separate decision will select an
optimization and define its correctness and performance target. Candidate areas include directory
enumeration, file metadata/archive access, database lookups, and cover generation. No performance
claim is made until before/after timings are available.

The user identified automatic post-scan verification as the dominant stage for their 5,500+ comic
library. Its selected follow-up is to reuse the completed scan's transient path snapshot for
missing-only cleanup, while leaving the explicit full Library Health verification unchanged (see
`2026-10-15-scan-auto-verification-design.md`). A real-library before/after timing is still needed
before claiming a measured speedup.

## Testing

- `LibraryFolderScannerTests`: diagnostics counts reflect available watched roots, enumerated files,
  supported files, known paths, candidate paths, and imported issues for a synthetic tree; a second
  no-change scan reports zero candidates/imports while retaining correct enumeration counts.
- Scanner diagnostics timings are nonnegative and total elapsed time is at least the measured
  substage durations within normal stopwatch granularity (avoid asserting exact wall-clock values).
- `PreferencesScreenViewModelTests`: a successful Scan Now summary contains scan-stage values and,
  when applicable, distinct cover and automatic-verification durations; no cover duration is
  claimed when no new issues were found.
- Existing scan result/event tests continue to verify event counts and elapsed time.
- Run only the touched test classes and the project's fast test set during implementation; no real
  library scan is part of automated tests.
- Manual measurement: the user runs Scan Now on the real library and shares the resulting Activity
  summary. Compare stage times across repeat runs before choosing an optimization.

## Out of scope

- Changing file discovery, extension matching, path comparison, metadata extraction, or import
  behavior in this diagnostics phase.
- Persistent telemetry, background monitoring, filenames/paths in diagnostics, or new Preferences
  UI.
- Cover-decoding or database optimization before the timing breakdown identifies them as a
  bottleneck.
- Changing the separate book/PDF/EPUB library scanner.
