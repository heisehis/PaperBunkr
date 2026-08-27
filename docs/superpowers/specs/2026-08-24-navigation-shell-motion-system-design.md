# Navigation Shell & Motion System

**Status:** Implemented 2026-08-24. See docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-plan.md for what was actually verified.
**Sub-project 2 of 7** in the full UI rework (see [Design Language Foundation](2026-08-24-design-language-foundation-design.md) for the full phase breakdown). Phase 1 is implemented, tested, and verified.

## Background

Today's nav rail ([MainWindow.axaml](../../../src/Paperbunkr.App/Views/MainWindow.axaml)) is a fixed 64px icon-only vertical strip: Home, Library, Books, Smart Lists, Reading Lists, Story Events, Plugins (separated), then Undo/Redo and Preferences at the bottom. Every top-level screen is an always-instantiated `ContentControl` toggled via `IsVisible` — switching screens is an instant cut, no transition. Phase 1 defined `PbMotionFast`/`PbMotionEase` motion tokens but the only thing consuming them so far is the FloatingPanel overlays' open/close (5 overlay screens) — no screen-to-screen motion exists anywhere in the app yet.

This phase does two things: restructures the nav rail into a collapsible/hover-expand shape, and wires up real directional-slide transitions between top-level screens — the first actual use of Phase 1's motion tokens for primary navigation, fulfilling what "fluid and reactive" was supposed to mean for the app as a whole.

## Scope

**In scope:**
- Nav rail becomes collapsible: 64px collapsed (unchanged width), hover-expands to 200px as a temporary overlay with labels, with a pin control to make the expanded state permanent (persisted).
- Plugins moves out of the rail entirely and becomes a new tab inside Preferences, reusing the existing `PluginScreenViewModel`/`PluginScreen.axaml` content unchanged.
- Directional slide transitions between the 7 remaining rail-anchored top-level screens (Home, Library, Books, Smart Lists, Reading Lists, Story Events, Preferences), using a new `TransitioningContentControl` + `PageSlide` in place of today's always-instantiated `IsVisible`-toggled `ContentControl`s for just this group.
- Rail restyled with Phase 1's tokens: glow-ring active state, vector icons (from the existing `PbIcon*`/`Icons.axaml` set, extended as needed), surface colors.
- The bottom utility row's standalone "Reader" button (Book_Open icon) is removed from the rail entirely, along with `MainViewModel.GoReader()`/`GoReaderCommand` (the method only that button ever called - confirmed nothing else references it; `PreferencesScreenViewModel` has its own unrelated same-named `GoReaderCommand` for its Reader *preferences tab*, a naming coincidence, not a dependency). The rail button only ever jumped back to whatever book was already loaded in the persistent `Reader` screen (`Reader.EnsureIssueLoaded()`) - a redundant shortcut, not a real entry point. `MainViewModel.IsReader` itself stays (it also drives the actual Reader `ContentControl`'s visibility, a drill-down screen unaffected by this change) - only the rail-specific `GoReader()` command and button go away. Actual reader access (opening a specific book from Library, Detail, Reading Lists, etc. via `GoReaderForIssue`/`GoReaderForIssueInReadingList`) is completely unaffected. The bottom rail group becomes Undo, Redo, separator, Preferences.

**Out of scope (deferred):**
- Drill-down screens (Detail, MangaDetail, Reader, BookReader, PdfReader) keep today's instant-cut behavior unchanged. These aren't lateral rail moves, need their own push/pop slide convention, and are getting their own redesigns in Phases 5-6 anyway — building transition logic for them now would likely be thrown away.
- The 5 FloatingPanel overlays already have open/close motion from Phase 1 - untouched here.
- Any change to the Plugins screen's own content/behavior - only *where* it's hosted changes.

## Nav rail structure

- **Collapsed** (default): 64px, same visual footprint as today.
- **Hover-expand**: pointer entering the rail expands it to 200px as a temporary overlay drawn on top of the content area (not a layout reflow) - labels appear next to each icon (Home, Library, Books, Smart Lists, Reading Lists, Story Events, then Undo/Redo, then Preferences). Leaving collapses it back. Overlay rather than reflow specifically so a quick hover doesn't jar the content underneath.
- **Pin**: once expanded, a pin toggle button appears. Pinning makes 200px the rail's real, permanent width - this *does* reflow the content area (genuine layout change, not an overlay), and persists via a new `AppSettings.NavRailPinned` bool (`SkinService`-style load-on-startup, save-on-toggle - see [PreferencesScreenViewModel.cs](../../../src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs)'s existing `ReducedMotion` property for the exact shape to mirror, though this lives on `MainViewModel` since it's rail/shell state, not an Appearance-tab preference).
- Width and label-fade transitions use the existing `PbMotionFast`/`PbMotionEase` tokens - no new duration token.
- `Button.rail.plugin`'s distinct border style (in `MainWindow.axaml`) becomes dead code and is removed along with the Plugins rail button itself.

