# First-Run Onboarding — Welcome Screen + Optional Live Tour

## Background

Today's first-launch experience (`App.axaml.cs:53-132`) is: if `!PaperbunkrDb.HasAnySeries()` (a
fresh install) *and* a default ComicRack CE library is detected on disk
(`File.Exists(MigrationViewModel.GetDefaultCePath())`), the app auto-opens `MigrationOverlay`
directly, framed entirely as "ComicRack CE Migration" (`MigrationOverlay.axaml:143`). If no CE
install is detected — the common case for a user who never used ComicRack — nothing happens at
all: they land on an empty Home screen with zero guidance, no obvious way to add a library folder
short of finding Preferences → Libraries themselves.

`docs/onboarding.md` §14 (Migration UX) only ever specified *migration* UX — a general first-run
welcome flow for non-CE users was never designed. This spec adds one, and folds CE migration into
it as one path among several rather than the assumed default.

## Scope

**In scope:**
1. A `WelcomeOverlay` shown on first launch (gated on a new persisted flag, not on `HasAnySeries()`
   — see Persistence below), offering three equal setup paths: add a comic folder, add a book
   folder, or import from ComicRack CE (auto-badged when detected).
2. A one-time, non-blocking offer of a short live tour after the welcome screen closes, regardless
   of which path (including Skip) was taken.
3. A `WelcomeTourOverlay` — a live spotlight walkthrough that navigates the real nav rail and
   highlights each of its seven stops in place, with a callout bubble per stop.

**Explicitly out of scope:**
- Any change to `MigrationOverlay`'s internal Locate→Preview→Conflicts→Commit→Results flow — it's
  reused as-is, just no longer auto-launched.
- A replay entry point for the tour (Preferences/Help). Per the "auto-offer once, then gone"
  decision, once resolved it's gone for good this install.
- Tour content for Reader/BookReader/PdfReader/any drill-down screen — the tour is scoped to the
  seven lateral rail stops only (see UI below for why).
- Any change to how `HasAnySeries()` is used elsewhere in the codebase (backup/integrity checks,
  etc.) — this spec only stops using it as the onboarding trigger.

## Persistence

Two new `AppSettings` fields (`src/Paperbunkr.Data/Entities/AppSettings.cs`), same
load-in-constructor/save-on-change pattern as `MinimizeToTrayNoticeShown`:

```csharp
/// <summary>Whether the first-run WelcomeOverlay has been shown and closed (docs/superpowers/specs/
/// 2026-08-31-first-run-onboarding-design.md). Default false. Deliberately independent of
/// HasAnySeries() - a user who skips, or adds a folder with zero comics in it, must never see the
/// welcome screen re-trigger on a later launch just because the library is still empty.</summary>
public bool WelcomeScreenShown { get; set; }

/// <summary>Whether the one-time post-welcome tour offer has been shown (accepted or declined) -
/// see WelcomeScreenShown. Flips true the moment the offer is *shown*, not just when it's answered,
/// so an app close mid-prompt can't cause it to reappear next launch.</summary>
public bool WelcomeTourOffered { get; set; }
```

`WelcomeScreenShown` replaces `isFreshInstall` as the gate in `App.axaml.cs`. `defaultCePathFound`
detection is kept (same code, `File.Exists(MigrationViewModel.GetDefaultCePath())`) but now feeds a
`CeInstallDetected` flag on `WelcomeOverlayViewModel` for badging the CE card, not an auto-launch
decision.

```csharp
// App.axaml.cs, replacing the current offerFirstRunMigration block
using var settingsContext = PaperbunkrDb.CreateContext();
var appSettings = settingsContext.GetOrCreateAppSettings();
bool showWelcomeOverlay = !appSettings.WelcomeScreenShown;
bool ceInstallDetected = File.Exists(MigrationViewModel.GetDefaultCePath());
...
if (showWelcomeOverlay)
{
    mainViewModel.OpenWelcomeOverlayCommand.Execute(ceInstallDetected);
}
```

(The existing `graphics.json` sync block already opens an `AppSettings` context around this point
in `OnFrameworkInitializationCompleted` — this reuses that same read rather than opening a second
context.)

## `WelcomeOverlay`

New `WelcomeOverlay.axaml`/`.axaml.cs` + `WelcomeOverlayViewModel.cs`, styled as a `floatingPanel`
matching `MigrationOverlay`'s own visual language (same `Border Width="580" Classes="floatingPanel"`
shell, same `primaryWide`/`rowAction` button classes). Opened/closed via `MainViewModel` the same
way Migration already is: `IsWelcomeOverlayOpen` bool, `OpenWelcomeOverlayCommand`/
`CloseWelcomeOverlayCommand`, wired into the existing `EscapeCommand` dispatch
(`MainViewModel.cs:1388`-adjacent) so Esc closes it like every other overlay.

Content:
- Headline + one-line tagline. No ComicRack framing anywhere in this layer.
- Three equal action cards, each an icon + title + one-sentence description + a button:
  - **Add Comic Folder** — invokes the same folder-picker logic as
    `PreferencesScreenViewModel.AddFolderCommand` (extracted or called directly), then closes.
  - **Add Book Folder** — same, for `AddBookFolderCommand`.
  - **Import from ComicRack CE** — closes `WelcomeOverlay` and calls the existing
    `OpenMigrationOverlayCommand`. When `CeInstallDetected` is true, this card shows the same
    checkmark+"Found the default CE install" treatment `MigrationOverlay.axaml:162-165` already
    uses, so the signal isn't lost by moving it out of the auto-launch path.
- A quiet "Skip for now" link at the bottom, visually secondary to the three cards.

