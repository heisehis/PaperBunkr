# Grid type-ahead, Shift+arrow range-select, and Ctrl+Q — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md*

Verified against current source (not memory): `GridKeyboardNavigation.TryHandleArrowKey(ItemsControl,
Control, Key)` has 3 required params today, both its non-virtualized and virtualized paths compute a
target item/container before focusing; `TileSelectionController<T>` already has the anchor-range
logic; `LibraryScreenViewModel.ToggleIssueSelection(IssueListRow, bool)` /
`ToggleSeriesSelection(SeriesCardSample, bool)`, `DetailTabsViewModel.ToggleIssueSelection(IssueCardSample,
bool)`, and `BooksScreenViewModel.ToggleBookSelection(BookCardSample, bool)` are the exact
selection-toggle methods each screen's own Shift+Click handler already calls — range-select reuses
these, not `TileSelectionController.Toggle` directly. `Paperbunkr.Common`'s ported `cYo.Common.Text.StringUtility`
auto-initializes `Articles` (and thus `IsArticle`/`StartsWith(...,ignoreArticles)`) in its static
constructor — no setup call needed before first use.

## Step 1: `GridKeyboardNavigation` gains an optional selection-extend hook
**Files:** `src/Paperbunkr.App/Views/GridKeyboardNavigation.cs` (edit)
**What:** Add `Action<object>? onNavigated = null` as a 4th parameter to `TryHandleArrowKey`
(backward compatible — existing call sites in `SmartScreen.axaml.cs`/`BookDetailScreen.axaml.cs`
need no changes). In the non-virtualized path, after computing `target` and before/after focusing,
call `onNavigated?.Invoke(target)`. In `TryHandleArrowKeyVirtualized`, after resolving `focusable`,
call `onNavigated?.Invoke(focusable.DataContext ?? target.DataContext)` if either has a non-null
`DataContext`. Callers decide whether to pass a real callback based on their own
`e.KeyModifiers.HasFlag(KeyModifiers.Shift)` check — `TryHandleArrowKey` itself stays modifier-unaware,
matching its existing "pure core, thin wrapper" split.
**Depends on:** none
**Verify:** `GridKeyboardNavigationTests.cs` — new cases asserting `onNavigated` fires with the
correct target item for Left/Right/Up/Down/Home/End, and does not fire when null/omitted (existing
14 cases must stay green, unchanged behavior when the parameter is omitted).

## Step 2: Wire Shift+arrow range-select at the 3 real call sites
**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit, `OnCardKeyDown`),
`src/Paperbunkr.App/Views/BooksScreen.axaml.cs` (edit, `OnCardKeyDown`),
`src/Paperbunkr.App/Views/DetailTabs.axaml.cs` (edit, `OnIssueTileKeyDown`)
**What:** At each site, before calling `TryHandleArrowKey`, build the callback only when Shift is
held:
- **Library** (`item is IssueListRow or SeriesCardSample` dispatch already exists at line ~164):
  `Action<object>? extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? target => { if (target is IssueListRow row) vm.ToggleIssueSelection(row, isShiftHeld: true); else if (target is SeriesCardSample card) vm.ToggleSeriesSelection(card, isShiftHeld: true); } : null;` then pass `extend` as the 4th arg.
- **Books**: `Action<object>? extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? target => { if (DataContext is BooksScreenViewModel vm && target is BookCardSample card) vm.ToggleBookSelection(card, isShiftHeld: true); } : null;`
- **DetailTabs**: same shape, `target => { if (target is IssueCardSample issue) viewModel.ToggleIssueSelection(issue, isShiftHeld: true); }`.

No changes to `SmartScreen.axaml.cs`/`BookDetailScreen.axaml.cs` — they keep calling
`TryHandleArrowKey` with 3 args (Shift stays a no-op there, matching today).
**Depends on:** Step 1
**Verify:** New cases in `LibraryScreenViewModelTests`/`BooksScreenViewModelTests`/`DetailTabsViewModelTests`
(all three exist already) calling
`ToggleIssueSelection`/`ToggleSeriesSelection`/`ToggleBookSelection` with `isShiftHeld: true` twice
(simulating two Shift+arrow presses) and asserting the inclusive range between the two indices ends
up selected — this exercises the same `TileSelectionController` anchor logic these VMs' existing
Shift+Click tests already cover, just invoked the way keyboard would.

## Step 3: `TypeAheadSearch` — new shared component
**Files:** new `src/Paperbunkr.App/Views/TypeAheadSearch.cs`
**What:** Mirrors `GridKeyboardNavigation`'s own "pure core + thin live-control wrapper" split:
- `public sealed class Buffer` — mutable state (`CurrentText`, `LastTicks`), one instance per
  screen/grid (a field on the code-behind, not a static/shared instance — matches CE's one-`KeySearch`-
  per-`ItemView` model).
- `public static bool TryMatch<T>(Buffer buffer, char typedChar, IReadOnlyList<T> items, Func<T, string> textSelector, out T? match) where T : class` —
  pure core, no Avalonia types: ports `KeySearch.Select(char)`'s exact buffer/timeout logic
  (2500ms idle reset via `DateTime.UtcNow.Ticks` or `Environment.TickCount64`, backspace shrinks the
  buffer, only commits the new buffer text when a match is found — mirrors `if (flag) currentText = arg;`),
  matching via `cYo.Common.Text.StringUtility.StartsWith(textSelector(item), buffer, StringComparison.OrdinalIgnoreCase, ignoreArticles: true)`,
  first match in `items`' given order wins (matches CE's `FirstOrDefault`).
