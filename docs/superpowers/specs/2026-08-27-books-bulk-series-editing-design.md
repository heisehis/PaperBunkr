# Books Bulk Edit, Series Editor & Empty-Series Pruning (Piece B3) — Design

**Status:** Implemented 2026-08-27. App.Tests 1047 green, Data.Tests 457 green; app launches clean
(the two new overlays + the Books-grid/BookDetail/MainWindow XAML changes weave and load). Full
on-screen click-through not done this session (no computer-use).

**Implementation notes:**
- `CheckBox.tileSelect` setters copied into `BooksScreen.axaml` (per-UserControl-copy precedent).
- Shift-range works: `BooksScreen.axaml.cs` adds a tunnel `PointerPressed` on the content grid that
  feeds `BooksScreenViewModel.SetShiftHeld` before `CardClickCommand` runs.
- `BookDetailScreenViewModel` gained `ReloadCurrent()` (reloads whichever of Book/Series mode is
  active) so an overlay closing over Series mode doesn't flip it to Book mode.
- Series-mode buttons are "Edit series" + "Edit all books" (the count is in the overlay header).

**Piece B3 of the Books-section follow-up.** Builds on B1 (`BookDetailScreen` +
`2026-08-27-book-details-screen-design.md`) and B2 (`BookPropertiesOverlay`, the generalised
`MetadataEditHistoryService` with `MetadataEditTarget { Issue, Book }` +
`BookMetadataSnapshot` + `RecordBookEdit` — `2026-08-27-book-properties-editor-design.md`).

Four components in one spec, coherent "multi-book / series management" theme:
(a) Books-grid multi-select, (b) a bulk book-properties overlay, (c) a `BookSeries` properties
overlay (rename + metadata), (d) silent auto-pruning of emptied series.

## Background

**Books grid today:** `BooksScreenViewModel` holds `_allCards` and `Rebuild()` (filter→sort→group);
`BooksScreen.axaml` renders a shared `BookCardTemplate` (inline in that file) in a flat
`ItemsControl` or a grouped one. Card click → `SelectBookCommand` → `_goBookDetail`. Grid card
context menu: "Edit…" (B2) + "Delete Book". Grouped-by-Series section headers are a `Button` (real
series) / inert `DockPanel` ("Standalone"/Author), the `Button` bound to `OpenSeriesCommand`. **No
selection model.**

**Selection pattern (Library):** `TileSelectionController<TCard> where TCard : ISelectableCard`
(`int Id`, `bool IsSelected { get; set; }`). `Toggle(orderedItems, item, isShiftHeld)` — plain and
ctrl/checkbox click both additively toggle one item; shift extends a range from the last toggle.
`Clear`, `UnionForAction(rightClickedId)`, `SelectedIds`, `Count`. `IssueListRow` is an
`ObservableObject` so its `IsSelected` drives a `CheckBox.tileSelect` with
`Classes.forceVisible="{Binding HasSelection}"`. `LibraryScreenViewModel` re-applies `IsSelected`
from the live set on every grid rebuild so selection survives a re-sort.

**B2 overlay pattern:** `BookPropertiesScreenViewModel` — ctor
`(Action goBack, Action<string,string>? notify = null, MetadataEditHistoryService? history = null)`
+ internal `(…, Func<PaperbunkrDbContext> contextFactory, …)` seam; buffered `Load`/`Save`/`Cancel`;
`HasUnsavedChanges()`; composited in `MainWindow.axaml` behind `#B0000000` with a corner-X
`Button.rail`; `MainViewModel` holds the `_isXOverlayOpen` flag, `GoXForY` / `CloseXOverlay`
(reload-underneath) / `Escape` branch / `TryLeaveCurrentEditor` OR-clause + `LeaveAndNavigate`
clear.

