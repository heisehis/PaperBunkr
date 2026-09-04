# Drag-and-Drop Import — Design

*Part of the "Library browsing extras" backlog (docs/Paperbunkr-Roadmap.md) — the next open item after
browse history and live folder-watch scanning, both already shipped.*

## CE-parity check (standing rule)

Checked `_reference/ComicRackCE` first. CE actually has three distinct drop behaviors, not one:

- **Window/reader-level** (`MainForm.cs:1849-1859`) — dropping a single file onto the main window
  or the open reader just **opens** that file (`OpenSupportedFile`); it never touches the scanner
  and isn't an import at all. Only ever accepts exactly one file.
- **Main comic grid** (`ComicBrowserControl.cs:2430-2543`) — dropping files/folders from Explorer
  onto the library list is a real, silent import via `Program.Scanner.ScanFilesOrFolders(...)` —
  the same call "Add Folder to Library" uses. No dialog, no toast. Comics dragged internally can
  also be dropped here to reorder, and dragging a tile *out* to Explorer exports the real file
  (`itemView_ItemDrag`, sets a genuine `FileDrop` clipboard format).
- **Reading-list/query tree sidebar** (`ComicListLibraryBrowser.cs:979-1136`) — richest behavior:
  files/folders dropped onto the Library tree node import; `.cbl` files dropped anywhere import as
  a new reading list (`ImportList`); real comics dragged from the grid onto a reading-list node add
  membership; dropped onto a smart list, they merge matchers instead; dropped onto empty tree
  space, they auto-create a new list.

This only ports as *behavior*, not code — CE is WinForms (`IDataObject`/`DragEventArgs`), Avalonia's
DnD API is unrelated. Confirmed with the user which pieces to bring over (see Scope below) — this
is a deliberate subset, not full CE parity, because CE's split-pane window (grid + tree visible at
once) doesn't exist in Paperbunkr's full-screen navigation model.

## Scope

