# Startup pipeline — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md*

## Implementation status (2026-09-09)

All 13 code steps landed on `claude/library-health` (commits `65be377`, `562fe20`, `a337384`,
`a32d18b`, `81daa0e`, `885bf4c`). Notes on where implementation chose a shape the plan left open:

- **Step 6** — `FirstLookShell` built as the code-only `TemplatedControl` (Title/Subtitle/Footer),
  not the "convention" alternative, since Welcome and What's New share the header exactly.
- **Step 9** — the About "What's New" trigger uses a `PreferencesScreenViewModel.WhatsNewRequested`
  event (MainViewModel subscribes, same pattern as `ReaderDisplaySettingsChanged`) instead of a
  new ctor param — avoids touching every `PreferencesScreenViewModel` test's construction.
- **Step 5** — a bare `throw` from the now-async startup body would only hit the log-only
  unobserved-task handler, so a new `DiagnosticsService.ReportFatalStartupError` (strict
  no-Continue dialog + exit) is called instead, and the whole sequence is wrapped so any
  unexpected startup exception routes there too.
- **Step 8** — `Button.linkText` added to `Styles/Primitives.axaml` (shared by both first-look
  overlays) rather than a local per-view style.
- **Step 13** — **no change made.** `WelcomeTourOverlay` already builds on `Border.floatingPanel`
  + `Pb*` tokens; it's already consistent with `FirstLookShell`. Forcing an edit would be churn.

**Not yet verified (blocked / manual):** a clean `dotnet build` + full targeted test run + all
on-screen verification — a running `Paperbunkr.App` instance held the output DLLs locked during
implementation. Needs the app closed, then: build, `dotnet test --filter` the six suites below, and
launch the exe to click through splash → welcome/What's-New → the DB-recovery and reduced-motion
paths.

Three units in one spec: **Splash** (steps 2–6), **What's New** (steps 7–11), **Welcome redesign**
(steps 12–14). Steps 1 and 7 are the only real cross-unit dependencies. Everything is
CommunityToolkit.Mvvm + `.axaml`; overlays follow the existing `OverlayShell` host pattern in
`MainWindow.axaml`; migrations follow `AddNavRailHoverExpandEnabled` (no-op `Down`, `Migrate()`
round-trip test).

---

## Step 1: `AppSettings.LastRunVersion` + migration
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`src/Paperbunkr.Data/Migrations/*_AddLastRunVersion.cs` + `.Designer.cs` (new, via `dotnet ef`),
`src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs` (regen),
`src/Paperbunkr.Data.Tests/AddLastRunVersionMigrationTests.cs` (new)
**What:** add `public string? LastRunVersion { get; set; }` near `WelcomeScreenShown`, XML-doc per
the spec (four-part assembly version string; null until first written; written every launch). No
`OnModelCreating` config needed (nullable string, null is the sentinel). Scaffold the migration
(`dotnet ef migrations add AddLastRunVersion --project src/Paperbunkr.Data`), then hand-edit
`Down()` to a no-op with the standard orphan-column comment (copy from `AddNavRailHoverExpandEnabled`).
**Depends on:** none
**Verify:** `AddLastRunVersionMigrationTests` — `context.Database.Migrate()`, set the value, reopen,
assert it round-trips and defaults to `null`. `dotnet test --filter AddLastRunVersionMigrationTests`.
Do **not** re-scaffold once applied (standing rule).

