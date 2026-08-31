# Comprehensive Keyboard Operability — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-31-keyboard-operability-design.md*

Survey notes (exact current shapes, confirmed by reading the files, that change or sharpen the
design doc's own wording):

- `ContextMenuEntry`/`IContextMenuProvider`/`ContextMenuHost` already exist exactly as the design
  describes. `ContextMenuHost.OnProviderChanged` currently registers one handler
  (`PointerReleasedEvent`, Bubble, `handledEventsToo: true`) — the keyboard path is a second handler
  registered the same way, not a rewrite.
- `LibraryContextMenuBuilder` (`src/Paperbunkr.App/ViewModels/LibraryContextMenuBuilder.cs`) is the
  template every new/fixed builder mirrors: a standalone class taking the screen's ViewModel in its
  constructor, `Build(object? target) -> IReadOnlyList<ContextMenuEntry>?` switching on target type,
  `ContextMenuEntry.Item`/`.SubMenu`/`.Separator` helpers, a `Compact()` static helper that drops
  leading/trailing/consecutive separators left behind by omitted entries.
- **Real finding that changes the Smart Lists plan from the design doc's open caveat**:
  `VirtualizingWrapPanel` (`src/Paperbunkr.App/Controls/VirtualizingWrapPanel.cs`) already implements
  `protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from,
  bool wrap)` — complete, correct, virtualization-safe analytic index math (`fromIndex ± 1` for
  Left/Right, `fromIndex ± _itemsPerRow` for Up/Down), calling its own `ScrollIntoView(toIndex)` to
  realize and return the target container. This is Avalonia's native `INavigableContainer` extension
  point. It is currently **dead code for Smart Lists** — `SmartScreen.axaml`'s `ResultsList` is a bare
  `<ItemsControl>` (not `ListBox`/`SelectingItemsControl`), and a bare `ItemsControl` never invokes
  `GetControl` on arrow keys (only `ListBox`-family controls wire that automatically). So Smart Lists
  needs a *different* fix than `GridKeyboardNavigation.TryHandleArrowKey` (Step 10 below) —
  `TryHandleArrowKey` assumes every item has a realized container via `ContainerFromIndex`, which is
  false for a virtualizing panel and would silently limit navigation to whatever's currently on
  screen. The correct fix invokes Avalonia's own keyboard-navigation entry point so `GetControl`
  actually runs; **the exact public API for that (`KeyboardNavigationHandler` or equivalent) needs
  confirming against the installed Avalonia assembly at Step 10's implementation time** (same
  reflection-based verification approach already used this session for the `Key.Back` gesture-name
  fix), not guessed here.
- `GridKeyboardNavigation.TryHandleArrowKey(ItemsControl, object currentItem, Key)` is the actual
  reusable entry point (not just the pure `Navigate<T>` core) — walks `itemsControl.ContainerFromIndex`
  for every index, calls `Navigate`, focuses the resolved container. `LibraryScreen.axaml.cs`'s
  `OnCardKeyDown` is the exact pattern every non-virtualized rollout screen mirrors: a `KeyDown`
  handler on the card `Button` finding its `FindAncestorOfType<ItemsControl>()` and calling
  `TryHandleArrowKey`.
- Dead-menu content, confirmed verbatim from the current (non-rendering) XAML:
  - `MangaDetailScreen.axaml:188-198` (chapter row, `DataContext` = a chapter-group-list item type —
    confirm exact type name at Step 2): Edit Properties → `EditChapterPropertiesCommand`, Show in
    Explorer → `RevealChapterCommand`, — Mark as Read → `MarkChapterReadCommand`, Mark as Unread →
    `MarkChapterUnreadCommand`, — Set Cover… → `ChangeChapterCoverCommand`, Reset Cover →
    `ResetChapterCoverCommand`, all on `MangaDetailScreenViewModel`, all taking the row as parameter.
  - `BookDetailScreen.axaml:128-134` (bookmark row, `BookBookmarkSummary`): Delete Bookmark →
    `DeleteBookmarkCommand`.
  - `BookDetailScreen.axaml:186-192` (series-mode book card, `BookCardSample`): Edit… →
    `EditBookInSeriesCommand`.
  - `ReaderScreen.axaml:199-214` (page thumbnail, confirm exact row type at Step 4): Page Type ▸
    (Story/Cover/Advertisement/Deleted → `SetPageTypeStoryCommand`/`SetPageTypeCoverCommand`/
    `SetPageTypeAdvertisementCommand`/`SetPageTypeDeletedCommand`), Rotate ▸ (0°/90°/180°/270° →
    `SetPageRotation0Command`/`SetPageRotation90Command`/`SetPageRotation180Command`/
    `SetPageRotation270Command`), all on `ReaderScreenViewModel`.
