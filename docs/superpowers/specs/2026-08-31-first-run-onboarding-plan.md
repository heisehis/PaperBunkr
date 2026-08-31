# First-Run Onboarding — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-31-first-run-onboarding-design.md*

Decisions locked in during planning (both were left as "implementation-time calls" in the design):
- **Tour offer surface:** a small centered card overlay, same visual pattern as the existing
  "Unsaved changes" discard-confirm card (`MainWindow.axaml:1017-1035`) — not an extended
  `WindowNotificationManager` toast. Avalonia's `Notification` model (title/message/type/expiration)
  has no clean two-button affordance; the discard-confirm card already proves this exact shape
  (icon + text + two buttons) in this codebase.
- **Scrim cutout geometry:** `Avalonia.Media.CombinedGeometry` with `GeometryCombineMode.Exclude`
  over a full-window `RectangleGeometry` and a `RectangleGeometry` (with `RadiusX`/`RadiusY`) sized
  to the target control's inflated bounds. Verify the exact API shape against the `avalonia-docs` MCP
  server at the start of Step 7 (this is the one piece of real implementation risk in the plan) before
  writing the control.

## Step 1: `AppSettings` fields + EF migration
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), new EF migration in
`src/Paperbunkr.Data/Migrations/`
**What:** Add `WelcomeScreenShown` (bool, default false) and `WelcomeTourOffered` (bool, default
false) per the design doc's Persistence section (doc comments included). Scaffold the migration the
same way `20260831030412_AddLastScreenState` was (two nullable/bool columns, no data migration
needed — `dotnet ef migrations add AddWelcomeOnboardingFlags --project src/Paperbunkr.Data`).
**Depends on:** none
**Verify:** `dotnet build` on `Paperbunkr.Data`; migration applies cleanly against a scratch db
(`dotnet ef database update` or via `PaperbunkrDb.EnsureCreated()` in a throwaway test).

## Step 2: Nav rail target names for the tour
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** Add `x:Name="SmartListsRailButton"` and `x:Name="ReadingListsRailButton"` to the two rail
buttons that currently have neither (lines ~205-211, ~212-218). Add
`AutomationProperties.AutomationId="EventsRailButton"` to the Continuity button (~219-225) to match
its siblings' existing convention (it's the only lateral rail button missing one). `HomeRailButton`,
`LibraryRailButton`, `BooksRailButton`, `PreferencesRailButton` already have `AutomationId`s
(confirmed present) — reused as-is for `FindControl` lookups in Step 7, no change needed there.
**Depends on:** none
**Verify:** `dotnet build`; existing `Paperbunkr.App.UiTests` automation targeting the unchanged
`AutomationId`s still resolves (no rename of any existing id).

## Step 3: `WelcomeOverlayViewModel` + `WelcomeOverlay` view
**Files:** `src/Paperbunkr.App/ViewModels/WelcomeOverlayViewModel.cs` (new),
`src/Paperbunkr.App/Views/WelcomeOverlay.axaml` (new), `src/Paperbunkr.App/Views/WelcomeOverlay.axaml.cs` (new)
**What:** Mirrors `MigrationOverlayViewModel`'s shape and construction style — a small VM taking
`IFilePickerService`, `Action reloadFolderWatch`, and `Action openMigrationOverlay` (callback into
`MainViewModel.OpenMigrationOverlayCommand`, same indirection `PreferencesScreenViewModel` already
uses for the same command). Exposes:
- `bool CeInstallDetected` (set once at construction from a param passed by `MainViewModel`, which
  already computes `File.Exists(MigrationViewModel.GetDefaultCePath())` — see Step 6).
- `[RelayCommand] AddComicFolder()` — `await _filePicker.PickFolderAsync("Add Comic Library Folder")`
  → insert into `WatchedFolders` via `PaperbunkrDb.CreateContext()` if not a duplicate path → save →
  call `_reloadFolderWatch()` → invoke the shared `_requestClose` callback. Mirrors
  `PreferencesScreenViewModel.AddFolder()` (`PreferencesScreenViewModel.cs:948-968`) exactly, using
  `PaperbunkrDb.CreateContext()` directly (matching `MigrationViewModel`'s convention, not
  `PreferencesScreenViewModel`'s injected `_contextFactory` — this VM is closer in weight/shape to
  `MigrationViewModel`).
