# Reading Lists Screen Redesign — Design

**Date:** 2026-08-28
**Follows:** the Preferences rework (`2026-08-28-preferences-rework-design.md`). First of two
"remaining screens" redesigns folded into UI-rework Phase 7 — the Story Events screen is its own
later cycle (blocked on in-flight metadata phases 4d–4g).

This is a **full layout/UX redesign**, not the mechanical token/icon restyle already applied to
`ReadingScreen.axaml` this session (that restyle is superseded by this).

## Background

The current Reading Lists screen is a 236 px contextual sidebar (list picker, shared pattern with
Smart Lists / Story Events) plus `ReadingScreen.axaml` as one long scroll:
header (56×80 arc cover + name + tags + Edit/Copy/Refresh) → "Type:" row → collapsible event-link
search → status banner → 3 stat cards → a 7-button toolbar (2 dead: AniList/MAL) → collapsible
arc-search panel → a *second* search box (add issues) → then the list (groups → dense 8-column
rows: number, 30×42 cover, name, owned/missing + Read/Link, role combo, ↑ ↓, trash, always-visible
Notes textbox).

Problems: the list — the point of the screen — sits below ~400 px of chrome; two lookalike search
boxes do different jobs; dead disabled buttons; cramped rows; no sense of reading *progress* or
"what's next"; the real cover art + synopsis that CBL Manager downloads is a thumbnail.

`ReadingScreenViewModel` is 911 lines and already carries all the behavior (list load, groups,
library search + add, arc search + refresh, event linking, tag filter, CBL/CSV import-export,
per-row move/remove/role/notes/relink). This redesign **re-presents** that behavior; it does not
rebuild the data paths.

## Goal

