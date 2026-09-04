---
title: App-Wide & Library Keyboard Shortcuts — Design Spec
status: shipped 2026-08-31 (see docs/Paperbunkr-Roadmap.md's own entry for what changed during
  implementation, notably the Left/Right-degenerates-to-Up/Down finding in the sidebar section)
related: docs/superpowers/specs/2026-08-31-keyboard-operability-design.md (context-menu keyboard
  trigger + grid-nav rollout, landed 56df3a2), docs/superpowers/specs/2026-08-16-remappable-reader-
  shortcuts-design.md, src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs
supersedes: paperbunkr-keyboard-control-spec.md (external draft dropped into the session
  2026-08-31, reviewed against the live codebase and found mostly already-shipped or already
  covered elsewhere — see Background)
---

# App-Wide & Library Keyboard Shortcuts

## Background

A user-supplied draft spec (`paperbunkr-keyboard-control-spec.md`) proposed building keyboard
control for Paperbunkr across three surfaces — reader, library grid, app-wide chrome — from
scratch, in three phases: fixed defaults, a central remapping registry, then a remapping UI.
Reviewing it against the actual codebase (not assuming it was greenfield) found most of it already
exists, in a more precise form than the draft proposed:

- **Reader shortcuts, the registry, and the remapping UI are already shipped.**
  `KeyboardCommandRegistry` (`src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs`) is the
  `CommandId → KeyGesture` registry the draft's §8.2 asked for, with a `ConflictContext` enum
  (`Always`/`PagedUnzoomed`/`PagedZoomed`/`Continuous`) that already generalizes the draft's "R4/R10
  need explicit mutual-exclusion" idea into four mode buckets. Preferences → Keyboard Shortcuts
  (`KeyboardShortcutsSection.axaml`) is the draft's §9 Phase-3 UI: per-command remap dropdowns,
  live conflict warnings, Import/Export Layout. Defaults are CE-verified (registry's own doc
  comment cites `_reference/ComicRackCE/ComicRack/MainForm.cs InitializeKeyboard`, with one
  deliberate, documented deviation).
- **The draft's proposed reader defaults (§5) contradict what's actually shipped** — fit modes are
  `1`-`5` (not `W`/`H`/`F`), zoom is `Z`/`Shift+Z` (not `Ctrl+=`/`Ctrl+-`), fullscreen is `F`
  primary (not `F11`). Adopting the draft's defaults would mean changing live, user-facing
  behavior, not adding new — explicitly not done here.
- **The draft's library-grid spatial-nav requirement (its L1) is already built and its rollout is
  confirmed fully landed**, not just "the express subject of" the sibling spec — re-checked by
  reading the actual files, not taken on the sibling plan's word: `GridKeyboardNavigation`
  arrow-key wiring (`OnCardKeyDown`/`TryHandleArrowKey`) is present in `LibraryScreen.axaml.cs`,
  `DetailTabs.axaml.cs`, `BooksScreen.axaml.cs`, and `BookDetailScreen.axaml.cs`; `SmartScreen.axaml.cs`
  has its own confirmed variant built on `VirtualizingWrapPanel.GetControl` (the plan's Step 10
  virtualization-aware path). All of `2026-08-31-keyboard-operability-design.md`'s grid-rollout
  scope is done. Not re-touched here — see the confirmation note below instead of taking this on
  faith.
- **Card-grid arrow-key movement and sidebar arrow-key movement are two different, unrelated gaps**
  — the sibling plan's own Step 8/9 correction explicitly *excluded* the contextual sidebar
  (`MainWindow.axaml`'s `ShowContextualSidebar` panel) from grid-nav rollout, reasoning that a
  single-column list already gets Up/Down "for free" from Tab order. That's true for *reaching*
  each row via Tab, but it's a materially different, slower affordance than the Up/Down
  arrow-key movement the card grids already have — Tab also moves through every other focusable
  control in the window, not just sibling sidebar rows. Nobody's actually built arrow-key movement
  *within* the sidebar. That's new scope, added below.

Subtracting all of that, what's left is genuinely unbuilt: **app-wide hotkeys** (nothing exists
yet — no `HotKey=` usage anywhere in the codebase; `MainWindow.axaml`'s `Window.KeyBindings`
currently holds exactly two entries, `Escape` and `Back`), **sidebar arrow-key movement** (a real
gap neither the original draft nor the sibling spec covers), and a handful of **library-grid
actions that already have backing commands but no keyboard entry point**. This spec covers that
remainder.