- New-menu commands, confirmed to already exist (no new ViewModel methods needed, per the design's
  "mirror existing commands" rule):
  - comic `DetailTabsViewModel` (issue tile, `IssueCardSample`): `EditIssuePropertiesCommand`,
    `QuickRateCommand`, `OpenIssueInReaderCommand` (doc-commented "Card-view Read/Continue").
  - `CollectionMemberRowViewModel` (Collection editor member row) already has its **own**
    `RemoveCommand` (`[RelayCommand(CanExecute = nameof(CanRemove))]`, gated on `!IsRuleMatched` -
    smart-collection rule-matched rows can't be removed) — the menu provider is trivial, just
    `ContextMenuEntry.Item("Remove from Collection", row.RemoveCommand)`, no parameter needed since
    the command is already bound to `this`.
  - `ReadingListItemRowViewModel` (Reading List member row) already has its **own** per-row commands:
    `RemoveCommand`, `OpenCommand`, `ToggleReadCommand` (a single toggle, not separate Mark
    Read/Unread as the design doc guessed — corrected below), `SetRoleCommand(EventMembershipRoleOption?)`.
  - Continuity/Events cards have no per-card single-target edit/delete command - `EventsScreenViewModel`
    exposes `SelectEvent(StoryEventSummary?)` / `DeleteActiveEvent()` (no-id, acts on whatever's
    currently active) and `MainViewModel.OpenEditEventDialog`/`OpenEditContinuityDialog` (same,
    no-id). The context menu composes these: select the card first, then invoke the existing
    edit/delete action - confirmed as the right shape at Step 7, since no new "edit/delete by id"
    command needs adding.

## Step 1: `ContextMenuHost` keyboard trigger
**Files:** `src/Paperbunkr.App/Controls/ContextMenuHost.cs` (edit)
**What:** In `OnProviderChanged`, add a second `AddHandler(InputElement.KeyDownEvent, ...)`
registration alongside the existing `PointerReleasedEvent` one. New `OnKeyDown(Control host, HostState
state, KeyEventArgs e)`: matches `e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers ==
KeyModifiers.Shift)`; on match, walks `DataContextChain` starting from
`TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() as Visual` (reuses the existing
`DataContextChain` helper unchanged); builds/shows the flyout via the same `Build`/entry logic as
`OnPointerReleased`, but `flyout.ShowAt(focusedControl)` (no `showAtPointer`) instead of
`ShowAt(host, showAtPointer: true)`. No focused element, or no provider entries anywhere in the
chain → no-op, `e.Handled` left false.
**Depends on:** none
**Verify:** new `ContextMenuHostTests.cs` (new file, or extend if one already exists - check at
implementation time) covering: Menu key with a focused element that has entries → flyout shown;
Shift+F10 same; plain F10 (no Shift) → no-op; no focused element → no-op; focused element with no
provider match → no-op.

## Step 2: MangaDetail chapter row menu
**Files:**
- `src/Paperbunkr.App/ViewModels/MangaDetailContextMenuBuilder.cs` (new) — mirrors
  `LibraryContextMenuBuilder`'s shape exactly, one `Build(object? target)` case for the chapter-group
  item type (confirm its exact class name by reading `MangaDetailScreenViewModel`'s
  `ChapterGroupsList`-adjacent properties at implementation time), 6 entries per the survey notes
  above, 2 separators, no submenus needed.
- `src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit) — remove the dead `<Button.ContextMenu>`
  block (lines 188-198), add `controls:ContextMenuHost.Provider="{Binding}"` to the screen root
  (confirm `MangaDetailScreenViewModel` itself is the right provider vs. delegating to the new
  builder class the way `LibraryScreenViewModel` does - mirror whichever wiring shape
  `LibraryScreenViewModel`/`BooksScreenViewModel` actually use for `IContextMenuProvider`, read at
  implementation time).
**Depends on:** none
**Verify:** new `MangaDetailContextMenuBuilderTests.cs`, same shape as
`LibraryContextMenuBuilderTests.cs` (temp SQLite + `AvaloniaTestCollection`).

## Step 3: BookDetail bookmark + series-card menus
**Files:**
- `src/Paperbunkr.App/ViewModels/BookDetailContextMenuBuilder.cs` (new) — two `Build` cases:
  `BookBookmarkSummary` → Delete Bookmark; `BookCardSample` → Edit….
- `src/Paperbunkr.App/Views/BookDetailScreen.axaml` (edit) — remove both dead `<ContextMenu>` blocks
  (lines 128-134, 186-192), add `controls:ContextMenuHost.Provider="{Binding}"` to the screen root.
**Depends on:** none
**Verify:** new `BookDetailContextMenuBuilderTests.cs`.

## Step 4: ReaderScreen page-thumbnail menu
**Files:**
- `src/Paperbunkr.App/ViewModels/ReaderPageContextMenuBuilder.cs` (new) — one `Build` case for the
  page-thumbnail row type (confirm exact type at implementation time), two `ContextMenuEntry.SubMenu`
  calls (Page Type, Rotate) each with 4 children - the first real use of nested submenus in a new
  builder, exercising `ContextMenuHost.Build`'s existing recursion.
- `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit) — remove the dead `<Border.ContextMenu>` block
  (lines 199-214), add `controls:ContextMenuHost.Provider="{Binding}"` to the screen root (or the
  relevant ancestor - confirm Reader's own root structure supports this cleanly, since Reader's chrome
  is more complex than a simple screen; may need the provider attached to the page-scrubber's own
  container instead of the whole screen root if the whole-screen root has other pointer-handling
  concerns - checked at implementation time).
