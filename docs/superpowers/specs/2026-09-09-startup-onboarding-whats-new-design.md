# Startup pipeline — Splash + Welcome redesign + "What's New"

**Date:** 2026-09-09
**Status:** approved, pending write-up into an implementation plan
**Scope:** `src/Paperbunkr.App/App.axaml.cs`; new `Views/SplashWindow.axaml(.cs)` +
`ViewModels/SplashViewModel.cs`; new `Views/WhatsNewOverlay.axaml(.cs)` +
`ViewModels/WhatsNewOverlayViewModel.cs`; new `Services/ReleaseVersion.cs`; a shared "first-look"
shell style; restyle of `Views/WelcomeOverlay.axaml` + `ViewModels/WelcomeOverlayViewModel.cs`;
`ViewModels/MainViewModel.cs` (sequencing + wiring); `Views/AboutSection*` (manual-open button);
`src/Paperbunkr.Data/Entities/AppSettings.cs` + one migration; `Views/MainWindow.axaml` (host the
new overlay).

## Why

Three related gaps in the launch-to-Home experience, folded into one spec at the user's direction
(the alternative — three separate specs — was floated and rejected):

1. **No splash.** `App.axaml.cs`'s `OnFrameworkInitializationCompleted` runs the entire startup
   sequence synchronously — DB integrity check, migrations (`HasAnySeries`/`EnsureCreated`), skin
   apply, `MainWindow` construction, plugin discovery/precompile, scheduler startup pass — and the
   window only appears once all of it finishes. On a large library (multi-GB SQLite, a real
   `PRAGMA integrity_check`, a pending migration) that is several seconds of nothing.
2. **The first-run welcome needs a refresh.** `WelcomeOverlay` (three setup cards + Skip, from
   `docs/superpowers/specs/2026-08-31-first-run-onboarding-design.md`) works but predates the
   2026-09 brand + chrome + motion work and doesn't visually connect to anything.
3. **No "what's new" after an update.** `docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md`
   surfaces the changelog in three places — the pre-download `UpdateAvailableOverlay` teaser, the
   Preferences → About accordion, and the update-ready toast's "view changelog" link — but there
   is no post-restart "here's what you just got" moment. The 2026-09-09 installer redesign
   (`2026-09-09-installer-redesign-design.md`) removed the installer's own changelog page on the
   explicit premise that release notes move into the app. This closes that loop.

An external spec (Gemini-authored, pasted into the design conversation) proposed a splash + a
3-step onboarding *wizard* (library → reader direction → theme). Reviewed against the codebase and
mostly not adopted: it assumed `appsettings.json` (PaperBunkr uses a SQLite `AppSettings` entity),
`v1.0.0` (the app is `0.3.0-beta`), a 3-way theme toggle (the app has a skin system —
`AppSettings.ActiveSkinKey`, four built-ins + custom), and a global reader-direction pick (it's
per-series/per-issue with an app default). The splash concept, the 400 ms anti-flash floor, and
the "transform/opacity, not layout bounds" motion guidance were taken; the wizard was not (see
Decision 4). It also omitted the "what's new after update" requirement entirely, which was the
user's actual original ask.

Grounded against: `avalonia-app-development` and `avalonia-graphics-animation` /
`avalonia-pro-max/motion` subskills (splash is DIY on desktop — no built-in `SplashScreen`;
`Animation` `IterationCount="INFINITE"` is all-caps; `BoxShadow` only on `Border`), and the
`project_paperbunkr_poster_glow_transition_bug` memory (animating a `BoxShadow` property caused a
real invalidation race — the glow here pulses `Opacity`, never the shadow).

## Decisions

### 1. One combined spec, three isolated units

Per the user's call. The three units share exactly one thing — the visual shell (Decision 3) — and
are otherwise independent: the splash knows nothing about onboarding; What's New knows nothing
about the splash. Each is separately testable.

### 2. Splash is a separate borderless `Window`, step-based determinate progress

**Separate window, not a full-window overlay in `MainWindow`.** `MainWindow` + `MainViewModel`
construction is itself part of the slow path, and `desktop.MainWindow` auto-shows once
`OnFrameworkInitializationCompleted` returns — a splash overlay inside it would fight that. A
standalone `SplashWindow` shown first, closed when the main window is ready, is the clean Avalonia
desktop pattern (there is no built-in `SplashScreen` for `IClassicDesktopStyleApplicationLifetime`).