- `public static bool TryHandleTextInput<T>(Buffer buffer, string typedText, ItemsControl itemsControl, Func<T, string> textSelector) where T : class` —
  live-control wrapper: for each char in `typedText` (Avalonia's `TextInputEventArgs.Text` can be
  multi-char, e.g. IME composition — feed one char at a time to `TryMatch`, keep the last result),
  materializes `items` from the `ItemsControl`'s current bound collection (not realized containers —
  unlike arrow-nav, type-ahead must be able to jump to an off-screen/unrealized item), and on a
  match: clears the grid's own selection (via whatever the caller's screen already exposes — see
  Step 4, this method returns the matched item and lets the caller clear selection + focus, rather
  than owning selection-clearing itself, since that's screen-specific), scrolls it into view and
  focuses it reusing the exact same realized-container-or-virtualized-`INavigableContainer` lookup
  `GridKeyboardNavigation.TryHandleArrowKey` already has (extract that shared "resolve item ->
  focusable control" logic into a small internal helper both files call, rather than duplicating it -
  name it `GridFocusHelper.FocusItem(ItemsControl, object item)` in a new tiny internal static class
  both `GridKeyboardNavigation` and `TypeAheadSearch` reference, since it doesn't belong to either
  concept exclusively). Returns whether a match was found/focused (caller sets `e.Handled` on that).
**Depends on:** none (independent of Steps 1-2)
**Verify:** New `TypeAheadSearchTests.cs` — multi-character buffering builds up a match ("b" then
"a" finds "Batman" not "Superman"), timeout resets the buffer (simulate via an injectable clock or a
buffer field the test can directly manipulate rather than a real `Thread.Sleep(2500)`), backspace
shrinks the buffer, no-match leaves the buffer/current match unchanged, article-skipping finds "The
Amazing Spider-Man" when typing "ama" (proves `StringUtility.StartsWith`'s `ignoreArticles: true` is
actually wired through, not just present).

## Step 4: Wire `TypeAheadSearch` onto Library, Books, Smart Lists
**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit),
`src/Paperbunkr.App/Views/BooksScreen.axaml.cs` (edit),
`src/Paperbunkr.App/Views/SmartScreen.axaml.cs` (edit)
**What:** Each screen's code-behind gets one `TypeAheadSearch.Buffer` field and a `TextInput` handler
attached to its actual grid `ItemsControl`(s) (Tunnel-routed from the screen root, matching how
`BooksScreen`'s own `OnContentPointerPressed` is already wired Tunnel on `ContentGrid` for the
analogous "catch it before the card control does" reason):
- **Library**: text-selector `item => item switch { IssueListRow row => row.SeriesName, SeriesCardSample card => card.Name, _ => string.Empty }`, dispatched against whichever of `IssueList.Rows`/`Covers` is currently bound (issue vs. series granularity — same dispatch `OnCardKeyDown` already does). On match: clear selection via the existing `Selection.Clear(...)` the screen's own multi-select already exposes, then focus via `GridFocusHelper`.
- **Books**: text-selector `card => card.Title`, against `Books` (ungrouped) — grouped mode's
  flattened order needs the same treatment `OrderedCards` (Books' own existing "flatten groups"
  property) already provides, reuse it rather than a new flattening.
- **Smart Lists**: text-selector picks whichever of `Results`/`SeriesResults`/`NovelResults` is
  currently populated (mirrors the screen's own `HasIssueResults`/`HasSeriesResults`/`HasNovelResults`
  mutual-exclusivity) — `IssueCardSample`/`SeriesCardSample`/`BookCardSample` each need their real
  display-label property confirmed against that screen's own template bindings before wiring (verify
  exact property per type while implementing this step, since `IssueCardSample.Title` in this
  screen's own construction is just `"#{number}"` per `SmartScreenViewModel.cs` — check whether a
  series-name-bearing property exists on the row for a more useful type-ahead target, or whether
  falling back to the issue number as CE itself would for a numbered-only display is actually
  correct here).
**Depends on:** Step 3 (and Step 1's `GridFocusHelper` extraction)
**Verify:** Manual/on-screen only for the actual `TextInput` wiring (matches this project's standing
no-computer-use caveat for UI event routing); the underlying match logic is already covered by Step
3's tests.

## Step 5: Ctrl+Q
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit, `OnMainWindowKeyDown`)
**What:** One new `else if` branch in the existing post-TextBox-gate chain (same tier as `Ctrl+,`/
`Ctrl+Tab`/`Ctrl+Z`/`Ctrl+Y`, after the `if (e.Source is TextBox) return;` line ~141-144 — Ctrl+Q
doesn't need to fire while a search box has focus, matching Preferences' own `Ctrl+,` precedent, not
Escape/`Ctrl+P`'s "works everywhere" tier):
```csharp
else if (e.Key == Key.Q && e.KeyModifiers == KeyModifiers.Control)
{
    Close();
    e.Handled = true;
}
```
`Close()` already flows through `OnWindowClosing`'s tray-aware logic (verified in the design doc's
§2) — no new close-path code needed.
**Depends on:** none
**Verify:** No unit test — matches this file's own established precedent (`Ctrl+Tab`'s key-routing
itself isn't tested either, only the VM command it dispatches to, which doesn't apply here since
`Close()` is a framework method, not a VM command). On-screen verification only.

## Step 6: Build + verify
**What:** Full `Paperbunkr.App` build; run `GridKeyboardNavigationTests`, new `TypeAheadSearchTests`,
and the extended `LibraryScreenViewModelTests`/`BooksScreenViewModelTests`/`DetailTabsViewModelTests`.
On-screen verification of type-ahead's jump behavior, Shift+arrow's felt range-select, and Ctrl+Q's
tray-vs-quit behavior stays out of scope this session (no computer-use) — flag per the design doc §6.