**Depends on:** none
**Verify:** new `ReaderPageContextMenuBuilderTests.cs`, including a submenu-structure assertion
(`entry.Children` has 4 items for each of the two parent entries).

## Step 5: comic Detail issue-tile menu
**Files:**
- `src/Paperbunkr.App/ViewModels/DetailIssueContextMenuBuilder.cs` (new) — one `Build` case for
  `IssueCardSample`: Edit Properties → `EditIssuePropertiesCommand`, Open in Reader →
  `OpenIssueInReaderCommand`, Quick Rate… → `QuickRateCommand`.
- `src/Paperbunkr.App/Views/DetailTabs.axaml` and/or `DetailScreen.axaml` (edit) — add
  `controls:ContextMenuHost.Provider="{Binding}"` at whichever root already hosts `DetailTabsViewModel`
  as its `DataContext` (confirm exact element at implementation time - `DetailTabs.axaml` is a
  sub-view, not the screen root, so the provider may need to live there instead of `DetailScreen.axaml`).
**Depends on:** none
**Verify:** new `DetailIssueContextMenuBuilderTests.cs`.

## Step 6: Collection editor member-row menu
**Files:**
- `src/Paperbunkr.App/ViewModels/CollectionMemberContextMenuBuilder.cs` (new, thin) — one `Build`
  case for `CollectionMemberRowViewModel`: `ContextMenuEntry.Item("Remove from Collection",
  row.RemoveCommand)` — no parameter, the row's own `RemoveCommand` is already bound to itself.
  `IsEnabled` follows the command's own `CanExecute` (already gated on `!IsRuleMatched` inside
  `RemoveCommand` itself - `ContextMenuHost.Build`'s `MenuItem.Command = entry.Command` picks up
  `ICommand.CanExecute` automatically via Avalonia's command binding, no extra wiring needed here).
- `src/Paperbunkr.App/Views/CollectionPropertiesOverlay.axaml` (edit) — add
  `controls:ContextMenuHost.Provider="{Binding}"` to the overlay root.
