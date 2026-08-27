# Delete Functionality — Design Spec

*Date: 2026-08-22. Scope: the user flagged "I don't see a way to delete stuff throughout the whole
Paperbunkr, on every list." A survey (Explore agent, not assumed) confirmed the gap is real and
larger than one screen: none of Reading Lists, Smart Lists, Library (series/issues), Books, or
Story Events have a way to delete the thing itself - only sub-items within them (a reading list's
own items, a smart list's own conditions, an event's own members). This spec covers the shared
mechanism; each of the five screens is its own follow-up slice using it.*

## 1. What already existed

The survey found exactly one real destructive-delete pattern in the whole app,
`MissingFileRowViewModel` (Needs Review → Missing Files → Remove): a two-step inline confirm - one
click arms a 3-second window (button relabels to "Confirm remove?"), a second click within that
window commits, letting it lapse silently cancels. No modal. Its own doc comment already called
this out as the pattern to match for future destructive deletes - it just never got extracted, so
it existed exactly once, hand-rolled.

Two lower-stakes deletes exist in Preferences (Virtual Tags, Watched Folders) with immediate,
unconfirmed delete - appropriate there (low-consequence, easily re-added config), not the pattern
for deleting a user's actual list/library data.

No app-wide confirmation dialog service exists, and none is being added - the two-step inline
pattern deliberately avoids needing one.

## 2. Shared mechanism: `TwoStepConfirm`

```csharp
public partial class TwoStepConfirm : ObservableObject
{
    public TwoStepConfirm(Action onConfirmed, string idleLabel = "Remove", string armedLabel = "Confirm remove?");
    public string Label { get; }       // observable - bind directly for a text button
    public bool IsArmed { get; }       // observable - bind for an icon-only button's visual state
    public IRelayCommand TriggerCommand { get; }
    public void Cancel();              // reverts to idle without confirming - call when the row itself is going away
}
```

Extracted out of `MissingFileRowViewModel`, which now composes one instead of hand-rolling its own
`DispatcherTimer`/relabeling logic - refactored as part of this pass (not left duplicated
alongside the new shared version), zero behavior change confirmed by its own screen's existing
manual verification plus the new `TwoStepConfirmTests`.

Any row/tile needing a destructive delete holds one as a property (convention: name it
`DeleteConfirm`), constructed with a closure over whatever needs deleting:
```csharp
DeleteConfirm = new TwoStepConfirm(() => DeleteReadingList(listId), idleLabel: "Delete", armedLabel: "Confirm delete?")
```
Text-button screens bind `{Binding DeleteConfirm.Label}`/`{Binding DeleteConfirm.TriggerCommand}`
directly (e.g. `MigrationOverlay.axaml`'s Missing Files row). Icon-only screens (sidebar rows too
narrow for label text) bind `{Binding DeleteConfirm.IsArmed}` to swap between two overlaid
differently-colored icon `Border`s (faint gray idle, `PbBadgeBrush` red when armed) and use
`DeleteConfirm.Label` as the button's `ToolTip.Tip` instead, so the "Confirm delete?" state is
still discoverable on hover even without inline text.

## 3. Slice 1: Reading Lists sidebar (shipped this pass)

`ReadingListSummary` (sidebar row model) gained a `required TwoStepConfirm DeleteConfirm` property,
constructed in `ReadingScreenViewModel.RefreshSidebar()` per row, closing over that row's own list
id. `ReadingScreenViewModel.DeleteReadingList(int)`:

- Removes the `ReadingList` row. `ReadingListItem.ReadingListId`'s FK is `DeleteBehavior.Cascade`
  (confirmed in `PaperbunkrDbContext.OnModelCreating`, not assumed) - deleting the list
  automatically cascade-deletes its items at the database level, no explicit item-removal loop
  needed. The referenced `Issue`s themselves are never touched (`ReadingListItem.Issue`'s FK is
  `Restrict` precisely so a list delete can't cascade into deleting a comic the user still owns
  and may have in other lists).
- If the deleted list was the active one: falls back to the next available list (same ordering
  `EnsureListLoaded` already uses), or clears the screen entirely (`ListName`/`Subtitle`/stats all
  reset, `HasNoReadingLists` empty state takes over) if none are left.

XAML: `MainWindow.axaml`'s Reading Lists sidebar row gained a second `Grid` column - a small ghost
icon button (trash icon, `Trash_Empty.png`) next to the existing select-list button, using the
overlaid-icon `IsArmed` pattern from §2.

## 4. Slice 2: Smart Lists sidebar

Near-identical port of Slice 1, with one real difference: `SmartListSummary.DeleteConfirm` is
**nullable** - `null` for a built-in/maintenance list (`SmartList.IsSystem`), which the screen's
own existing `IsReadOnly` rule already forbids editing, let alone deleting. Only `CustomLists`
rows get a real `DeleteConfirm`; the XAML delete button only exists in the `CustomSmartLists`
template, not the built-in/maintenance ones. `SmartListCondition`'s FK is `DeleteBehavior.Cascade`,
so a list's conditions go with it automatically.

