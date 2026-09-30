# Library bulk actions — Design

*Grilled 2026-09-29 (three rounds + five design sections, all confirmed). Visual companion screens: `.superpowers/brainstorm/1235-1790702217/content/`
(`selection-bar.html`, `icon-bar-v2.html`, `dialogs.html`).*

## Scope

More actions, and more of them working on a multi-selection, in the comics **Library** screen: CE parity gaps, Paperbunkr's own
inconsistencies, and three Paperbunkr-native additions. One mechanism (an action catalog) feeds the right-click menu, the selection bar,
and the keyboard.

**Out of scope:** the Books (EPUB/PDF) screen, the Library preview panel's own buttons, remappable Library shortcuts (the catalog prepares
for it; `KeyboardCommandRegistry` stays Reader-only), CE's Open With ▸ (needs an external-program manager), Mark Checked/Unchecked
(no `Checked` field in the data model), Browse Books ▸ (search and the preview pills cover it), Repack & Inject Metadata (backlog item,
own spec — it is a slice of CE's Export dialog), cover actions (stay on Detail), undo for Mark Read/Unread and the Series ▸ setters.

## CE reference (verified in `_reference/ComicRackCE`)

| CE behavior | Where | Used for |
|---|---|---|
| My Rating ▸ None/1–5 sets every selected editable book regardless of current values; ✓ only when all share (`GetRating()` returns -1 otherwise); undo marker "Change Rating"; Alt+Shift+0–5 | `MainForm.cs:133-164`, `ComicBrowserControl.cs:1065-1092` | Q14 |
| Mark as ▸ Read/Unread Alt+Shift+R/U | `ComicBrowserControl.Designer.cs:317-369` | shortcuts |
| Reveal Ctrl+G, Copy Data Ctrl+C (first selected), Paste Data… Ctrl+V (all selected) | `ComicBrowserControl.cs:2724, 2808, 2852` | shortcuts, §2 |
| Paste dialog: one checkbox per field, bold = source has a value, Mark defined/All/None, ticks remembered in `Settings.PasteProperties` (default none), a ticked empty value overwrites, list fields replaced whole, no undo | `ComicDataPasteDialog.cs:49-199`, `ComicBook.cs:2313-2335` | §3 (we add undo) |
| Clear Data: `ResetProperties()` resets story/credit/catalog fields, Rating, CommunityRating, custom values, PageCount→0; keeps read state, file info, pages; prompt "…can be reverted with Undo"; undo marker | `ComicBook.cs:2131`, `ComicBrowserControl.cs:2819-2827` | §2, §3 (we keep PageCount) |
| Refresh: plain = thumbnails only; Ctrl = re-read ComicInfo.xml overwriting the DB | `ComicBrowserControl.cs:2594-2618`, `ComicBook.cs:2483-2504` | §2 (made explicit: two entries) |
| Filter/search change rebuilds the list; hidden books lose selection, only the focused one survives if visible | `ComicBrowserControl.cs:2036-2104`, `ItemView.cs:2276-2281` | §1 selection pruning |
| Show in List ▸ lists every list containing the (first) book | `ComicBrowserControl.cs:3087, 3157` | §2 |
| Invert Selection | `ComicBrowserControl.cs:1013` | §2 |

Deliberate deviations: undo on Paste Data; Clear Data does not zero PageCount or touch the series (a Paperbunkr series is its own entity);
Refresh's Ctrl modifier becomes a visible second entry; Ctrl+Delete's skip-confirmation is **not** adopted.

## 1. Architecture

### Action catalog

`ViewModels/LibraryActions/LibraryActionCatalog.cs` holds one `LibraryAction` per action:

| Member | Meaning |
|---|---|
| `Id` | stable string (`"rating"`, `"paste-data"` …) — future remap key |
| `Icon` | `FluentIcons.Common.Symbol` |
| `Gesture` | Avalonia `KeyGesture?`; its display string is the menu hint and the tooltip suffix |
| `AppliesTo` | flags: `Issue`, `Series`, `RemoteIssue`, `RemoteSeries` |
| `Group` | bar/menu group; a separator goes between groups |
| `OnBar`, `OnMenu` | which surfaces show it |
| `Build(LibraryActionContext)` | returns a `ContextMenuEntry?` — label (with count), enabled, ✓, children — or null when hidden |

`LibraryActionContext`: the clicked row or card (null for bar/keyboard), the resolved id set (`UnionForAction` for a click, the current
selection otherwise), selection kind (issue/series), `HasFile`, `IsRemote`. **Every count in a label comes from the resolved set** — this
fixes the current bug where right-clicking an unselected tile inside a selection reads "Mark as" but acts on all of them.

### Surfaces

1. **Right-click** — `LibraryContextMenuBuilder.Build` asks the catalog for its menu actions in menu order and inserts group separators.
   `ContextMenuEntry` / `ContextMenuHost` unchanged. Remote menus = the catalog filtered by `RemoteIssue`/`RemoteSeries`.
2. **Selection bar** — new `Views/SelectionActionBar` replaces the hand-written row at `LibraryToolbar.axaml:284-315`: count text, then an
   `ItemsControl` of icon buttons. An entry with children gets a ▾ and opens them through `ContextMenuHost.ShowMenu` (the existing "…"-button
   helper), rebuilt at click time. Tooltip = label + gesture; `AutomationProperties.Name` = label. Delete and Clear
   are right-aligned. The bar's two hand-rolled popups (Plugins, Add to List) are retired.
3. **Keyboard** — `OnLibraryScreenKeyDown` looks the gesture up in the catalog and runs that action on the current selection. Skipped when a
   `TextBox` has focus (Ctrl+C/V in search keep their text meaning). Existing `/`, Ctrl+A, Delete, Ctrl+I move onto catalog entries.

### Code layout

Command bodies stay `[RelayCommand]`s on `LibraryScreenViewModel`, in new partials beside `.Compare.cs`: `.Rating.cs`,
`.DataTransfer.cs` (copy/paste/clear), `.Refresh.cs`, `.SeriesMerge.cs`, `.ShowInList.cs`, `.SelectionActions.cs` (invert, copy paths,
read-up-to-here, series-wide setters).

New services:
- `Services/MetadataClipboardService` — shared singleton holding the `FieldClipboard` record, moved out of `IssuePropertiesScreenViewModel`
  (which now uses the service, so a copy in the editor pastes in the Library and vice versa). App-internal, not the system clipboard.
- `Services/IssueFileRescanService` — re-reads one issue from its file: `EmbeddedComicInfoReader.TryRead` + `CeLibraryMigrator.MapStoryFields`
  with full overwrite, plus page count and file size.

Selection (`TileSelectionController`): `PruneTo(visibleIds)` after every rebuild of the visible rows (CE parity, replaces the current
re-sync that keeps hidden ids); `InvertWithin(visibleIds)`; `OnGranularityChanged` clears the other kind's selection.

## 2. Actions

"Selection" = the resolved set. "Local" = remote books filtered out (`LocalIssuesOnly`).

| Action | Applies to | Behavior |
|---|---|---|
| **My Rating ▸** None, ★1–★5, then Quick Rate… | issues (local) | Sets `Issue.Rating` on all, whatever their values. ✓ only when all share. Per book: write-back enqueued when file fields differ and `RatingChanged` log (as Quick Rate). One undo entry "Rate N books". Quick Rate… stays single. |
| **Mark as ▸ Read up to here** | one issue (local) | The series' non-special issues in `OrderByRun()` order up to and including the clicked one → marked read (`IssueReadStateResolver.MarkAsRead`), then tracker auto-sync. No undo (same as Mark Read). Disabled with a tooltip when `NumberSortKey()` is null. |
| **Series ▸** setters | issues or series | Apply to every distinct series in the selection. ✓ only when all share. |
| **Show in List ▸** | one issue | Reading lists and collections containing it (a collection holding its series counts); click navigates. Empty → disabled "(Not in any list)". |
| **Copy Data** Ctrl+C | one issue | First selected → `MetadataClipboardService`. Toast "Copied data from *Series #N*". |
| **Paste Data…** Ctrl+V | issues (local) | Enabled only with clipboard content. Opens the paste dialog (§3). One undo entry. |
| **Clear Data…** | issues (local) | After confirmation, resets every history-registry field to empty/null. Keeps read state, file info, pages, page tags, bookmarks, cover, series. One undo entry. |
| **Refresh ▸ Refresh thumbnails** | issues / series' issues (with file) | `CoverThumbnailService.TryGenerateThumbnail(force: true)` per book, skipping books with a custom cover. Activity Center job, then tiles refresh. |
| **Refresh ▸ Re-read info from file…** | issues / series' issues (with file) | After confirmation, `IssueFileRescanService` per book. One undo entry for the batch. Cancellable Activity Center job; cancel keeps finished books and the undo covers them. |
| **Show in Explorer** Ctrl+G | with file | `RevealIssues()` for many, `RevealIssue` for one; series → `RevealSeries`. |
| **Copy file paths** Ctrl+Shift+C | issues / series' issues | Full paths, one per line, display order, to the system clipboard (`IClipboard`). Fileless/remote skipped. Label "Copy file path" for one. |
| **Merge series…** | 2+ local series | Merge dialog (§3). |
| **Invert Selection** | current kind | Within visible rows. |
| Series menu additions | series | Mark Read/Unread, Add to Reading List, Plugins, Bulk Edit — expanded to the series' issues via `SelectionBarIssueIds`. |
| **Delete ▾** on the bar | both | "Remove from library (keep files)" / "Delete…", same as the menu (closes the todo's keep-file "Not done"). |

### Menu order (issue)

Open · Edit Properties… · My Rating ▸ · Mark as ▸ (Read, Unread, Read up to here) · Add to Reading List ▸ · Add to Collection ▸ ·
Show in List ▸ │ Go to Series · Series ▸ │ Scrape… · Organize… │ Copy Data · Paste Data… · Clear Data… · Write metadata to file(s) ·
Refresh ▸ │ Show in Explorer · Copy file path(s) · Compare files… │ Plugins ▸ │ Select All · Invert Selection · Clear Selection │ Delete ▸

Series menu: Open Series · Bulk Edit… · Mark as ▸ · Add to Reading List ▸ · Add to Collection ▸ │ Content Type/Direction/Status ▸ │
Scrape · Organize · Write metadata · Refresh ▸ · Merge series… │ Show in Explorer · Copy file paths │ Plugins ▸ │ Select All · Invert ·
Clear │ Delete Series ▸.

### Selection bar (icon-only, left → right)

Issue selection: count │ Bulk Edit, My Rating ▾ │ Mark Read, Mark Unread, Add to ▾ (Reading List / Collection sections, each ending in
"New…") │ Scrape, Organize, Write metadata, Paste Data, Clear Data, Refresh ▾ │ Show in Explorer, Copy file paths, Plugins ▾ (hidden
when none) ⟶ Delete ▾, Clear.

Series selection: My Rating, Paste Data and Clear Data drop out; Merge series… appears after Write metadata.

### Keyboard

| Keys | Action |
|---|---|
| Alt+Shift+0…5 | My Rating |
| Alt+Shift+R / U | Mark Read / Unread |
| Ctrl+G | Show in Explorer |
| Ctrl+C / Ctrl+V | Copy Data / Paste Data… |
| Ctrl+Shift+C | Copy file paths |
| Ctrl+I, Ctrl+A, Delete, `/` | unchanged |
| Esc | clear selection, only when the shell's `EscapeCommand` had nothing else to close |

All skip when a `TextBox` has focus.

## 3. Dialogs and confirmations

**Paste Data** (`PasteDataOverlay` + VM, in `OverlayShell`): the book-owned `BulkFieldRegistry` fields in the bulk editor's own groups (Main /
Artists / Plot & Notes); title "Paste data onto N books", subtitle names the source book. Bold = source value present. Mark defined / All / None. Ticks
persist in a new `AppSettings.PasteDataFields` (comma-separated field ids; null = none ticked, CE's default). "Paste K fields" button,
disabled at 0. Ticked fields overwrite every target; an empty value clears; list fields replaced whole. Snapshot → save → one history
record. Write-back enqueued with the bulk editor's rule. Toast "Pasted K fields onto N books · Ctrl+Z to undo". No per-target re-read from
file first (CE merges in unsaved file state; Paperbunkr has none).

**Merge series** (`MergeSeriesOverlay` + VM): radio rows (cover, name, publisher, issue count); default target = most issues. The
duplicate count is computed up front with `SeriesMergeHelper`'s `(EffectiveNumber, EffectiveVolume)` rule and warned: "N issues exist in
both. The duplicate rows are removed from the library; the files on disk are not touched. This can't be undone." Confirm "Merge into *X*"
→ `MergeInto` per source, one `SaveChanges`, Library reload, selection cleared.

**Confirmations** (existing `ConfirmDialog`):
- Clear Data: "Remove all entered data from N books? Read progress, files and covers are kept. This can be reverted with Undo." —
  **Clear** / Cancel.
- Re-read: "Replace the library's metadata for N books with what's stored in their files? This can be reverted with Undo." — **Re-read** /
  Cancel.

## 4. Errors and edge cases

- **Remote:** remote rows get only Open, Mark Read/Unread, Go to Series, Copy Data, Select All / Invert / Clear (no Show in List - a remote
  book is never in a local list); remote series cards get Open and the selection entries. Mixed
  selections run editing actions on local books; the toast appends "· M remote books skipped".
- **Fileless / unreadable:** Refresh, Re-read, Reveal, Copy paths skip fileless books. A missing or unreadable file counts as failed on the
  job (`itemsFailed`, summary "Re-read 40 books · 2 failed"). A file with **no ComicInfo.xml** leaves the book unchanged (counted as "no
  embedded info") — never a wipe.
- **Undo:** one history record per bulk action; a cancelled Re-read records only finished books.
- **Concurrency:** jobs work on id lists captured at start; a book deleted mid-job is skipped. A second Re-read while one runs is queued
  (`startQueued`).
- **Digit shortcuts:** Avalonia's `KeyGesture.Parse` reads a bare "3" as `Key.Tab` (enum value 3); the catalog maps digits to `D0`–`D9`
  itself (`LibraryActionCatalog.ParseGesture`), and `ContextMenuHost` shows no hint for a digit or range gesture rather than a wrong one.
- **Detach-while-routing** (CLAUDE.md gotcha): bar flyout closes, dialog closes and any row-changing result (Merge, post-Mark-Read
  pruning) are deferred with `Dispatcher.UIThread.Post`; the Merge confirm opens a fresh `DbContext` inside the posted lambda.
- **Disabled states:** Paste until something is copied; Merge below 2 local series; Read up to here without a numeric series number.

## 5. Testing

In `Paperbunkr.App.Tests`, headless framework + SQLite in-memory context, as the existing Library tests.

- `LibraryActionCatalogTests` — per target (issue/series/remote/mixed) the right actions and counts; the unselected-tile label regression
  ("Mark 4 as" with 3 selected); gestures unique; bar order per kind.
- `LibraryContextMenuBuilderTests` — existing tests stay green against the catalog; new order, Show in List, Series ▸ ✓ with mixed values.
- VM command tests per partial: rating (bulk + undo + write-back), read-up-to-here (run order, specials excluded, tracker sync), paste
  (ticked only, empty clears, lists replaced, one undo restores), clear (read state/cover/series kept), series setters across a selection,
  merge (duplicates removed, issues moved, target kept), copy paths (order, fileless skipped).
- Selection: prune after a search change; Mark Read under the Unread filter prunes; granularity switch clears the other kind; invert.
- Services: `IssueFileRescanService` (overwrite, no-ComicInfo, missing file); `MetadataClipboardService` shared between editor and Library.
- Headless view: `SelectionActionBar` renders one button per bar action with tooltip and automation name; ▾ opens a flyout; Alt+Shift+3
  rates the selection; Ctrl+C in the search box does not copy data.
- No FlaUI/on-screen automation without asking. `avalonia-pro-max/review-checklist` on the bar and both dialogs before calling it done.