**`SplashWindow.axaml`:** `SystemDecorations="None"`, `CanResize="False"`, `Topmost="True"`,
`WindowStartupLocation="CenterScreen"`, `TransparencyLevelHint="None"`, ~480×340. Background is the
literal brand dark `#0A0B0D` (the app's real Default-skin page colour, same value the installer
uses) — **not** the pasted spec's `#121214`, and **not** the active skin: the splash is a pre-app
brand moment that renders before `SkinService.ApplyPersistedSettings()` has necessarily run.

Content: the real emblem (`avares://Paperbunkr.App/Assets/paperbunkr-logo-source.png`), a 3px
determinate `ProgressBar` (240 wide, accent-gradient fill), a status `TextBlock` under it, and
`Paperbunkr {version}` bottom-right at ~10.5px muted.

**Determinate, step-based** progress (chosen over indeterminate): the startup phases are a known
fixed list, and a moving-but-meaningless bar is worst exactly when it matters (a 20 s migration).

### 3. Shared "first-look" shell

A single reusable card chrome — `FirstLookShell` (a `TemplatedControl` or a documented
`Border.floatingPanel` + header/footer convention; implementation plan picks) — used by **both**
the redesigned Welcome overlay and the new What's New overlay:

- **Header band:** the emblem (40–44px) + a title + a one-line subtitle, on a subtle
  `#0A0B0D → surface` vertical gradient that visually rhymes with the splash.
