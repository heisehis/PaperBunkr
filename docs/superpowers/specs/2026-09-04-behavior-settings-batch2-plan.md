# Behavior settings, second batch — Implementation Plan

*Implements: docs/superpowers/specs/2026-09-04-behavior-settings-batch2-design.md*

## Step 1: `AppSettings` columns + migration

**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`src/Paperbunkr.Data/Migrations/*_AddBehaviorSettingsBatch2.*` (new, scaffolded),
`src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs` (regenerated)

**What:** Add four bools to `AppSettings` with the design's XML-doc comments:
`RestoreSessionOnStartup = true`, `ScanFoldersOnStartup` (false), `PromptReviewOnFinish` (false),
`EnableDragDropImport = true`. Add explicit `builder.Property(a => a.X).HasDefaultValue(...)` for
all four in `PaperbunkrDbContext.OnModelCreating`'s AppSettings block — the CLR `= true` initializer
is *not* picked up as a SQL default, so without this the true-by-default columns backfill an
existing row to `false` and silently break current behavior. Then `dotnet ef migrations add
AddBehaviorSettingsBatch2 -p src/Paperbunkr.Data -s src/Paperbunkr.Data` (do **not** run
`migrations remove` first — it reverts the cumulative model snapshot to `AddActivityRuns`'s
designer snapshot, which is missing `Issue.CoverAspectRatio` because those two migrations were
authored on parallel branches, and the next `add` then re-adds `CoverAspectRatio` spuriously).
`Down()` is a **no-op** with the same rationale as `20260903211057_AddMetadataWriteBackSettings`.
Do **not** run `database update`.

*Landed as `20260904045621_AddBehaviorSettingsBatch2`.*

**Depends on:** none
**Verify:** `AddBehaviorSettingsBatch2MigrationTests` (new): migrate to HEAD, assert the four
defaults on `GetOrCreateAppSettings()` and round-trip flipped values; a second test migrates down
to `PriorMigration = "20260903215515_AddActivityRuns"` and asserts the no-op `Down` leaves the four
columns and the singleton row intact. (A raw-SQL "backfill an already-existing row" test was
dropped — `INSERT INTO AppSettings (Id)` fails on the many other `NOT NULL`-without-SQL-default
columns, and SQLite's `ALTER TABLE ... ADD COLUMN ... DEFAULT` backfill is well-defined anyway.)
**2/2 pass.**

## Step 2: `RestoreSessionOnStartup` gate

**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)

**What:** `MainViewModel.GoHome()` is private, so the gate lives *inside* `RestoreLastScreen()`
rather than in `App.axaml.cs`: right after it reads `settings`, `if
(!settings.RestoreSessionOnStartup) { GoHome(); return; }`. `App.axaml.cs` keeps calling
`RestoreLastScreen()` unchanged.

**Depends on:** Step 1
**Verify:** `MainViewModelTests.RestoreLastScreen_WithRestoreSessionOnStartupOff_GoesHome_
IgnoringPersistedScreen` — a valid persisted `LastScreenKey`/`EntityId` plus the toggle off lands
on Home. `dotnet test src/Paperbunkr.App.Tests`.

## Step 3: `ScanFoldersOnStartup` background job

**Files:** `src/Paperbunkr.App/App.axaml.cs` (edit)

**What:** After the existing fire-and-forget `Task.Run` blocks (~line 99), add a
`if (settings.ScanFoldersOnStartup) { _ = Task.Run(async () => { … }); }` that opens an
`mainViewModel.Activity.StartJob(ActivityJobKind.LibraryScan, "Startup folder scan")`, awaits
`new LibraryFolderScanner().ScanAllAsync(…)` then `new BookFolderScanService().ScanAllAsync(…)`,
and `job.Succeed(...)` / `job.Fail(...)` around it. Match the `progress` arg type the
`PreferencesScreenViewModel.ScanNow` / `ScanBooksNow` callers use (a `Progress<(int,int)>`; pass a
no-op sink). No cover-generation pass. `mainViewModel` is already constructed at that point (line
125) — move this block to just after line 130 if ordering needs it.

