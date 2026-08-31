# Comprehensive Keyboard Operability

**Split out from:** `docs/superpowers/specs/2026-08-30-app-shell-navigation-history-design.md`, whose
own brainstorm surfaced this as a distinct, larger subsystem (different mechanisms, different
screens, little code overlap with the shell's back/forward/breadcrumb work) and deliberately deferred
it here.

## Background

P5 (`docs/alpha-todo.md`, shipped 2026-08-09) delivered Tab order/focus traversal across the original
6 rail-nav screens, keyboard access for the original dialogs (Issue Properties, Bulk Editing,
Preferences), standard Enter/Space/Esc shortcuts, and 2D spatial arrow-key movement through Library
cards and Detail issue tiles (`docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-
design.md`). Everything built since then inherited none of it automatically. A survey (reading the
actual current files, not assuming) found three concrete, unrelated gaps:

1. **Context menus have zero keyboard trigger anywhere.** `ContextMenuHost`
   (`src/Paperbunkr.App/Controls/ContextMenuHost.cs`) is the one shared mechanism screens use for
   right-click menus (`MenuFlyout`, since a plain `ContextMenu` popup — confirmed by reading this
   file's own doc comment — doesn't render at all in this Avalonia 12 + FluentAvalonia build). It's
   wired exclusively to `PointerReleasedEvent` filtered to `MouseButton.Right`. Currently adopted by
   Library and Books only.
2. **Four context menus are dead code today, for everyone, not just keyboard users.** They still use
   the plain `<ContextMenu>` element that `ContextMenuHost`'s own doc comment says doesn't render:
   `MangaDetailScreen.axaml` (chapter row), `BookDetailScreen.axaml` (bookmark row and series-mode
   book card), `ReaderScreen.axaml` (page thumbnail scrubber).
3. **Spatial 2D grid navigation** (`GridKeyboardNavigation.Navigate<T>`, a reusable, panel-agnostic
   core already extracted to pure math) is wired into exactly two places: Library's card grid and
   Detail's issue tiles. Books, BookDetail's series-mode cards, Smart Lists' results grid, and
   Continuity/Events cards have none.

CE precedent isn't directly applicable here — CE's WinForms `ContextMenuStrip`/`ListView` controls get
keyboard operability (Menu key, arrow-key selection) from the platform for free; this app's custom
Avalonia `MenuFlyout`/`WrapPanel` card-grid pattern has no such built-in equivalent, so this is
genuinely new plumbing, not a port of CE behavior.

## Scope

**In scope:**
1. Keyboard trigger (Menu key / Shift+F10) added to `ContextMenuHost` itself — the one shared
   mechanism, so this fixes/enables it everywhere it's used, now and for future adopters.
2. Migrate the 4 dead `<ContextMenu>` instances to `ContextMenuHost`/`IContextMenuProvider` — fixes a
   real bug (they render for no one today) and gets keyboard support from the same mechanism.
3. Add new context menus (via the same mechanism) to 4 screens that have none today: comic
   `DetailTabs` issue tiles, Continuity/Events cards, the Collections editor's member list, Reading
   Lists member rows. Content mirrors each screen's own existing commands — not new functionality.
4. Roll `GridKeyboardNavigation` out to Books, BookDetail's series-mode cards, Continuity/Events
   cards, and Smart Lists' results grid.
5. A verification pass (not a design change) confirming existing toolbar Flyout/Popup triggers
   (Library's View & Sort, Filter, etc. — already `Command`-bound `Button`s) are keyboard-operable,
   fixing only what's actually found broken.

**Explicitly out of scope:**
- Any change to menu *content* beyond mirroring what each screen's ViewModel already exposes as a
  command — no new actions invented for this spec.
- Collections' own grid view — it reuses Library's main grid (confirmed by reading the Collections
  design spec), already covered by Library's existing `GridKeyboardNavigation` wiring. Only the
  Collection editor overlay's own member *list* (linear, not a spatial grid) is in scope, and only
  for its context menu (§3 above), not spatial nav.
- Rebuilding `ContextMenuHost`'s pointer-based path — untouched, this is purely additive.

## Keyboard trigger for `ContextMenuHost`

`OnProviderChanged` currently registers one handler:
```csharp
host.AddHandler(InputElement.PointerReleasedEvent, (_, pe) => OnPointerReleased(host, state, pe),
    RoutingStrategies.Bubble, handledEventsToo: true);
```
Add a second:
```csharp
host.AddHandler(InputElement.KeyDownEvent, (_, ke) => OnKeyDown(host, state, ke),
    RoutingStrategies.Bubble, handledEventsToo: true);
```
`OnKeyDown` fires the same menu-building/showing logic as `OnPointerReleased`, triggered by
`e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift)`, with two
differences from the pointer path:
- **Target lookup**: starts from `TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() as
  Visual` instead of `e.Source as Visual` — same `DataContextChain` walk from there, unchanged.
- **Anchor**: `flyout.ShowAt(focusedControl)` (no `showAtPointer` argument) instead of
  `flyout.ShowAt(host, showAtPointer: true)` — there's no pointer position to anchor to for a
  keyboard-triggered menu, so it opens attached to the focused control itself, matching how a
  Windows/CE-style Menu-key invocation anchors to the focused item.

No focused element, or a focused element with no entries from any ancestor in its `DataContextChain`
→ silent no-op, matching today's already-established "right-click on empty space does nothing"
behavior.

## Migrating the 4 dead menus + adding 4 new ones

Both use the identical shape: a new `IContextMenuProvider` implementation (or an existing screen
ViewModel implementing it directly, matching `LibraryScreenViewModel`'s/`BooksScreenViewModel`'s
existing pattern) building a `List<ContextMenuEntry>` from the screen's own existing commands, plus
`controls:ContextMenuHost.Provider="{Binding}"` on the screen's root element.

**Dead menus → live, ported content unchanged:**

| Screen | Item | Menu content (verbatim from the dead XAML) |
|---|---|---|
| `MangaDetailScreen` | chapter row (`ChapterGroupsList`) | Edit Properties, Show in Explorer, — Mark as Read, Mark as Unread, — Set Cover…, Reset Cover |
| `BookDetailScreen` | bookmark row | Delete Bookmark |
| `BookDetailScreen` | series-mode book card | Edit… |
| `ReaderScreen` | page thumbnail | Page Type ▸ (Story / Cover / Advertisement / Deleted), Rotate ▸ (0° / 90° / 180° / 270°) — nested submenus via `ContextMenuEntry.Children`, already supported by `ContextMenuHost.Build`'s existing recursion |

**New menus, content mirrored from each screen's own existing single-item commands:**

| Screen | Item | Menu content | Backing commands (already exist) |
|---|---|---|---|
| comic `DetailTabs` (Issues tab) | issue tile | Edit Properties, Open in Reader, Quick Rate… | `DetailTabsViewModel`'s existing `goToProperties`/`openInReader`/`onQuickRate` callbacks |
| Continuity/Events | event/continuity card | Edit details, Delete | Existing sidebar "⋯ Manage" menu actions, exposed per-card |
| Collections editor | member row (Series/Issue/Book) | Remove from Collection | Existing member-removal flow in `CollectionPropertiesScreenViewModel` |
| Reading Lists | member row | Remove from List, Mark Read, Mark Unread, Set Role… | Existing bulk commands (`RemoveSelectedMembers`, `MarkSelectedRead`/`Unread`, `SetRoleForSelectedMembers`), invoked with a single-item selection |

## Spatial grid navigation rollout

Four screens wired to the existing, unchanged `GridKeyboardNavigation.Navigate<T>` core, following
the same pattern `LibraryScreen.axaml.cs`/`DetailTabs.axaml.cs` already establish (a `KeyDown` handler
building `GridItem<T>` from each realized child's `Bounds`, calling `Navigate`, moving focus to the
result):

- Books grid (plain `WrapPanel`, both its templates)
- BookDetail series-mode book cards (plain `WrapPanel`)
- Continuity/Events cards (plain `WrapPanel`, both instances)
- **Smart Lists' results grid** — uses `controls:VirtualizingWrapPanel`, this project's own custom
  virtualizing panel (built for cover-memory virtualization, per its own design spec). This is a real
  wrinkle, flagged honestly rather than assumed identical to the other three: `Navigate<T>` needs a
  `Rect Bounds` per item, but a virtualizing panel doesn't realize off-screen items, so they have no
  real bounds to hand it. Resolved at plan/implementation time via one of: (a) analytic bounds
  computed from item index + the panel's known column count/item size, independent of realization
  state, or (b) an "ensure realized + scroll into view" step before computing bounds the normal way.
  Which approach depends on what `VirtualizingWrapPanel`'s current API already exposes — read at
  implementation time, not guessed here.

## Flyout/popup keyboard operability — verification pass

Not a design change: a check that existing toolbar `Flyout`/`Popup` triggers (Library's View & Sort,
Filter, Search Mode, Add-to-List popups — all `Command`-bound `Button`s, confirmed by reading
`LibraryToolbar.axaml`) are already keyboard-operable end-to-end — `Tab` reaches the trigger button,
`Enter`/`Space` opens the popup (native `Button` behavior, no custom pointer-only handler found),
arrow keys move through the popup's own items, `Escape` closes it (`IsLightDismissEnabled="True"`
already set on all four existing popups). Fixed only if something concrete is actually found broken
during implementation — this section exists to *confirm*, not to design new behavior.

## Error handling

- Menu key/Shift+F10 with nothing focused, or a focused element whose `DataContextChain` yields no
  provider entries → silent no-op, matching today's "right-click empty space" behavior exactly.
- `GridKeyboardNavigation.Navigate` at a grid boundary (first/last item, edge row/column) clamps
  rather than wrapping — existing, unchanged, documented behavior of the core function.
- A screen with zero items in its grid/list → the relevant `KeyDown` handler is a no-op (nothing to
  navigate to or build a menu for).

## Testing

- **New `IContextMenuProvider` implementations** (8 total: 4 dead-menu fixes +
  4 new-menu additions) each get a test file mirroring the existing shape of
  `LibraryContextMenuBuilderTests`/`BooksContextMenuBuilderTests` — entries present/absent by state,
  correct commands wired, nested submenu structure for the Reader page-thumbnail case.
- **`ContextMenuHost`'s keyboard path** — a focused-element-based build/show test parallel to
  whatever the existing pointer-path test coverage looks like (surveyed at implementation time).
- **Grid navigation rollout** — the core `GridKeyboardNavigation.Navigate<T>` math is already fully
  covered by `GridKeyboardNavigationTests` and needs no re-testing; each newly-wired screen gets one
  smoke test confirming the `KeyDown` handler is actually attached and calls into it, not a full
  re-verification of the navigation math itself.
- Actual on-screen keyboard/visual verification (Menu key opening a menu in the right spot, arrow
  keys moving focus visibly, popups behaving correctly) is manual-only — no unattended GUI automation
  available in this environment, the same standing caveat as every other input-focused spec in this
  project. Given how much of this spec was already corrected by exactly this kind of manual check on
  the navigation-history work it's split from, on-screen verification here isn't optional busywork —
  budget real time for it before calling this done.

## Roadmap

Once landed, add a `docs/alpha-roadmap.md` Beta-backlog entry (matching the pattern the navigation-
history and MediaRelation specs used) and note in `docs/superpowers/specs/2026-08-30-app-shell-
navigation-history-design.md`'s own follow-up reference that this is the keyboard-operability spec it
pointed to.
