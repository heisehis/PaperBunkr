# Reading lists: organize and track (pitch spec A)

Spec A of the Reading List / CBL Manager pitch (Roadmap, "Reading List / CBL Manager pitch", 2026-09-28). It covers
#2 list folders, #5 the printable checklist (plus CSV export), #6 the per-list completion forecast and #7 overlap detection.
Spec B (`2026-09-28-reading-lists-build-from-events-design.md`) covers #10, #12 and the grouping fix. Build A first.

The decisions came from a grilling session on 2026-09-28 (three rounds, all recommendations accepted). Mockups:
`rl-round2.html` in that session's scratchpad (V1–V4 below).

## Facts this rests on (verified 2026-09-28)

**ComicRack CE** (`_reference/ComicRackCE`):
- **Folders.** `ComicListItemFolder` (`ComicRack.Engine/Database/ComicListItemFolder.cs`) nests to any depth: a folder's
  `Items` can hold folders. Its `Collapsed` flag is persisted, set in `AfterExpand`/`AfterCollapse` in
  `ComicListLibraryBrowser.cs:795-807`. `ComicFolderCombineMode` gives a folder a book view: Or (union), And (intersection)
  or Empty.
- **Folder verbs** in `ComicListLibraryBrowser.cs`: New Folder, Edit (name, notes, combine mode), Rename, Remove, Sort (folders
  first, then names A–Z ignoring articles), Expand/Collapse all, and drag a list into a folder or reorder it (Ctrl-drag copies).
  Export Folder writes one `.cbl` per list and a sub-directory per sub-folder (`:1266-1339`). A `.cbl` dropped on a non-folder
  node lands in an auto-created "Temporary Lists" folder, which is cleared on every load.
- **Not in CE:** printing (no print API anywhere), per-list pace or forecast, and overlap detection between lists. The nearest
  pieces are "Show in List", And-mode folders and "Not In" smart lists. CE exports only `.cbl`; a bundled sample script does a
  semicolon CSV of the selected books.
- **CBL format:** `<Book>` has `Series`, `Number`, `Volume`, `Year`, `Format`, `<Id>` and `<FileName>`. There is no notes field,
  and the container does not write the list's description.

**Paperbunkr** (working tree, 2026-09-28):
- **Nothing nests yet.** `ReadingList`, `Collection` and `Continuity` have no parent field, and the app has no `TreeView` or
  `TreeDataTemplate`. The reading-lists sidebar is one flat `ItemsControl` of `ReadingListSummary` rows
  (`MainWindow.axaml:659-730`) with a hover delete (`TwoStepConfirm`).
- **List order.** `ReadingList.SortOrder` exists, but no UI reorders lists; CBL and CSV imports leave it at 0.
- **Per-item notes already exist** (`ReadingListItem.Notes`, edited through "Add a note" on the row), so pitch item #4 is done
  locally. Remote viewers see no reading lists at all (`/v1/lists` is names only and the client never calls it). That part
  moves to the remote-sharing follow-ups.
- **Exports today:**
  - CBL (`CblReadingListIO`)
  - plain text / Markdown (`ReadingListTextExporter`, which leaves out notes, group labels and owned status)
  - CSV is import only (`CsvReadingListIO`: `Series,Number,Volume,Year,Format`)
  - no PDF or print path exists
  - SkiaSharp 3.119.x is already referenced transitively, so `SKDocument.CreatePdf` is available and unused
- **Pace.** `StatsResolver` works from `ReadingEvent` (append-only; `Kind` Opened/Finished, `ItemType` Comic/Novel, `ItemId`
  without an FK). Nothing scopes pace to a reading list, and the burn-down (`ComputeBurnDown`) is the only forecast.
- **Overlap.** Nothing detects it between lists. `RefreshSidebar` already loads every list with its items.
- **Owned/missing.** A row is missing when `Issue.FileIsMissing`; unmatched imports are placeholder `Issue`s
  (`IsPlaceholder = true`).
- **Write path.** `ReadingListManager` (static; the caller saves) is the sanctioned write path for list contents and announces
  `ReadingListChangedEvent`.

## Decisions