Every exit path (a card's action completing and closing the overlay, Skip, or Esc/X) calls the same
`CloseWelcomeOverlay()`, which sets `appSettings.WelcomeScreenShown = true` and saves — one write
path, no per-button duplication. Migrating later is unaffected: Preferences → Libraries →
"Migrate…" (`LibrarySection.axaml:164`) still opens the same `MigrationOverlay` at any time.

## Tour offer

`CloseWelcomeOverlay()` also checks `!appSettings.WelcomeTourOffered`; if true, shows a small
non-blocking prompt — reusing the existing toast mechanism (`MainViewModel.ShowToast`,
already used by `LiveFolderWatchService` per `MainViewModel.cs:81`) extended with an optional
action button, or a compact dismissible card if the toast surface can't carry two buttons cleanly
(implementation-time call, not architecturally significant either way) — reading *"Want a quick
tour of Paperbunkr?"* with **Take the tour** / **No thanks**. Either answer immediately sets
`WelcomeTourOffered = true` and saves. This fires regardless of whether the welcome screen's own
exit was a real action or Skip — the two are independent choices (confirmed).

## `WelcomeTourOverlay`

New `WelcomeTourOverlay.axaml`/`.axaml.cs` + `WelcomeTourOverlayViewModel.cs`. Rendered as the
topmost sibling in `MainWindow`'s outer `Grid` (`MainWindow.axaml:149`), same layering
`MigrationOverlay`/`WelcomeOverlay` already use, so it draws over the rail, sidebar, and content
area alike.

**Steps** — one per lateral rail stop, in rail order:

| # | Stop | Nav command invoked | Target element |
|---|------|---------------------|-----------------|
| 1 | Home | `GoHomeCommand` | `HomeRailButton` (existing `AutomationId`) |
| 2 | Library | `GoLibraryCommand` | `LibraryRailButton` |
| 3 | Books | `GoBooksCommand` | `BooksRailButton` |
| 4 | Smart Lists | `GoSmartCommand` | new `x:Name="SmartListsRailButton"` |
| 5 | Reading Lists | `GoReadingCommand` | new `x:Name="ReadingListsRailButton"` |
| 6 | Continuity | `GoEventsCommand` | `EventsRailButton` (add `AutomationId` to match sibling
    convention, currently missing) |
| 7 | Preferences | `GoPreferencesCommand` | `PreferencesRailButton` |

Deliberately scoped to the nav rail, not screen-internal content: this makes the tour look
identical whether the user just imported 2,000 issues or is looking at a completely empty library
(the common case right after Skip), and avoids six different "what if this widget is empty"
branches. Reader/BookReader/PdfReader are excluded since they require an open book that may not
exist at tour time.

**Mechanics per step:**
1. `WelcomeTourOverlayViewModel` invokes the step's nav command directly (it holds a reference to
   the relevant `MainViewModel` commands, passed at construction — same shape
   `MigrationOverlayViewModel` already takes callbacks in its own constructor).
2. Code-behind resolves the target control by name (`MainWindow.FindControl<Control>(name)`,
   available since the overlay lives in the same visual tree) and computes its bounds relative to
   the overlay's own coordinate space via `TranslatePoint`.
3. The overlay draws a full-window dimmed scrim with a cutout around those bounds (a hole-punched
   geometry — exact Avalonia API, `CombinedGeometry` vs. an `EvenOdd`-filled multi-figure
   `PathGeometry`, is an implementation-time detail to verify against the `avalonia-graphics-
   animation`/`avalonia-custom-controls` subskills or the `avalonia-docs` MCP server, not fixed
   here) plus a callout `Border` near the cutout with a title, one-sentence description, and
   Next/Back/Skip buttons.
4. Window resize while the tour is open re-runs the bounds lookup for the current step (subscribe
   to the target control's `LayoutUpdated`, or the overlay's own `SizeChanged` — implementation
   detail).

Skip/Next-past-last-step both close `WelcomeTourOverlay` with no further persistence needed (
`WelcomeTourOffered` is already `true` from the offer step).

## Error handling

- `WelcomeOverlay`'s folder-picker cards use the exact same picker commands Preferences already
  uses — any existing error handling there (cancelled dialog, invalid path) is inherited unchanged,
  nothing new to add.
- If `FindControl` fails to resolve a tour step's target (shouldn't happen — every target is a
  permanently-present rail button, not conditionally rendered) the step is skipped rather than
  crashing the tour, advancing straight to the next stop.
- `WelcomeScreenShown`/`WelcomeTourOffered` writes use the same `AppSettings` save path as every
  other settings field — no new failure mode.

## Testing

- **`WelcomeOverlayViewModelTests`** (new) — each card's command closes the overlay and sets
  `WelcomeScreenShown`; CE-detected vs. not toggles the badge; Skip/Esc both close and set the flag
  identically to a completed card action.
- **`MainViewModelTests`** (extend) — `OpenWelcomeOverlayCommand`/`CloseWelcomeOverlayCommand`
  wiring; `App.axaml.cs`'s gate logic (`WelcomeScreenShown` false → overlay opens; true → it
  doesn't) covered at the level the existing `offerFirstRunMigration` logic was tested at, if any
  such coverage exists today — otherwise this is new coverage, not a regression risk.
- **`WelcomeTourOverlayViewModelTests`** (new) — step sequencing (Next/Back/Skip bounds-checked at
  first/last step), each step invoking the correct nav command, `WelcomeTourOffered` set on offer
  shown not on accept/decline.
- Live spotlight visuals (cutout geometry, callout positioning, resize behavior): manual/on-screen
  verification only, stated honestly as unverified at design time — same standing caveat this
  project already applies to other desktop-rendering specs (no unattended GUI automation available
  in this environment).

## Roadmap

Once landed, update `docs/alpha-todo.md` (per this project's standing rule) with status + commit
ref + what was actually verified, not just what the commit message claims.