Real bug caught by its own test: `DeleteSmartList`'s original fallback branch called
`EnsureListLoaded()` and returned without ever calling `RefreshSidebar()` in the case where nothing
was left to load - `EnsureListLoaded` only refreshes the sidebar as a side effect of successfully
loading *something* via `LoadSmartList`. In production there's always a seeded built-in list to
fall back to, so this only showed up because the test fixture's bare `EnsureCreated()` doesn't seed
one - but the underlying logic was wrong regardless of how likely production is to hit it. Fixed:
`RefreshSidebar()` now always runs, unconditionally, at the end of `DeleteSmartList`.

## 5. Slice 3: Story Events sidebar

Same shape as Slice 1 again. `ReadingList.StoryEventId`'s FK is `DeleteBehavior.SetNull` (confirmed,
not assumed) - a `ReadingList` tracking an event's reading order survives the event's deletion,
just unlinked, rather than being cascade-deleted itself.

## 6. Slice 4 & 5: Library (Series/Issue) and Books - context-menu delete

Library's series/issue tiles and Books' cards trigger delete from a right-click `ContextMenu`
rather than an always-visible row button. A `ContextMenu` closes after every click, so
`TwoStepConfirm`'s timed re-click doesn't apply here - there's no persistently-visible control left
to click a second time. Used a **nested submenu** instead: a top-level "Delete Series"/"Delete
Issue"/"Delete Book" `MenuItem` containing one child "Yes, delete this ___" - opening the submenu
and clicking the child is a deliberate two-step action without needing a timer, dialog, or shared
state at all. Library has 14 near-identical `ContextMenu` blocks (one per density-mode ×
granularity `DataTemplate`, an existing pattern from before this feature - every other tile action
here is already duplicated the same way, so this follows the codebase's own established
convention rather than introducing resource-sharing it doesn't otherwise use).

**File handling** (confirmed with the user): deleting a Series/Issue/Book also moves its file(s) to
the OS Recycle Bin - recoverable, matching CE's own "remove from library" flow (whose recycle-bin
step is itself optional/opt-in, never a silent permanent delete) - rather than only touching the
database. `RecycleBinHelper` wraps `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(...,
RecycleOption.SendToRecycleBin)` - ships in the base `Microsoft.NETCore.App` shared framework,
confirmed present by a real build rather than assumed; no extra package reference needed. A
missing file, permission error, or non-Windows OS is swallowed - the library-database removal this
accompanies must never be blocked by a filesystem hiccup.

**Real latent bug found and fixed while building this:** `ReadingListItem.Issue` and
`EventMembership.Issue` are both `DeleteBehavior.Restrict`, deliberately (so deleting one issue
can't silently cascade into an unrelated reading list or event) - which means removing an `Issue`
still referenced by either throws a `DbUpdateException` unless those references are removed first.
`NeedsReviewViewModel.RemoveMissingFile` (the app's *original* destructive-delete, predating this
whole feature) never handled either case - it would have thrown for any missing-file issue that
also happened to be in a reading list or event. New shared helper `LibraryDeletionHelper.RemoveIssue`/
`RemoveSeries` does this correctly (remove cross-references → recycle the file → remove the row),
and `RemoveMissingFile` was fixed to use it too, rather than left as a second, differently-buggy
delete path. Covered by `LibraryScreenViewModelTests.DeleteIssue_RemovesReadingListReferencesFirst_SoTheDeleteDoesNotThrow`.

Books has no such cross-reference problem - `BookBookmark`'s FK is the only thing pointing at
`Book`, and it's `Cascade` - so `BooksScreenViewModel.DeleteBook` doesn't need the shared helper,
just a direct recycle-and-remove.

## 7. Testing

`Paperbunkr.App.Tests`:
- `TwoStepConfirmTests` - idle/armed/confirmed state transitions, custom label support. Doesn't
  wait out the real 3-second auto-revert (too slow/flaky for a unit test), only the immediate
  click-driven transitions.
- `ReadingScreenViewModelTests`: two-click-required-before-delete, cascade-deletes items but never
  the underlying `Issue`, falls back to another list when the active one is deleted, clears the
  screen when the last list is deleted.
- `SmartScreenViewModelTests`: built-in/maintenance lists have no `DeleteConfirm` at all, two-click
  delete removes a custom list, falls back to a built-in list, a system list can never be deleted
  (confirmed by its `DeleteConfirm` being null in the first place, not by trying and failing).
- `EventsScreenViewModelTests`: two-click delete, clears the screen when the last event is deleted.
- `LibraryScreenViewModelTests`: deleting an issue leaves its (now-empty) series alone; deleting a
  series removes every one of its issues too; deleting an issue still referenced by a reading list
  doesn't throw (the regression test for the bug in §6).
- No test for the live Recycle Bin call itself or the two Books/Library `ContextMenu` submenu
  interactions - same "no live OS/network side effects in CI" stance already taken elsewhere in
  this codebase; those are manual/live-verification items.