- `[RelayCommand] AddBookFolder()` — same shape against `BookFolders`, mirroring
  `PreferencesScreenViewModel.AddBookFolder()` (`PreferencesScreenViewModel.cs:1042-1061`). No
  folder-watch reload needed (book folders have no live watch, matching the existing method).
- `[RelayCommand] ImportFromCe()` — invokes `_openMigrationOverlay()` then `_requestClose()`.
- `[RelayCommand] Skip()` — just `_requestClose()`.
- `private readonly Action _requestClose` (constructor param, wired by `MainViewModel` to its own
  `CloseWelcomeOverlay()` in Step 4 — same indirection pattern `NewReadingListViewModel` already uses
  for its own close callback).

`WelcomeOverlay.axaml`: `Border Width="580" Classes="floatingPanel"` shell (same as
`MigrationOverlay.axaml:138`), headline + tagline, three `Button`-based cards (icon via
`fi:SymbolIcon`, title, one-sentence description) bound to `AddComicFolderCommand`/
`AddBookFolderCommand`/`ImportFromCeCommand`, the CE card showing the same checkmark+label treatment
`MigrationOverlay.axaml:162-165` uses when `CeInstallDetected` is true, and a quiet "Skip for now"
`Button` (ghost/link styling) bound to `SkipCommand`. No code-behind logic needed beyond the standard
`InitializeComponent()` partial class (per this project's `AVLN2000` build gotcha — add both files in
the same step, per `CLAUDE.md`).
**Depends on:** none (constructor params are plain delegates/services, no dependency on Step 1's
fields directly — persistence happens in `MainViewModel.CloseWelcomeOverlay`, Step 4).
**Verify:** New `WelcomeOverlayViewModelTests.cs` (Step 10) covers the command logic; view is
exercised visually in Step 11's manual pass.

## Step 4: Wire `WelcomeOverlayViewModel` + tour-offer state into `MainViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:**
- Construct `Welcome = new WelcomeOverlayViewModel(new FilePickerService(), LiveFolderWatch.Reload, OpenMigrationOverlay, CloseWelcomeOverlay)`
  alongside the other overlay VMs (near `Migration = new MigrationOverlayViewModel(...)`,
  `MainViewModel.cs:67`). Expose `public WelcomeOverlayViewModel Welcome { get; }`.
- `[ObservableProperty] private bool _isWelcomeOverlayOpen;` and
  `[RelayCommand] private void OpenWelcomeOverlay(bool ceInstallDetected) { Welcome.CeInstallDetected = ceInstallDetected; IsWelcomeOverlayOpen = true; }`
  (mirrors `OpenMigrationOverlay`, `MainViewModel.cs:456-461`).
- `private void CloseWelcomeOverlay()` — sets `IsWelcomeOverlayOpen = false`; persists
  `WelcomeScreenShown = true` via `PaperbunkrDb.CreateContext()` + `GetOrCreateAppSettings()` +
  `SaveChanges()`; then, if `!appSettings.WelcomeTourOffered` (re-read/reuse the same context),
  sets `WelcomeTourOffered = true`, saves, and sets `IsTourOfferOpen = true`. One method, one write
  path, matching the design's "every exit path calls the same `CloseWelcomeOverlay()`" requirement.
- `[ObservableProperty] private bool _isTourOfferOpen;`
  `[RelayCommand] private void TakeTour() { IsTourOfferOpen = false; OpenWelcomeTourOverlay(); }` (the
  latter added in Step 8) and `[RelayCommand] private void DeclineTour() => IsTourOfferOpen = false;`
- Add `IsWelcomeOverlayOpen`/`IsTourOfferOpen` branches to `Escape()` (`MainViewModel.cs:1385-1400`
  region), closing/declining the same way the corner-X button will (see Step 5) — `IsWelcomeOverlayOpen`
  calls `Welcome.SkipCommand.Execute(null)` (routes through the one close path, not a duplicate),
  `IsTourOfferOpen` calls `DeclineTour()`.
**Depends on:** Step 3 (`WelcomeOverlayViewModel` must exist), Step 1 (`AppSettings` fields).
**Verify:** `dotnet build`; exercised by `MainViewModelTests` extension in Step 10.

