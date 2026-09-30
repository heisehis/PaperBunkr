# Keyboard focus reclaim, Phases 2-6 — Design
*Extends: docs/superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md (Phase 1: Library, user-verified 2026-09-29).*

## Scope

Rolls the Phase 1 mechanism (`FocusReclaimer` + `VirtualizedFocus`) out to the rest of the app in one pass, per "all of them at once":
Continuity, Books, Preferences, Wanted, and the MainWindow contextual sidebar. Reading Lists and Library already have it.

Two failure shapes, unchanged from Phase 1:
- **Shape A** - sibling views permanently attached and toggled by `IsVisible`. Switching hides whatever held focus and nothing takes its place.
- **Shape B** - a bound collection resets or loses a row in place while the view stays visible. The focused control is detached and focus is gone.

## What changes in the shared helpers

`FocusReclaimer` gains three things:

1. **Conditional content-change triggers.** Attach and `IsVisible` triggers stay unconditional `Reclaim()` (the view was just shown; it should own focus).
   Content-change triggers must not steal focus from a sibling region, because a list reload can fire while the user is in the sidebar or the nav rail.
   - `ReclaimIfFocusWithinOrNowhere()` - the condition is captured **synchronously at signal time**, then the reclaim is posted. Used by Continuity,
     Books, Preferences, Wanted.
   - `ReclaimIfFocusLost()` - for a region that cannot tell from the signal whether it held focus. Only reclaims if, once deferred, focus is genuinely gone.
     Used by the sidebar together with a last-focus-was-in-the-sidebar flag.
2. **Usable-focus check.** A focused element that has itself become invisible (hidden by an `IsVisible` flip, not detached) is inside the region by ancestry
   but cannot receive keys. The post-check now treats it as focus lost. `IsInside` is unchanged.
3. **`FocusFirstButton(scope, prefer)`** - shared fallback: the first focusable, enabled, effectively visible `Button` under a scope, preferring a predicate
   (an "active" tab, a checked toggle).

## Per screen

| Screen | Region | Fallback target | Content triggers |
|---|---|---|---|
| Continuity | screen | active view-toggle chip (`Button.segToggle.on`), else the empty prompt's first button | `Page`, `DetailView`, `Timeline.Sections` |
| Wanted | screen | last-used row of the visible `vlist` (`VirtualizedFocus.FocusIndex`), else the active tab header (`Button.tab.active`) | `ActiveTab`, `QueueItems`, `SeriesRows`, `ReleaseDays` |
| Books | screen | last-used card (control + index, remembered via `GotFocus`), else the first card of the first list | `IsGrouped`/`GroupField`, `Books`, `Groups` |
| Preferences | screen | active nav item (`Button.prefNavItem.active`), or the first search result while searching | `ActiveSection`, `IsSearching` |
| MainWindow sidebar | `Border.contextualSidebar` (`x:Name="ContextualSidebar"`) | the `sideItemButton` at the last-focused row position (clamped) | `CollectionChanged` on the ten sidebar lists, or a replaced `ItemsSource` |

Notes that are not obvious from the code:
- **Books** has two kinds of `ItemsControl` under one screen: the flat list and, when grouped, an outer Groups control whose rows contain one inner card list each.
  The active card lists are found by filtering on `Items[0] is BookCardSample`, which excludes the outer control and the series-header button.
- **Books** cards had the same default keyboard-focus rectangle stacking with the custom glow ring as Library did; `Button.card` gets `FocusAdorner="{x:Null}"`.
- **Sidebar**: the list's own `CollectionChanged` handler detaches the focused row before ours runs, so `ReclaimIfFocusWithin` cannot work (focus is already gone).
  A window-level `GotFocus` handler records whether the last focus landed in the sidebar and which row; the list handlers reclaim only when that flag is set and focus
  is gone. The flag clears as soon as focus lands anywhere else, so a screen navigation followed by a sidebar reload never pulls focus back.
- **Continuity/Wanted/Preferences** bodies are Shape A. Their `Page`/`ActiveTab`/`ActiveSection` switches are the trigger; with `ReclaimIfFocusWithinOrNowhere` a
  switch made from the sidebar or rail (focus elsewhere) leaves focus where it is.

## Out of scope
- Home, Insights, Smart Lists and the reader screens (they are not lists of focusable rows that reset in place; not reported).
- Changing arrow-key navigation itself (`GridKeyboardNavigation`).

## Testing
Headless view tests per screen (new files, following `LibraryScreenViewTests` and its override-and-restore token harness): initial focus with no prior click, and
that each switch/reset above leaves focus inside the screen. Sidebar: a headless `MainWindow` is too heavy, so the sidebar path is covered by unit-level tests on
`FocusReclaimer` (`ReclaimIfFocusLost` semantics) plus an on-screen check. On-screen verification is the only real check for the sidebar and for how the fallback
target *looks* (ring on the active chip/nav item).