## Step 2: `ReleaseVersion` service
**Files:** `src/Paperbunkr.App/Services/ReleaseVersion.cs` (new),
`src/Paperbunkr.App.Tests/ReleaseVersionTests.cs` (new)
**What:** pure static.
- `static Version Current` → `Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0,0,0,0)`.
- `static string DisplayString` → `$"{Current.Major}.{Current.Minor}.{Current.Build}-beta"` (the
  splash badge / "what's new in {version}" text — single release stream, matches CI's derivation).
- `static bool TryParseHeading(string headingVersion, out Version v)` → strip any `-suffix`,
  `Version.TryParse` the `x.y.z`, normalise to 3 components (`new Version(maj,min,build)`).
- `static bool IsNewerThan(Version a, Version b)` → compare `Major`/`Minor`/`Build` only.
**Depends on:** none
**Verify:** `ReleaseVersionTests` — `TryParseHeading` on `"0.3.0-beta"`, `"0.1.1-alpha"`, `"1.2"`,
`"garbage"`; `IsNewerThan` for greater / equal / downgrade / revision-only-differs. Targeted filter.

## Step 3: `SplashViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/SplashViewModel.cs` (new),
`src/Paperbunkr.App.Tests/SplashViewModelTests.cs` (new)
**What:** `ObservableObject`. Properties `double Progress` (0–1), `string StatusMessage`,
`string VersionText` (= `$"Paperbunkr {ReleaseVersion.DisplayString}"`). A `ReportPhase(int index,
int total, string message)` that sets `Progress = index/(double)total` + `StatusMessage`. The
400 ms floor lives here as `async Task EnforceMinimumVisibleAsync(DateTime shownAtUtc,
TimeSpan floor)` (or takes an injected delay func) so a test can assert it waits when work
finished early and doesn't when it didn't.
**Depends on:** Step 2
**Verify:** `SplashViewModelTests` — phase reporting sets Progress/StatusMessage; the floor helper
delays to the floor from an early finish, returns immediately past it. Targeted filter.

## Step 4: `SplashWindow` view
**Files:** `src/Paperbunkr.App/Views/SplashWindow.axaml` + `.axaml.cs` (new)
**What:** `Window` — `SystemDecorations="None"`, `CanResize="False"`, `Topmost="True"`,
`ShowInTaskbar="False"`, `WindowStartupLocation="CenterScreen"`, `Width="480" Height="340"`,
`Background="#0A0B0D"` (literal, not a skin resource — renders before skin apply). Content: a
`Grid` centring a `Panel` (glow `Border` with a static `RadialGradientBrush` fill behind an
`Image Source="avares://Paperbunkr.App/Assets/paperbunkr-logo-source.png"`), a 240-wide 3px
`ProgressBar` (`Minimum=0 Maximum=1`, accent `LinearGradientBrush` fill) bound to `Progress`, a
status `TextBlock` bound to `StatusMessage`, and a bottom-right `TextBlock` bound to `VersionText`.
Motion (a `Styles` block on the window):
- emblem entrance: `Style` on load with a `TransformOperationsTransition` + opacity, or an
  `Animation` on first show — fade + scale 0.9→1.0, `{DynamicResource PbMotionStandard}`,
  `CubicEaseOut`.
- breathing: `<Style Selector="Image.pbLogo">` with `<Style.Animations><Animation
  IterationCount="INFINITE" Duration="0:0:2.4">` keyframing `ScaleTransform` 1.0↔1.035; a parallel
  `Animation` on the glow `Border` keyframing `Opacity` 0.35↔0.7. **Never** animate `BoxShadow`.
- `.axaml.cs`: a `public async Task FadeOutAndCloseAsync()` — set `Opacity = 0` with a 150 ms
  transition (`PbMotionFast`, `CubicEaseIn`), `await Task.Delay(150)`, `Close()`.
- Reduced motion: read `SkinService.GetReducedMotion()` in the ctor; if true, add a
  `reducedMotion` class to the root that a `Style` uses to null out the breathing animation and
  hold the glow at 0.5 opacity.
**Depends on:** Step 3
**Verify:** manual/on-screen (headless can't render a borderless top-level). `dotnet build`.
AVLN2000 guard: add the `.axaml.cs` in the same commit as the `.axaml` (new `x:Class`).

## Step 5: `App.axaml.cs` startup restructure
**Files:** `src/Paperbunkr.App/App.axaml.cs` (edit)
**What:** the risky one — restructure `OnFrameworkInitializationCompleted` into splash-first +
async init, preserving every existing ordering guarantee and the DB-recovery / crash paths.
- Make the method `async void` (Avalonia supports this on the override; the classic-desktop
  lifetime does not await it, and `ShutdownMode.OnLastWindowClose` keeps the app alive while only
  the splash window is open).
- New order: create + `Show()` `SplashWindow` (DataContext a `SplashViewModel`) → run the DB block
  (`DatabaseIntegrityService.CheckIntegrity` → `HandleDatabaseRecovery` unchanged if it fails →
  `HasAnySeries` → `EnsureCreated`) inside `await Task.Run(...)` with `splashVm.ReportPhase` calls,
  keeping the `LogCrash`+rethrow on migration failure → back on the UI thread: `ceInstallDetected`,
  `CoverAspectRatioStore.ContextFactory`, the three fire-and-forget `Task.Run`s (unchanged),
  `SkinService().ApplyPersistedSettings()`, graphics.json sync → build `NavigationTransitionCoordinator`
  + `MainViewModel` + `MainWindow`, set `desktop.MainWindow`, `mainWindow.Show()` →
  `await splashVm.EnforceMinimumVisibleAsync(...)` then `await splash.FadeOutAndCloseAsync()` →
  the post-window block unchanged (`RestoreLastScreen`/deep-link, welcome-vs-update sequencing —
  **replaced by Step 9's call**, `FreezeWatchdogService`, `PluginHostService` + all `AttachHost`,
  `Scheduler.Start`, `desktop.Exit` handlers, "Startup complete.").
- `PluginHostService.Initialize` etc. still run **after** `MainWindow` exists (unchanged).
- Move the `DiagnosticsService.LogMilestone` strings into the phase reports so the splash text and
  `startup.log` stay in sync.
**Depends on:** Steps 3, 4 (and Step 9 for the sequencing call — land 9 first or stub the call)
**Verify:** manual — cold start shows the splash, phases advance, main window appears, splash
fades; a forced integrity failure still shows `DatabaseRecoveryWindow` (no splash deadlock); a
thrown migration still hits the crash reporter. `dotnet build` + launch the exe (grep `startup.log`
for the phase milestones and "Startup complete.").

## Step 6: shared `FirstLookShell`
**Files:** `src/Paperbunkr.App/Styles/Overlays.axaml` (edit) or a new
`src/Paperbunkr.App/Styles/FirstLook.axaml` (+ register in `App.axaml`); optionally
`src/Paperbunkr.App/Controls/FirstLookShell.cs` (new)
**What:** a reusable card chrome for Welcome + What's New: brand header band (emblem + title +
subtitle on a subtle `#0A0B0D`→`PbSurface2` vertical gradient), content slot, footer slot
(left link + right action). Either a code-only `TemplatedControl` with `Title`/`Subtitle`/`Footer`
properties (the `OverlayShell`/`BrandMark` pattern) or — simpler and matches `WelcomeOverlay`
today — a documented `Border.floatingPanel` + `Grid` layout convention both views copy. Pick the
control if the two headers would otherwise diverge; the convention if they stay identical.
**Depends on:** none
**Verify:** consumed by Steps 8 and 12; `dotnet build`. Run `avalonia-pro-max/review-checklist`
against the new style before calling it done (hardcoded hex vs skin tokens — the header's `#0A0B0D`
is deliberate and matches the splash; everything else uses `Pb*` brushes).

## Step 7: `WhatsNewOverlayViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/WhatsNewOverlayViewModel.cs` (new),
`src/Paperbunkr.App.Tests/WhatsNewOverlayViewModelTests.cs` (new)
**What:** mirrors `UpdateAvailableOverlayViewModel`'s shape (small VM, `_requestClose` action).
- `IReadOnlyList<ChangelogEntry> Entries`, `bool CurrentEntryOnly`, `string HeaderText`
  (`"Updated to Paperbunkr {DisplayString}"` for the multi case, `"What's new in {DisplayString}"`
  for current-only).
- `void Show(IReadOnlyList<ChangelogEntry> entries, bool currentOnly)`.
- `GotItCommand` / `OpenFullChangelogCommand` (the latter = close + a `_goAbout` callback) →
  `_requestClose`.
- A static `SelectEntriesSince(IReadOnlyList<ChangelogEntry> all, string? lastRunVersion)` →
  entries strictly newer than `lastRunVersion` per `ReleaseVersion`, in file order (already
  newest-first); returns all when `lastRunVersion` is null? **No** — null means fresh install,
  caller handles that separately (fresh install shows nothing, just writes the marker). Empty when
  none newer.
**Depends on:** Step 2
**Verify:** `WhatsNewOverlayViewModelTests` — `SelectEntriesSince` with a fixed entry list +
various `lastRunVersion` (one newer, several newer, none newer, equal); `Show(currentOnly:true)`
→ one entry, right header. Targeted filter.

## Step 8: `WhatsNewOverlay` view
**Files:** `src/Paperbunkr.App/Views/WhatsNewOverlay.axaml` + `.axaml.cs` (new)
**What:** on the `FirstLookShell` (Step 6). Header = emblem + `HeaderText` + subtitle. Body =
`ItemsControl` over `Entries`; per entry an `Expander`/`ToggleButton` (mirror `AboutSection.axaml`'s
`changelogExpandHeader` + `ChangelogBodyToGroupsConverter` + `ChangelogBodyGroup` DataTemplate —
reuse those exact styles/converters, don't reinvent). First item expanded, rest collapsed
(`IsChecked` bound to index==0, or a `FirstOrDefault` check). In `CurrentEntryOnly` mode the
`ItemsControl` has one item, always expanded, no toggle chrome. Footer: "Full changelog in
Preferences → About" link (left) + "Got it" primary button (right).
**Depends on:** Steps 6, 7
**Verify:** manual/on-screen. `dotnet build`. Same-commit `.axaml`+`.axaml.cs` (AVLN2000).

## Step 9: `MainViewModel` — What's New wiring + startup sequencing
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:**
- `WhatsNewOverlayViewModel WhatsNew { get; }` constructed near `Welcome`/`Update`
  (`new WhatsNewOverlayViewModel(CloseWhatsNewOverlay, GoAboutFromWhatsNew)`).
- `[ObservableProperty] bool _isWhatsNewOverlayOpen;` + `[RelayCommand] void CloseWhatsNewOverlay()`.
- Add `IsWhatsNewOverlayOpen` to `IsEditorOverlayOpen` and to the `Escape()` chain (→ `GotItCommand`).
- `public async Task ShowWhatsNewOrCheckForUpdatesAsync()` — the sequencing entry point called from
  `App.axaml.cs` on the **not-first-run** branch (replaces the bare `Task.Run(CheckForUpdatesOnStartupAsync)`):
  read `LastRunVersion`; if non-null and `ReleaseVersion.IsNewerThan(Current, parsed)` →
  load+parse `CHANGELOG.md` (reuse the `LoadNewestChangelogBody` path → generalise to
  `LoadChangelogEntries()`), `WhatsNew.Show(SelectEntriesSince(...), currentOnly:false)` +
  `IsWhatsNewOverlayOpen = true` (via `Dispatcher.UIThread.Post`, like the update check's tail);
  else → `await CheckForUpdatesOnStartupAsync()`. **Always** write `LastRunVersion = Current.ToString()`
  in a `finally` (covers the fresh-install and no-change branches too — App.axaml.cs calls a small
  `PersistLastRunVersion()` on the first-run branch as well).
- `void OpenWhatsNewOverlayCurrentOnly()` — for the Welcome link + the About button: load entries,
  `WhatsNew.Show(currentOnly:true)`, open.
- `GoAboutFromWhatsNew()` → `GoPreferencesCommand.Execute(null); Preferences.GoAboutCommand.Execute(null)`
  (check the exact About-nav command name in `PreferencesScreenViewModel`).
**Depends on:** Steps 2, 7
**Verify:** a unit test for the sequencing decision (extract a `static WhatsNewDecision Decide(bool
firstRun, string? lastRunVersion, Version current)` returning `Welcome`/`WhatsNew`/`UpdateCheck`)
covering all branches incl. fresh-install-writes-marker. `dotnet build`. Targeted filter.

## Step 10: host the overlay in `MainWindow.axaml`
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** add, next to the `WelcomeOverlay`/`UpdateAvailableOverlay` shells (~line 1043):
```xml
<controls:OverlayShell IsOpen="{Binding IsWhatsNewOverlayOpen}" CloseCommand="{Binding WhatsNew.GotItCommand}"
                        CloseButtonAutomationId="WhatsNewOverlayCloseButton">
    <views:WhatsNewOverlay DataContext="{Binding WhatsNew}" />
</controls:OverlayShell>
```
**Depends on:** Steps 8, 9
**Verify:** manual/on-screen; `dotnet build`.

## Step 11: About "What's New" button
**Files:** `src/Paperbunkr.App/Views/Preferences/AboutSection.axaml` (edit),
`src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit, both ctors),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — pass the callback)
**What:** add `Action openWhatsNew` to `PreferencesScreenViewModel`'s public + internal ctors
(thread through the `: this(...)` chain), expose `[RelayCommand] void OpenWhatsNew() => _openWhatsNew()`.
In `AboutSection.axaml`, a "What's New" `Button Classes="headerAction ghost"` next to the
`CurrentVersion` display (line ~75). In `MainViewModel` line 243, pass `OpenWhatsNewOverlayCurrentOnly`.
**Depends on:** Step 9
**Verify:** manual; `dotnet build`. The existing `PreferencesScreenViewModelTests` construct via the
internal ctor — update those call sites (add the new arg).

## Step 12: `WelcomeOverlay` restyle + CE de-emphasis + link
**Files:** `src/Paperbunkr.App/Views/WelcomeOverlay.axaml` (edit),
`src/Paperbunkr.App/ViewModels/WelcomeOverlayViewModel.cs` (edit)
**What:** re-lay the overlay onto `FirstLookShell` (Step 6) — brand header (emblem + "Welcome to
Paperbunkr" + subtitle), the three `setupCard` buttons in the content slot, footer = "What's new
in {version} →" link (left) + "Skip for now" (right, moved from its current centered position).
CE card: when `CeInstallDetected` is false, render it last with a `muted` class (lower opacity, no
accent hover); when true, keep it inline + badged. Add `[RelayCommand] void ShowWhatsNew() =>
_showWhatsNew()` to the VM; add `Action showWhatsNew` to its ctor.
**Depends on:** Steps 6, 9
**Verify:** `WelcomeOverlayViewModelTests` — add a `ShowWhatsNew` invokes-callback test; existing
tests still pass (ctor arg added — update the one construction site in the test + line 163 of
MainViewModel). Manual for the layout. Targeted filter.

## Step 13: `WelcomeTourOverlay` palette restyle
**Files:** `src/Paperbunkr.App/Views/WelcomeTourOverlay.axaml` (edit)
**What:** light touch — align the callout bubble's surface/border/text to the same `Pb*` tokens
and corner radius as `FirstLookShell` so the tour that fires right after the welcome screen looks
of a piece. No VM or behaviour change.
**Depends on:** Step 6 (for the tokens/radius to match)
**Verify:** manual/on-screen; `dotnet build`.

## Step 14: design-doc + roadmap upkeep
**Files:** `docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md` (edit if any
call changed during impl), `docs/Paperbunkr-Roadmap.md` (add a "shipped" line post-merge, like
prior features)
**Depends on:** all
**Verify:** n/a.

---

## Test strategy summary

- **Automated (targeted `dotnet test --filter` only — full App.Tests suite flakes under headless
  concurrent load):**
  - `AddLastRunVersionMigrationTests` — `Migrate()` round-trip + null default (Step 1).
  - `ReleaseVersionTests` — parse/compare edge cases (Step 2).
  - `SplashViewModelTests` — phase progress + 400 ms floor via an injected delay seam (Step 3).
  - `WhatsNewOverlayViewModelTests` — `SelectEntriesSince` selection + header/current-only (Step 7).
  - `MainViewModel` sequencing — a pure `Decide(firstRun, lastRunVersion, current)` covering
    Welcome / WhatsNew / UpdateCheck incl. fresh-install-writes-marker (Step 9).
  - `WelcomeOverlayViewModelTests` — new `ShowWhatsNew` callback test; keep existing green (Step 12).
- **Build gate:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` after every step; new
  `.axaml` always lands with its `.axaml.cs` in the same commit (AVLN2000 gotcha).
- **Manual / on-screen (unavoidable):** the splash window render + animation + 400 ms feel + fade
  handoff; the DB-recovery-failure path still working with the splash; reduced-motion; the Welcome
  and What's New overlay layouts; the tour restyle; the full launch sequencing on a real
  fresh-install DB vs. a version-bumped DB vs. an unchanged launch. Run the built exe; check
  `%AppData%\Paperbunkr\logs\startup.log` for the phase milestones.
- `avalonia-pro-max/review-checklist` before calling the splash + shell + overlay XAML done.
