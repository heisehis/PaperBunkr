# Library Folder Management Redesign — Design (Phase 3 of Preferences)

**Continuing the Library section redesign** — the Preferences Tile-Hub Redesign's §4 deferred
list, item 1: "Library folder management — Comic Library Folders + Book Folders repeaters."
Follows [Library Health Dashboard Redesign](2026-09-07-library-health-redesign-design.md) (Phase
2, shipped/committed as `2f8e437`), which established this Library page's visual language: icon
chips, semantic color, healthy empty states, disclosure/grouping for secondary content, and the
"operations already auto-toast via `IActivityService`, don't duplicate with inline status text"
pattern.

## Background

`LibrarySection.axaml` has two structurally near-identical blocks: **Comic Library Folders** (a
watched-folder repeater — path, per-row watch toggle, Open/Remove — plus 7 action buttons: Add
Folder, Scan Now, Generate Covers, Sync Metadata, Repair Missing Covers, Verify & Repair Covers,
Clear Comic Cover Cache) and **Book Folders** (path, Open/Remove, no watch toggle, 3 buttons: Add
Folder, Scan Now, Clear Book Cover Cache). Both still use the pre-redesign `groupBox`/plain-`Grid`
row treatment.

A CE-precedent check (`PreferencesDialog`'s "Libraries" tab) found CE's actual folder UI is far
sparser: a checkbox-list where the checkbox *is* the watch toggle (no separate label), plus
Add/Change/Remove/Open buttons and one Scan button. **None of the 5 cover/metadata maintenance
buttons exist in CE's folder UI at all** — CE's closest equivalent ("Generate Cover Thumbnails")
lives on the main Library screen's own toolbar, operating on selected books. So those 5 buttons
are a Paperbunkr-original addition sitting in a spot CE never put anything like them — worth
knowing, but out of scope to relocate here (see Non-goals).

Reached via `/grilling` + 2 rounds in the visual companion (an initial single-direction mockup,
then 5 explicit structural alternatives). The user picked **segmented Folders/Maintenance tabs**
over 4 alternatives (icon-chip-with-disclosure, spacious per-folder cards, ultra-minimal
overflow-menu list, always-visible muted toolbar).

## Goals (this phase)

- Both blocks get the same visual primitives: a folder icon chip per row (color-coded: amber for
  Comic, green for Book, purely to tell the two lists apart at a glance — no semantic meaning
  beyond that, unlike Library Health's status-driven chip colors), icon-only Open/Delete buttons,
  and a bare toggle with no redundant "Watch for changes" label (Comic Folders only — Book Folders
  has no per-row watch concept today and isn't gaining one).
- Each block becomes a 2-tab segmented view: **Folders** (the repeater + Add Folder + Scan Now)
  and **Maintenance** (the other buttons — 5 for Comic, 1 for Book). Switching tabs fully replaces
  the visible content within that block's card; this is not a disclosure (Library Health's
  pattern) and not a dropdown menu — a real tab switch, per the user's explicit choice.
- Empty state: when a folder list has zero entries, show a "no folders yet, add one to get
  started" prompt instead of a blank repeater — same reasoning as Library Health's empty states.
- Every maintenance operation (`ScanNow`, `ScanBooksNow`, `GenerateCovers`, `SyncMetadata`,
  `RepairMissingCovers`, `VerifyCovers`, the two cache-clear commands) already runs inside an
  `_activity.StartJob(...)` call with `job.Succeed`/`job.Fail` — confirmed by inspection, all 7
  already auto-toast via the existing `IActivityService` pipeline (same discovery Library Health's
  Verify Now redesign made). Their inline `ScanStatus`/`BookScanStatus` text is exactly as
  redundant as `LibraryHealthVerifyStatus` was — remove it.
- One shared `BusyIndicator` per block's Maintenance tab, bound to whichever job is currently
  running there.

## Non-goals

- Not relocating the 5 cover/metadata maintenance buttons out of Preferences to the Library
  screen's own toolbar, even though that's closer to CE's actual placement — a genuine
  architectural change (touching `LibraryScreen.axaml`/`LibraryToolbar.axaml`, not just this
  section) that deserves its own brainstorm, not a rider on a Preferences visual redesign.
- No "Change folder" button (CE has one; Paperbunkr doesn't and isn't gaining one — remove +
  re-add covers the same need, and nobody's asked for it).
- No change to `LibraryFolderScanner`'s actual scan/generate/sync/repair logic — presentation only.
- Virtual Tags editor (§4 item 3) is a separate phase, not touched here.

## Architecture

### 1. Shared row treatment

Both blocks' `ItemsControl` templates (`WatchedFolderSummary`/`BookFolderSummary`) get: a small
folder icon chip (24-26px, same soft-tint pattern as Library Health's stat chips — amber for
Comic, green for Book), the path, then icon-only buttons for Open/Remove (reusing `fi:SymbolIcon`
Open/Delete, matching Library Health's icon-button convention). Comic rows additionally get a
bare `ToggleSwitch` (no `OnContent`/`OffContent`, no adjacent label `TextBlock` — the current
"Watch for changes" caption is dropped, with a `ToolTip.Tip` carrying that same text instead).

### 2. Segmented tabs

New per-block state: a bool (`IsComicFoldersMaintenanceTabActive` / `IsBookFoldersMaintenanceTabActive`,
default `false` — Folders tab shown first) toggled by two small tab buttons at the top of each
card. Avalonia has no built-in segmented-control primitive already in use elsewhere in this app,
so this follows the existing button-pair-with-`Classes.active` convention already used for the
Preferences sidebar's own nav buttons (`Classes.active="{Binding ...}"`), scaled down to two
inline tab buttons rather than the sidebar's vertical stack.

**Folders tab:** the row repeater + empty state + `Add Folder`/`Scan Now` buttons (unchanged
commands, restyled).

**Maintenance tab:** Comic Folders shows all 5 buttons (Generate Covers, Sync Metadata, Repair
Missing Covers, Verify & Repair Covers, Clear Comic Cover Cache) plus the shared `BusyIndicator`;
Book Folders shows its 1 button (Clear Book Cover Cache) the same way. Every maintenance button
(including folder-tab's own Scan Now, since it's also a `_activity`-tracked operation) becomes
disabled while **any** operation in that block is running, not just its own flag — necessary now
that one shared `BusyIndicator`/job reference represents "what's currently running" for the whole
block; without this, two operations could overlap and the indicator would only ever reflect
whichever started last.

### 3. Empty state

Below the (now-icon'd) row repeater, when `WatchedFolders.Count == 0` /
`BookFolders.Count == 0`: a success-tinted chip + "No folders yet — add one to start building
your library" (Comic) / "...your book collection" (Book), matching Library Health's empty-state
visual shape (icon chip + short message, no border/card of its own).

### 4. Busy indicator + toast cleanup

New `[ObservableProperty] ActivityJob? _currentComicFolderJob` / `_currentBookFolderJob`, set at
the start of `ScanNow`/`GenerateCovers`/`SyncMetadata`/`RepairMissingCovers`/`VerifyCovers` (Comic)
and `ScanBooksNow`/the book cache-clear command (Book), cleared in each `finally`. Remove
`ScanStatus`/`BookScanStatus` properties and their bindings entirely — completion/failure already
surfaces as a toast via each operation's existing `job.Succeed`/`job.Fail` call, same as Library
Health's Verify Now.

## ViewModel changes (summary)

- New: `IsComicFoldersMaintenanceTabActive`, `IsBookFoldersMaintenanceTabActive` (bool, default
  false) + their toggle commands.
- New: `CurrentComicFolderJob`, `CurrentBookFolderJob` (`ActivityJob?`).
- New: `HasWatchedFolders` (`WatchedFolders.Count > 0`), `HasBookFolders` (`BookFolders.Count > 0`)
  for the empty-state bindings (mirroring `HasMissingFileItems`'s shape from Library Health).
- Removed: `ScanStatus`, `BookScanStatus`.
- Changed: every Comic maintenance command's `IsEnabled` binding switches from its own individual
  `!IsXxx` flag to `!(IsScanning || IsGeneratingCovers || IsSyncingMetadata || IsRepairingCovers ||
  IsVerifyingCovers)`; Book's switches to `!(IsScanningBooks || IsClearingBookCoverCache)`. The
  individual flags themselves are unchanged internally (still what each command's own guard checks
  and sets), only the buttons' combined `IsEnabled` expression changes.

## Testing

- `PreferencesScreenViewModelTests`: existing `ScanNow`/`GenerateCovers`/etc. tests updated to
  assert on the `showToast` callback (established pattern from Library Health's Bulk Relink test)
  instead of the removed `ScanStatus`/`BookScanStatus` properties. New cases: tab-toggle commands
  flip the right bool; `HasWatchedFolders`/`HasBookFolders` reflect list state; a maintenance
  button is disabled while a *different* operation in the same block is running (the new combined
  `IsEnabled` behavior).
- Manual on-screen pass (standing no-unattended-GUI caveat): empty Comic/Book folder lists show
  the "add one to get started" prompt; switching Folders/Maintenance tabs preserves state
  correctly; running a scan disables the other maintenance buttons in that block and shows the
  busy indicator; completion shows as a toast, not inline text; the bare watch toggle's tooltip
  reads correctly.

## Deliverable

No new `SettingsRow`s here (this block stays outside that primitive, same reasoning as Library
Health's §4 - dynamic repeaters, not static rows) — nothing to add to
`docs/preferences-descriptions-todo.md`.
