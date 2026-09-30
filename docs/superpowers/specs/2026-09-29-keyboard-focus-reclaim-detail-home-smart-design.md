# Keyboard reach for Detail, Home, Smart Lists and Insights — Design
*Follows: docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md (its "Out of scope" listed Home, Insights and Smart Lists).*
*Status (2026-09-30): built. Home user-confirmed on screen; every other screen covered headless only.*

## Goal
Every screen in the app can be reached and driven from the keyboard, with Paperbunkr's glow ring (not the theme's white/black frame) showing where
focus is. Same contract as the earlier rollout: with no prior click, focus lands inside the screen; focus survives a tab, section, view-mode or
collection reset; a reset that happens while focus is in a sibling region (rail, sidebar) never pulls focus away from it.

## Mechanism
- `FocusReclaimer` (`Views/FocusReclaimer.cs`): one instance per screen, `Reclaim()` on attach and on `IsVisible` turning true,
  `ReclaimIfFocusWithinOrNowhere()` on bound-collection resets, `ReclaimIfFocusLost()` on tab or mode switches. Additions:
  - `TryStepVertically` - Up/Down to the nearest focusable, for row Buttons.
  - `TryMoveDirectionally` - screen-wide arrow fallback: an arrow nothing else handled moves to the nearest control in that direction.
  - `InRegion(region)` - search options for both: `FindNextElement` searches the whole window unless `SearchRoot` is set (it found the nav rail,
    status bar and closed-modal buttons); `IgnoreOcclusivity` so below-the-fold controls still count.
  - `BringIntoViewWithRing` - scrolls a control into view with room for its ring.
- `GridKeyboardNavigation` (shared grid arrow helper): the non-virtualized path focused the item container (a `ContentPresenter`, not focusable)
  and handed the panel the tile instead of its container - arrows silently did nothing in every `WrapPanel` grid (Detail issue tiles, Book series
  cards, Smart results). Now focuses the control inside the container, as a keyboard move, with ring-aware scrolling.
- `AccordionPanel` (the Home Spotlight): same container trap - Left/Right never moved focus. Fixed the same way.

## Focus ring
- **App-wide**: one `:is(Control)` style in `Styles/Primitives.axaml` sets a `FocusAdornerTemplate` that draws `PbGlowRing` inside an outer Border
  inflated 16px (room for the Vivid tier's halo). Controls with a ring of their own set `FocusAdorner` null locally, which beats the style.
- **Paint inside your own bounds.** Avalonia only repaints inside a visual's bounds, so a ring painted outside shows partly or not at all (the
  Library lesson of 2026-09-26, repeated on Home). Home's `PosterTile` has a 6px gutter (`Padding`) its ring sits in; shelves take the gutter back
  out of their margins and spacing so covers don't move.
- **No clipping ancestors.** Plain `ItemsControl`s clip by default and cut rings at list edges: a global `ItemsControl { ClipToBounds=False }`.
  `UserControl`s clip too - `PosterTile`, `DetailTabs`, `DetailBand`, `ReadingStatusPicker`, `PosterRail`, `SettingsRow`, `SettingsTabStrip` and
  the Home section host no longer do. Horizontal scrollers (Home shelves, `PosterRail`) reach 8px past their column with content stepped back in.
- **Inside ring** where the container must clip: `PbGlowRingInset` (built by `ThemeService.BuildGlowRingInset`, follows skin and glow tier) on
  the Detail view-mode pill segments and the full-width Preferences nav items.
- Hover keeps its old faint edge on Home tiles, so a hovered tile and the keyboard-focused tile never look alike.

## Per screen

| Screen | Gap found | Change |
|---|---|---|
| Home (`HomeScreen`, `PosterTile`) | Tiles neither focusable nor keyboard-activatable; no arrows; rebuilds dropped focus | Enter/Space open, Delete dismisses (deferred a tick, focus moves to the neighbour), Left/Right/Home/End walk the visual row (Because-You-Read's lead card included), Up/Down to the next row. Default focus: the Spotlight's open panel. The Spotlight is one group: Left/Right switch panels, Up/Down leave it, arriving lands on the open panel, rotation pauses while focused. Search box gives up Up/Down |
| Smart Lists | No arrow handler on series/novel cards; resets dropped focus; Grouped Review unmanaged | Arrow handler on all cards; reclaimer on the result collections; Grouped Review moves focus in, restores it on close, Escape closes |
| Comic detail (`DetailTabs`, shared with Manga) | Enter/Space only toggled selection; switches and resets dropped focus; arrows dead (grid helper) | Enter opens, Space focuses, Ctrl/Shift+Space toggles the selection. Reclaimer on `IssueGroups`, `Specials`, `ActiveTab`, `IssueViewMode` |
| Manga detail | Chapter rebuilds dropped focus; Related/Details/Activity left focus nowhere | Reclaimer on `ChapterGroups`/`MangaActiveTab`: first chapter row, else the active tab header. Up/Down step chapter rows |
| Book detail | Book/series modes are sibling panels; series-card arrows dead (grid helper) | Reclaimer on `Mode`, `SeriesBooks`, `Chapters`, skipping the back link; Up/Down step chapter/bookmark rows |
| Insights | Tab bodies permanently attached | Reclaimer falling back to the active tab header; tab content gets top room for its first row's ring |
| All | Closed modals (`modalScrim`) were only faded out, so their buttons stayed Tab stops | Disabled while not `.open` |

## Not covered
- Publisher/character detail and the Plugin screen have no headless setup; Plugin screen UI belongs to the plugin.
- Readers and editors keep their own key handling; the readers keep `FocusAdorner` null (no ring, at the user's request).

## Testing
Headless (`FocusTestHarness`): `PosterTileKeyboardTests`, `AccordionPanelKeyboardTests`, `DetailTabsFocusTests`, `HomeAndSmartFocusTests`,
`InsightsScreenFocusTests`, `MangaDetailScreenFocusTests`, `BookDetailScreenFocusTests`, `FocusRingRenderTests`, plus a `FocusRings_AreNotClipped`
audit (`FocusTestHarness.ClippedFocusRings`) on Home/Smart, Detail (all views), Manga (all tabs), Book (both modes), Insights, Wanted, Books,
Continuity, Library, Preferences and Reading Lists. `FocusTestHarness.AppResources()` loads the real tokens from `Styles/AppTokens.axaml` (moved out
of `App.axaml` for this); never build a throwaway `App` for it - a second FluentAvaloniaTheme leaks a platform-event hook. The key fixes were
mutation-checked (each test fails on the old code). Full suite 4876/4876 on 2026-09-29.