- **Content slot:** cards (Welcome) or rendered changelog (What's New).
- **Footer slot:** a left-aligned secondary link + a right-aligned action (Skip / Got it).

Both overlays are hosted in `MainWindow.axaml` behind the existing `OverlayShell`/dimmed-backdrop
mechanism, same as `WelcomeOverlay`/`UpdateAvailableOverlay` today. The `WelcomeTourOverlay`
spotlight is **not** on this shell (it's a live overlay on real UI) — it's only restyled to match
the palette.

### 4. Welcome stays setup-only — no wizard, no reader/theme steps

The 2026-08-31 design deliberately chose "three equal paths" over a linear wizard, with CE
migration folded in as one path rather than the assumed default. That holds. Reader direction and
skin already live in Preferences and are not worth a gated step each before first use. So:

`WelcomeOverlayViewModel` keeps its exact command surface (`AddComicFolderCommand`,
`AddBookFolderCommand`, `ImportFromCeCommand`, `SkipCommand`) and its `CeInstallDetected` flag.
Changes are presentation + two behaviours:

- Restyled onto the shell (Decision 3); header shows the emblem + "Welcome to Paperbunkr".
- **CE card de-emphasis when not detected.** When `CeInstallDetected` is true: badged, equal
  weight with the two folder cards (as today). When false: rendered last, muted (lower contrast,
  no accent hover) — most non-CE users never need it and the two folder cards should lead.
- **"What's new in {version} →"** link in the footer → opens `WhatsNewOverlay` in *current-entry-only*
  mode (Decision 6). Gives a brand-new user the "what is this / what's new" context the installer's
  removed changelog page used to.

Gate unchanged: `AppSettings.WelcomeScreenShown`. Post-close tour offer
(`AppSettings.WelcomeTourOffered` → `WelcomeTourOverlay`) unchanged in flow.

### 5. "What's New" — auto after a version bump, every release since last run

**Persistence:** one new field, `src/Paperbunkr.Data/Entities/AppSettings.cs`, same
load-in-`GetOrCreateAppSettings`/save-on-change pattern as `WelcomeScreenShown`:

```csharp
/// <summary>The Paperbunkr version string last seen running on this machine (four-part assembly
/// version, e.g. "0.3.0.0"), used to decide whether to show the What's New overlay on startup
/// (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md). Null until the
/// first launch that writes it. Written every launch regardless of whether the overlay shows.</summary>
public string? LastRunVersion { get; set; }
```

Migration `AddLastRunVersion` — a nullable `TEXT` column with a **no-op `Down()`** (standing rule:
any new `AppSettings`/`Issues` column needs a no-op `Down()` to satisfy the orphan-column rule —
see `project_paperbunkr_migration_rollback_orphan_column_bug`). Its up-down-up test follows the
`project_paperbunkr_migration_updown_up_test_antipattern` fix pattern.

**`Services/ReleaseVersion.cs`** — a small pure static:

- `Current` → `Assembly.GetExecutingAssembly().GetName().Version` (`0.3.0.0`).
- `TryParseHeading(string version, out Version)` → parses a `ChangelogEntry.Version` string
  (`"0.3.0-beta"`, `"0.1.1-alpha"`) by dropping any `-suffix` and `Version.Parse`-ing the `x.y.z`
  (single release stream — the prerelease tag carries no ordering). Padded to three components.
- `IsNewerThan(Version a, Version b)` → compares on the first **three** components only (assembly
  version's 4th/revision segment is always `0` per the CI derivation and carries no meaning).

**Startup check** — added to `MainViewModel`, run off the UI thread exactly like
`CheckForUpdatesOnStartupAsync` (its doc comment explains why: a captured UI `SynchronizationContext`
+ synchronous work under the first `await` froze the UI thread past the watchdog):

1. Read `LastRunVersion`.
2. If non-null and `ReleaseVersion.IsNewerThan(Current, parsed(LastRunVersion))`:
   load + parse `CHANGELOG.md` via the same path `MainViewModel.LoadNewestChangelogBody` already
   uses (`Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md")` → `ChangelogParser.Parse`),
   take every entry whose parsed version is strictly `> LastRunVersion`, newest-first, and open
   `WhatsNewOverlay` with them (marshalled back to the UI thread via `Dispatcher.UIThread.Post`,
   same as the update check's tail). Missing `CHANGELOG.md` → show nothing.
3. Equal version, downgrade, or zero entries in range → show nothing.
4. **Always** write `LastRunVersion = Current.ToString()` (step 4 runs whether or not step 2 showed
   anything — a patch with no changelog entry still advances the marker).

### 6. "What's New" overlay — rendering and manual open

**`WhatsNewOverlayViewModel`** carries `IReadOnlyList<ChangelogEntry> Entries` and a
`bool CurrentEntryOnly` flag. Rendering reuses the **existing** `ChangelogBodyFormatter.Format`
(`ChangelogBodyGroup` — `Category` + `Lines`), the same renderer the Preferences → About accordion
uses (`2026-09-07-about-redesign-design.md`) — no second changelog-rendering implementation.

- The newest entry renders **expanded** (version + date header, then each `ChangelogBodyGroup` as
  an accent sub-heading + lines).
- Older entries (skipped releases) render **collapsed** — a one-line tappable summary
  (`{version}  ·  {first N words of the body}`), expanding in place on click. Matches the user's
  stated preference for discrete sections over one long scroll
  (`feedback_dislikes_infinite_scroll_navigation`).
- `CurrentEntryOnly` mode (opened from the Welcome link, or the About button) shows just the
  latest entry, expanded, no collapsed list.
- Footer: "Full changelog in Preferences → About" link + a primary **"Got it"** button; both close
  the overlay.

**Manual open:** a "What's New" text button beside `CurrentVersion` in the About section
(`AboutSectionViewModel`/its view) → opens `WhatsNewOverlay` with `CurrentEntryOnly = true`. (The
About accordion stays as the full browsable history; this button is the nicer single-entry read.)

### 7. Startup sequencing — one first-look modal at most

`App.axaml.cs` today: heavy sync work → `desktop.MainWindow = mainWindow` → `RestoreLastScreen` →
`if (!WelcomeScreenShown) OpenWelcomeOverlay` → `if (!welcomeOverlayOpened) Task.Run(CheckForUpdatesOnStartupAsync)`.

New order:

```
SplashWindow shown
  └─ InitializeAsync (heavy work, progress) ── 400 ms floor ──┐
                                                              ▼
                          build MainWindow, show it, fade+close splash
                                                              │
                        ┌─────────────────────────────────────┤
       first run?  ──────┤ yes → WelcomeOverlay → (post-close) tour offer
       (WelcomeScreenShown = false)                           │  … and skip the update check this launch (as today)
                        │ no                                  │
       version bumped? ──┤ yes → WhatsNewOverlay (Decision 5)  │
       (LastRunVersion)  │ no                                  │
                        └─→ Task.Run(CheckForUpdatesOnStartupAsync)  → UpdateAvailableOverlay (pre-download teaser)
```

The three branches are mutually exclusive, so two first-look modals can never stack. The
version-bump write of `LastRunVersion` (Decision 5 step 4) still happens on the first-run and
no-change branches too (a fresh install writes it so the *next* update shows What's New; an
unchanged launch just re-writes the same value).

The pre-download `UpdateAvailableOverlay` and the update-ready toast's `ActivityLinkKind.UpdateChangelog`
link are **unchanged** — they operate on the update-check path only, which What's New never
collides with (different launch).

### 8. Motion

All motion is `Opacity` / `RenderTransform` only (no `Width`/`Height`/`Margin` animation), using
the app's existing `PbMotion*` resources, and every piece honours reduced motion
(`SkinService.GetReducedMotion`, already wired into `NavigationTransitionCoordinator`; the tokens
are zeroed by `SkinService.ApplyReducedMotion`).

- **Splash emblem entrance:** fade + `ScaleTransform` 0.9→1.0, `PbMotionStandard` (~220 ms),
  `CubicEaseOut`.
- **Splash "breathing" while loading:** a keyframe `Animation`, `IterationCount="INFINITE"`
  (all-caps or it won't parse), ~2.4 s: `ScaleTransform` 1.0↔1.035 on the emblem, and separately
  `Opacity` 0.35↔0.7 on a **static** radial-gradient glow `Border` behind it. The glow's blur is
  never animated — only its opacity (the `poster_glow_transition_bug` was an animated `BoxShadow`
  property racing invalidation). On reduced motion: no breathing, static glow at mid opacity.
- **Splash → main window:** splash `Opacity` 1→0 over 150 ms (`PbMotionFast`), `CubicEaseIn`,
  then `Close()`.
- **Overlay card enter/exit** (Welcome, What's New): fade + subtle scale (0.98→1.0) on enter using
  the existing `OverlayShell` transition; exit ~70% of the enter duration. Reuses whatever
  `WelcomeOverlay`/`UpdateAvailableOverlay` already do — not a new mechanism.

## Components (for the implementation plan)

| Unit | New | Changed |
|---|---|---|
| Splash | `Views/SplashWindow.axaml(.cs)`, `ViewModels/SplashViewModel.cs` | `App.axaml.cs` (`OnFrameworkInitializationCompleted` restructured: splash-first, heavy work async with progress, 400 ms floor, main-window build + splash fade-out) |
| Shared shell | `FirstLookShell` style/control + a shell doc comment | — |
| Welcome | — | `Views/WelcomeOverlay.axaml` (onto the shell, CE-card de-emphasis, "what's new" link), `ViewModels/WelcomeOverlayViewModel.cs` (link command), `WelcomeTourOverlay.axaml` (palette restyle only) |
| What's New | `Views/WhatsNewOverlay.axaml(.cs)`, `ViewModels/WhatsNewOverlayViewModel.cs`, `Services/ReleaseVersion.cs` | `ViewModels/MainViewModel.cs` (startup version check + sequencing + host wiring), `Views/MainWindow.axaml` (host the overlay), About section view + VM (manual-open button) |
| Persistence | migration `AddLastRunVersion` (+ no-op `Down`, up-down-up test) | `src/Paperbunkr.Data/Entities/AppSettings.cs` (`LastRunVersion`) |

## Testing

- **`SplashViewModel`** — progress advances 0→1 through the reported phases; `StatusMessage`
  tracks; the 400 ms floor holds when `InitializeAsync`'s work completes early (inject a clock or
  assert on a `Task.Delay`-shaped seam).
- **`ReleaseVersion`** — `TryParseHeading` on `"0.3.0-beta"` / `"0.1.1-alpha"` / malformed;
  `IsNewerThan` comparing on three components, ignoring the revision segment; downgrade and
  equal cases.
- **`WhatsNewOverlayViewModel`** — given a fixed `CHANGELOG.md` fixture + a `LastRunVersion`,
  selects exactly the entries strictly newer, newest-first; `CurrentEntryOnly` yields one;
  zero-in-range yields an empty list (caller shows nothing).
- **Sequencing** — a unit-testable decision function (`first run` / `version bumped` / `neither`)
  returning which modal to open, exercised for all three branches incl. fresh-install-writes-marker.
- **Migration** — `AddLastRunVersion` up→down→up, following the antipattern fix (seed real data,
  don't assert on a schema-snapshot round-trip that the antipattern note describes).
- All via **targeted `dotnet test --filter`** subsets — the full App.Tests suite mass-fails under
  concurrent headless load (`project_paperbunkr_full_suite_headless_flake`).
- The splash *window* itself, the reduced-motion paths, and the on-screen sequencing are
  **manual/on-screen verification** — Avalonia headless can't meaningfully assert a borderless
  top-level's render or a cross-window fade.

## Explicitly out of scope (YAGNI)

- **The 3-step onboarding wizard** (reader direction, theme) from the pasted spec — Decision 4.
- **A replay entry point for the nav-rail tour** — the 2026-08-31 design's "auto-offer once, then
  gone" call stands; only What's New gets a manual re-open.
- **Skinning the splash** to the active skin — it's a fixed brand moment (Decision 2).
- **Changing the pre-download `UpdateAvailableOverlay` or the update-ready toast** — Decision 7.
- **A general Help menu / Help nav-rail item** — considered for the What's New re-open, dropped in
  favour of the single About button (no new chrome).
- **Progress for the fire-and-forget startup tasks** (`ActivityHistoryStore.PruneOnStartup`,
  auto-backup) — they already run off-thread and don't gate the window; the splash doesn't wait on
  them.
