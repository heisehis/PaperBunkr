# Auto-Update and Changelog — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md*

**Revision note (2026-09-01, same day):** steps below were written and executed against Velopack.
Mid-session, a real crash plus user-surfaced evidence of rough Velopack installer UX prompted a
proper library survey and a switch to NetSparkleUpdater.SparkleUpdater instead — see the design
doc's own revision note for the full rationale. Concretely: Step 13 ("Retire the Inno Setup
installer") did NOT happen — the installer stays, NetSparkle just runs it. Steps 5/9/10/11/14 (the
update engine itself, both overlay/toast VMs, the download flow, and the CI workflow) were built
against Velopack's API first, then rewritten against NetSparkle's - the UI/UX shape those steps
describe (ask-before-download, overlay+toast, ChangelogParser reuse) stayed the same; only the
underlying engine's API calls changed. This file is kept as the execution record, not re-edited
step-by-step to match - treat the design doc's revision note as the source of truth for what
actually shipped.

Entry point for the update/changelog UI is a new **About** section in the existing Preferences
screen (`PreferencesSection` enum + sidebar, `PreferencesScreenViewModel`) — the design doc left this
open pending a survey of the current navigation surface; there is no menu bar/Help entry anywhere in
the app, every other informational/settings surface already lives in Preferences, so this is the
natural fit rather than inventing a new nav pattern.

The update-ready toast reuses the app's existing `WindowNotificationManager`-based toast host
(`MainWindow.axaml.cs:298-317`, the same mechanism `ToastProgressView` uses) via a new event pair on
`MainViewModel`, not a new host mechanism.

## Step 1: Package reference, version, changelog copy-to-output
**Files:** `src/Paperbunkr.App/Paperbunkr.App.csproj` (edit)
**What:** Add `<PackageReference Include="Velopack" Version="..." />` (latest stable at implementation
time) to the existing `PackageReference` `ItemGroup` (`csproj:29-51`). Add `<Version>0.2.0.0</Version>`
to the main `PropertyGroup` (`csproj:1-10`ish, alongside `TargetFramework`/`OutputType`). Add
`<None Include="../../CHANGELOG.md" CopyToOutputDirectory="PreserveNewest" />` to the existing
`Content`/`None` `ItemGroup` near the `7z.dll` entry (`csproj:26-28`).
**Depends on:** none
**Verify:** `dotnet restore` / `dotnet build` succeeds with the new package resolved.

## Step 2: CHANGELOG.md
**Files:** `CHANGELOG.md` (new, repo root)
**What:** Keep a Changelog format. First entry documents this feature itself:
```markdown
# Changelog

## [0.2.0-beta] - 2026-09-01
### Added
- Auto-update via Velopack, checking GitHub Releases on startup and on demand.
- In-app changelog, viewable from Preferences → About.
```
**Depends on:** none
**Verify:** none (content file) — Step 12's `ChangelogParserTests` parses this exact file.

## Step 3: `AppSettings.CheckForUpdatesOnStartup` + migration
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), new EF Core migration under
`src/Paperbunkr.Data/Migrations/`
**What:** Add `public bool CheckForUpdatesOnStartup { get; set; } = true;` following the existing
property/doc-comment style in the file (e.g. `ReducedMotion`, `OpenLastPage`). Run
`dotnet ef migrations add AddCheckForUpdatesOnStartup --project src/Paperbunkr.Data` (matching the
naming convention of `AddLibrarySearchSuggestions`, `AddWelcomeOnboardingFlags`).
**Depends on:** none
**Verify:** Migration applies cleanly against a throwaway db; `Paperbunkr.Data` test suite (if any
exercises `AppSettings` round-trips) still passes.