| # | Decision |
|---|---|
| Q1 | Two specs: this one (A: #2, #5, #6, #7) and B (#10, #12, grouping fix). A is built first. |
| Q2 | #1 (GCD as an arc source) is **dropped**: the GCD dump has only 582 story arcs, and the extract deliberately has no arc tables (GCD spec G7). |
| Q3 | #4 is done locally. Notes are added to the text export, the new CSV and the checklist. Showing lists to remote viewers moves to the remote-sharing follow-ups. |
| Q5 | Folders follow CE: unlimited nesting, collapsed state remembered, drag a list into a folder or use "Move to folder", drag to reorder, export a folder as a directory of `.cbl` files. **Deviation:** no combine modes, because a union of ordered lists has no order and And-mode is a crude version of #7. |
| Q6 | Folders hold reading lists only, not Smart Lists. |
| Q21 | Data model: a dedicated `ReadingListFolder` table (self-referencing parent) plus `ReadingList.FolderId`. |
| V1 | Sidebar: an indented tree. Folder rows have a chevron, a bold name and a count; each level indents 16px. It's built as a flattened list with a depth indent, not an Avalonia `TreeView`. |
| V2 | Clicking a folder shows its overview: a header with totals, then a card per sub-folder and per list. |
| Q7 | Deleting a folder moves its lists and sub-folders up to its parent, after a two-step confirm. Deleting a folder never deletes lists. |
| Q8 | New, imported and arc-built lists go into the selected folder, or the top level when no folder is selected. **Deviation:** CE's "Temporary Lists" folder is not used. |
| Q9 | Importing a directory of `.cbl` files recreates it as a folder tree, the reverse of folder export. |
| Q22 | Inside a folder, folders come first, then lists, each group with its own sort order. Drag reorders within a group. **Sort A–Z** matches CE: folders first, leading articles ignored. |
| Q23 | Sidebar right-click menus. Lists: Rename, Move to folder ▸, Export, Delete. Folders: New list here, New folder, Rename, Sort A–Z, Export as folder of `.cbl`, Delete. The hover delete button stays. |
| Q24 | A folder has an optional description (as CE's folder notes), shown in the overview header and as the row tooltip. |
| Q10 | The forecast's pace is comics finished in the last 90 days across the whole library, applied to the list's unread items. |
| Q11 | The forecast is hidden when fewer than 5 comics were finished in the window. It notes missing issues: "· 2 still missing". |
| Q12 | The forecast is appended to the progress label under the header progress bar. There's none in the sidebar. |
| Q13 | The checklist PDF is drawn with SkiaSharp `SKDocument.CreatePdf`. Paper size is Letter or A4, from the regional setting. |
| V4 | The checklist is text only, with no covers. |
| Q14 | CSV export is added, with the import's columns plus `Read`, `Owned` and `Note`. |
| Q27 | The checklist is under Manage ▸ Export ▸ Printable checklist (PDF)…, then a save dialog, then an Activity Center "Checklist saved" alert with an **Open** action. There's no folder checklist. |
| Q15 | Two lists overlap when the issues they share are ≥ 60% of the smaller list. This is computed in memory on sidebar refresh. |
| V3 | The overlap notice is a banner in the list header (Compare · Merge… · Not a duplicate). |
| Q25 | Compare opens a read-only overlay with three columns (only here / in both / only there); Merge… is also available from it. |
| Q16 | Merge B into A: A keeps its order, and each B-only issue is inserted after the nearest earlier shared issue. B's notes and roles fill A's blanks. B is then deleted after a confirm, unless "Keep the other list" is ticked. |
| Q26 | "Not a duplicate" is permanent for that pair; the dismissal is removed only when either list is deleted. |
| Q30 | No plugin API change. Folder moves aren't announced, and list contents still go through `ReadingListManager`. |

## Design

### 1. Folders: data

New entity `ReadingListFolder` (`Paperbunkr.Data/Entities/ReadingListFolder.cs`):

| Field | Type | Notes |
|---|---|---|
| `Id` | int | |
| `Name` | string | required |
| `Description` | string? | Q24 |
| `ParentFolderId` | int? | self-FK. `DeleteBehavior.Restrict`: deleting goes through `ReadingListFolders.Delete` (below), which re-parents first |
| `SortOrder` | int | among the folders in the same parent |
| `IsCollapsed` | bool | default false |
| `CreatedAt` / `UpdatedAt` | DateTime | |

`ReadingList` gains `FolderId` (int?, FK `SetNull` as a backstop) and `Folder`. Its existing `SortOrder` now means "order among
the lists in the same folder". The migration `AddReadingListFolders` creates the table and the column, then renumbers the
existing lists' `SortOrder` 0..n by the current order (`SortOrder`, then `Id`), because imports left many at 0. `Down()` drops
the table and leaves the column (the project's orphan-column convention).

New static `ReadingListFolders` (`Paperbunkr.Data/ReadingLists/ReadingListFolders.cs`) is the one write path for folder
structure. The caller saves, as with `ReadingListManager`.
- `Create(ctx, name, parentId)` appends at the end of the parent's folders.
- `Rename(ctx, folderId, name)` and `SetDescription(...)`.
- `Move(ctx, folderId, newParentId, index)` refuses a move into itself or a descendant (the cycle check CE does in
  `RecursionTest`) and renumbers both parents.
- `MoveList(ctx, listId, folderId, index)` renumbers the old and new folder's lists.
- `Delete(ctx, folderId)` re-parents child folders and lists to the deleted folder's parent, appends them after that parent's
  existing items in their current order, then removes the folder (Q7).
- `SortAlphabetically(ctx, folderId?)` sorts folders A–Z, then lists A–Z, ignoring leading "The ", "A ", "An " (CE's
  `ComicListLibraryBrowser` sort). `null` means the top level.
- `SetCollapsed(ctx, folderId, bool)`.

### 2. Folders: sidebar

`ReadingScreenViewModel.RefreshSidebar` builds one flattened `ObservableCollection<ReadingSidebarNode>` from folders and lists:
a depth-first walk with folders first, then lists, skipping the children of collapsed folders. `ReadingSidebarNode` is an
abstract base with `Depth` and two subclasses: `ReadingFolderNode` (folder id, name, description, child count, collapsed) and
the existing `ReadingListSummary`, which gains `Depth` and `FolderId`. The sidebar `ItemsControl` gets two `DataTemplate`s by
type. Each row's left margin is `8 + 16 × Depth` (V1 A). The list row is otherwise unchanged: name, thin progress bar, hover
delete.

The folder row has a chevron (click toggles collapse and persists it), the name in semibold and a faint child count
(lists, recursively). Its tooltip is the description. Clicking the name opens the folder overview (§3).

**Tag filter:** while a tag filter is active the sidebar shows matching lists flat, as today, with no folders. Showing folders
that contain nothing matching would be noise.

**Drag and drop** (load `avalonia-input-interaction` before building):
- Rows start a drag with the payload `ReadingSidebarDrag(kind, id)`.
- Dropping on the top or bottom third of a same-kind row reorders before or after it.
- Dropping on the middle of a folder row moves the item into that folder, at the end.
- Dropping a list on a list in another folder moves it into that folder at that position.
- A folder can't be dropped into itself or a descendant; the drop shows the "not allowed" effect.
- File drops (`.cbl`/`.csv`/folders) keep going to `ImportDroppedPathsAsync`. The two are told apart by the data format.
- The drop target draws a 2px accent line (reorder) or an accent-tinted row (into a folder).

**Right-click menus** use the shared MenuFlyout mechanism (see the context-menu rebuild work) with the Q23 items. "Move to
folder ▸" lists the folder tree indented, plus "Top level". "Export" on a list opens the existing export submenu.

**Delete:**
- A folder row's hover delete uses `TwoStepConfirm` with the label "Delete folder (lists are kept)".
- The removal is posted to the dispatcher. CLAUDE.md's runtime gotcha applies: the row raising the click is removed from the
  collection.
- Every new row-removing command (delete folder, move list out of a collapsed parent, merge-deletes-B) posts its collection
  change with `Dispatcher.UIThread.Post`.

**Where new lists land (Q8):** `ReadingScreenViewModel` tracks `SelectedFolderId`. It's set when a folder overview is showing,
or to the open list's folder when a list is showing. `NewReadingListViewModel`, the CBL/CSV import paths,
`ArcReadingListBuilder.CreateFromArcAsync` (new optional `folderId` parameter) and drag-drop import all place the new list
there. A new list is appended at the end of that folder's lists.

### 3. Folder overview (V2 A)

When a folder is selected, the Reading screen's main pane shows a `ReadingFolderOverview` view instead of the list view (same
host grid, `IsVisible` switch):
- **Header:** the folder name (`pbTextHeading`), the description, and a meta line with totals over every list in the folder,
  recursively: "3 lists · 1 folder · 212 issues · 64 read · 18 missing". Actions: New list here, New folder, Rename, Export as
  folder of `.cbl`.
- **Cards:** a `WrapPanel` of fixed-width cards, sub-folders first, then lists in order. A list card shows a 2×2 cover mosaic
  from `ReadingListCoverMosaic.PickCoverKeys` (or its arc cover), the name, "read / total" and a 3px progress bar. A folder card
  shows a mosaic of up to four of its lists' first covers and "N lists". Clicking a card opens it. Right-click on a card gives
  the same menu as the sidebar row.
- Empty folder: "This folder is empty. Drag lists here, or create one."

### 4. Folder import and export (Q9)

- **Export as folder of `.cbl`:** pick a directory, then write one `.cbl` per list (`CblReadingListIO`, file name = list name
  with invalid characters replaced) and a sub-directory per sub-folder, recursively. Name collisions get " (2)". The job runs
  under `IActivityService.StartJob(ActivityJobKind.Other, "Exporting folder …")`.
- **Import a directory:** "Import folder of .CBL…" in the sidebar header's ＋ menu, and dropping a directory on the Reading
  screen. It creates a folder per directory, recursively (skipping directories with no `.cbl`/`.csv` anywhere below them), and
  imports each file into its folder through the existing import path. The whole tree goes under the current `SelectedFolderId`.

### 5. Completion forecast (#6)

New pure `ReadingListForecast.Compute(IReadOnlyList<DateTime> finishedComicUtc, int unreadCount, int missingUnreadCount,
DateTime nowUtc)` in `Paperbunkr.Data/Metadata/`. It returns `ForecastResult? (DateTime FinishBy, int MissingUnread)`:
- `finishedComicUtc` = `ReadingEvent` rows with `Kind = Finished`, `ItemType = Comic`, `TimestampUtc` within the last 90 days,
  counting each distinct `ItemId` once. A re-read counts as reading, so it's one per event; see the note below.
- Fewer than 5 → `null` (Q11). `unreadCount` 0 → `null` (the label already says complete).
- pace = count / 90 per day; FinishBy = now + unreadCount / pace days.

*Note:* re-reads emit a new `Finished` event (Insights spec). They are **counted**, because they took reading time; the
forecast is about throughput, not new issues.

`ReadingScreenViewModel` loads the 90-day finished timestamps once per screen visit (cached, invalidated on
`ReadingEventRecorded`) and recomputes on list load and on read-state changes. `ProgressLabel` becomes, for example:
"7 of 12 read · at your pace, done by ~Nov 2026 · 2 still missing". The date is month and year ("~Nov 2026"); within 31 days
it says "~in 3 weeks" / "~in 5 days". Remote issues count like local ones; Insights already includes remote reads.

### 6. Checklist PDF and CSV export (#5, Q14)

New `ReadingListChecklistPdf.Write(Stream, ChecklistModel, PaperSize)` in `Paperbunkr.App/Services/` (App, because it uses
SkiaSharp fonts). `ChecklistModel` is built in Data from the list: name, meta counts, a print date, and ordered rows
`(Position, GroupLabel?, Display, Year, IsRead, IsOwned, Note?)`.

Layout (V4 A):
- 36pt margins.
- Title in the default sans font at 16pt bold, with a meta line under it: "12 issues · 7 read · 2 missing · printed 28 Sep 2026
  from Paperbunkr".
- A table with the columns: checkbox (filled when read) · # · issue ("Series #Number", with the note on a second line in
  italic grey) · year · status ("owned" or a red bold "MISSING").
- Group labels are full-width shaded rows (consecutive runs, the same rule as spec B §1).
- Rows never split across a page break. The header row repeats on each page, and the footer says "Paperbunkr" and "page / total".
- Paper size: `RegionInfo.CurrentRegion.IsMetric` → A4, otherwise Letter.
- Fonts come through `SKFontManager.Default` with a fallback so non-Latin series names render.

**Entry point:** Manage ▸ Export ▸ "Printable checklist (PDF)…" → `IFilePickerService` save dialog (default name
`<list name> checklist.pdf`). The write runs as `IActivityService.StartJob(ActivityJobKind.Other, "Saving checklist")` and ends
with `Succeed("Checklist saved", new ActivityLink(ActivityLinkKind.ExternalUrl, <file:// URI>))`, so the completion toast and
finished-job row open the PDF in the system viewer. If the `ExternalUrl` handler refuses `file://`, add an `OpenFile` link kind
next to it rather than special-casing the URL.

**CSV export:** `CsvReadingListIO.Write` has the header `Series,Number,Volume,Year,Format,Read,Owned,Note` (RFC 4180 quoting). The
importer finds columns by header name (`CsvReadingListIO.cs:22-32`), so the extra columns are ignored and an exported file
re-imports; a round-trip test pins that. The
menu item is Manage ▸ Export ▸ "As .CSV…".

**Text export and notes (Q3):** `ReadingListTextExporter` appends `— <note>` after a line whose item has a note. CBL export is
unchanged, because the format has no field for notes.

### 7. Overlap detection (#7)

New pure `ReadingListOverlap.Find(IReadOnlyList<(int ListId, IReadOnlySet<int> IssueIds)> lists, IReadOnlySet<(int, int)>
dismissed)` in `Paperbunkr.Data/ReadingLists/`. It returns `OverlapPair(int ListA, int ListB, int Shared, int SmallerCount)`
for each pair where Shared / SmallerCount ≥ 0.60 **and SmallerCount ≥ 5** (so tiny lists don't match trivially), minus the
dismissed pairs. Pairs are normalized with the smaller id first. It runs inside `RefreshSidebar` on the issue-id sets that are
already loaded; building an inverted index from issue to lists keeps it well under O(n²) for real libraries.

New entity `ReadingListOverlapDismissal` (`Id`, `ListAId`, `ListBId`, `CreatedAt`; unique on the pair; both FKs cascade from
`ReadingList`, which is how Q26's "removed when either list is deleted" happens). It's in the same migration as §1.

**Banner (V3 A):** when the open list is in one or more pairs, a `PbAccentSoftBrush` banner sits under the meta line: "Shares
**27 of 31** issues with **Infinite Crisis**". If there are several partners, it shows the highest overlap first and adds
"+2 more ▾", a flyout that picks a partner. The actions are Compare, Merge… and Not a duplicate.

**Compare overlay (Q25):** an `OverlayShell` with three columns (Only in *this* · In both · Only in *other*). Each column lists
`Series #Number (Year)` in that list's order, with counts in the column headers. The footer has Merge… and Close. It's
read-only.

**Merge (Q16):** `ReadingListMerger.Merge(ctx, intoListId, fromListId)` in Data:
- Walks B in order. A B-only issue is inserted directly after the last issue already placed that A and B share (at the top
  when there is none). `SortOrder` is renumbered once at the end.
- For shared items, B's `Notes`, `Role` (with its source and reason) and `GroupLabel` fill A's null fields and never overwrite
  them.
- B's tags are added to A where missing. `StoryEventId` is left alone.
- All writes to A go through `ReadingListManager` (`AddIssues` then an ordered placement, announced as one compound
  `Record(...)`).
- Then B is deleted unless the confirm dialog's "Keep the other list" box is ticked. The confirm (a `ConfirmDialog`) states the
  numbers: "Add 4 issues from *X* to *Y*, then delete *X*".

## Error handling

- Folder moves that would create a cycle are refused in `ReadingListFolders.Move`; the UI prevents them before that.
- PDF write failure (disk, permissions): the job fails with the exception message as an Activity Center error alert, and no
  partial file is left (write to a temp file in the same directory, then move it into place).
- Folder export stops at the first failed file, reported in the job's alert; files already written stay.
- Folder import reports each failed file in the job alert's detail and imports the rest.
- A forecast with no data is hidden, never shown as "never".
- Overlap computation errors can't break the sidebar: it's pure over in-memory sets, and the unit tests cover the edge cases.

## Testing

- **Data unit tests:**
  - `ReadingListFolders`: create, move, the cycle refusal, delete re-parenting (order kept), sort ignoring articles, and
    renumbering.
  - The migration's `SortOrder` renumbering.
  - `ReadingListForecast`: the threshold, the math, and the "in N days" boundary.
  - `ReadingListOverlap`: the threshold, the minimum size, dismissals, pair normalization, and 3-way overlaps.
  - `ReadingListMerger`: insertion positions, notes and roles filling blanks, no overwrite, the announcement.
  - CSV round trip.
  - Text export with notes.
- **App tests:**
  - The sidebar flattening: depth, collapsed children hidden, flat list under a tag filter.
  - Drop-target resolution (a pure helper that takes a drop zone and returns a move).
  - `SelectedFolderId` placement for new and imported lists.
  - The folder overview totals.
  - `ReadingListChecklistPdf`: page count for 100 rows, a group row never orphaned at the bottom of a page, and bytes starting
    with `%PDF`.
- **Headless render check** of the sidebar with nested folders, the folder overview and the banner (the About-polish PNG render
  trick), then `avalonia-pro-max/review-checklist` before calling the UI done: brushes from `Pb*` resources only, and no
  hardcoded colours. The checklist's paper colours are the exception, since print is always black on white.

## Out of scope

- Folder combine modes (Q5), Smart List folders (Q6), a folder checklist (Q27), plugin hooks for folders (Q30).
- Reading lists for remote viewers (moved to the remote-sharing follow-ups, Q3).
- Cover thumbnails in the checklist (V4 B).
- A forecast in the sidebar (Q12).

## Defaults filled in while writing (flag in review)

These weren't asked explicitly; they follow from the decisions:
- The overlap minimum is **5 issues in the smaller list**.
- Under a tag filter, the sidebar shows matching lists **flat**.
- Re-reads **count** toward pace.
- Folder counts are **recursive**.

## Implementation notes (2026-09-28)

Built the same day from `2026-09-28-reading-lists-pitch-plan.md`. It is **uncommitted**, and nobody has seen it in the running app
yet. The headless renders and `avalonia-pro-max/review-checklist` were done; the pass fixed the chevron glyphs (now icons),
accessible names, a magic icon size, and card height/width on the overview.

Where the build differs from the design:

- **One migration for both specs.** `AddReadingListFolders` adds the folder table, the dismissal table, `FolderId`, and spec B's
  `ContinuityId` / `ContinuityOrderKind`. It also renumbers `SortOrder`, through a temp table: a correlated `UPDATE` sees rows it
  already changed. The generated `Down()` is kept; it only reverses this migration's own `ReadingLists` changes.
- **Where new lists land.** CBL, CSV and arc builders now put a new list at the end of the top level
  (`ReadingListFolders.PlaceNewList`). The Reading screen and the New Reading List dialog then move it into the selected folder
  (`ReadingScreenViewModel.PlaceCreatedList`). Library-screen "add to new list" and drag-dropped `.cbl` files stay at the top level.
- **Merge confirm.** It uses the shared `IDialogService` with two buttons, "Merge and delete X" and "Merge, keep both", instead of
  a checkbox; the dialog has no checkbox slot. Dismissing the dialog cancels.
- **Sidebar Delete from the right-click menu** confirms with the same dialog. A two-step button can't work inside a menu, which
  closes after one click. The hover delete button keeps `TwoStepConfirm`.
- **Rename is inline**, a text box in the row (Enter saves, Esc cancels, losing focus saves), for folders and lists alike. The app
  has no text-prompt dialog.
- **The sidebar moved** out of `MainWindow.axaml` into its own `Views/ReadingListsSidebar` control, so the row drag code-behind has a
  home. It copies the in-process `DataFormat` drag used by `ContinuityOverviewView`. Moving by keyboard is only through the
  right-click / Menu-key "Move to folder"; there is no keyboard reorder within a folder.
- **Forecast.** Re-reads count. The pace is cached per screen visit (`InvalidateForecast` on `GoReading`).
- **Checklist PDF size.** SkiaSharp embeds whole Segoe UI faces, so a one-page checklist is about 1.3 MB. That is acceptable for a
  file the user prints; font subsetting isn't available in this SkiaSharp build.
- **Folder export and import** are Data-level `ReadingListFolderIO`. The spec only named the App commands.

**Tests:**
- Data: `ReadingListFoldersTests`, `ReadingListTrackEnginesTests`, `AddReadingListFoldersMigrationTests`.
- App: `ReadingListGroupingAndChecklistTests`, `ReadingScreenPitchTests` (pure tree and drop helpers, sidebar, folders, forecast,
  overlap, merge), `ReadingPitchViewTests` (headless XAML load).
