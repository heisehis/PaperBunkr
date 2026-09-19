# Series/Event Name Matching + Empty-Row Library Cleanup — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-17-series-name-matching-and-empty-row-cleanup-design.md*

Survey notes that shape this plan (found reading the actual files, not assumed from the design doc):

- `ReadingListMatcher.ResolveOrCreatePlaceholder` has **two** exact-match series lookups, not one:
  `FindExisting` (line 29, filters issues by exact series name) AND its own separate exact lookup at
  line 56 (`context.Series.FirstOrDefault(s => s.Name.ToLower() == seriesName.ToLower())`) used when
  no matching issue is found but the series itself might already exist. Both need the cascade fix,
  unified through one shared series-lookup helper.
- `NeedsReviewViewModel.SeriesConflicts` already uses a fully reusable `SeriesConflictRowViewModel`
  (`incomingName, matchedName, similarity, onMerge, onKeepSeparate`) — its own doc comment says it's
  "deliberately unaware" of its data source. Reused as-is for "Find Similar Series" instead of a new
  row type. `MergeSeriesInto` (currently `private static` in `NeedsReviewViewModel.cs:363`) is
  extracted to a new `Paperbunkr.App.Services.SeriesMergeHelper` so both view models can call it.
- No new persisted table needed for "Find Similar Series" — Q9 settled on-demand-only, so candidates
  are computed live from the existing `Series` table each time the button is pressed, not stored.
- `MissingFileRowViewModel` (`IssueId, DisplayLabel, onRelink, onRemove, onDismiss`) is reused as-is
  for the empty-issue-card list — it doesn't care *why* a row is being shown, just needs its own
  `Issue.EmptyRowAcknowledged` flag (separate from `MissingAcknowledged`) for Dismiss.
- `PageDecodeCore.TryOpenProvider` (`Paperbunkr.App.Services`, internal) already returns `null` for
  an unopenable-or-zero-page archive and is in the same project as `LibraryHealthService` — no new
  dependency needed for the corrupt-archive probe.