## Step 5: `MainWindow.axaml` markup for welcome overlay + tour-offer card
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** Two new overlay blocks, appended after the existing "Unsaved-changes discard confirm"
block (`MainWindow.axaml:1014-1035`, still the topmost existing element) so both render above
everything already there:
1. Welcome overlay — same backdrop-popup pattern as every other overlay in this file (`Border
   IsVisible="{Binding IsWelcomeOverlayOpen}" Background="#B0000000"` → centered `Grid` →
   `<views:WelcomeOverlay DataContext="{Binding Welcome}" />` + the standard corner-X `Button`
   (`Command="{Binding Welcome.SkipCommand}"`, `AutomationId="WelcomeOverlayCloseButton"`) — exact
   copy of the `MigrationOverlay` block's structure (`MainWindow.axaml:819-831`).
2. Tour-offer card — same small centered `Border` structure as the discard-confirm block
   (`MainWindow.axaml:1017-1035`), `IsVisible="{Binding IsTourOfferOpen}"`, icon + "Want a quick tour
   of Paperbunkr?" text + two buttons (`Classes="discardConfirm ghost"` "No thanks" →
   `DeclineTourCommand`, `Classes="discardConfirm primary"` "Take the tour" → `TakeTourCommand`) —
   reusing the `discardConfirm` style classes already defined in `MainWindow.axaml.Styles`
   (lines 127-146), no new styles needed.
**Depends on:** Step 3, Step 4.
**Verify:** `dotnet build`; manual pass in Step 11.