## Step 4: `ChangelogParser`
**Files:** `src/Paperbunkr.App/Services/ChangelogParser.cs` (new)
**What:** Parses `CHANGELOG.md` text into `IReadOnlyList<ChangelogEntry>`, where
`record ChangelogEntry(string Version, string? Date, string Body)` — one entry per `## [version] -
date` heading, `Body` is the raw markdown between that heading and the next `##` (or end of file).
Static method, e.g. `ChangelogParser.Parse(string markdown)`, so it needs no DI/service wiring.
Shared by Step 8 (About section, renders all entries) and Step 9 (update-available overlay, renders
just the newest entry's `Body`).
**Depends on:** none
**Verify:** New `ChangelogParserTests.cs` in `Paperbunkr.App.Tests` — multi-entry parse, single-entry
parse, entry-not-found, trailing/leading whitespace handling. Use an inline string fixture, not a
read of the real `CHANGELOG.md` (keeps the test independent of that file's future edits).

## Step 5: `UpdateService`
**Files:** `src/Paperbunkr.App/Services/UpdateService.cs` (new)
**What:**
```csharp
public class UpdateService
{
    private readonly UpdateManager _manager =
        new(new GithubSource("https://github.com/heisehis/PaperBunkr", null, false));

    public bool IsInstalled => _manager.IsInstalled;
    public Task<UpdateInfo?> CheckForUpdatesAsync() => _manager.CheckForUpdatesAsync();
    public Task DownloadUpdatesAsync(UpdateInfo info, Action<int>? onProgress = null) =>
        _manager.DownloadUpdatesAsync(info, onProgress);
    public void ApplyUpdatesAndRestart(UpdateInfo info) => _manager.ApplyUpdatesAndRestart(info);
}
```
Every call site (Steps 9, 11) must check `IsInstalled` first — `UpdateManager` throws when not
running from a Velopack-managed install (i.e. `dotnet run`/IDE debugging).
**Depends on:** Step 1 (package reference)
**Verify:** New `UpdateServiceTests.cs` — construction doesn't throw, `IsInstalled` is `false` under
the test runner (not a Velopack install). Real check/download/apply against the network is explicitly
**not** unit-tested (per the design doc's Testing section) — noted as a comment in the test file
pointing at the manual verification step (Step 14).

## Step 6: Wire `VelopackApp.Build().Run()`
**Files:** `src/Paperbunkr.App/Program.cs` (edit)
**What:** Add `Velopack.VelopackApp.Build().Run();` as the literal first statement in `Main`, before
`DiagnosticsService.Install()` (`Program.cs:20`).
**Depends on:** Step 1
**Verify:** `dotnet build` + manual launch (`dotnet run`) still starts the app normally — Velopack
no-ops gracefully outside a managed install.

## Step 7: `PreferencesSection.About`
**Files:** `src/Paperbunkr.App/Models/PreferencesSection.cs` (edit),
`src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)
**What:** Add `About` to the `PreferencesSection` enum (`PreferencesSection.cs:11-21`) — last position,
so it lands at the bottom of the sidebar via the existing `Enum.GetValues`-derived `Order`. Add
`"About"` is already what `ToString()` gives, so no `Label()` override needed. In
`PreferencesScreenViewModel`, mirror the existing `Is*Section`/`Go*` pattern
(`PreferencesScreenViewModel.cs:188-195`, `403-437`): add `IsAboutSection`, `GoAboutCommand`, and the
`OnPropertyChanged(nameof(IsAboutSection))` line in `OnActiveSectionChanged`. Add new observable state
for this section: `CurrentVersion` (from `Assembly.GetExecutingAssembly().GetName().Version`),
`ChangelogEntries` (from `ChangelogParser.Parse` against the bundled `CHANGELOG.md`, loaded once),
`UpdateCheckResultText` (string, set after a manual check), and a `CheckForUpdatesOnStartup` bool
property bound to `AppSettings.CheckForUpdatesOnStartup` (same load/save pattern the other toggles on
this ViewModel already use). Add a `CheckForUpdatesCommand` that calls `UpdateService
.CheckForUpdatesAsync()` and always sets `UpdateCheckResultText` (hit or "up to date"), matching CE's
`alwaysCheck` branch.
**Depends on:** Step 3 (setting), Step 4 (parser), Step 5 (service)
**Verify:** `PreferencesScreenViewModelTests.cs` — new tests for `CheckForUpdatesOnStartup` load/save
round-trip and `ChangelogEntries` populated from a test fixture.

## Step 8: About section UI
**Files:** `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit)
**What:** Add the About section's content panel alongside the existing per-section panels (find the
existing `IsXSection`-gated panel pattern in this file and mirror it). Contents: current version
label, "Check for Updates" button + result text, `CheckForUpdatesOnStartup` toggle (reuse whatever
toggle control the other Preferences boolean settings use), and a scrollable list of
`ChangelogEntries` — newest expanded, older entries in a collapsed/scrollable list. Add the sidebar
entry for `PreferencesSection.About` if the sidebar is a hand-written list rather than fully derived
from `PreferencesSectionMeta.Order` (check `PreferencesScreen.axaml`'s sidebar markup to confirm which
it is before assuming).
**Depends on:** Step 7
**Verify:** Manual on-screen check (per `avalonia-pro-max/review-checklist` — load the `avalonia`
skill before writing this XAML, per `CLAUDE.md`) — navigate to Preferences → About, confirm version,
changelog rendering, and the toggle persist correctly.

## Step 9: `UpdateAvailableOverlay`
**Files:** `src/Paperbunkr.App/Views/UpdateAvailableOverlay.axaml` (new),
`src/Paperbunkr.App/Views/UpdateAvailableOverlay.axaml.cs` (new — **must be added in the same step as
the `.axaml`**, per the `AVLN2000`/`CompileAvaloniaXaml` build gotcha in `CLAUDE.md`),
`src/Paperbunkr.App/ViewModels/UpdateAvailableOverlayViewModel.cs` (new),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** Mirror `WelcomeOverlay`'s shape (simple informational overlay, not a data-editing form):
new version number, the newest `ChangelogEntry.Body` (via `ChangelogParser`), and three actions —
Download, Not now, and a "Don't check for updates on startup" checkbox wired to persist
`AppSettings.CheckForUpdatesOnStartup = false` immediately on check (matching CE's
`MainForm.cs:4529-4544` dialog shape). On Download: calls into `MainViewModel`'s download flow
(Step 11), closes itself. In `MainViewModel`, add `UpdateAvailableOverlayViewModel Update { get; }`
+ `IsUpdateAvailableOverlayOpen` bool, mirroring `Welcome`/`IsWelcomeOverlayOpen`
(`MainViewModel.cs:86, 199, 214, 553-565`). Add the overlay's conditional presentation in
`MainWindow.axaml` alongside where `WelcomeOverlay` is hosted.
**Depends on:** Step 3, Step 4, Step 5, Step 7 (for the persisted-toggle write path)
**Verify:** Manual on-screen check — cannot be triggered without a real newer GitHub release to
compare against (or a temporary manual `UpdateInfo` stub during development); note this explicitly
rather than claiming test coverage that doesn't exist.

## Step 10: Update-ready toast
**Files:** `src/Paperbunkr.App/ViewModels/UpdateReadyToastViewModel.cs` (new),
`src/Paperbunkr.App/Views/UpdateReadyToastView.axaml` (new),
`src/Paperbunkr.App/Views/UpdateReadyToastView.axaml.cs` (new, same-step requirement as Step 9),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit), `src/Paperbunkr.App/Views/MainWindow.axaml.cs`
(edit)
**What:** `UpdateReadyToastViewModel` — title text + `RestartNowCommand`, `LaterCommand`,
`WhatsNewCommand` (opens Preferences → About, per Step 7/8). No progress bar — this is `ToastProgress
ViewModel`'s sibling, not a reuse of it (the design doc calls out that stretching the existing
determinate-progress VM to also carry action buttons doesn't fit). Add
`UpdateReadyToastRequested`/`UpdateReadyToastCloseRequested` events on `MainViewModel`, mirroring
`ProgressToastRequested`/`ProgressToastCloseRequested` (`MainViewModel.cs:169-175`). Wire them in
`MainWindow.axaml.cs` mirroring the `ProgressToastRequested` subscription block
(`MainWindow.axaml.cs:305-317`) — same `_notificationManager.Show(view, ..., expiration:
TimeSpan.Zero)` pattern, new `UpdateReadyToastView` instead of `ToastProgressView`.
**Depends on:** none directly, but only meaningfully invoked once Step 11 exists
**Verify:** Manual on-screen check, same caveat as Step 9 — needs a real or stubbed update-ready
state to trigger.