- Library Health lives in `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (not a
  separate `LibraryHealthSection.axaml` — moved into the Library sidebar section per the 2026-09-07
  redesign). New XAML goes in that file's existing `library.health` `Border.groupBox`, as two more
  `StackPanel` blocks after the existing Missing Files / Recently Removed ones, each with its own
  local `DataTemplate` in `UserControl.Resources` (templates are locally-scoped per file in this
  codebase, confirmed - not shared globally).

## Step 1: `TitleNormalizer` shared utility
**Files:** `src/Paperbunkr.Data/Metadata/TitleNormalizer.cs` (new), `src/Paperbunkr.Data.Tests/TitleNormalizerTests.cs` (new)
**What:** CE-parity port of `ComicInfo.SeriesEquals`/`rxVolume`/`rxSpecial` (`_reference/ComicRackCE/ComicRack.Engine/ComicInfo.cs:123-125,1579-1592`) exactly as specified in the design doc: `NamesMatch(a, b, ignoreVolume = true)` (exact → optional volume-strip → `StripDown`), `StripDown(s)` (strips `[^a-z0-9]|\bthe\b|\band\b`, for direct use as a grouping key).
**Depends on:** none
**Verify:** new tests — exact hit short-circuits before StripDown runs; `":"` vs `" - "` vs en/em-dash all fold; `"Vol. 2"`/`"v2"` fold only when `ignoreVolume: true`; `"Batman"` vs `"The Batman"` fold under `StripDown` (documented risk, not a bug).

## Step 2: `LibraryFolderScanner` scan-time fix (forward)
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit), `src/Paperbunkr.App.Tests/LibraryFolderScannerTests.cs` (edit)
**What:** At the exact-miss branch (`LibraryFolderScanner.cs:211`), before creating a new `Series`, retry against `seriesByName`'s existing keys using `TitleNormalizer.NamesMatch`. Insert this check alongside (not replacing) the existing TPB `StripCollectionWording` fold immediately below — same "existing-series-only" contract, never uses the normalized form to name a new series.
**Depends on:** Step 1
**Verify:** new test — two files for the same series, one embedded `"X: Y"`, one filename-fallback `"X - Y"`, land on one `Series`. Existing TPB-fold and series-mismatch-proposal tests in this file must stay green (uses the file's existing `Prompt`-policy isolation trick).

## Step 3: `ReadingListMatcher` fix
**Files:** `src/Paperbunkr.Data/ReadingLists/ReadingListMatcher.cs` (edit), `src/Paperbunkr.Data.Tests/ReadingListMatcherTests.cs` (edit if it exists, else new)
**What:** Add a private `FindSeriesByCascade(context, seriesName)` (exact `OrdinalIgnoreCase` first, then `TitleNormalizer.NamesMatch` against every `Series.Name` in-memory — same "personal-library scale" rationale already documented in this file's own header comment). Use it in `FindExisting` (replacing the exact `Series.Name` filter with "issue belongs to `FindSeriesByCascade` result") and in `ResolveOrCreatePlaceholder`'s own series lookup at line 56 (replacing its separate exact-only lookup).
**Depends on:** Step 1
**Verify:** new tests — CBL/CSV entry naming a punctuation-variant of an existing series resolves to the real `Series` (not a new placeholder) both when a matching issue already exists and when only the series exists (a new issue number for it).

## Step 4: `StoryArcGroupingResolver` / `StoryEventResolver` / `ContinuityResolver` fix
**Files:** `src/Paperbunkr.Data/Metadata/StoryArcGroupingResolver.cs` (edit), `src/Paperbunkr.Data/Metadata/StoryEventResolver.cs` (edit), `src/Paperbunkr.Data/Metadata/ContinuityResolver.cs` (edit), corresponding `*Tests.cs` (edit)
**What:**
- `StoryArcGroupingResolver.GetCandidates`: group key becomes `(TitleNormalizer.StripDown(arcName), publisherKey)`. Re-key `dismissed` and `existingMemberIssueIdsByName` (lines 89-100) through `StripDown` too, so a dismissal/membership recorded under one spelling suppresses every punctuation variant. Candidate `ArcName` displayed = the raw spelling carried by the most member issues in the group (ties broken by earliest year, consistent with the existing member-ordering at line 139).
- `StoryEventResolver.GetOrCreate` / `ContinuityResolver.GetOrCreate`: on exact-match miss, retry with `TitleNormalizer.NamesMatch(existing.Name, trimmed, ignoreVolume: false)` before creating a new row.
**Depends on:** Step 1
**Verify:** new tests per file — two punctuation-variant `StoryArc` tags produce one candidate with the majority spelling; dismissal re-keying works against a variant spelling; `StoryEventResolver`/`ContinuityResolver` reuse an existing row on a StripDown-only hit instead of duplicating.

## Step 5: `Issue.IsContentEmpty` + `Issue.EmptyRowAcknowledged` + migration
**Files:** `src/Paperbunkr.Data/Entities/Issue.cs` (edit), new EF migration under `src/Paperbunkr.Data/Migrations/`
**What:** Two new columns, both `bool`, default `false`, placed near `MissingVerificationCount`/`MissingAcknowledged` with matching doc-comment style. `IsContentEmpty` = file exists but `PageDecodeCore.TryOpenProvider` can't open it / reports 0 pages. `EmptyRowAcknowledged` = Dismiss flag for the new Empty Rows list (mirrors `MissingAcknowledged`, kept separate since dismissing "this file is corrupt" is a different judgment than dismissing "this file is gone"). Generate via `dotnet ef migrations add AddIssueIsContentEmptyAndAcknowledged -s src/Paperbunkr.Data` (per this project's own documented gotcha: `-s src/Paperbunkr.App` fails, EF.Design is `PrivateAssets=all` in Data.csproj).
**Depends on:** none
**Verify:** new migration up/down test following this project's existing migration-test pattern (check a recent one, e.g. the `AddMissingVerificationCountAndRemovedLibraryEntry` test, for the exact shape).

## Step 6: `LibraryHealthService` content-empty detection
**Files:** `src/Paperbunkr.App/Services/LibraryHealthService.cs` (edit), `src/Paperbunkr.App.Tests/LibraryHealthServiceTests.cs` (edit)
**What:** In `Verify` (`LibraryHealthService.cs:75-93`), for issues where `exists` is true, additionally call `PageDecodeCore.TryOpenProvider(issue.FilePath!)`; dispose it immediately; `null` → `issue.IsContentEmpty = true`, otherwise `false`. Skip the probe entirely when `!exists` (leave `IsContentEmpty` as `false` — that case belongs to Missing Files, not Empty Rows, per the design's Q5/Q11 split). Add `EmptyContentCount` to `LibraryHealthVerifyResult` for the summary card, and an `EmptySeriesCount` computed alongside (zero-`Issue` `Series` rows — a pure count query, not per-file, can live in the same `Verify` method or be computed lazily by the view model at refresh time like `LibraryHealthConfirmedMissing` already is — simplest is the latter, no `VerifyAsync` change needed for the series-level count).
**Depends on:** Step 5
**Verify:** new tests — `Verify_FlagsContentEmpty_ForCorruptOrZeroPageFile`, `Verify_DoesNotFlagContentEmpty_ForMissingFile`, `Verify_ClearsContentEmpty_WhenFileBecomesReadable`.

## Step 7: `SeriesMergeHelper` extraction
**Files:** `src/Paperbunkr.App/Services/SeriesMergeHelper.cs` (new), `src/Paperbunkr.App/ViewModels/NeedsReviewViewModel.cs` (edit)
**What:** Move `MergeSeriesInto` (`NeedsReviewViewModel.cs:363-382`) verbatim into a new `internal static class SeriesMergeHelper { public static void MergeInto(PaperbunkrDbContext context, Series source, Series target) }`. `NeedsReviewViewModel.ResolveConflict` calls `SeriesMergeHelper.MergeInto` instead of its own private method. No behavior change.
**Depends on:** none
**Verify:** existing `NeedsReviewViewModelTests` series-conflict-merge cases stay green unchanged (pure extraction).

## Step 8: "Find Similar Series" (backward remediation)
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit), `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit), `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:**
- New `ObservableCollection<SeriesConflictRowViewModel> SimilarSeriesCandidates`, populated only by a new on-demand `[RelayCommand] FindSimilarSeries()` (not part of `RefreshLibraryHealth`'s automatic reload, per Q9): groups all `Series` by `TitleNormalizer.StripDown(Name)`, any group with 2+ members becomes one row per pair beyond the first (target = member with the most issues, ties broken by lowest `Id`; source = every other member in the group) with `similarity: 1.0`.
- `onMerge` calls `SeriesMergeHelper.MergeInto(context, source, target)` then removes the row from `SimilarSeriesCandidates` (deferred via `Dispatcher.UIThread.Post`, same pattern as `RemoveMissingFile`, since this runs from the row's own `Click` still routing). `onKeepSeparate` just removes the row from the list (no persisted "kept separate" state needed - it's a live on-demand computation, not a queue).
- New XAML block in `LibrarySection.axaml`'s `library.health` `Border.groupBox`: a "Find Similar Series" button (`FindSimilarSeriesCommand`) + `ItemsControl` over `SimilarSeriesCandidates` using a new local `SimilarSeriesRowTemplate` (`x:DataType="vm:SeriesConflictRowViewModel"`), styled like `MigrationOverlay.axaml`'s existing `ConflictRowTemplate` (same view model, new template instance since templates are locally-scoped per file here).
**Depends on:** Steps 1, 7
**Verify:** new `PreferencesScreenViewModelTests` — `FindSimilarSeries_GroupsByStripDownKey_TargetIsMostIssues`, `Merge_CallsSeriesMergeHelper_AndRemovesRow`, `KeepSeparate_RemovesRowWithoutMerging`.

## Step 9: "Empty Rows" (Library Health, new sub-section)
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit), `src/Paperbunkr.App/ViewModels/EmptySeriesRowViewModel.cs` (new), `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit), `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:**
- New `EmptySeriesRowViewModel(int SeriesId, string DisplayLabel, Action onRemove, Action onDismiss)` — Remove ( `TwoStepConfirm`-gated, same as `MissingFileRowViewModel`) + Dismiss only, no Relink concept for a series with nothing to relink.
- `Series.EmptyRowAcknowledged`? — **no**, per design Q5 empty-series is a pure zero-`Issue` count check with no persisted dismiss state needed at the `Series` level for v1; Dismiss here just removes a `Series` with 0 issues in the same "Remove" action (dismissing has nothing to acknowledge against on next load if the row still has 0 issues, it would just reappear) — **resolve this in code review during Step 9**: if a genuine no-op Dismiss is wanted (e.g. an intentionally-empty placeholder series the user wants to keep), add `Series.EmptyRowDismissed` (bool) via the same Step 5 migration; otherwise Dismiss = Remove for empty series and only one button is needed. Default to adding the flag (consistent with every other Library Health list offering a real Dismiss), included in Step 5's migration as a third column if this file is edited before Step 5 lands, or a follow-up migration otherwise.
- `EmptySeriesItems` (new) and `EmptyIssueItems` (reusing `MissingFileRowViewModel`, keyed on `IsContentEmpty && !EmptyRowAcknowledged`) both populated inside `RefreshLibraryHealth` (automatic, unlike Find Similar Series — detection here is just a query over already-verified state, not an expensive live computation).
- One combined "Empty Rows" `StackPanel` section in `LibrarySection.axaml`, after Recently Removed, following the exact same "all-clear checkmark vs list" shape as Missing Files, with two `ItemsControl`s (empty series, empty issues) under one header.
**Depends on:** Steps 5, 6
**Verify:** new `PreferencesScreenViewModelTests` — `RefreshLibraryHealth_ListsZeroIssueSeries`, `RefreshLibraryHealth_ListsContentEmptyIssues_ExcludesAcknowledged`, `RemoveEmptySeries_DeletesRow`, `DismissEmptySeries_SetsAcknowledged`.

## Step 10: Full-suite regression pass
**Files:** none (verification only)
**What:** Run `App.Tests` and `Data.Tests` subsets touched by every step above (not the full suite, per this project's own documented full-suite headless flake).
**Depends on:** Steps 1-9
**Verify:** all new + existing tests in the touched files green; forced clean rebuild (`rm` the App project's `obj/Debug/net8.0/*.dll/.pdb` + `dotnet build`) per this project's own Avalonia new-view build gotcha, since Step 9 adds no new `.axaml` file (edits an existing one) so this is likely unnecessary but cheap to confirm.