**Depends on:** Step 1
**Verify:** Build + manual (drop a file into a watched folder while the app is closed, launch with
the toggle on, confirm it appears and the activity center shows the job). No unit test — it's an
`App.axaml.cs` composition block, same as the neighboring triggers which also have none.

## Step 4: `PromptReviewOnFinish` in the reader

**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)

**What:**
- Add `private bool _reviewPromptShown;` near `_loadedIssueId` (line 53); set it `false` in
  `Load(...)` right after `_loadedIssueId = issue.Id;` (line 765).
- Add `public event Action<int>? ReviewPromptRequested;` near the other events
  (`ReaderDisplaySettingsChanged` sibling — actually that one's on Preferences; put it by
  `CurrentPageIndexChanged` ~line 424).
- Add `private void MaybePromptReviewOnFinish()`: return if `_reviewPromptShown` or
  `_loadedIssueId` is null; open a `PaperbunkrDb.CreateContext()`, return if
  `!GetOrCreateAppSettings().PromptReviewOnFinish`; else set `_reviewPromptShown = true` and
  `ReviewPromptRequested?.Invoke(issueId)`.
- Call it from the `!TryGetAdjacentIssuePreview(...)` early-return in **`TriggerChapterTransition`**
  (line 2146) and **`ChapterBoundaryOverscroll`** (line 2166), guarded by `if (forward)` in both.
  (`JumpChapterExplicitly` at 2213 — deliberate button press — does **not** prompt.)
- `MainViewModel`: after `Reader = new ReaderScreenViewModel(NavigateBack, keyBindingService);`
  (line 110), add `Reader.ReviewPromptRequested += OpenQuickRateOverlay;` (method group already
  matches `Action<int>`, defined ~line 982).

**Depends on:** Step 1
**Verify:** `ReaderScreenViewModelTests` (new cases, subscribe to `vm.ReviewPromptRequested`):
fires with `_issue2Id` when paging past the last page of the last issue with the setting on;
does not fire with the setting off; does not fire mid-book (`_issue1Id`, still has a next issue,
autonav on); does not fire on backward under-run at page 0; fires at most once across repeated
`NextPageCommand` executes. Helper `SetPromptReviewOnFinish(bool)` mirroring `SetAutoNavigateComics`.

## Step 5: `EnableDragDropImport` gate

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit),
`src/Paperbunkr.App/Views/ReadingScreen.axaml.cs` (edit)

**What:**
- Each VM gets `public bool DragDropImportEnabled { get { using var context =
  PaperbunkrDb.CreateContext(); return context.GetOrCreateAppSettings().EnableDragDropImport; } }`
  (read fresh; both VMs already use `PaperbunkrDb.CreateContext()` directly everywhere).
- Each `OnDragOver`: `e.DragEffects = (DataContext as XxxViewModel)?.DragDropImportEnabled == true
  && e.DataTransfer.Formats.Contains(DataFormat.File) ? Copy : None;`
- Each `OnDrop`: after the `DataContext is not … vm` guard, `if (!vm.DragDropImportEnabled)
  return;`
- Also early-return in each `ImportDroppedPathsAsync` if the flag is off (belt-and-suspenders,
  cheap).

**Depends on:** Step 1
**Verify:** `LibraryScreenViewModelTests` / `ReadingScreenViewModelTests` — `DragDropImportEnabled`
reflects the persisted `AppSettings.EnableDragDropImport`; `ImportDroppedPathsAsync` no-ops when
off. Manual: drag a file onto Library with the toggle off → no drop cursor, nothing imported.

