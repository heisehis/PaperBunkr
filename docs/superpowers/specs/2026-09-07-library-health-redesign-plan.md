# Library Health Dashboard Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-07-library-health-redesign-design.md*

**Correction found during survey (§8 of the design doc):** the design speculated a new
`PreferencesScreenViewModel.ToastRequested` event + `MainViewModel` subscription to route status
text through toasts. Survey found this infrastructure already exists and is already wired:
`PreferencesScreenViewModel` already takes an `Action<string, string> showToast` constructor
parameter (`_showToast`, `PreferencesScreenViewModel.cs:68/112`), and `VerifyLibraryHealthNow`
*already* runs inside an `IActivityService` job (`_activity.StartJob(ActivityJobKind.LibraryVerify,
...)`, `PreferencesScreenViewModel.cs:2695`) whose `Succeed`/`Fail` already raises a toast
automatically via `ActivityService`'s existing `ShouldToast`/`CompletionToastRequested` pipeline
(default `ActivityToastPolicy.Always`, no override at this call site). So: Verify Now already
toasts today — the only change needed there is deleting the now-redundant
`LibraryHealthVerifyStatus` inline text. Bulk Relink has no job/toast at all today — it gets one
by calling the already-injected `_showToast` field directly, no new plumbing. The design's *goal*
(status text → toasts) is unchanged; only the mechanism differs from what §8 sketched.

## Step 1: Confirmed-missing threshold — AppSettings column + migration
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), new migration pair via
`dotnet ef migrations add AddLibraryHealthConfirmedMissingThreshold -s src/Paperbunkr.Data`
(`-s` must be the Data project — the App project fails, see project memory), `PaperbunkrDbContextModelSnapshot.cs` (auto-updated by the EF tool)
**What:** Add `public int LibraryHealthConfirmedMissingThreshold { get; set; } = 2;` next to
`LastLibraryHealthVerifyUtc` (`AppSettings.cs:523`). Generate the migration, then hand-fix
`Down()` to a no-op for this column — mirror `20260906153921_AddMissingVerificationCountAndRemovedLibraryEntry.cs`'s
`Down()` exactly (same orphan-column rollback-chain rule from CLAUDE.md/project memory: any new
`AppSettings`/`Issues` column needs a no-op `Down()`).
**Depends on:** none
**Verify:** new `AddLibraryHealthConfirmedMissingThresholdMigrationTests`, mirroring
`AddMissingVerificationCountAndRemovedLibraryEntryMigrationTests`'s shape (column exists with
default 2; `Down()` is a no-op). `dotnet build` on `Paperbunkr.Data`.