## Step 6: `App.axaml.cs` — replace the auto-migration gate
**Files:** `src/Paperbunkr.App/App.axaml.cs` (edit)
**What:** Replace the `isFreshInstall`/`offerFirstRunMigration` block (`App.axaml.cs:53-65`,
`129-132`) with:
```csharp
bool ceInstallDetected = File.Exists(MigrationViewModel.GetDefaultCePath());
```
computed once where `defaultCePathFound` used to be (no more `isFreshInstall` check needed here —
`HasAnySeries()` stays used only for its existing unrelated purposes, per the design's explicit
note that this spec doesn't touch those). After `mainViewModel` is constructed and
`RestoreLastScreen()`/`OpenDeepLink()` has run (same position as today's `offerFirstRunMigration`
check, `App.axaml.cs:129-132`):
```csharp
using (var settingsContext = PaperbunkrDb.CreateContext())
{
    if (!settingsContext.GetOrCreateAppSettings().WelcomeScreenShown)
    {
        mainViewModel.OpenWelcomeOverlayCommand.Execute(ceInstallDetected);
    }
}
```
(A short-lived context here, separate from the existing graphics.json-sync context a few lines
earlier at `App.axaml.cs:92` — that one is scoped to its own `try`/`catch` block and closes before
this point, so no reuse without restructuring; not worth doing for one extra read.)
**Depends on:** Step 1, Step 4.
**Verify:** `dotnet build`; manual pass in Step 11 (fresh scratch db → welcome overlay opens; relaunch
→ it doesn't).

## Step 7: `WelcomeTourOverlayViewModel` + `WelcomeTourOverlay` view (live spotlight)
**Files:** `src/Paperbunkr.App/ViewModels/WelcomeTourOverlayViewModel.cs` (new),
`src/Paperbunkr.App/Views/WelcomeTourOverlay.axaml` (new),
`src/Paperbunkr.App/Views/WelcomeTourOverlay.axaml.cs` (new)
**What:**
- `WelcomeTourOverlayViewModel` holds an ordered `IReadOnlyList<TourStep>` (new small record:
  `string TargetElementName, string Title, string Body, Action Navigate`), built in its constructor
  from the seven `MainViewModel` nav commands passed in (`GoHomeCommand`, `GoLibraryCommand`,
  `GoBooksCommand`, `GoSmartCommand`, `GoReadingCommand`, `GoEventsCommand`,
  `GoPreferencesCommand` — all already public `IRelayCommand`s on `MainViewModel`, confirmed from
  the nav rail's own bindings), matching the design's step table exactly:
  `HomeRailButton`/`LibraryRailButton`/`BooksRailButton`/`SmartListsRailButton`/
  `ReadingListsRailButton`/`EventsRailButton`/`PreferencesRailButton` (the last five/two new names
  land in Step 2).
  `[ObservableProperty] private int _currentStepIndex;` plus `TourStep CurrentStep => Steps[CurrentStepIndex];`,
  `bool IsFirstStep`/`IsLastStep`, `[RelayCommand] Next()` (invokes `CurrentStep.Navigate()` after
  incrementing, or calls the completion callback on last step), `Back()`, `Skip()` (both call the
  same completion callback immediately). Constructor takes `Action onFinished` (wired to
  `MainViewModel.CloseWelcomeTourOverlay` in Step 8). `Open()` resets `CurrentStepIndex = 0` and
  invokes `Steps[0].Navigate()` immediately, so the first stop's screen is already showing when the
  overlay becomes visible.
- `WelcomeTourOverlay.axaml.cs` (code-behind, not the ViewModel — bounds are a UI-tree concern): on
  `CurrentStepIndex` change (subscribe via `DataContextChanged`/`PropertyChanged` in the constructor,
  same pattern other code-behind-driven Avalonia controls in this codebase use) and on the control's
  own `SizeChanged`, resolve the current step's target via
  `this.FindAncestorOfType<MainWindow>()?.FindControl<Control>(step.TargetElementName)`, compute its
  bounds via `target.TranslatePoint(new Point(0,0), this)` + `target.Bounds.Size`, and set two bound
  properties the XAML reads: a `Rect CutoutBounds` and a callout anchor `Point`. If `FindControl`
  returns null (shouldn't happen — every target is a permanently-present rail button, per the
  design's error-handling note), skip silently to `Next()` rather than rendering a broken frame.
- `WelcomeTourOverlay.axaml`: a `Panel` filling the window, a `Path` whose `Data` is built from
  `CutoutBounds` using `CombinedGeometry`/`GeometryCombineMode.Exclude` (see the plan header's
  locked-in geometry approach — confirm exact Avalonia API via the `avalonia-docs` MCP server before
  writing this), `Fill="#B0000000"`, `IsHitTestVisible="True"` (blocks all interaction with the
  screen underneath — this is a guided display, not an interactive walkthrough, per the design), and
  a callout `Border` (same `floatingPanel`-adjacent styling as other overlay cards) positioned near
  `CutoutBounds`, showing `CurrentStep.Title`/`Body` and Next/Back/Skip `Button`s bound to the VM's
  commands (`IsVisible="{Binding !IsFirstStep}"` on Back, Next's `Content` reads "Finish" on
  `IsLastStep`).
**Depends on:** Step 2 (target names must exist), Step 4 (needs `MainViewModel`'s nav commands,
which already exist — no new dependency there, just consumed).
**Verify:** New `WelcomeTourOverlayViewModelTests.cs` (Step 10) covers step sequencing/bounds/command
invocation without touching Avalonia rendering; the actual cutout/callout visuals are manual-only
(Step 11), matching the design's own stated testing limit.

## Step 8: Wire `WelcomeTourOverlayViewModel` into `MainViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:**
- Construct `WelcomeTour = new WelcomeTourOverlayViewModel(GoHomeCommand, GoLibraryCommand, GoBooksCommand, GoSmartCommand, GoReadingCommand, GoEventsCommand, GoPreferencesCommand, CloseWelcomeTourOverlay)`
  alongside the other overlay VMs. `public WelcomeTourOverlayViewModel WelcomeTour { get; }`.
- `[ObservableProperty] private bool _isWelcomeTourOverlayOpen;`
- `private void OpenWelcomeTourOverlay() { WelcomeTour.Open(); IsWelcomeTourOverlayOpen = true; }`
  (called from `TakeTour()`, Step 4).
- `private void CloseWelcomeTourOverlay() => IsWelcomeTourOverlayOpen = false;`
- Add an `IsWelcomeTourOverlayOpen` branch to `Escape()`, calling `WelcomeTour.SkipCommand.Execute(null)`.
**Depends on:** Step 7.
**Verify:** `dotnet build`; `MainViewModelTests` extension (Step 10).

## Step 9: `MainWindow.axaml` markup for the tour overlay
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** One more block, appended last (topmost of all) in the outer `Grid`:
`<views:WelcomeTourOverlay DataContext="{Binding WelcomeTour}" IsVisible="{Binding IsWelcomeTourOverlayOpen}" />`
— unlike every other overlay here, this one is *not* wrapped in the shared `Border
Background="#B0000000"` backdrop pattern, since `WelcomeTourOverlay` draws its own scrim internally
(it needs a hole punched in it, which the shared full-opacity backdrop can't do).
**Depends on:** Step 7, Step 8.
**Verify:** `dotnet build`; manual pass in Step 11.

## Step 10: Tests
**Files:** `src/Paperbunkr.App.Tests/WelcomeOverlayViewModelTests.cs` (new),
`src/Paperbunkr.App.Tests/WelcomeTourOverlayViewModelTests.cs` (new),
`src/Paperbunkr.App.Tests/MainViewModelTests.cs` (edit)
**What:**
- `WelcomeOverlayViewModelTests` — same fixture shape as `NewReadingListViewModelTests`
  (`PaperbunkrDbContext.DatabasePathOverride` redirected to a temp SQLite file, `FakeFilePickerService`
  substitutable to return a canned path or null-for-cancel). Cases: `AddComicFolder` with a picked
  path inserts a `WatchedFolder` row and calls the close callback; cancelled picker (`null` path)
  does neither; duplicate path is not re-inserted (mirrors `PreferencesScreenViewModel`'s existing
  dedup check); `AddBookFolder` same shape against `BookFolders`; `ImportFromCe` invokes the passed
  callback and closes; `Skip` just closes; `CeInstallDetected` reflects the constructor/assignment
  value.
- `WelcomeTourOverlayViewModelTests` — pure VM logic, no Avalonia rendering needed: `Open()` resets to
  step 0 and invokes step 0's navigate action; `Next()`/`Back()` bounds-checked at first/last step and
  invoke the right step's navigate action; `Skip()` and `Next()`-past-last-step both invoke
  `onFinished`; all seven steps present in rail order.
- `MainViewModelTests` — extend with: `OpenWelcomeOverlayCommand` sets `IsWelcomeOverlayOpen` and
  forwards `CeInstallDetected`; `CloseWelcomeOverlay` persists `WelcomeScreenShown` and, only on its
  first-ever call, opens the tour offer and persists `WelcomeTourOffered`; a second `CloseWelcomeOverlay`
  call (simulating the overlay being reopened via the Preferences path someday, or defensively) does
  not re-open the tour offer; `TakeTour`/`DeclineTour` both close the offer, only `TakeTour` opens
  `WelcomeTour`; `Escape()` closes whichever of Welcome/TourOffer/WelcomeTour is open, matching the
  existing pattern's precedence style.
**Depends on:** Steps 3, 4, 7, 8.
**Verify:** `dotnet test src/Paperbunkr.App.Tests` (and `Paperbunkr.Data.Tests`/`Paperbunkr.App.Tests`
full suite to catch any regression from the `AppSettings` schema change).

## Step 11: Manual on-screen verification
**Files:** none (verification only)
**What:** Using the `run` skill against a scratch `%AppData%\Paperbunkr` profile (or a temp
`PaperbunkrDbContext.DatabasePathOverride`-style throwaway db, matching how this project already
avoids polluting the real dev db per the "worktrees share the per-user dev db" gotcha):
1. Fresh launch → `WelcomeOverlay` appears, no CE framing, three cards + Skip.
2. Add Comic Folder → OS picker → overlay closes → tour offer appears.
3. Decline tour → relaunch app → welcome overlay does *not* reappear (persistence confirmed).
4. Fresh scratch db again → Skip → tour offer still appears (confirms the "skip ≠ suppress tour"
   decision) → Take the tour → verify all seven stops highlight the correct rail button, screen
   behind changes to match, Next/Back/Skip/Finish all work, then relaunch → tour never reappears.
5. Fresh scratch db with a `%AppData%\cYo\ComicRack Community Edition\ComicDb.xml` present (or
   temporarily point `MigrationViewModel.GetDefaultCePath()`'s target) → welcome overlay's CE card
   shows the "Found the default CE install" badge; clicking it opens the existing `MigrationOverlay`
   unchanged.
**Depends on:** all prior steps.
**Verify:** Human confirmation of each bullet above — stated explicitly as the only real
verification for the live-spotlight visuals, matching this project's standing posture on desktop-
rendering specs with no unattended GUI automation available.