- `src/Paperbunkr.App/ViewModels/CollectionPropertiesScreenViewModel.cs` (edit) — implement
  `IContextMenuProvider`, delegating to the new builder (mirror whichever exact delegation shape
  Step 2 settles on).
**Depends on:** none
**Verify:** new `CollectionMemberContextMenuBuilderTests.cs`, including a rule-matched row → `Remove`
entry present but `IsEnabled: false` (via the command's own `CanExecute`).

## Step 7: Reading List member-row menu
**Files:**
- `src/Paperbunkr.App/ViewModels/ReadingListMemberContextMenuBuilder.cs` (new) — one `Build` case for
  `ReadingListItemRowViewModel`: Open → `row.OpenCommand`; toggle entry labeled from current read
  state (`row.IsRead ? "Mark as Unread" : "Mark as Read"`) → `row.ToggleReadCommand`; Set Role ▸
  submenu, one child per `EventMembershipRoleOption` → `row.SetRoleCommand`; — Remove from List →
  `row.RemoveCommand`, `isDanger: true`. (Corrects the design doc's guess of separate Mark
  Read/Mark Unread entries - the row only has one `ToggleReadCommand`.)
- `src/Paperbunkr.App/Views/ReadingScreen.axaml` (edit) — add
  `controls:ContextMenuHost.Provider="{Binding}"` to the screen root.
- `src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs` (edit) — implement `IContextMenuProvider`.
**Depends on:** none
**Verify:** new `ReadingListMemberContextMenuBuilderTests.cs`, including the toggle label flipping
with `IsRead`.

## Step 8: Continuity/Events card menu
**Files:**
- `src/Paperbunkr.App/ViewModels/EventsCardContextMenuBuilder.cs` (new) — one `Build` case for
  whichever card summary type `EventsScreen.axaml`'s card `ItemsControl`s actually bind (confirm
  exact type - `StoryEventSummary` is the closest confirmed name, but the *displayed* card model may
  differ, e.g. a `EventCardSample`/`ContinuityCardSample` wrapper - read `EventsScreenViewModel`'s
  card-collection properties at implementation time): Edit details, Delete. Since
  `EventsScreenViewModel` has no per-id edit/delete command, each entry's effective action is
  "select this card, then invoke the existing no-id action" - implemented either as a small
  composed `RelayCommand` added to `EventsScreenViewModel` (e.g. `EditEventFromContextMenuCommand(int
  id)` that calls `SelectEvent`/`SelectContinuity` then `MainViewModel`'s dialog opener) or, if
  `MainViewModel.OpenEditEventDialog`/`OpenEditContinuityDialog`/`DeleteActiveEvent` are reachable
  from `EventsScreenViewModel` already (check its constructor callbacks), a direct two-call compose
  inside the builder itself. Exact shape decided at implementation time against what's actually
  wired through the constructor today - this is the one builder in this plan needing a small new
  compose-command rather than a pure mirror, since no existing single command already does "act on
  this specific card" for Events.
- `src/Paperbunkr.App/Views/EventsScreen.axaml` (edit) — add
  `controls:ContextMenuHost.Provider="{Binding}"` to the screen root.
**Depends on:** none
**Verify:** new `EventsCardContextMenuBuilderTests.cs`.

## Step 9: Grid navigation rollout — non-virtualized screens
**Files:**
- `src/Paperbunkr.App/Views/BooksScreen.axaml` + `.axaml.cs` (edit) — mirror
  `LibraryScreen.axaml.cs`'s `OnCardKeyDown` exactly (`KeyDown` on the card `Button`, walk
  `FindAncestorOfType<ItemsControl>()`, call `GridKeyboardNavigation.TryHandleArrowKey`), wired to
  both of Books' `WrapPanel`-backed templates (lines ~235, ~266 per the earlier grep).
- `src/Paperbunkr.App/Views/BookDetailScreen.axaml` + `.axaml.cs` (edit, `.axaml.cs` new if it doesn't
  exist yet - check at implementation time) — same pattern for the series-mode book-card `WrapPanel`
  (line ~177-192).
- `src/Paperbunkr.App/Views/EventsScreen.axaml` + `.axaml.cs` (edit) — same pattern for both
  `WrapPanel`-backed card lists (lines ~318, ~587 per the earlier grep).