**Comic bulk editor (`BulkIssuePropertiesScreenViewModel`):** registry-driven
(`BulkFieldRegistry`), list-field token diffing, template expansion, `ComicInfo.xml` write-back,
per-field `BulkFieldViewModel.IsStaged`. **B3's book bulk editor deliberately does NOT reuse this
machinery** — the book field set is 6 plain scalars/strings, no list fields, no templates, no file
write-back, so hand-written staged fields are simpler and clearer.

**Series today:** `BookSeries { Id, Name, SortName?, Author?, Books }`. Created only by the scan or
B2's `ResolveSeries`. Never renamed anywhere, never deleted anywhere. B1's Series mode
(`BookDetailScreenViewModel`, `Mode == Series`) shows `SeriesName` / `SeriesAuthor` /
`SeriesBookCountLabel` + a read-only card grid.

**CE note:** no prose-reading concept in CE — nothing to port (verified in the original Novels
spec).

## Decisions

| Area | Decision |
|---|---|
| **One spec, modular plan** | B3d (pruning) is independent; B3a→B3b have a real dependency (bulk editor needs a selection); B3c pairs with B3d. Ordered in the plan, not split into separate specs. |
| **(a) Selection = `TileSelectionController`** | Reuse it verbatim. `BookCardSample` becomes `sealed partial class : ObservableObject, ISelectableCard` — `[ObservableProperty] bool _isSelected`, everything else stays `init`, `FromBook` unchanged. |
| **(a) Selection lifetime** | View-model-lived, not persisted. `Rebuild()` re-applies `IsSelected` from `Selection.SelectedIds` (survives re-sort/re-group). `LoadFromDatabase()` clears it (a real data reload). Same as Library. |
| **(a) Gesture** | `CheckBox.tileSelect` on each card (`Classes.forceVisible="{Binding HasSelection}"` — visible on hover, sticky once anything's selected), plus the card `Button` itself calls `ToggleBookSelection(card, isShiftHeld)` **only while `HasSelection`** (so a first plain click still navigates to Details; once you're in selection mode, clicks toggle). Matches Library. |
| **(a) Action bar** | While `HasSelection`, the chrome row (search + Group + Sort) is replaced by a selection bar: "**N selected**" · **Edit…** · **Delete** · **Clear**. `Delete` = the existing per-book delete over the whole set (Recycle Bin + rows + cover-cache invalidate + prune). |
| **(b) Bulk fields** | Author, Summary, Published date, Series name, Series author, Series sort name — each a value input + an **"apply to all selected"** checkbox (`IsStaged`). **No Title.** Only staged fields are written. |
| **(b) Mixed values** | On `Load`, a field whose value differs across the selection shows blank with a `Watermark="— multiple —"`; an agreed value prefills (and stays unstaged until the user edits or ticks it). |
| **(b) Series in bulk** | A staged Series name resolves once (create/reuse by case-insensitive name, or detach if blank) and is applied to **every** selected book. Series author / sort name, if staged, write to the resolved row. Every distinct *old* series among the selection is pruned if now empty (component d). |
| **(b) Undo** | One `Target = Book` history entry spanning all selected book ids — new `MetadataEditHistoryService.RecordBookEdits(desc, beforeDict, afterDict)`. Skipped if nothing was staged. Series-row and pruning changes are **not** in the entry (same carve-out as B2). |
| **(b) Entry points** | Grid action-bar "Edit…" (`Selection.SelectedIds`); B1 Series-mode **"Edit all N books"** button (`SeriesBooks` ids). Both → `MainViewModel.GoBulkBookPropertiesForBooks(IReadOnlyList<int>)`. |
| **(c) Series editor** | New `BookSeriesPropertiesOverlay` + VM. Fields: **Name** (renames the `BookSeries` row *in place* — the deliberate opposite of B2's "name box never renames" rule, because this *is* the series editor), **Sort name**, **Author**. Buffered Save/Cancel, `HasUnsavedChanges()`. |
| **(c) Name guards** | Blank name → blocked, toast "Series name can't be empty." Name equal (case-insensitive) to a **different** existing `BookSeries` → blocked, toast "A series called \"{name}\" already exists." (Merging two series is out of scope for B3.) Re-saving the same name (case/whitespace only) is fine. |
| **(c) Undo** | **Not undoable** — series-row edits are outside the per-book history, exactly as B2 decided for series Author/SortName. |
| **(c) Entry points** | B1 Series-mode **"Edit series"** button; Books-grid grouped-by-Series **section-header context menu** → "Edit series…". Both → `MainViewModel.GoBookSeriesPropertiesForSeries(int)`. |
| **(d) Pruning** | `BookSeriesMaintenance.PruneIfEmpty(PaperbunkrDbContext context, int? bookSeriesId)` — non-null id AND `!context.Books.Any(b => b.BookSeriesId == id)` → `context.BookSeries.Remove(row); context.SaveChanges();`. **Silent** — no toast, no history entry. |
| **(d) Call sites** | After the write in: `BookPropertiesScreenViewModel.Save` (capture the pre-edit `BookSeriesId` first), `BulkBookPropertiesScreenViewModel.Save` (every distinct pre-edit id), `BookDetailScreenViewModel.DeleteBook`, `BooksScreenViewModel.DeleteBook`, `BooksScreenViewModel.DeleteSelection`. |
| **No schema change** | Every field exists. `BookSeries` deletion is a plain row remove (its `Books` FK is already nullable; a pruned series by definition has no books). |

## Components

### 1. `BookCardSample` → selectable
`sealed partial class BookCardSample : ObservableObject, ISelectableCard`. Add
`[ObservableProperty] private bool _isSelected;`. `Id => BookId` (explicit interface or a plain
`int Id => BookId` property). All existing `init` properties and `FromBook` unchanged.

### 2. `BooksScreenViewModel` — selection
- `public TileSelectionController<BookCardSample> Selection { get; } = new();`
- `IReadOnlyList<BookCardSample> OrderedCards` — the currently displayed flat order (flatten
  `Groups` when grouped, else `Books`), for `Selection.Toggle`'s range logic.
- `ToggleBookSelection(BookCardSample card, bool isShiftHeld)` (public, for the card-click gesture)
  + `[RelayCommand] ToggleBookSelectionCheckbox(BookCardSample card)` → `ToggleBookSelection(card, false)`.
  Both call `OnSelectionChanged()` → raises `HasSelection` / `SelectionCount` / `SelectionCountLabel`.
- `HasSelection => Selection.Count > 0`, `SelectionCount => Selection.Count`,
  `SelectionCountLabel => $"{Selection.Count} selected"`.
- `[RelayCommand] ClearSelection()` → `Selection.Clear(_allCards); OnSelectionChanged();`
- `[RelayCommand] EditSelection()` → `_goBulkEdit(Selection.SelectedIds.ToList())` (no-op if empty).
- `[RelayCommand] DeleteSelection()` → delete every selected book (extract the body of the current
  `DeleteBook` into `DeleteBooks(IEnumerable<int>)`; prune emptied series; `Selection.Clear()`;
  `LoadFromDatabase()`).
- `Rebuild()`: after repopulating `Books`/`Groups`, set each card's `IsSelected = Selection.IsSelected(card.BookId)`.
- `SelectBook` (card click → Details): **only** when `!HasSelection`; the XAML gates this.
- New ctor args: `Action<IReadOnlyList<int>> goBulkEdit`, `Action<int> goEditSeries` (folded in with
  the B2 `goEditBook` arg — full ctor becomes
  `(goBookDetail, goBookSeriesDetail, goEditBook, goBulkEdit, goEditSeries, goLibrarySettings)`).
- `[RelayCommand] EditSeries(int? bookSeriesId)` → `if (id) _goEditSeries(id)` (grouped-header menu).

### 3. `BooksScreen.axaml` — checkboxes + action bar
- `BookCardTemplate`: overlay a `CheckBox` (`Classes="tileSelect"`,
  `Classes.forceVisible="{Binding $parent[UserControl].((vm:BooksScreenViewModel)DataContext).HasSelection}"`,
  `IsChecked="{Binding IsSelected, Mode=OneWay}"`,
  `Command="…ToggleBookSelectionCheckboxCommand" CommandParameter="{Binding}"`) top-left of the cover.
  Reuse the `CheckBox.tileSelect` style already defined for Library (it lives in a shared style file
  — confirm at implementation; if it's `LibraryScreen`-local, copy the handful of setters into
  `BooksScreen.axaml`, matching the existing per-UserControl-copy precedent).
- The card `Button.Command` binds to a new `CardClickCommand(BookCardSample)` on the VM which
  routes: `HasSelection ? ToggleBookSelection(card, _shiftHeld) : _goBookDetail(card.BookId)`.
  `SelectBookCommand` is removed (its one job moves into `CardClickCommand`). `_shiftHeld` is a
  plain bool field set by a `BooksScreen.axaml.cs` `PointerPressed` handler on the content
  `ItemsControl` (`e.KeyModifiers.HasFlag(KeyModifiers.Shift)`), reset to false after each
  `CardClickCommand` runs — the same code-behind-feeds-VM trick Library uses for
  `ToggleIssueSelection(row, isShiftHeld)`. If the shift plumbing fights the template bindings,
  ship plain additive toggle only for v1 (acceptable cut).
- Chrome row: wrap the existing search+Group+Sort `Grid` and a new selection bar in a container;
  `IsVisible="{Binding !HasSelection}"` on the chrome, `IsVisible="{Binding HasSelection}"` on the
  bar. Bar: `SelectionCountLabel` · `Button` "Edit…" (`EditSelectionCommand`) · `Button` "Delete"
  (`DeleteSelectionCommand`, danger styling) · `Button` "Clear" (`ClearSelectionCommand`).
- Grouped-by-Series header `Button`: add a `ContextMenu` with "Edit series…" →
  `EditSeriesCommand` / `CommandParameter="{Binding BookSeriesId}"` (already null-guarded, and the
  button only renders for real series).

### 4. `BulkBookPropertiesScreenViewModel` + `BulkBookPropertiesOverlay`
- Ctor `(Action goBack, Action<string,string>? notify = null, MetadataEditHistoryService? history = null)`
  + internal contextFactory seam. `_history ??= Shared`.
- `Load(IReadOnlyList<int> bookIds)`: load books `Include(BookSeries)`; `HeaderLabel =
  $"Editing {n} books"`; for each of the 6 fields compute agreed-value-or-blank and set
  `Value`/`Watermark`; capture `_beforeSnapshots = books.ToDictionary(b => b.Id, BookMetadataSnapshot.Capture)`.
- Fields as `[ObservableProperty]` pairs: `Author`/`ApplyAuthor`, `Summary`/`ApplySummary`,
  `PublishedDate` (`DateTimeOffset?`)/`ApplyPublishedDate`, `SeriesName`/`ApplySeries`,
  `SeriesAuthor`, `SeriesSortName` (the two series-detail toggles ride `ApplySeries` + `HasSeriesName`).
  Editing a value auto-ticks its `Apply*` (an `OnValueChanged` partial), matching the comic editor's
  auto-stage-on-edit.
- `HasUnsavedChanges()` → any `Apply*` true.
- `Save`: gather `_pruneCandidates = books.Select(b => b.BookSeriesId).Where(id => id != null).Distinct()`.
  For each staged scalar, write to all books. If `ApplySeries`: resolve once (blank → detach all;
  else create/reuse), attach every book; write `SeriesAuthor`/`SeriesSortName` onto the resolved row.
  `SaveChanges()`. `RecordBookEdits(...)` if anything staged. Prune each `_pruneCandidate` (skip the
  just-resolved id). `goBack()`.
- `Cancel` → `goBack()`.

`BulkBookPropertiesOverlay.axaml`: same `Border.floatingPanel` shell as `BookPropertiesOverlay`;
each row = `CheckBox` (apply) + label + input; a "Series" `groupBox` with the name box +
author/sortname (`IsEnabled` on `HasSeriesName`). Minimal code-behind (AVLN2000).

### 5. `BookSeriesPropertiesScreenViewModel` + `BookSeriesPropertiesOverlay`
- Ctor `(Action goBack, Action<string,string>? notify = null)` + internal seam. No history.
- `Load(int bookSeriesId)`: load the row; `HeaderLabel = $"Edit series “{name}”"`;
  `Name`/`SortName`/`Author` buffered; `_loadSignature` for `HasUnsavedChanges`.
- `Save`: blank `Name` → `notify`, return. `Name` case-insensitively equal to another
  `BookSeries.Id != _seriesId` → `notify`, return. Write `Name`/`SortName`/`Author` (null-if-empty
  for the latter two), `SaveChanges()`, `goBack()`.
- `Cancel` → `goBack()`.

`BookSeriesPropertiesOverlay.axaml`: small `Border.floatingPanel` (~440 wide) — NAME / SORT NAME /
AUTHOR fields + Cancel/Save. Minimal code-behind.

### 6. `BookSeriesMaintenance` (new static)
```
public static class BookSeriesMaintenance
{
    /// Deletes bookSeriesId's BookSeries row iff no Book still references it. Silent - no toast,
    /// no undo entry (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md (d)).
    public static void PruneIfEmpty(PaperbunkrDbContext context, int? bookSeriesId);
}
```
Opens no context of its own — the caller passes its live one and has already `SaveChanges()`d the
membership change. Internally: `context.Books.Any(...)` then `Remove` + `SaveChanges`.

### 7. `MetadataEditHistoryService` — multi-book record
```
public void RecordBookEdits(string description,
    Dictionary<int, Dictionary<string,string?>> before,
    Dictionary<int, Dictionary<string,string?>> after)
{
    _undoStack.Push(new() { Description = description, Target = MetadataEditTarget.Book, Before = before, After = after });
    _redoStack.Clear();
}
```
`RecordBookEdit` (B2, singular) becomes `RecordBookEdits(description, new(){[bookId]=before}, new(){[bookId]=after})`.
`Apply`'s `Target == Book` branch already loops all keys.

### 8. `BookDetailScreenViewModel` — series-mode buttons
- Ctor gains `Action<IReadOnlyList<int>>? goBulkEdit = null` and `Action<int>? goEditSeries = null`
  (after the B2 `goEditBook` arg).
- Series mode: `[RelayCommand] EditAllSeriesBooks()` → `_goBulkEdit(SeriesBooks.Select(c => c.BookId).ToList())`;
  `[RelayCommand] EditSeries()` → `if (_bookSeriesId is int id) _goEditSeries(id)`.
- `BookDetailScreen.axaml` Series-mode header: add "Edit series" + "Edit all N books" buttons
  (`Button.detailAction ghost`), the latter `IsVisible` when `SeriesBooks.Count > 0`.
- `DeleteBook`: after the row delete + `SaveChanges`, `BookSeriesMaintenance.PruneIfEmpty(context, _bookSeriesId)`.

### 9. `BookPropertiesScreenViewModel` — prune on detach
`Save`: capture `int? previousSeriesId = book.BookSeriesId;` before `ResolveSeries`. After
`SaveChanges()` (and after the history record), if `previousSeriesId != book.BookSeriesId`,
`BookSeriesMaintenance.PruneIfEmpty(context, previousSeriesId)`.

### 10. `MainViewModel` + `MainWindow.axaml`
- `BulkBookProperties = new BulkBookPropertiesScreenViewModel(CloseBulkBookPropertiesOverlay, ShowToast);`
  `BookSeriesProperties = new BookSeriesPropertiesScreenViewModel(CloseBookSeriesPropertiesOverlay, ShowToast);`
- `Books` ctor updated: `new BooksScreenViewModel(GoBookDetailForBook, GoBookSeriesDetailForSeries,
  GoBookPropertiesForBook, GoBulkBookPropertiesForBooks, GoBookSeriesPropertiesForSeries, GoLibraryFoldersPreferences)`.
- `BookDetail` ctor updated: `… , GoBulkBookPropertiesForBooks, GoBookSeriesPropertiesForSeries)`.
- `[ObservableProperty] bool _isBulkBookPropertiesOverlayOpen;` + `_isBookSeriesPropertiesOverlayOpen;`
  + `OnChanged` → `IsBulkBookProperties` / `IsBookSeriesProperties`; the read-only bool aliases.
- `GoBulkBookPropertiesForBooks(IReadOnlyList<int> ids)` → `if (ids.Count == 0) return;
  BulkBookProperties.Load(ids); IsBulkBookPropertiesOverlayOpen = true;`
- `GoBookSeriesPropertiesForSeries(int id)` → `BookSeriesProperties.Load(id); IsBookSeriesPropertiesOverlayOpen = true;`
- `[RelayCommand] CloseBulkBookPropertiesOverlay()` / `CloseBookSeriesPropertiesOverlay()` — set flag
  false, then `if (IsBookDetail) BookDetail.ReloadCurrentBook(); else Books.LoadFromDatabase();`
  (both also serve as the VM `goBack` and the corner-X command).
- `Escape`: `else if (IsBulkBookProperties) …CancelCommand…` / `else if (IsBookSeriesProperties) …`.
- `TryLeaveCurrentEditor`: `|| (IsBulkBookProperties && BulkBookProperties.HasUnsavedChanges())
  || (IsBookSeriesProperties && BookSeriesProperties.HasUnsavedChanges())`; `LeaveAndNavigate`
  clears both flags.
- `RefreshAfterHistoryChange` already handles `bookDetail`/`books`.
- `MainWindow.axaml`: two more `<Border IsVisible="…" Background="#B0000000">` blocks with the
  overlay + corner-X `Button.rail`, cloned from the B2 block.

## Data flow

```
Books grid: checkbox / (click while HasSelection) ─► ToggleBookSelection ─► Selection set + IsSelected flags
            action bar "Edit…" ─► GoBulkBookPropertiesForBooks(SelectedIds)
            action bar "Delete" ─► DeleteBooks(SelectedIds) + PruneIfEmpty per old series
            grouped header ctx "Edit series…" ─► GoBookSeriesPropertiesForSeries(id)

Series mode: "Edit all N books" ─► GoBulkBookPropertiesForBooks(SeriesBooks ids)
            "Edit series" ─► GoBookSeriesPropertiesForSeries(id)

BulkBookProperties.Save ─► write staged fields to all books ─► resolve+attach series ─► SaveChanges
                        ─► RecordBookEdits(before/after per id)
                        ─► PruneIfEmpty(each distinct old series)  ─► CloseBulkBookPropertiesOverlay ─► reload

BookSeriesProperties.Save ─► guard blank / collision ─► rename+write row ─► SaveChanges ─► reload

BookProperties.Save (B2) ─► … ─► PruneIfEmpty(previousSeriesId if it changed)
BookDetail.DeleteBook / Books.DeleteBook(s) ─► RecycleBin + remove + SaveChanges ─► PruneIfEmpty
```

## Error handling

- Bulk `Load` with an id set that's since partly deleted → operate on the survivors; empty → close.
- `BookSeriesProperties.Load` missing row → `goBack()`.
- Blank / colliding series name → toast, overlay stays open, nothing written.
- `PruneIfEmpty` with a null id or a row that has books → no-op. Row already gone → `Find` returns
  null → no-op.
- Selection referencing a card no longer in `_allCards` after a reload → `Selection` still holds the
  id but no visible card; `LoadFromDatabase` clears the selection anyway, so this is transient.
- A pruned series that some open overlay still references → the overlay's next `Save` re-`Find`s and
  no-ops/closes; acceptable (narrow race, no corruption).

## Testing

**`BooksScreenViewModelTests`** (extend)
- `ToggleBookSelection` adds/removes; shift-range selects the span; `ClearSelection` empties it and
  resets `IsSelected`.
- Selection survives a sort change (`IsSelected` re-applied after `Rebuild`); cleared by
  `LoadFromDatabase`.
- `EditSelectionCommand` invokes `goBulkEdit` with exactly the selected ids; no-op when empty.
- `DeleteSelectionCommand` removes every selected book and prunes a series left empty.
- `EditSeriesCommand` invokes `goEditSeries` for a real series id, no-op for null.

**`BulkBookPropertiesScreenViewModelTests`** (new)
- Only staged fields written; an unstaged prefilled field left untouched on every book.
- Mixed pre-values → blank + watermark; agreed pre-value → prefilled, unstaged.
- Staged Series name → every book attached to the (one) resolved row; distinct old series pruned.
- `SeriesAuthor`/`SeriesSortName` land on the resolved row.
- Exactly one history entry, keyed by every edited book id; `Undo` restores all; skipped when
  nothing staged.
- `HasUnsavedChanges` true after ticking/editing a field, false on a clean load and post-Save.
- `Cancel` writes nothing.

**`BookSeriesPropertiesScreenViewModelTests`** (new)
- Rename in place — the row's `Name` changes, book memberships unchanged, sibling books see it.
- Blank name → `notify`, no write.
- Name == another series (different case) → `notify`, no write; same name re-saved is fine.
- SortName / Author round-trip (null-if-empty).
- `HasUnsavedChanges` transitions; missing row → `goBack`.

**`BookSeriesMaintenanceTests`** (new)
- Prunes a series with zero books; leaves one with ≥1; null id no-ops; already-deleted id no-ops.

**`MetadataEditHistoryServiceTests`** (extend)
- `RecordBookEdits` with two book ids → `Undo` restores both, `Redo` re-applies both.

**`BookPropertiesScreenViewModelTests`** / **`BookDetailScreenViewModelTests`** (extend)
- B2 `Save` that detaches the last book from a series prunes it; reassigning to another series
  prunes the vacated one.
- `BookDetailScreenViewModel.DeleteBook` of a series' last book prunes the series.

**Build** — 4 new `.axaml` (`BulkBookPropertiesOverlay`, `BookSeriesPropertiesOverlay`, and their
`.axaml.cs`); land each `.cs` with its `.axaml`; single `dotnet build` + **launch** at the end
(0 Errors insufficient, CLAUDE.md).

**Manual** — grid: hover checkboxes, click/ctrl/shift select, Edit N, Delete N, Clear; Series mode:
"Edit all books", "Edit series"; grouped header "Edit series…"; rename a series and watch the cards
+ group header update; detach a series' last book (via B2 and via bulk) and confirm the group
disappears; delete a series' last book and confirm the same; Undo a bulk edit; both skins.

## Risks / notes

- **Shift-range plumbing** needs the key modifier from code-behind into the VM (Library's precedent).
  If it fights the `BookCardTemplate`'s command bindings, ship plain additive toggle only for v1 —
  an explicit, acceptable cut.
- **`CheckBox.tileSelect` style location** — reuse if it's in a shared style dict; else copy the
  setters into `BooksScreen.axaml` (per-UserControl-copy precedent already in this codebase).
- **Pruning has no undo** (per the approved choice). A bulk reassign + Undo restores memberships but
  not a pruned row — consistent with B2's series/cover carve-out from history.
- **Series merge is out of scope** — a rename that would collide is blocked, not merged.
- **4 new `.axaml`** — the AVLN2000 gotcha bites per-file; discipline: `.cs` beside `.axaml`, one
  build+launch verification at the end of the arc.
- **YAGNI cuts:** no bulk Title, no per-book override of a bulk value, no "select all" button
  (Clear + the checkboxes are enough for realistic book libraries), no undo for series edits or
  pruning.
