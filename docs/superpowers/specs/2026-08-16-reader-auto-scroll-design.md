# Reader auto-scroll / hands-free mode

**Date:** 2026-08-16
**Status:** Approved, pending implementation plan
**Backlog ref:** `docs/Paperbunkr-Roadmap.md` reader-polish backlog, last remaining item after
remappable keyboard shortcuts (docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-
design.md) and split-page nav.

## Context

`docs/Paperbunkr-Roadmap.md` lists "auto-scrolling/hands-free mode" as open backlog. CE's actual
`AutoScrolling` toggle (`_reference/ComicRackCE/ComicRack.Engine/Display/ComicDisplay.cs:1313-
1399`) is not what the name suggests: it's a keyboard-arrow behavior switch for zoomed, paged
books - when on, arrow keys skip fine pixel-panning and jump straight to the next page/"part"
(CE's zoomed-page-split-into-segments concept, `DisplayPart`/`PartPageToDisplay`); when off, arrows
pan within the page. There's no timer or interval anywhere in that code path. Paperbunkr has no
equivalent "Part" concept, so porting this literally would mean inventing that whole mechanism from
scratch with nothing real behind it - against this project's standing "only add capability that's
actually backed" rule.

Per user direction, this spec instead builds the feature the "/hands-free mode" qualifier actually
points at: a modern webtoon-reader-style passive auto-scroll on top of Paperbunkr's existing
continuous/webtoon scroll mode (shipped 2026-08-10) - the page scrolls forward automatically at a
set speed, with no CE-parity claim (a deliberate, named deviation, same category as this project's
other documented ones).

## 1. Command & UI

New `Reader.ToggleAutoScroll` command in `KeyboardCommandRegistry`, default gesture `S` (matches
CE's own default key for its differently-behaving `AutoScrolling`, so muscle memory lines up even
though the behavior doesn't), `ConflictContext.Continuous` (only meaningful in continuous mode,
same context as the scroll direction commands).

A toolbar button appears in `ReaderScreen.axaml`'s toolbar, **visible only when
`IsContinuousMode`** - same visibility-gating (`IsVisible="{Binding IsContinuousMode}"`) the
fit-mode picker and double-page toggle already use, since `ScrollOffset`/continuous scrolling don't
exist outside that mode. Its flyout holds one speed slider, following the exact "Adjust" flyout
pattern already in `ReaderScreen.axaml` (Brightness/Contrast/Saturation/Gamma).

## 2. State & timer

`ReaderScreenViewModel` gains:
- `IsAutoScrolling` (bool, `[ObservableProperty]`) - drives the toggle button's active state and
  the `ToggleAutoScrollCommand`.
- `AutoScrollSpeed` (double, px/sec, `[ObservableProperty]`) - session-only, resets each launch,
  same lifetime as `ZoomLevel`/pan (no Preferences default in this pass, per user direction).
  Default 60 (a comfortable reading pace, given the existing 80px `WheelScrollStepPixels` per
  wheel-notch). Slider range 20-300.
- A `DispatcherTimer` (`AutoScrollTickInterval = TimeSpan.FromMilliseconds(40)`), lazily created
  and `.Stop()`/`.Start()` around toggling - the exact pattern `_positionSaveTimer` already
  establishes (`ReaderScreenViewModel.cs:1268-1276`). Each tick sets
  `ScrollOffset = ClampScrollOffset(ScrollOffset + AutoScrollSpeed * interval.TotalSeconds)`.

**Stopping conditions** (all route through one `StopAutoScroll()` helper that stops the timer and
sets `IsAutoScrolling = false`):

- **Manual scroll interaction.** Any `ScrollOffset` write not originating from the timer's own tick
  turns auto-scroll off. Implemented with a private `_settingScrollOffsetFromAutoScroll` guard flag
  set immediately before the timer's own write and checked in `OnScrollOffsetChanged` - same
  guard-flag shape as `PreferencesScreenViewModel`'s `_suppressBackupSettingsApply`. Deliberately a
  hard stop, not pause-then-auto-resume (user direction) - re-toggling resumes from wherever the
  user left it.
- **End of book.** If a tick's `ClampScrollOffset` result doesn't move `ScrollOffset` at all
  (already saturated at the max), stop rather than spin a no-op timer forever.
- **Leaving the book.** `Load()` (new issue) and `GoBack()` both stop it - alongside the existing
  overlay-timer stop at `GoBack` (`ReaderScreenViewModel.cs:1450`) and `Load`'s existing
  `ScrollOffset = 0` reset.
- **Leaving continuous mode.** The tick handler itself no-ops and stops if `!IsContinuousMode`,
  cheaper than hooking every reading-mode-change call site individually.

## 3. `PageCanvas` wiring

None needed beyond the new keyboard gesture. `ScrollOffset` is already `TwoWay`-bound
(`ScrollOffsetProperty`, `BindingMode.TwoWay`) - the timer writes it from the ViewModel side
exactly like every other programmatic write already does (e.g. thumbnail-click scroll-to-page).
`ReaderToggleAutoScrollGesture` follows the same `StyledProperty<KeyGesture>` +
`OnKeyDown`-gesture-match pattern the remappable-shortcuts work just established, gated
`if (IsContinuous)` in `PageCanvas.OnKeyDown` (alongside the existing continuous-mode branch, not
the Always-context block - it's meaningless outside continuous mode).

## 4. Testing

`ReaderScreenViewModelTests`: toggle on/off via command, speed value clamps to the slider range,
a manual `ScrollOffset` write while auto-scrolling turns it off, reaching the clamped max stops the
timer, `LoadIssue`/`GoBack` both reset `IsAutoScrolling` to false. No new pure-math test file - this
is timer/state-flag wiring, not a formula, same category as the remappable-shortcuts spec's own
testing note.

## Explicitly out of scope

CE's literal `AutoScrolling`/"Part" navigation toggle (would need the Part-splitting concept built
from scratch first - no existing capability to hang a keyboard shortcut off of, per this session's
other spec's same standing rule). A persisted Preferences default speed (deferred - see §2).
Pause-then-auto-resume behavior (deferred - see §2, hard stop chosen instead). Split-page part
navigation (separate, still-open backlog item, not part of this spec).