**Depends on:** none
**Verify:** `GridKeyboardNavigationTests.cs` (existing) already covers the core math and needs no
changes; each screen gets one new smoke test in its own `*ScreenViewModelTests.cs` (or a thin
`*ScreenTests.cs` if the existing test files are ViewModel-only and don't touch the View's
code-behind - confirmed at implementation time) confirming the handler is attached and delegates
correctly, not re-testing navigation math.

## Step 10: Grid navigation rollout — Smart Lists (virtualized)
**Files:** `src/Paperbunkr.App/Views/SmartScreen.axaml` + `.axaml.cs` (edit, `.axaml.cs` new if it
doesn't exist)
**What:** **Not** `GridKeyboardNavigation.TryHandleArrowKey` (per the survey note above - wrong tool,
assumes full realization). Instead, wire a `KeyDown` handler on the `ResultsList` `ItemsControl` (or
its card `Button`) that invokes Avalonia's own keyboard-navigation entry point so
`VirtualizingWrapPanel.GetControl` (already correct, already implemented) actually runs. First
sub-step: confirm the exact public API via reflection against the installed `Avalonia.Base.dll`/
`Avalonia.Controls.dll` (same approach already used this session to resolve the `Key.Back` gesture-
name question) - the likely candidate is `Avalonia.Input.KeyboardNavigationHandler`'s `Move`/`GetNext`
method or an equivalent public surface reachable from a `Control`, but this must be verified against
the real assembly, not assumed. Once confirmed, the handler translates arrow keys to a
`NavigationDirection`, invokes that API from the focused card, and focuses whatever control comes
back - mirroring `TryHandleArrowKey`'s "returns true even on a clamped no-op" contract so arrow keys
never bubble to scroll the parent `ScrollViewer`.
**Depends on:** none (but do this step after Step 9, since Step 9's simpler pattern is good context
to have fresh before tackling the one virtualization-aware case)
**Verify:** manual/on-screen only for the actual navigation feel (scrolling to reveal off-screen
items while arrowing past the viewport edge) - this is exactly the kind of thing that can't be
meaningfully unit-tested without a real layout pass, same standing caveat as this project's other
input-focused specs. If the reflection-confirmed API turns out to have a clean pure-logic seam,
add a unit test for that seam; don't force one if it doesn't.

## Step 11: Flyout/popup keyboard-operability verification
**Files:** none expected (verification only) — `src/Paperbunkr.App/Views/LibraryToolbar.axaml`
touched only if something is actually found broken.
**What:** Manually Tab to each of Library's four toolbar trigger buttons (Search Mode, Filter, View
& Sort, Add-to-List), confirm Enter/Space opens the popup, arrow keys move through its items,
Escape closes it. Same check for any new popups introduced by Steps 2-8 (the Reading List "Set
Role ▸" submenu, etc. - though submenus are `MenuFlyout`-native and already keyboard-navigable by
Avalonia's own `MenuItem` behavior, not custom popups, so likely nothing to check there specifically
beyond the top-level menu itself).
**Depends on:** Steps 1-8 (needs their new menus to exist to verify)
**Verify:** manual/on-screen only, per the design doc's own explicit call that this section is a
verification pass, not new design.

## Step 12: Full-suite verification
**Verify:** `dotnet build` on the full solution; `Paperbunkr.App.Tests`, `Paperbunkr.Data.Tests`,
`Paperbunkr.Plugins.Tests` all green; app smoke-launched via `PowerShell Start-Process` (this
project's own documented gotcha about backgrounded shell jobs) confirming no startup crash; then a
real on-screen pass (Menu key/Shift+F10 on at least one item per fixed/new screen, arrow-key
navigation on at least one rolled-out grid, the Smart Lists virtualized case specifically) - budgeted
as real time, not skipped, given how much of the navigation-history spec this one is split from was
only actually verified correct through exactly this kind of manual check.
**Depends on:** all prior steps

## Roadmap
Once landed: `docs/alpha-roadmap.md` Beta-backlog entry, and update
`docs/superpowers/specs/2026-08-30-app-shell-navigation-history-design.md`'s follow-up reference to
point at this plan as landed (matching the pattern already used for that spec's own roadmap note).
