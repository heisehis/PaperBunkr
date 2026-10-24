# Library scan diagnostics — implementation plan

*Implements: [2026-10-15-library-scan-diagnostics-design.md](2026-10-15-library-scan-diagnostics-design.md)*

## Step 1: Return full-scan stage diagnostics

**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs`

**What:** Add a diagnostics record to the completed scan result. Count available watched roots, enumerated paths, supported comic files, already-known paths, candidates, and imported issues. Measure enumeration/filtering, `ImportFiles`, and total full-scan time without adding another traversal or archive read. Preserve the existing plugin completion event contract.

**Depends on:** none.

**Verify:** Run `LibraryFolderScannerTests`, including count assertions for a new scan and a no-change rescan.

## Step 2: Include downstream Scan Now stage timings

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs`

**What:** Measure cover generation and optional automatic verification separately. Append compact counts and stage durations to the existing Activity Center success summary, retaining the existing user-facing added-issues and removal text. Do not include titles or paths.

**Depends on:** Step 1.

**Verify:** Run the focused Scan Now tests in `PreferencesScreenViewModelTests`, covering no additions, cover generation, and automatic verification.

## Step 3: Add summary and diagnostics regression coverage

**Files:** `src/Paperbunkr.App.Tests/LibraryFolderScannerTests.cs`, `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs`

**What:** Assert accurate scanner counts, nonnegative stage durations, zero new candidates on an idempotent scan, and Activity Center summaries with separate scan/cover/verification stages as applicable.

**Depends on:** Steps 1–2.

**Verify:** Run the touched test classes and the project's `Speed!=Slow` App test set.

## Step 4: Update the approved design status

**Files:** `docs/superpowers/specs/2026-10-15-library-scan-diagnostics-design.md`

**What:** Mark implementation complete and record commit and verification evidence. Leave the later measured optimization as a separate follow-up, since this spec establishes the baseline only.

**Depends on:** Steps 1–3.

**Verify:** Compare reported counts/timings with the Activity history result and keep the note clear that no speedup has yet been claimed.