## Plugins relocation

Plugins becomes a new tab in Preferences, following the exact pattern the existing Appearance/Behavior/Libraries/Reader/Advanced/Developer tabs already use (`IsAppearanceTab`-style computed bool, `GoAppearance`-style `[RelayCommand]`, a `prefTab` button in the tab strip). The tab's content is the existing `PluginScreen` view hosted via `ContentControl`, `DataContext="{Binding Plugin}"` - `Plugin` moves from being a `MainViewModel`-level screen to a property `PreferencesScreenViewModel` exposes (or `MainViewModel` still owns the `PluginScreenViewModel` instance and hands it to `PreferencesScreenViewModel`'s constructor, matching how `Migration` is already threaded through today - exact wiring is an implementation-time call between these two equally-valid shapes, not a design-level decision).

`MainViewModel` loses: `IsPlugin`, `GoPluginCommand`, the Plugin `ContentControl` slot in `MainWindow.axaml`, and the rail's Plugins button + preceding separator.

## Screen transition system

**The problem:** `IsVisible` doesn't animate - toggling it is an instant show/hide, and all 13 top-level `ContentControl`s already exist permanently in the tree (each bound to its own persistent ViewModel instance, e.g. `Content="{Binding Library}"`), so there's no natural "enter/exit" moment to hook a transition into.

**The fix:** Avalonia ships `TransitioningContentControl` (`Avalonia.Controls`) with a `PageSlide` page-transition built for exactly this - old content slides out, new slides in, direction is an explicit parameter. The 7 rail-anchored screens move into **one** `TransitioningContentControl` bound to a new `MainViewModel.ActiveScreenContent` property (returns whichever of `Home`/`Library`/`Books`/`Smart`/`Reading`/`Events`/`Preferences` matches `CurrentScreen`, re-evaluated in `OnCurrentScreenChanged` alongside the existing `IsX` flag updates). The drill-down screens (Detail/Reader/etc.) stay exactly as they are today - separate, always-instantiated `ContentControl`s with `IsVisible` toggles, siblings of the new `TransitioningContentControl` in the same layout position, so only one of "the transitioning group" or "a drill-down screen" is ever visible - unchanged from today's mutual-exclusivity, just one fewer moving part inside the lateral group.

**Direction:** a fixed rail order - `Home(0) → Library(1) → Books(2) → Smart(3) → Reading(4) → Events(5) → Preferences(6)`. Moving to a higher index slides the new screen in from the right (old exits left); a lower index slides in from the left. This comparison is pure C# (`MainViewModel` computing a `PageSlide.SlideAxis`/forward-or-backward value from two rail-order integers), independent of Avalonia's visual tree - genuinely unit-testable.

Existing per-screen state (scroll position, loaded data, etc.) is unaffected - each ViewModel instance is unchanged, only which one the `TransitioningContentControl`'s `Content` points to changes, the same as today's `IsVisible` swap just via a different control.

## Testing

- Rail-order → slide-direction computation: unit tests covering forward/backward/same-screen cases, pure C# logic with no Avalonia dependency.
- `NavRailPinned` persistence: a test mirroring Phase 1's `ReducedMotion_Change_PersistsToAppSettings_AndAppliesLive` shape.
- Plugins-in-Preferences: existing `PluginScreenViewModelTests` (if any) keep working unchanged since the ViewModel itself isn't touched; a `PreferencesScreenViewModelTests` case confirms the new tab's `IsPluginsTab`/`GoPlugins` wiring, mirroring the existing tab tests.
- Hover-expand visuals and the actual `PageSlide` motion are manual/on-screen verification only - the same honest limitation noted in Phase 1 (this dev build's computer-use identity mismatch blocks interactive verification), stated up front rather than claimed as tested.

## Open questions / deferred

- Exact wiring shape for how `PreferencesScreenViewModel` obtains the `PluginScreenViewModel` instance (constructor parameter vs. property already on `MainViewModel`) is an implementation-time call - both are consistent with existing patterns in this codebase.
- Whether `TransitioningContentControl`'s content-swap interacts badly with any of the 7 screens' code-behind (e.g. logic assuming it's never removed from the visual tree) is unverified at design time - checked per-screen during implementation, not exhaustively audited here.