**Mechanism precedent, confirmed by reading `MainWindow.axaml:22-36`:** app-shell-wide gestures in
this codebase are declared directly as `<KeyBinding>` entries in `MainWindow.axaml`'s
`Window.KeyBindings`, bound straight to `MainViewModel` commands — not routed through
`KeyboardCommandRegistry`, which its own doc comment states is deliberately Reader-scoped (its
`ConflictContext` values are defined purely in terms of `PageCanvas`'s paged/continuous states).
This spec follows that existing precedent rather than inventing a second registry mechanism.

## Scope

**In scope:**
1. App-wide hotkeys, added to `MainWindow.axaml`'s `Window.KeyBindings`, same mechanism as the
   existing `Escape`/`Back` entries:
   - `Ctrl+,` → open Preferences (reuses whatever existing navigate-to-preferences path
     `CurrentScreen = "preferences"` already goes through — see `MainViewModel.cs`'s existing
     `CurrentScreen = "library"`-style navigate methods for the pattern to mirror).
   - `Ctrl+Tab` / `Ctrl+Shift+Tab` → cycle the 7 top-level rail screens forward/back, using the
     existing `RailOrder` dictionary (`MainViewModel.cs:43-52`: home, library, books, smart,
     reading, events, preferences) as the cycle order — no new ordering concept needed, it's
     already exactly the sequence `OnCurrentScreenChanging` uses for slide-direction.
2. Sidebar arrow-key movement (`Up`/`Down`, `Home`/`End`) within the contextual sidebar
   (`MainWindow.axaml`'s `ShowContextualSidebar` panel), across all four screens that populate it
   today: Library, Smart, Reading, Events. See the dedicated section below — this is new plumbing,
   not a rename of the existing grid mechanism.
3. Library grid, added to `LibraryScreen`/`LibraryToolbar`'s existing input surfaces:
   - `Ctrl+A` → select all visible (issue or series grain, matching whichever
     `SelectAllVisibleIssuesCommand`/`SelectAllVisibleSeriesCommand` the context menu already
     exposes per `LibraryContextMenuBuilder.cs:83,138-139` — same commands, new gesture, no new
     logic).
   - `Delete` → remove selected item(s), gated behind the same confirm flow the toolbar/context-menu
     delete path already uses (confirm at implementation time which existing command that is — not
     guessed here, since "remove from library" already exists as a screen action per project memory
     but wasn't located by name in this pass).
   - `/` → focus the library search box (`SearchBox`, `LibraryToolbar.axaml:173`) from anywhere the
     grid has focus.
   - `Esc` inside the search box, when it has text → clear the query and return focus to the grid.
     **Real, confirmed gap**: `OnSearchBoxKeyDown` (`LibraryToolbar.axaml.cs:34-60`) currently maps
     `Escape` only to `CloseSuggestionsCommand` — it does not clear `SearchQuery` or move focus.
     This is an edit to that existing handler, not new plumbing.
4. Nothing here needs a new remapping surface — see the "Registry" note below for why these stay
   hardcoded like `Escape`/`Back`, not added to `KeyboardCommandRegistry`.

**Explicitly out of scope (real feature work, not plumbing — flagged so it isn't silently
dropped):**
- **Command palette** (draft's A1, `Ctrl+Shift+P`). A searchable, keyboard-driven command list is
  a real UI feature — what it searches (just `KeyboardCommandRegistry`, or every command in the
  app, per the draft's own open question #2), how results render, how it composes with the rail
  nav — not something to default through a cleanup spec. Needs its own brainstorming pass if
  wanted.
- **Type-ahead jump-to-cover-by-letter** (draft's L9). Same reasoning: whether repeated presses of
  one letter cycle matches (Windows Explorer convention) or build a multi-letter prefix, and what
  the reset-timeout is, are real interaction decisions.
- **Range-select from anchor** (draft's L4, `Shift+`arrow). `TileSelectionController` (used by both
  `Selection`/`SeriesSelection`) would need to gain anchor/range-select semantics beyond its current
  per-item toggle — a model change, not just a gesture binding. Left for whoever picks this up to
  scope separately once single-item `Ctrl+A`/`Delete` land and the toggle model's actual shape is
  fresh context.
- **Quit gesture** (draft's A6). Alt+F4 (Windows) already closes the window via the OS; no evidence
  found of an unsaved-work-confirmation flow that would need a custom `Ctrl+Q` binding to hook into.
  Not adding a redundant binding speculatively — revisit only if a real need surfaces.
- **Card-grid arrow-key movement itself.** Already fully shipped and confirmed across every
  screen that has one (Library, Detail issue tiles, Books, BookDetail series-cards, Smart Lists) —
  see the confirmation note above and the Background section. Not the same thing as sidebar
  movement (in scope, below) — don't conflate the two when reviewing this spec.
- Context-menu Menu-key/Shift+F10 trigger — landed via `2026-08-31-keyboard-operability-design.md`,
  not re-touched.
- Reader-surface shortcuts — already shipped and remappable via `KeyboardCommandRegistry`; not
  touched by this spec.

## Registry: why these stay hardcoded, not added to `KeyboardCommandRegistry`

`KeyboardCommandRegistry`'s `ConflictContext` enum is defined entirely in terms of `PageCanvas`'s
reader states — extending it to cover app-wide/library commands would mean either stretching
`ConflictContext` to mean something it currently doesn't, or adding a parallel enum, for six new
gestures. `MainWindow.axaml`'s existing `Escape`/`Back` bindings already establish the "shell-wide
gesture = hardcoded `Window.KeyBindings` entry, not a registry row" precedent (confirmed by that
binding's own doc comment, `MainWindow.axaml:29-30`, stating this explicitly). This spec's six new
gestures (2 app-wide, 4 library) follow that same precedent. If remapping ever becomes a real ask
for these specifically, that's a registry-generalization task to scope then, against a concrete
request — not spun up preemptively for gestures nobody's asked to remap yet.

## App-wide hotkeys

Add to `MainWindow.axaml`'s existing `Window.KeyBindings` block:

```xml
<KeyBinding Gesture="Ctrl+OemComma" Command="{Binding OpenPreferencesCommand}" />
<KeyBinding Gesture="Ctrl+Tab" Command="{Binding CycleScreenForwardCommand}" />
<KeyBinding Gesture="Ctrl+Shift+Tab" Command="{Binding CycleScreenBackCommand}" />
```

(`OemComma` is Avalonia's `Key` enum name for the physical comma key — confirm against the
installed `Avalonia.Base.dll` at implementation time, same reflection-verification approach already
used in this codebase for the `Back` gesture name per `MainWindow.axaml:31-34`'s own comment; don't
assume `KeyGesture.Parse("Ctrl+,")` works without checking.)

`CycleScreenForwardCommand`/`CycleScreenBackCommand` are two new small `[RelayCommand]` methods on
`MainViewModel`: look up `RailOrder[CurrentScreen]` (falling back to a no-op if `CurrentScreen`
isn't in `RailOrder` — i.e. a drill-down screen like Reader/Detail is active, where cycling
top-level views doesn't mean anything), add/subtract 1 mod `RailOrder.Count`, find the key at that
index, set `CurrentScreen` to it. `OpenPreferencesCommand` either already exists (there's clearly
an existing navigate-to-preferences path, since `preferences` is a normal `RailOrder` entry) or is
a one-line new command setting `CurrentScreen = "preferences"` — confirm which at implementation
time by grepping `MainViewModel.cs` for the rail nav button's existing preferences command binding.

**Focus-swallowing caveat** (per the Avalonia accessibility skill's own documented common mistake):
a `Window.KeyBindings` entry can be swallowed by a focused `TextBox` before it reaches the window
level, depending on routing. The existing `Escape`/`Back` bindings presumably already work with
text focus somewhere in the app (Escape needs to work while, say, editing a field) — verify that
assumption holds empirically for `Ctrl+,`/`Ctrl+Tab` too during implementation (e.g., with focus in
the library search box), rather than assuming Avalonia's `KeyBinding` routing is uniform across all
gestures.

## Sidebar arrow-key movement

**Structure, confirmed by reading `MainWindow.axaml`:** the contextual sidebar
(`ShowContextualSidebar`, a 236px `Border` docked left, `MainWindow.axaml:277-281`) is a single
`ScrollViewer` > `StackPanel` holding one visibility-gated content block per screen —
`IsVisible="{Binding IsLibrary}"` (line 285), `IsSmart` (422), `IsReading` (589), `IsEvents` (662).
Each block is hand-authored XAML, not one repeated `ItemsControl`/`DataTemplate` the way the card
grids are: a mix of focusable `Button.sideItemButton` rows (e.g. "All Series", each collection,
each content-type filter) interleaved with non-focusable `TextBlock.sideHeading` group labels
("COLLECTIONS", "CONTENT TYPE", "CUSTOM", "SERIES", "NOVELS", etc. — Library alone has at least
three such groups per the `sideHeading` occurrences at lines 317/399/459). This is the real
difference from the card-grid case: there's no single reusable `ItemsControl` wrapper like
`GridKeyboardNavigation.TryHandleArrowKey` to drop in per screen, because these aren't
`ItemsControl`-templated items.

**What Tab already does vs. what's missing:** Tab already reaches every `Button.sideItemButton` in
document order (headings are `TextBlock`s, not tab stops, so Tab already skips them correctly) —
that's the P5 baseline the sibling plan's Step 8/9 correction pointed to. What's missing is
`Up`/`Down` moving focus *between sidebar rows specifically*, without leaving the sidebar the way
Tab does once it reaches the last row (Tab from the sidebar's last item continues into the main
content area; a Library/Explorer-style sidebar keeps Up/Down scoped to the list itself, matching
this app's own card-grid convention where arrow keys don't leave the grid either).

**Design — reuse the existing pure math, write a new live-control wrapper:**
`GridKeyboardNavigation.Navigate<T>`'s core (`GridKeyboardNavigation.cs:30-63`) already handles this
correctly for a single-column list without modification: `Up`/`Down` finds the nearest row
above/below by `Bounds.Y`, which for a one-column list is just "the previous/next button" — no new
pure logic needed. `Left`/`Right` naturally no-op (single column, nothing beside any row), which is
the right behavior here anyway (unlike the grids, there's no "move to the equivalent column in the
row above" case for a 1-wide list). What's new is the live wrapper, since
`TryHandleArrowKey`'s `ItemsControl.ContainerFromIndex` approach doesn't apply to hand-authored
`Button`s in a `StackPanel`:

- A `KeyDown` handler on the sidebar's outer `Border`/`ScrollViewer` (`MainWindow.axaml.cs` — new
  file if one doesn't already exist for this view, confirm at implementation time), collecting
  every visible, enabled `Button` with the `sideItemButton` class from the currently-active
  `IsVisible` block's logical/visual subtree (`GetVisualDescendants().OfType<Button>()` filtered by
  `Classes.Contains("sideItemButton")`, in visual order — visual order already matches intended
  nav order since these are declared top-to-bottom in a `StackPanel`), building `GridItem<Button>`
  from each one's `Bounds`, then calling `GridKeyboardNavigation.Navigate` and focusing the result.
- Scoped per active screen (only the currently-`IsVisible` block's buttons are collected) so
  switching screens doesn't leave stale targets from a hidden block.
- Same "always handled on Up/Down/Home/End" contract as `TryHandleArrowKey` (returns/marks handled
  even on a clamped boundary no-op), so arrow keys don't bubble up and scroll the sidebar's own
  `ScrollViewer` out from under the focused row.

**Confirm at implementation time, not assumed here:** whether one shared handler on the sidebar
`Border` (dispatching by whichever `IsVisible` block is active) is cleaner than one handler per
screen's block — the four blocks differ enough in structure (Library nests a `TextBox` for inline
collection-rename, Events has delete-confirm rows) that a single generic collector may need small
per-block exceptions (e.g. skip a row mid-rename so arrow keys don't yank focus out of an active
`TextBox` edit). Read all four blocks in full before committing to one shared implementation shape.

## Library grid actions

**`Ctrl+A` — select all:**
Add a `KeyBinding` (or a `KeyDown` handler alongside `LibraryScreen.axaml.cs`'s existing
`OnCardKeyDown`, matching whichever wiring shape that file already uses for `GridKeyboardNavigation`
calls) invoking `SelectAllVisibleIssuesCommand` or `SelectAllVisibleSeriesCommand` depending on
which grain the grid is currently displaying (issue-tile vs. series-card — confirm the exact
view-mode flag `LibraryScreenViewModel` uses to distinguish them at implementation time).

**`Delete` — remove selected:**
Locate the existing remove-from-library command (bound today only to a toolbar button and/or
context-menu entry — confirmed to exist per project history, exact command name not pinned down in
this review pass) and bind `Delete` to it when the grid has focus and `HasSelection` is true.
Confirm at implementation time whether that command already gates on a confirm dialog itself, or
whether the confirm step lives in the calling view — the keyboard path must go through the same
confirm gate the toolbar button does, not bypass it.

**`/` — focus search box:**
A `KeyBinding` scoped to the library grid (or a `KeyDown` handler alongside the existing card-grid
key handling) that calls `.Focus()` on the `SearchBox` (`LibraryToolbar.axaml:173`). Only fires when
focus is somewhere in the grid, not globally — typing `/` while some other text field has focus
should type a literal `/`, not steal focus.

**`Esc` in search box clears and returns focus:**
Edit `OnSearchBoxKeyDown`'s existing `Key.Escape` case (`LibraryToolbar.axaml.cs:55-58`):
- If `SearchQuery` is non-empty: clear it (goes through the existing `SearchQuery` setter, so the
  normal reload/save path fires unchanged) and call `.Focus()` on whatever the grid's own root
  `ItemsControl` is, instead of (or in addition to) the current `CloseSuggestionsCommand.Execute`.
- If `SearchQuery` is already empty: leave today's behavior (close suggestions only) — matches the
  draft's own L7 requirement ("only while filter box has text").

## Error handling

- `Ctrl+Tab`/`Ctrl+Shift+Tab` while `CurrentScreen` is a drill-down screen not in `RailOrder`
  (Reader, Detail, MangaDetail, BookDetail, etc.) → no-op, same "nothing to cycle" behavior as
  `IsLateralScreen` already models elsewhere in `MainViewModel`.
- `Ctrl+A`/`Delete` with zero visible items → no-op (`SelectAllVisibleIssuesCommand`'s existing
  `CanExecute`/empty-list behavior, unchanged).
- `Delete` with no selection → no-op (gated on `HasSelection`, same property the context-menu
  `ClearSelection` entry already uses per `LibraryContextMenuBuilder.cs:84`).
- `/` fired while the search box itself already has focus → harmless no-op (focusing an
  already-focused control).
- Sidebar `Up`/`Down` at the first/last row of the active block → clamps (matches
  `GridKeyboardNavigation.Navigate`'s existing no-wraparound behavior), same as the card grids.
- Sidebar arrow keys while focus is inside an in-progress inline edit (e.g. Library's
  new-collection-name `TextBox`, `MainWindow.axaml:327-329`) → must not steal focus mid-edit; the
  collector should exclude that row (or the handler should no-op whenever the currently focused
  element is a `TextBox`, not one of the collected `Button`s) — confirmed as a real interaction
  hazard by reading the Library sidebar block, not a hypothetical edge case.
- A sidebar block with zero rows visible (e.g., no collections yet) → no-op, same "nothing to
  navigate to" behavior as the card-grid case.

## Testing

- New `MainViewModel` cycle commands: unit tests mirroring whatever pattern
  `MainViewModelTests.cs` already uses for `NavigateBackCommand` — forward/back through all 7
  `RailOrder` entries including wraparound at both ends, no-op when `CurrentScreen` isn't in
  `RailOrder`.
- New library commands: unit tests in `LibraryScreenViewModelTests.cs` for the `Ctrl+A`/`Delete`
  gesture handlers' underlying logic (the commands themselves are already tested — this covers only
  the new "does the gesture reach the command" wiring, same "smoke test, not re-verify the math"
  principle `2026-08-31-keyboard-operability-plan.md`'s Step 9 already established for grid-nav
  rollout).
- `OnSearchBoxKeyDown`'s edited `Escape` case: a test confirming `SearchQuery` clears and stays
  unchanged appropriately based on whether it started empty.
- Sidebar movement: since `GridKeyboardNavigation.Navigate`'s pure core is reused unchanged, its
  existing `GridKeyboardNavigationTests.cs` coverage needs no changes. The new live wrapper gets one
  smoke test per screen block (Library/Smart/Reading/Events) confirming the handler is attached and
  the row collector finds exactly the expected `Button` set (excluding headings and, for Library,
  the in-progress-rename row) — mirroring the "wiring smoke test, not re-verify the math" principle
  the sibling plan's Step 9 already established for card-grid rollout.
- Actual on-screen verification (gesture reaches the command when focus is in a `TextBox` elsewhere
  in the window, `Ctrl+,` doesn't collide with any OS/IME comma-key convention, cycling feels right,
  sidebar Up/Down feels right on all four screens including Library's grouped-with-headings layout
  and doesn't hijack focus during an inline collection rename) is manual-only, per this project's
  standing caveat for input-focused specs — budget real time for it, not skipped.

## Roadmap

Once landed: add a `docs/Paperbunkr-Roadmap.md` Beta-backlog entry noting this closes the app-wide/
library remainder of the original external keyboard-control draft, and cross-reference from
`2026-08-31-keyboard-operability-design.md`'s own scope note so a future reader sees both specs
that together cover "keyboard operability" rather than assuming either one is the whole story.