## Step 2: De-staticify `LibraryHealthService.ConfirmedMissingThreshold`
**Files:** `src/Paperbunkr.App/Services/LibraryHealthService.cs` (edit),
`src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit, call sites only),
`src/Paperbunkr.App.Tests/LibraryHealthServiceTests.cs` (edit)
**What:** Change `public const int ConfirmedMissingThreshold = 2` (`LibraryHealthService.cs:29`)
to `public int ConfirmedMissingThreshold { get; set; } = 2` (instance property). Update every
static call site to go through the `_libraryHealth` instance instead:
`LibraryHealthService.cs:83`, and `PreferencesScreenViewModel.cs:1584, 1591, 2646, 2653, 2897`
(all currently read `LibraryHealthService.ConfirmedMissingThreshold` statically).
**Depends on:** none
**Verify:** existing 5 `LibraryHealthServiceTests` cases updated to construct with an explicit
threshold where relevant; one new case confirming a non-default threshold changes which issues
classify as confirmed-missing.

## Step 3: Surface the threshold as a setting
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:** New `[ObservableProperty] int _libraryHealthConfirmedMissingThreshold` (default 2,
range-clamped 1-5 in its `partial void On...Changed`), read from `AppSettings` on load into both
the bindable property and `_libraryHealth.ConfirmedMissingThreshold`; on change, persist to
`AppSettings` and update `_libraryHealth.ConfirmedMissingThreshold`, then call
`RefreshLibraryHealth` so confirmed-missing counts update immediately. The XAML control for this
lands in Step 6 (it's a `SettingsRow`, added alongside the rest of the visual rebuild).
**Depends on:** Steps 1 and 2.
**Verify:** new test: changing the property persists to `AppSettings` and changes
`HasConfirmedMissingItems`/`LibraryHealthConfirmedMissing` for a fixture issue sitting exactly at
the old vs. new threshold boundary.

## Step 4: Bulk-remove confirmation → shared `IDialogService`
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit — both
constructors), `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit, one call site),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit, `CreateViewModel` helper)
**What:** Add `IDialogService dialogService` to both `PreferencesScreenViewModel` constructors
(public and internal, `PreferencesScreenViewModel.cs:61-100`), store as `_dialogService`. Change
`OpenBulkRemoveConfirm` (`PreferencesScreenViewModel.cs:2892`) to `async Task`, replacing
`IsBulkRemoveConfirmOpen = true` with
`await _dialogService.ShowAsync(new ConfirmDialogRequest(Message: "...", Items: BulkRemovePreview, PrimaryLabel: "Confirm Remove All", IsDestructive: true))`;
on answer `== 0`, run the existing body of `ConfirmBulkRemoveCommand` inline. Delete
`IsBulkRemoveConfirmOpen` and `CancelBulkRemoveConfirmCommand` entirely. Update
`MainViewModel.cs:237`'s `new PreferencesScreenViewModel(...)` call to pass `Dialogs`
(`MainViewModel.cs:103` already constructs this — same instance `Plugin` already shares,
`MainViewModel.cs:151`).
**Depends on:** none structurally (independent of Steps 1-3).
**Verify:** `CreateViewModel` test helper gains `IDialogService? dialogService = null` defaulting
to `new FakeDialogService()` (already exists, shared with `PluginScreenViewModelTests`). New test
asserts `ShowAsync` is called with the expected `Items`/message, and that a primary (`0`) answer
triggers the same removal + `RefreshLibraryHealth` the old inline-confirm path did.

## Step 5: Verify Now job exposure + Bulk Relink → toast
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:**
- New `[ObservableProperty] ActivityJob? _currentLibraryHealthJob`, set to `job` at the start of
  `VerifyLibraryHealthNow` (`PreferencesScreenViewModel.cs:2686`) and cleared to `null` in its
  `finally`. Remove `LibraryHealthVerifyStatus` entirely (property + both assignments) — the
  ActivityJob pipeline already toasts on completion (see plan preamble).
- In `BulkRelinkMissingFiles` (`PreferencesScreenViewModel.cs:2826`), replace both
  `LibraryHealthBulkRelinkStatus = ...` assignments (`:2868-2870`) with
  `_showToast("Library Health", summary)`. Remove `LibraryHealthBulkRelinkStatus` entirely.
**Depends on:** none.
**Verify:** existing `IsVerifyingLibraryHealth`-toggling tests still pass; new test asserts
`CurrentLibraryHealthJob` is non-null while a verify is in flight and null after. Update the
existing bulk-relink test to assert against the `showToast` callback (the `CreateViewModel` helper
already accepts one, `PreferencesScreenViewModelTests.cs:103`) instead of the removed status
string.