## Step 11: Startup check + download flow wiring
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:** Locate the existing post-window-shown startup sequence in `MainViewModel` (where
`OpenWelcomeOverlay`/similar first-run checks already fire) and add: if `UpdateService.IsInstalled &&
AppSettings.CheckForUpdatesOnStartup`, fire-and-forget call `CheckForUpdatesAsync()`; on a non-null
result, open `UpdateAvailableOverlay` (Step 9) with the new version's changelog entry. Wire the
overlay's Download action to: reuse `ToastProgressViewModel` for the download progress (its
`Done`/`Total` fields fit an 0-100 percent callback cleanly — `Total = 100`, `Done` updated from
`UpdateService.DownloadUpdatesAsync(info, pct => toastVm.Done = pct)` — this is a legitimate reuse,
unlike the ready-state toast in Step 10) shown via the existing `ShowProgressToast`, then on
completion `CloseProgressToast` and fire the Step 10 ready-toast. `RestartNowCommand` on the ready
toast calls `UpdateService.ApplyUpdatesAndRestart(info)` directly — never automatic.
**Depends on:** Steps 3, 5, 9, 10
**Verify:** Manual on-screen check (same network-dependent caveat). Unit-testable slice: the
`IsInstalled && CheckForUpdatesOnStartup` gating logic, if it's factored as a small testable
predicate rather than inlined — add a test for it in `MainViewModelTests.cs` if so.