One view that serves reading and curating equally (user: "both equally" + "always-visible,
lighter touch"): a **reading-first surface** — cover, progress, ordered issue list as the hero —
with management controls present but **recessed** (a permanent "＋ Add issues", everything else
under a "⋯ Manage" menu; per-row handles on hover). No mode toggle.

## Non-goals / out of scope

- The Story Events screen (separate cycle).
- The Smart Lists / Story Events sidebars — only the Reading Lists sidebar changes here; it will
  visually diverge from the other two until they catch up (accepted).
- `ReadingListPropertiesOverlay` internals — "Edit details" still opens it unchanged.
- New import sources, new arc adapters, AniList/MAL list import (the dead buttons are just removed).
- Reordering the reading-list data model or the `ReadingList`/`ReadingListItem` entities.

## Layout

`ReadingScreen.axaml`'s single outer `ScrollViewer` is replaced by a
`Grid RowDefinitions="Auto,Auto,Auto,*"`: a fixed **top band** (row 0), **progress row** (row 1),
and **action row** (row 2), then a **scrolling issue list** (`ScrollViewer` in row 3) that owns
the remaining height.

### 1. Top band (Full)

- **Cover** — ~88×132, `PbRadius`, elevation shadow. Arc cover art (`ArcCoverImage`) when present;
  otherwise a placeholder tile (list glyph on `PbSurface2`).
- **Title** — `pbTextHeading` (Bebas), with the arc-source badge inline (`via ComicVine`) when
  `IsArcLinked`.
- **Meta line** — `N issues · N read · N missing` (+ `· hand-built` when not arc-linked).
- **Tag chips** — the existing `Tags` / `TagPillViewModel` row (click-to-filter, right-click
  reweight) unchanged.
- **Synopsis** — `Subtitle`/description, 2-line clamp + "more" toggle. When empty: an "add one"
  prompt that opens the properties overlay.

### 2. Progress row

- Progress bar + `"{ReadCount} of {TotalCount} read"`.
- One **Continue** button: `▶ Continue — {label}`.
  - Target = first item in sort order whose `Issue` is owned and `!Issue.HasBeenRead()`
    (`Paperbunkr.Data.Metadata.IssueMetadataExtensions`; an in-progress issue is a valid target
    and the label reads `Resume`).
  - All owned items read → button reads `Re-read from start`, target = first owned item.
  - No owned items → button hidden.
- Row is hidden entirely for an empty list.

### 3. Action row (recessed management)

- **`＋ Add issues`** — permanent `Button.primary`. Opens an inline search popover bound to the
  existing `SearchQuery` / `SearchResults` / `AddIssueCommand`; each result has a `＋ add`;
  new items append to the end (existing behavior).
- **`⋯ Manage`** — a `MenuFlyout` / small popover holding:
  - `Import .CBL / .CSV` → `ImportCblCommand` / `ImportCsvCommand`
  - `Export` submenu → `ExportCblCommand` / `ExportAsTextCommand`
  - `Link story event` → `ToggleLinkStoryEventCommand` (its search UI drops down under the action row)
  - `Edit details` → `OpenPropertiesCommand`
  - `Refresh from {source}` → `RefreshArcListCommand` (shown only when `IsArcLinked`)
  - `Build from a story arc` → `ToggleArcSearchCommand` (its panel drops down under the action row)
- The `IsLinking` relink banner and the arc-search / event-link drop-downs render between this row
  and the list when active (same VM flags, just relocated).
- The `AniList` / `MyAnimeList` disabled buttons are **deleted**.
- The 3 stat cards are **deleted** (their numbers moved into the meta line).

### 4. Issue list (the hero)

Grouped by `GroupLabel` — uppercase `pbTextCaption` section headers (unlabeled group = no header).

**Row anatomy (resting):** order number (Bebas, muted) · cover thumb 42×63 · title + `series ·
year` subtitle · state indicator on the right.

- **Read** (`Issue.HasBeenRead()`): row at ~55% opacity, green `PbIconCheck` on the right.
- **Next-up** (the Continue target): `PbSurface2` background + `PbBorder` outline, amber order
  number, a `▶ Read` button on the right, `next up` in the subtitle.
- **Unread owned:** plain row.
- **Missing** (`Issue is null || Issue.FileIsMissing`): dashed-outline cover placeholder,
  `missing — not in your library` subtitle in `PbBadge`-ish tone, a `Find & link` button
  (`LinkCommand`).
- **Note present:** the note text renders italic, indented under the title with a left rule
  (read-only display); editing happens in the hover affordance.

**Click behavior:** whole row is the click target — owned → `OpenCommand` (read); missing →
`LinkCommand`. The explicit `▶ Read` button stays only on the next-up row.

**Hover reveals** (row grows a left gutter): drag handle (`⠿`, drag-reorder), role picker
(`RoleOptions` / `SelectedRoleOption`), `＋ note` / edit-note (inline `TextBox`, `Notes` binding),
`mark read` / `mark unread` toggle (**new** — see ViewModel), remove `✕` (`RemoveCommand`). The
always-visible per-row Notes `TextBox` and the `↑ ↓` text buttons are gone.

### 5. Empty / new list

Title + `empty`, then a dashed empty-state card: `＋ Add issues` · `Import .CBL / .CSV` ·
`Build from a story arc`. No progress row, no group rendering.

### 6. Sidebar (polish)

The Reading Lists branch of the MainWindow contextual sidebar gains, per row: a mini cover
(24×36, first item's cover or arc cover) + name + a thin progress bar. The count badge is dropped
(progress conveys it). `New Reading List`, the per-row delete (`TwoStepConfirm`), and the
tag-filter chip are unchanged.

`Models/ReadingListSummary` gains: `CoverIssueId` (`int?`), `ReadCount` (`int`), `TotalCount`
already exists. `MainViewModel.RefreshSidebar` / wherever `ReadingListSummary` is built populates
them (one extra `Items` projection per list — the sidebar query already loads lists).

## ViewModel changes

`ReadingScreenViewModel`:
- `int ReadCount`, `int TotalCount` (already have `TotalIssues` as a string — replace the three
  stat strings with ints + a `double ProgressFraction`).
- `ReadingListItemRowViewModel? ContinueTarget`, `string ContinueLabel`, `bool HasContinueTarget`,
  `[RelayCommand] Continue()` (calls the target row's `OpenCommand`).
- `bool HasSynopsis`, `bool SynopsisExpanded` + toggle.
- The `⋯ Manage` menu is a XAML `MenuFlyout` (no VM flag needed).
- `bool IsEmptyList` (`!HasNoReadingLists && TotalCount == 0`).
- Delete: `OwnedIssues` / `MissingIssues` string props, the 3 stat-card bindings.

`ReadingListItemRowViewModel`:
- `bool IsRead` (from `Item.Issue?.HasBeenRead() == true`).
- `bool IsInProgress` (from `Item.Issue?.IsInProgress() == true`) — for the `Resume` label.
- `bool IsNextUp` — set by the parent after it computes `ContinueTarget`.
- `[RelayCommand] ToggleRead()` — **new behavior**: flips the issue's read state. Sets
  `Issue.LastPageRead` to `PageCount` (mark read) or `0`/`null` (mark unread) and persists via the
  existing `PaperbunkrDb` path, then tells the parent to recompute progress + next-up. This is the
  one genuinely new capability; everything else is a re-presentation. Confirm the exact
  mark-read/unread mechanism against how the reader itself writes `LastPageRead` before building.

Constructor signatures otherwise unchanged.

## Testing

- `ReadingScreenViewModelTests` (extend):
  - progress math: `ReadCount`/`TotalCount`/`ProgressFraction` for a mixed list.
  - `ContinueTarget`: first owned-unread; skips read; skips missing; all-read → `Re-read`
    label + first owned; no owned → `HasContinueTarget == false`.
  - `IsNextUp` set on exactly the target row.
  - `IsEmptyList` true for a list with zero items, false otherwise.
  - `ToggleRead` flips `IsRead` and moves `ContinueTarget` to the next unread.
- Row VM: `IsRead` / `IsInProgress` / `IsMissing` combinations.
- Sidebar: `ReadingListSummary` gets `CoverIssueId` + `ReadCount` populated.
- Manual on-screen pass (standing no-unattended-GUI caveat): the list renders and scrolls under a
  fixed top band; Continue jumps to the right issue; hover reveals row management; Add-issues
  popover appends; Manage menu routes each action; empty-state; a hand-built (non-arc) list shows
  the placeholder cover; sidebar covers + progress bars render.
- Full suite (not filtered) per the `DatabasePathOverride` / `AvaloniaTestCollection` isolation
  lesson.

## v2 revisions (2026-08-28, after on-screen review)

The first implementation shipped but read as a cluttered admin table. These revisions supersede the
conflicting parts above.

### Row — collapse management into one menu

`Border.itemRow` at rest shows **only**: reading-position number · 42×63 cover · title
(`"{Name} #{Number}"`) · `"{Series} · {Year}"` · state indicator. Nothing else.

- **Left number is the reading-order position** (1, 2, 3…), not `Issue.EffectiveNumber()`. The
  issue number moves into the title line. Add `int Position` to `ReadingListItemRowViewModel`, set
  by the parent while flattening groups.
- On **hover**: a drag handle (`⠿`) on the left and a single **`⋯`** button on the right appear.
  `⋯` opens a `MenuFlyout`: *Mark as read* / *Mark as unread* (whichever applies), *Set role ›*
  (submenu of `RoleOptions`), *Add a note* (reveals the inline note `TextBox` for that row),
  *Move up*, *Move down*, *Remove from list*. No always-visible role `ComboBox`, no `✓/○` button,
  no `▲▼` pair, no permanent "Add a note…" field.
- **Role**, when set, shows as a small read-only chip on the right (before `⋯`), not a dropdown.
- **Note**, when present, shows inline italic under the title (unchanged). Empty → nothing until
  "Add a note" is chosen from `⋯`.
- The hover-hide is done with `:is(Control).<class>` selectors (bare `Control` matches only the
  exact type — the v1 bug) or per-type styles, plus `Opacity`+`IsHitTestVisible`.

### Progress row

- Bar height **8 px** (was ~2 px hairline).
- Label reads: `"Not started · {TotalCount} issues"` when `ReadCount == 0`; `"{ReadCount} of
  {TotalCount} read"` while in progress; `"Finished"` when all read.
- The Continue button reads **`Start reading`** when `ReadCount == 0` (target still = first owned
  issue), `Resume — {Number}` / `Continue — {Number}` mid-list, `Re-read from start` when done.
- Sits directly under a full-width divider below the action bar; tighten the vertical gaps between
  top band → progress → action row so the list starts higher.

### Top band

- Source badge on its **own line** under the title, not trailing it.
- Meta line de-duplicated: `"{TotalCount} issues · {ReadCount} read · created {date}"` — drop
  `"0 missing"` when `MissingCount == 0`, single separators only.
- Cover placeholder is a real glyph tile (`▤` on `PbSurface2`) — shown whenever `ArcCoverImage`
  is null. (A downloaded-but-blank ComicVine placeholder still shows as-is; not worth sniffing.)
- Synopsis toggle is a single label: `"more"` when collapsed, `"less"` when expanded — not
  `"more / less"`.

### Sidebar

- Cover **32×48** (was 24×36); null → `▤` placeholder tile.
- List name **wraps to 2 lines** (`TextWrapping="Wrap"`, `MaxLines="2"`), no mid-word clip.
- Progress bar **5 px** + a caption line under it: `"{ReadCount} / {TotalCount} read"` or
  `"Not started · {TotalCount}"`.
- The per-row delete (`TwoStepConfirm`) moves to **hover-only**, top-right of the row.
- Selected row: **3 px** amber left bar + a warm `PbSurface2` tint.
- Taller rows (~9 px vertical padding).
- **"＋ New Reading List" moves from a bottom list-item to a compact `＋` icon button at the
  top-right of the sidebar header** (beside the "READING LISTS" title). The bottom dashed item is
  removed. The `＋` button now opens the New Reading List dialog (below), not `CreateNewCommand`
  directly.

### New Reading List dialog

Clicking the sidebar `＋` opens a backdrop-dimmed overlay (same pattern as
`ReadingListPropertiesOverlay` — a `MainViewModel` `IsNewReadingListDialogOpen` flag, rendered in
`MainWindow.axaml`). New `Views/NewReadingListOverlay.axaml` + `ViewModels/NewReadingListViewModel`.

Single screen with progressive disclosure:

- **Name** `TextBox` (default `"New Reading List"`), always visible.
- **Build it from…** — four selectable method cards:
  1. **Start blank** — nothing extra. Create → new empty list.
  2. **Import a file** — nothing extra. Create → file picker (`.cbl`/`.csv`, one picker, format
     inferred from extension) → `CblReadingListIO.Import` / `CsvReadingListIO.Import` (these
     already create the list); if the name field was changed from the default, rename the imported
     list to it.
  3. **A published story arc** — selecting it expands an inline block: source `ComboBox`
     (`ArcSourceOptions`) + query `TextBox` + Search button + results list. Each result has a
     "Use" that runs the existing `ArcReadingListBuilder.CreateFromArcAsync` and closes.
  4. **An existing story event** — selecting it expands an inline block: a `ComboBox` of the
     user's `StoryEvent`s. Create → new list seeded from that event's `Members` (ordered),
     carrying each `EventMembership.Role`, with `Type = Event` and `StoryEventId` set + linked.
- **Create** button: enabled once a method is chosen (and, for arc, an arc is picked via "Use"
  which self-completes; for event, an event is selected). **Cancel** / backdrop click / `Escape`
  closes with nothing created.
- On any successful create, `NewReadingListViewModel` invokes an `Action<int> onCreated(listId)`
  that `MainViewModel` wires to: close the overlay, `Reading.LoadReadingList(listId)`,
  `Reading.RefreshSidebar()`.

`ReadingScreenViewModel.CreateNew` gains an optional `string? name` param (used by the blank path);
the parameterless `CreateNewCommand` is no longer bound to any UI but kept for tests /
`GoLibraryFoldersPreferences`-style callers if any (verify none, then it can go).

### Testing additions

- Row `Position` numbering is 1-based and continuous across groups.
- Progress label text for the none/partial/all-read cases; Continue label `Start reading` when
  `ReadCount == 0`.
- `NewReadingListViewModel`: blank path creates a list with the given name; event path seeds items
  from the event's members with roles + `Type = Event` + `StoryEventId`; import/arc paths delegate
  to the existing IO/builder (a light test that the callback fires with the new id).
- Sidebar delete still works from its new hover position (existing `TwoStepConfirm` test covers
  the command; no new logic).

## Build note

Per CLAUDE.md: `ReadingScreen.axaml` is an existing compiled view (no `AVLN2000` risk). If a
build fails inside XAML compilation after `CoreCompile` succeeded, delete
`obj/Debug/net10.0/Paperbunkr.App.dll` + `.pdb` and rebuild rather than retrying `dotnet build`.