## Step 6: Rebuild the Library Health block in XAML
**Files:** `src/Paperbunkr.App/App.axaml` (edit — new brushes), `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit)
**What:** This is the visual rebuild approved in the brainstorming session's mockups. In one pass
over the existing `Tag="library.health"` block (`LibrarySection.axaml:172-250`):
- Add 3 new low-opacity tint brushes to `App.axaml` for the gold/danger/success chip backgrounds
  (~18% alpha of `PbBadgeColor`/`PbDangerColor`/`PbSuccessColor`); the amber chip reuses the
  existing `PbAccentSoftBrush` (`App.axaml:27`, already ~16% alpha) rather than adding a 4th.
- Replace the plain 4-column `Grid` (`:181-198`) with 4 `Border` stat tiles, each with an icon
  chip (list/alert/alert/clock `fi:SymbolIcon`, matching the approved mockup's color mapping:
  amber / gold / danger / success).
- `Verify Now` → icon+text primary button; `Remove All Confirmed Missing` → danger-tinted ghost
  button with a trash icon (drop the old plain `Content=` buttons). Add a `BusyIndicator`
  (`Job="{Binding CurrentLibraryHealthJob}"`) visible while `IsVerifyingLibraryHealth`, next to the
  Verify Now button. Remove the `LibraryHealthVerifyStatus`-bound `TextBlock`.
- Delete the inline bulk-remove confirm `Border` (`:208-226`) entirely — `IDialogService`/
  `ConfirmDialogView` renders it now (Step 4).
- `MissingFileRowTemplate` and `RemovedLibraryEntryRowTemplate` (`:13-36`) get icon+text buttons
  (link/Relink, x/Dismiss, trash/destructive Delete, restore-icon/Restore), matching Comic
  Folders' row buttons (`:64-79`) in the same file. Remove the
  `LibraryHealthBulkRelinkStatus`-bound `TextBlock`.
- Missing Files section (`:229-241`) stops being conditionally hidden
  (`IsVisible="{Binding HasMissingFileItems}"` removed from the container); add a healthy
  empty-state row (success chip + "No missing files") shown when `!HasMissingFileItems`.
- Recently Removed section (`:243-248`) becomes a disclosure: new
  `[ObservableProperty] bool _isRecentlyRemovedExpanded` (default `false`) +
  `ToggleRecentlyRemovedExpandedCommand` on `PreferencesScreenViewModel`. Header row is clickable,
  shows "Recently Removed · N" with a chevron when `HasRecentlyRemovedItems`, or
  "Recently Removed · All clear" with no chevron (and not clickable) when empty. The
  `ItemsControl` only renders when both `HasRecentlyRemovedItems` and `IsRecentlyRemovedExpanded`.
- Add the new threshold `SettingsRow` (Step 3's `LibraryHealthConfirmedMissingThreshold`, a
  `NumericUpDown` ranged 1-5) inside this block, near the action row.
**Depends on:** Steps 3 (bindable threshold property), 4 (dialog swap already landed so the old
panel can be deleted cleanly), 5 (`CurrentLibraryHealthJob`, removed status properties).
**Verify:** manual on-screen pass (standing no-unattended-GUI caveat) — covers everything in the
design doc's Testing section: healthy-library empty states on both lists, missing-file
relink/dismiss/delete via the new icon buttons, Remove All Confirmed opens the shared modal with
the right preview, changing the threshold changes bulk-remove eligibility, verify-complete and
bulk-relink toasts appear (not inline text), Recently Removed collapses/expands and shows "All
clear" with no chevron when empty. New `PreferencesScreenViewModelTests` case for the disclosure
toggle command (flips only when `HasRecentlyRemovedItems`).

## Step 7: Scanning → `SettingsRow`, then reorder the page
**Files:** `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit)
**What:** Convert the "Scanning" group's 2 toggle rows (`:151-161`,
`AutoRemoveMissingOnScan`/`DontReimportRemovedFiles`) from raw `Grid`+`ToggleSwitch` to
`SettingsRow`, matching Phase 1's conversions elsewhere (e.g. `GeneralSection.axaml`) — bindings
unchanged. Then move the (now fully rebuilt) Library Health `Border` block to the top of the
page's `StackPanel`, ahead of Comic Library Folders/Scanning/Book Folders/Migration/Virtual Tags.
Pure reordering — every block's `Tag` anchor value is unchanged, so `PreferenceIndex` jump-to-
anchor keeps resolving correctly regardless of visual order.
**Depends on:** Step 6 (reorders the finished block, not the old one).
**Verify:** manual on-screen; `PreferenceIndexTests` stays green untouched (anchors are tag-keyed,
not order-keyed).

## Step 8: Description checklist
**Files:** `docs/preferences-descriptions-todo.md` (edit)
**What:** Append the new threshold `SettingsRow`'s entry to the existing per-row checklist, per
the design doc's deliverable note (same tracking mechanism Phase 1 already uses — no new
mechanism).
**Depends on:** Step 6.
**Verify:** n/a (doc-only).
