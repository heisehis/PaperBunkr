# Focus audit: every screen, dialog and dropdown (2026-11-12)

This audit was done before the next update. It came with three reported problems: a dropdown scrolled the page behind it once its list reached the end; keyboard focus inside a dialog
(the scraper, for example) jumped back to the nav rail; and multi-select on the Detail screen was slow. All three are fixed (see "Fixes").

## Method

- Every `Views/*Screen` and Preferences section was checked for the focus plumbing the input-service design (§14) expects: `ScreenInput.Attach`, a `FocusReclaimer`,
  arrow moves that stay inside the screen (`FocusReclaimer.TryMoveDirectionally`) and any tunnel key handlers.
- Every modal surface (`OverlayShell`) was checked for what happens to focus on open, on Tab, on an arrow nothing handles, on a content swap and on close.
- Every `Popup` was checked for where a wheel turn goes once the popup cannot scroll any further. Avalonia 12.1.1 was decompiled to confirm the routing.

## Screens

| Screen | Input scope | Focus reclaim | Arrows stay in the screen | Notes |
|---|---|---|---|---|
| Home | yes | yes | yes | |
| Library | own (tunnel handlers) | yes | yes (2 regions) | tunnel card keys, see CLAUDE.md |
| Books | yes | yes | yes | 7 popups, all `PopupFit` |
| Book Detail | yes | yes | yes | |
| Detail (comic) | yes | in `DetailTabs` | yes | multi-select was slow, fixed |
| Manga Detail | yes | yes | yes | |
| Continuity / Events | yes | yes | yes | holds 2 in-screen dialogs (Compare, Smart fill) |
| Insights | yes | yes | yes | |
| Smart lists | yes | yes | yes | template gallery dialog already had Tab cycling |
| Wanted | yes | yes | yes (tunnel) | |
| Reading Lists | yes | in gallery/page/review views | in gallery view | |
| Preferences (all sections, plugin manager) | yes | yes | yes | sections rely on the screen's region |
| Issue / Bulk Issue / Bulk Series properties | yes | no (they are dialogs) | yes | now get focus when their dialog opens |
| Comic / Book / PDF reader, Compare | reader scopes | n/a | n/a | the arrow keys turn pages here on purpose |

The screens themselves were in good shape. Every browsing screen keeps its arrow moves inside itself, and every list that reloads gets its focus back.
The gaps were all in what sits **on top of** a screen.

## Dialogs (65 `OverlayShell`s: 60 in MainWindow, 5 inside screens)

Problems found (all fixed):

1. **Nothing put focus into a dialog when it opened.** Focus stayed on the button that opened it, or went nowhere when the opener was a menu item. With nothing focused, Avalonia starts its
   search from the window's first control, which is the nav rail. The first arrow, Tab or D-pad press therefore landed on the rail. This was the reported scraper behaviour.
2. **A Left press that nothing in the dialog used went to the nav rail.** `MainWindow.OnArrowKeyFallback` sends any unused Left to the rail, and it did not know about dialogs.
3. **Arrow keys did nothing else in a dialog**, because no dialog had a directional fallback (screens have one, dialogs did not).
4. **Tab walked out of the dialog** into the page behind. Only the Smart template gallery and the grouped-review card cycled.
5. **The scraper's modal host swaps its content between steps** (match review, issue review, preview, file conflict). Each swap detached the focused control, so every step started
   from the rail again.
6. **Closing a dialog dropped focus** unless it set `Reveal.ReturnFocusTo`, which almost none do.
7. The controller had the same problem: `UiNavigation.FocusFirst` and `Move` picked the window's first control.

## Dropdowns, menus and flyouts (30 `Popup`s in 13 files, the `SuggestBox` template, every context menu)

With `Win32PlatformOptions.OverlayPopups`, a popup's host (`OverlayPopupHost`) passes events on to its logical parent, the `Popup` in the page (`InteractiveParent => Parent`,
confirmed by decompiling). A scroll viewer only marks the wheel handled while it can still move. So once a dropdown list reached its end, the next notch bubbled into the page's scroll
viewer and the page scrolled behind the open dropdown. `ScrollViewer.IsScrollChainingEnabled="False"` would only have covered popups that can scroll; one class handler covers all of them.