## Step 6: Preferences UI + search index

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/Preferences/GeneralSection.axaml` (edit),
`src/Paperbunkr.App/Models/PreferenceIndex.cs` (edit)

**What:**
- `PreferencesScreenViewModel`: four `[ObservableProperty]` fields
  (`_restoreSessionOnStartup = true`, `_scanFoldersOnStartup`, `_promptReviewOnFinish`,
  `_enableDragDropImport = true`) in the behavior block (~line 320); four `On…Changed` partials
  → `PersistBehaviorSetting(s => s.X = value)`; four hydration reads in `Reload()` inside the
  `_suppressBehaviorApply` guard (~line 522).
- `GeneralSection.axaml`: new **Startup** `groupBox` (`Tag="general.startup"`, first) with the two
  startup checkboxes + the scan caption `TextBlock`; add the "Ask me to rate a comic when I finish
  it" checkbox to the existing **Reading** card; new **Library** `groupBox`
  (`Tag="general.library"`) with the drag-drop checkbox. Copy the exact `CheckBox` / `Border
  Classes="groupBox"` / `groupHeader` markup already in the file.
- `PreferenceIndex.cs`: add `general.startup` and `general.library` entries; append
  `"rate on finish"`, `"quick review"`, `"review prompt"` to the existing `general.reading`
  keywords.

**Depends on:** Step 1 (columns must exist for the persist hooks to compile)
**Verify:** `PreferencesScreenViewModelTests` — `EnsureLoaded` hydrates all four from `AppSettings`;
toggling each persists (mirror `TogglingBehaviorFlags_PersistsToAppSettings`).
`PreferenceIndexTests` passes with the two new anchors (it asserts anchor↔`Border.Tag`).
`dotnet test src/Paperbunkr.App.Tests`.

## Step 7: Full build + suite + roadmap/wiki

**Files:** `docs/Paperbunkr-Roadmap.md` (edit), `wiki/Preferences.md` (edit if it enumerates
General toggles)

**What:** `dotnet build`, full `dotnet test` (App + Data). Update the roadmap: the second
Behavior batch is now shipped (not just designed) — adjust the "designed 2026-09-04" line in the
"Preferences: Behavior / CE-parity toggle remainder" section. Add the four new toggles to
`wiki/Preferences.md` if it lists them.

**Depends on:** Steps 1–6
**Verify:** green build, green suite. Then the manual on-screen pass (user): flip all four, confirm
fresh-Home with restore off, startup scan picks up offline changes, finishing a comic pops the
rating overlay, drag-drop does nothing with import off. Run `avalonia-pro-max/review-checklist`
on the `GeneralSection.axaml` diff before calling UI done.

---

## Result (2026-09-04)

- `Paperbunkr.Data` + `Paperbunkr.App`: **build clean, 0 warnings / 0 errors.**
- `AddBehaviorSettingsBatch2MigrationTests`: **2/2.**
- `PreferencesScreenViewModelTests` + `MainViewModelTests` + `LibraryScreenViewModelTests`
  (filtered run): **281/281.**
- `ReaderScreenViewModelTests` (filtered run): **180/180.**
- `GeneralSection.axaml` review: the two new group cards and four checkboxes are copied verbatim
  from the file's existing `Border Classes="groupBox"` / `CheckBox` markup — every colour is a
  `{DynamicResource Pb*Brush}`, no hardcoded hex, no animation / `BoxShadows` / gradient, so the
  known skin-reactivity gotcha does not apply.
- **Deviations from the plan above:** the `RestoreSessionOnStartup` gate lives inside
  `MainViewModel.RestoreLastScreen()` (its `GoHome()` is private) rather than `App.axaml.cs`;
  `PromptReviewOnFinish` is wired as a `ReaderScreenViewModel.ReviewPromptRequested` event rather
  than a constructor callback (the 1-arg ctor has ~130 call sites); `OnModelCreating` needed
  explicit `HasDefaultValue` for all four columns.
- **Not committed** (per repo convention); still needs the user's on-screen pass.