## Step 12: Test cleanup pass
**Files:** `src/Paperbunkr.App.Tests/ChangelogParserTests.cs` (new, written in Step 4),
`src/Paperbunkr.App.Tests/UpdateServiceTests.cs` (new, written in Step 5),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit, written in Step 7)
**What:** Confirms all tests from Steps 4/5/7 are in place and the full suite is green. No new test
files beyond what those steps already specify — this step is the checkpoint, not new work.
**Depends on:** Steps 4, 5, 7
**Verify:** `dotnet test src/Paperbunkr.App.Tests` full run, green.

## Step 13: Retire the Inno Setup installer
**Files:** `installer/` (delete entirely — `Installer.iss`, `BuildInstaller.ps1`, `Assets/`),
`.gitignore` (edit, remove the `installer/publish/` and `installer/Output/` lines — `.gitignore:8-9`)
**What:** Delete the directory. Before deleting, grep the rest of the repo (docs included) for
references to `installer/` paths so nothing is left dangling — `docs/alpha-todo.md` almost certainly
references it (per the earlier survey) and needs updating, not just left with dead links (see
Step 15).
**Depends on:** none (can run any time; ordered last so the old installer stays available while the
rest of this lands, in case of a mid-implementation need to fall back)
**Verify:** `git status` shows the deletion cleanly; no remaining file in the repo references a path
under `installer/` except as historical prose in `docs/alpha-todo.md`.

## Step 14: Release workflow
**Files:** `.github/workflows/release.yml` (new)
**What:** Exactly the pipeline described in the design doc's "Release pipeline (CI)" section: tag
trigger on `v*-beta`, version-derivation + tag-match guard, changelog-section extraction (fail if
missing) reading `CHANGELOG.md` from Step 2's format, `dotnet publish` self-contained win-x64, `vpk
download github` → `vpk pack --releaseNotes <extracted section>` → `vpk upload github --publish`, no
`--pre`. `permissions: contents: write`.
**Depends on:** Step 1 (version location), Step 2 (changelog format), Step 13 (nothing left assuming
the old installer build step exists)
**Verify:** Cannot be verified without pushing a real tag — this is the manual verification step
named explicitly in the design doc's Testing section. Push `v0.2.0-beta` once Steps 1-13 are merged,
confirm the GitHub Release appears with the right notes and `.exe`/`Setup.exe` asset, then confirm a
build from *before* this tag successfully detects, downloads, and applies the update via the flow
built in Steps 9-11.

## Step 15: Roadmap doc note
**Files:** `docs/alpha-todo.md` (edit) and/or `docs/Paperbunkr-Roadmap.md` (edit)
**What:** Short note (with commit ref, added once the actual commit exists) that installer packaging
moved from Inno Setup to Velopack, and that auto-update + changelog shipped — per `CLAUDE.md`'s own
"update `docs/alpha-todo.md` by hand" rule for roadmap-relevant work. Also fix/remove whatever
`installer/`-path references Step 13 found.
**Depends on:** Step 13, and ideally done after a real commit exists to cite
**Verify:** none (doc-only)

---

**Suggested execution order:** 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12, then 13 → 14 → 15.
Steps 1-6 have no UI and can be done as one tight batch; 7-11 build the actual user-facing surface in
dependency order; 12 is a checkpoint; 13-15 are the cleanup/release tail that only make sense once
the feature itself works.