- **Library screen**: dropping files/folders imports them into the library (CE's grid behavior).
- **Reading List screen**: dropping files/folders onto an *open* reading list imports them (if new)
  and adds them as members of that list — the closest equivalent to CE's tree-node membership drop
  that fits a single-screen-at-a-time UI, chosen over a persistent-rail drop target since Paperbunkr
  has no such rail today.
- **`.cbl`/`.csv` dropped on either screen** import as a new reading list via the existing
  `CblReadingListIO`/`CsvReadingListIO` import paths, mirroring CE's own dropped-`.cbl` handling.
- **Explicitly out of scope**: window/reader-level "drop a file to open it" (a different feature —
  opening, not importing), drag-*out*-to-Explorer export, and reordering via drag. None of these
  were asked for and each is its own scoped feature if wanted later.
- **Feedback**: no drag-over visual affordance (kept invisible like CE, no new hover state) — but a
  completion toast reports what happened, reusing the existing toast mechanism
  (`MainViewModel.ShowToast`, already threaded through `LiveFolderWatchService`).

## Mechanism

One new service, `DragImportService` (`Paperbunkr.App/Services`), is the shared entry point for
both screens. It takes the raw list of dropped filesystem paths (files and folders mixed) and:

1. **Expands folders** to their contained comic files only (recursive enumerate, same extension
   filter `LibraryFolderScanner` already uses via `Providers.Readers.GetFileExtensions()`) — a
   `.cbl`/`.csv` file nested inside a dropped folder is not surfaced by this step and is not
   imported; CE itself only special-cases a `.cbl` when it's a direct member of the drop payload
   (`DragDropContainer.cs:70-74`), never one discovered by recursing into a dropped folder, so this
   matches that precedent rather than inventing new recursive behavior. Also **registers each
   dropped folder as a `WatchedFolder`** if not already registered — exact-path dedup, same check
   `PreferencesScreenViewModel.AddFolder` already does
   (`!context.WatchedFolders.Any(w => w.Path == path)`), `Watch` defaults to `false`, matching the
   manual "Add Folder" flow. A dropped folder is treated as an explicit "this belongs in my
   library" gesture, so it earns a lasting registration, not just a one-off scan.
2. **Splits the flattened file list** — the comic files just discovered inside dropped folders,
   plus every top-level dropped item that's a file rather than a folder — by extension into three
   buckets: `.cbl`/`.csv` → reading-list files; supported comic extensions → comic files; anything
   else → skipped/unsupported (counted, not imported). Because folder-expansion in step 1 already
   filters to comic extensions only, this third bucket in practice only ever contains top-level
   loose files the user dropped directly (e.g. a stray `.txt` sitting next to a `.cbz` on the
   Desktop) — never incidental non-comic files (cover art, NFOs) sitting inside a dropped folder,
   since those are never enumerated as candidates in the first place.
3. **Imports comic files** via `LibraryFolderScanner.ImportNewFilesAsync` — already handles
   arbitrary paths and already dedupes against existing `Issue.FilePath`, so no new dedup logic is
   needed here.
4. **Imports `.cbl`/`.csv` files** via the existing `CblReadingListIO.Import`/`CsvReadingListIO.Import`
   static methods, each wrapped in its own try/catch (a malformed reading-list file is skipped and
   counted, not a batch-aborting failure — same "one bad file doesn't stop the batch" contract
   `LibraryFolderScanner.ImportFiles` already has for comics).
5. **Resolves `IssueId`s** for every comic file in the drop — both freshly-imported ones and ones
   that already matched an existing `Issue` by path (a re-query by `FilePath` after the import step)
   — and returns them in the result. The Library screen ignores this list; the Reading List screen
   needs it to attach membership.

```csharp
public record DragImportResult(
    int Imported,
    int AlreadyInLibrary,
    int SkippedUnsupported,
    int ReadingListsImported,
    IReadOnlyList<int> IssueIds);

public class DragImportService
{
    public Task<DragImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken ct = default);
}
```

Constructed fresh at each call site (`new DragImportService()`), matching this app's established
"no DI container, construct stateless providers fresh" precedent (`FilePickerService`,
`CoverThumbnailService`, and others already follow this).

## Avalonia wiring

Both screens get the same shape: `DragDrop.AllowDrop` on the root `Grid` (dropping anywhere on the
screen works, not just one control — matches CE's "drop anywhere on the list" behavior), with
`DragOver`/`Drop` handlers in code-behind (`LibraryScreen.axaml.cs`, `ReadingScreen.axaml.cs`).
`DragOver` accepts only when `e.Data.Contains(DataFormats.Files)`, else `DragDropEffects.None`.
`Drop` reads `e.Data.GetFiles()` (`IEnumerable<IStorageItem>`) and resolves each to a local path via
`.TryGetLocalPath()` — the exact pattern `FilePickerService` already uses for its file/folder
pickers. Items with no local path (e.g. a browser-sourced drag with no real file behind it) are
silently dropped from the batch — they were never real files to import.

- **Library screen** (`LibraryScreen.axaml`, root `Grid` at line 618): `Drop` →
  `await DragImportService.ImportAsync(paths)` → `LibraryScreenViewModel.LoadFromDatabase()` to
  refresh the grid → one summary toast, e.g. *"12 comics imported"*, extended with
  *", 2 already in library"* / *", 3 skipped"* / *", 1 reading list imported"* only when those
  counts are non-zero. No toast at all when every count is zero (nothing happened).
- **Reading List screen** (`ReadingScreen.axaml`): `DragDrop.AllowDrop="{Binding !IsEmptyList}"` —
  only active when a specific list is open, consistent with how the rest of this screen already
  gates on `IsEmptyList`. When no list is open, the drop target is simply inert (OS shows a
  "not allowed" cursor); no new empty-state UI. On drop: same `ImportAsync` call, then bulk-insert
  `ReadingListItem` rows for the returned `IssueId`s not already members of the active list —
  reusing the exact existing-set/`nextOrder` pattern `ReadingScreenViewModel.AddSelectedIssues`
  already uses (`ReadingScreenViewModel.cs:267-295`) — then `LoadReadingList(listId)` + a toast like
  *"8 comics added (5 newly imported)"*.

Both `Drop` handlers are `async void` (standard for top-level UI event handlers); the awaited
continuation runs back on the UI thread automatically via Avalonia's synchronization context, so no
explicit `Dispatcher.UIThread` marshaling is needed for the reload/toast step.

## Edge cases

- **Concurrent imports** (dropping again while an earlier drop is still importing, or a live-watch
  flush firing mid-drop) — no new guard added. Each operation opens its own `DbContext` and the
  existing scan/live-watch paths already run concurrently today without a lock; this introduces no
  new risk. Deliberate non-goal, to avoid overengineering a rare case.
- **Folder already registered** — re-dropping a known folder just re-imports any new files inside
  it; no duplicate `WatchedFolder` row (same dedup as the Preferences "Add Folder" flow).
- **Empty folder / all-unsupported drop** — folder still gets registered (it was an explicit "watch
  this" gesture) but the toast reflects zero imports plus whatever was skipped, e.g.
  *"0 comics imported, 3 skipped"*.

## Testing

- **`DragImportServiceTests`** (new, `Paperbunkr.App.Tests`, no UI involved): comics-only batch,
  folder registration (new + already-registered), an already-in-library file resolves an `IssueId`
  without re-importing, an unsupported file is counted as skipped, `.cbl`/`.csv` each create a
  `ReadingList`, a malformed `.cbl` doesn't abort the rest of a mixed batch, and a full mixed batch
  (folder + loose file + `.cbl`) in one call.
- **ViewModel-level**: the Library/Reading-List reload-and-toast wiring is testable via the existing
  "inject a fake callback" pattern already used throughout this codebase's ViewModel tests — no
  real `DragEventArgs` needed, since the code-behind `Drop` handler itself stays thin (extract
  paths, delegate to the service/VM).
- **Real on-screen drag-and-drop is not automatable here** — same standing gap as every other
  GUI-interaction spec in this project: FlaUI/UIA3 can't reliably simulate an OS-level file drag
  from Explorer. This will need a manual on-screen check by the user, flagged explicitly rather than
  claimed as covered by automated tests.