Side note, not changed: 6 popups have no `PopupFit.ToWindow` (EventMapView, LibraryPreviewPanel, LibraryScreen, StatusBar, Wanted ReleasesView, the FormControls template). They are
fixed-size peeks and menus, not long lists, so none of them can currently run off the window.

## Detail screen multi-select

Each Ctrl+click, Ctrl/Shift+Space and Shift+arrow step called `DetailScreenViewModel.RefreshForSelection`, and every call rebuilt the whole hero:

- with 2+ tiles selected, it reloaded the series, all its issues and their tags from the database, plus the band and the meta badges;
- with 1 tile selected, it loaded the issue and redid a 1600×680 Gaussian backdrop blur on the UI thread.

The rebuild ran once per tile even though the hero shows the same whole-series view for 2, 3 or 20 selected tiles.

## Fixes

| Problem | Fix | Where |
|---|---|---|
| Page scrolls behind a dropdown | the wheel is marked handled at the popup's root (`OverlayPopupHost` and `PopupRoot` class handlers), after everything inside the popup has had it | `Controls/PopupWheelContainment.cs`, installed in `App.OnFrameworkInitializationCompleted` |
| No focus on open | an open shell focuses its first control (never the ✕), deferred to `Loaded`. The focus ring shows if the dialog was opened from the keyboard and stays hidden if it was opened with the mouse | `OverlayShell.FocusInitial` |
| Tab escapes | `KeyboardNavigation.TabNavigation = Cycle` while open | `OverlayShell.OnIsOpenChanged` |
| Arrows do nothing / Left goes to the rail | the shell's bubble handler moves to the nearest control inside the card and always keeps the arrow. MainWindow's rail fallback stands down while a dialog is open | `OverlayShell.OnShellKeyDown`, `MainWindow.OnArrowKeyFallback` |
| Focus lost on a content swap, or left behind on the page | a window tunnel handler, ahead of the input service: a navigation key with focus outside every open dialog puts focus into the topmost one | `OverlayShell.TryRedirectKey`, `MainWindow` constructor |
| Controller starts at the rail | `UiNavigation.FocusFirst` / `Move` go into the topmost dialog | `Services/Input/UiNavigation.cs` |
| Focus dropped on close | `Reveal` records what had focus when the surface opened and gives focus back to it on close (an explicit `ReturnFocusTo` still wins) | `Controls/Reveal.cs` |
| Slow multi-select | a selection-only change that leaves the hero showing the same thing (same single issue, or the series) skips the rebuild and only refreshes the edit label. The last 6 blurred issue backdrops are cached, keyed on the cover bitmap | `DetailTabsViewModel.IsSelectionOnlyChange`, `DetailScreenViewModel.RefreshForSelection` / `IssueBackdrop` |

"Inside a dialog" counts a dropdown or menu opened from inside it. The check walks from a popup host to its `Popup`, because popup content is not a visual descendant of its anchor.

## Verification

- New: `Controls/OverlayShellFocusTests` (7 tests: focus on open, return on close, arrows contained, Tab cycles, stray-key redirect, closed shell not modal, wheel contained in a popup).
  New: `DetailScreenViewModelTests.ExtendingAMultiSelection_DoesNotRebuildTheSeriesHero`.
- Passing: OverlayShell / OverlayShellMotion / Detail* / ScreenInput (255); RealWindowKeyboard, NavRail, UiNavigation, Gamepad, TextEntryMode, SuggestBox, every `*FocusTests`, FocusGaps,
  FocusReclaimer, GridKeyboardNavigation (144, fast set); RealWindowKeyboard slow sweeps (15).
- **Not yet seen on screen.** It still needs a hands-on pass in the real app: scraper flow from the keyboard, a long dropdown scrolled past its end, Ctrl+click across a large series.
