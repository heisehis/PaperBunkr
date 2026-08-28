# Reading Lists Screen Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-28-reading-lists-screen-redesign-design.md*

Known limitation (accepted, consistent with the rest of the app): `IssueReadStateResolver.MarkAsRead`
and `Issue.HasBeenRead()` no-op / return false when `Issue.PageCount` is null (unscanned/fileless),
so progress + Continue only reflect issues that have actually been opened once. Same behavior as
Library / Detail / Manga mark-as-read today.

## Step 1: row VM — read state + next-up + toggle
**Files:** `src/Paperbunkr.App/ViewModels/ReadingListItemRowViewModel.cs` (edit)
**What:** add `bool IsRead` (`Item.Issue?.HasBeenRead() == true`), `bool IsInProgress`
(`Item.Issue?.IsInProgress() == true`), `[ObservableProperty] bool _isNextUp`. Add a
`_onToggleRead` `Action<ReadingListItemRowViewModel>` ctor param + `[RelayCommand] ToggleRead()`
→ `_onToggleRead(this)`. `Number` already uses `EffectiveNumber()`; add `string SeriesLine`
(`"{Series} · {Year}"` / `"{Series}"` / `"missing — not in your library"`).
**Depends on:** none
**Verify:** build; row VM tests (Step 6).

## Step 2: screen VM — progress, continue, synopsis, empty
**Files:** `src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs` (edit)
**What:**
- Replace `TotalIssues`/`OwnedIssues`/`MissingIssues` (string) with `int TotalCount`,
  `int ReadCount`, `int MissingCount`, `int OwnedCount`, `double ProgressFraction`
  (`TotalCount == 0 ? 0 : (double)ReadCount / TotalCount`), computed in `LoadReadingList` from the
  built rows.
- `ReadingListItemRowViewModel? ContinueTarget` + `string ContinueLabel` + `bool HasContinueTarget`:
  after building `Groups`, flatten rows in order; target = first `IsOwned && !IsRead`
  (label `"Resume — {Number}"` if `IsInProgress`, else `"Continue — {Number}"`); if none unread
  but some owned → first owned, label `"Re-read from start"`; if no owned → `HasContinueTarget=false`.
  Set `ContinueTarget.IsNextUp = true`.
- `[RelayCommand] Continue()` → `ContinueTarget?.OpenCommand.Execute(null)`.
- `bool HasSynopsis` (`!string.IsNullOrWhiteSpace(Subtitle)` — keep `Subtitle` as the description
  holder but stop defaulting it to the "Cross-series reading order · tracked list" filler; empty =
  no synopsis), `[ObservableProperty] bool _synopsisExpanded`.
- `bool IsEmptyList` (`!HasNoReadingLists && TotalCount == 0`); raise on load.
- `ToggleReadRow(ReadingListItemRowViewModel row)` private handler: open context, `Find` the
  `Issue`, `IssueReadStateResolver.MarkAsRead/MarkAsUnread` (based on current `row.IsRead`),
  `SaveChanges`, then `LoadReadingList(_activeReadingListId)` to recompute everything. Pass into
  each row ctor.
- Delete: `HasNoItems` stays; the stat-string props go.
**Depends on:** Step 1
**Verify:** build; `ReadingScreenViewModelTests` (Step 6).

## Step 3: screen VM — sidebar summary fields
**Files:** `src/Paperbunkr.App/Models/ReadingListSummary.cs` (edit),
`src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs` (`RefreshSidebar`, edit)
**What:** `ReadingListSummary` gains `int? CoverIssueId`, `int ReadCount`, `double ProgressFraction`.
`RefreshSidebar`'s query already `.Include(r => r.Items)`; add `.ThenInclude(i => i.Issue)` and
populate: `CoverIssueId` = first item's `Issue?.Id`; `ReadCount` = items where
`Issue?.HasBeenRead() == true`; `ProgressFraction` from the two counts. `TotalCount` already set.
**Depends on:** none
**Verify:** build; sidebar summary test (Step 6).

## Step 4: ReadingScreen.axaml — full rewrite
**Files:** `src/Paperbunkr.App/Views/ReadingScreen.axaml` (rewrite),
`src/Paperbunkr.App/Views/ReadingScreen.axaml.cs` (unchanged unless a converter helper is needed)
**What:** `Grid RowDefinitions="Auto,Auto,Auto,*"`:
- **Row 0 top band** — 88×132 cover (`ArcCoverImage`, else placeholder `Border` w/ list glyph);
  `pbTextHeading` title + inline source badge (`IsArcLinked`); meta line
  (`{TotalCount} issues · {ReadCount} read · {MissingCount} missing` + `· hand-built` when
  `!IsArcLinked`); the existing `Tags` `ItemsControl` (verbatim); synopsis `TextBlock`
  (`MaxLines=2`, `TextTrimming`) + "more"/"less" toggle bound to `SynopsisExpanded`; when
  `!HasSynopsis` show an "Add a description" link → `OpenPropertiesCommand`. Hidden entirely when
  `IsEmptyList`.
- **Row 1 progress** — `ProgressBar` (`Value="{Binding ProgressFraction}"` Max 1) +
  `"{ReadCount} of {TotalCount} read"` + `Button.primary` `{Binding ContinueLabel}` /
  `ContinueCommand`, `IsVisible="{Binding HasContinueTarget}"`. Whole row hidden when `IsEmptyList`.
- **Row 2 action row** — `Button.primary` "＋ Add issues" toggling an inline search popover
  (`Flyout` or an `IsVisible` panel) over `SearchQuery`/`SearchResults`/`AddIssueCommand`;
  `Button.secondary` "⋯ Manage" with a `MenuFlyout`: Import .CBL (`ImportCblCommand`),
  Import .CSV (`ImportCsvCommand`), Export › .CBL (`ExportCblCommand`) / as text
  (`ExportAsTextCommand`), Link story event (`ToggleLinkStoryEventCommand`), Edit details
  (`OpenPropertiesCommand`), Refresh from source (`RefreshArcListCommand`,
  `IsVisible=IsArcLinked`), Build from a story arc (`ToggleArcSearchCommand`). Delete the
  AniList/MyAnimeList disabled buttons and the 3 `statCard`s. The `IsLinkingStoryEvent` search
  panel, the `IsArcSearchOpen` arc-search panel, and the `IsLinking` relink banner render just
  below this row when active (same bindings, relocated).
- **Row 3 list** — `ScrollViewer` › `ItemsControl` over `Groups` (unchanged VM) › per-group
  `pbTextCaption` header (when `HasLabel`) + `ItemsControl` over `Rows`. Row template:
  `Grid` — order # (`pbTextHeading`-ish small, muted; amber when `IsNextUp`), 42×63 cover
  (`CoverImageConverter`, dashed placeholder `Border` when `IsMissing`), title + `SeriesLine`,
  right side: `PbIconCheck` when `IsRead` / `Button` "▶ Read" when `IsNextUp` / "Find & link"
  (`LinkCommand`) when `IsMissing`. Row `Opacity=0.55` when `IsRead` (not next-up). Note
  `TextBlock` (italic, indented, left rule) `IsVisible` when `Notes` non-empty. Hover group
  (revealed via `:pointerover` on the row `Border`): drag handle placeholder, role `ComboBox`
  (`RoleOptions`/`SelectedRoleOption`), note edit `TextBox` (`Notes`), "mark read"/"mark unread"
  `Button` (`ToggleReadCommand`), remove `Button` (`RemoveCommand`). Row click = `OpenCommand`
  (owned) — wrap the main content in a `Button.rowOpenTarget` like today; missing rows' click =
  `LinkCommand`.
  **Drag-reorder:** keep `MoveUpCommand`/`MoveDownCommand` wired to the hover handle as
  ▲/▼ for now (real drag-drop is a follow-up, not in scope) — or a small up/down pair. Note this
  in the row's XAML comment.
- **Empty state** (`IsEmptyList`, replaces rows) — dashed `Border`: "Add issues to get started" +
  `＋ Add issues` / `Import .CBL / .CSV` (opens a small menu) / `Build from a story arc`.
**Depends on:** Steps 1–2
**Verify:** build (`AVLN` watch per CLAUDE.md); Step 7 manual.

## Step 5: MainWindow.axaml — sidebar polish
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (Reading Lists sidebar block, ~L436–500)
**What:** in the `ReadingLists` `ItemsControl.ItemTemplate`, replace the single label+count row
with: 24×36 cover `Border` (`CoverImageConverter` on `CoverIssueId`, placeholder when null) +
`StackPanel`( `sideItemLabel` name + a thin `ProgressBar`/`Border` bar `Value="{Binding
ProgressFraction}"` ). Drop the `countBadge`. Keep the `Classes.active` binding, the delete
`Button` (`DeleteConfirm`), the tag-filter chip block, and "New Reading List" as-is.
**Depends on:** Step 3
**Verify:** build; Step 7 manual.

## Step 6: unit tests
**Files:** `src/Paperbunkr.App.Tests/ReadingScreenViewModelTests.cs` (extend)
**What:**
- progress: mixed list → `ReadCount`/`TotalCount`/`ProgressFraction`.
- `ContinueTarget`: first owned-unread; skips read + missing; all-read → `"Re-read from start"` +
  first owned; no owned → `!HasContinueTarget`; in-progress target → `"Resume …"` label.
- `IsNextUp` set on exactly the target row, cleared elsewhere.
- `IsEmptyList` true at 0 items.
- `ToggleReadRow`: unread→read flips `IsRead` and advances `ContinueTarget`; read→unread reverts.
- sidebar: a `ReadingListSummary` in `Lists` has `CoverIssueId` + `ReadCount` + `ProgressFraction`
  populated from real items.
**Depends on:** Steps 1–3
**Verify:** `dotnet test --filter ReadingScreenViewModelTests`.

## Step 8 (v2): row → one ⋯ menu + reading-position number
**Files:** `src/Paperbunkr.App/ViewModels/ReadingListItemRowViewModel.cs`,
`src/Paperbunkr.App/Views/ReadingScreen.axaml`
**What:** add `[ObservableProperty] int _position` (1-based, set by parent). Add
`bool ShowRoleChip => SelectedRole is not null` + `string RoleChipLabel`. XAML: row at rest =
`Position` · cover · `"{Name} #{Number}"` (or just `Name` when `Number` is "?") · `SeriesLine` ·
state. Hover reveals a `⠿` handle (left) and a `Button` "⋯" (right) with a `MenuFlyout`:
Mark as read/unread (bind visibility to `!IsRead` / `IsRead`, both → `ToggleReadCommand`),
`Set role` submenu over `RoleOptions` (each `MenuItem` `Command` sets `SelectedRoleOption`),
Add a note (reveals a per-row note `TextBox` — a `[ObservableProperty] bool _noteEditing` on the
row), Move up/down, Remove from list. Delete the always-visible role `ComboBox`, `✓/○`, `▲▼`.
Fix the hover-hide selector: `Border.itemRow :is(Control).rowManage` (not bare `Control`).
**Verify:** build; Step 12 manual (rows read calm at rest).

## Step 9 (v2): progress + top band polish
**Files:** `src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs`, `Views/ReadingScreen.axaml`
**What:** VM `string ProgressLabel` (`"Not started · {TotalCount} issues"` / `"{ReadCount} of
{TotalCount} read"` / `"Finished"`); `ContinueLabel` → `"Start reading"` when `ReadCount == 0` &&
`HasContinueTarget`. XAML: `ProgressBar Height="8"`; badge on its own line under the Bebas title;
meta line `"{TotalCount} issues · {ReadCount} read · created {CreatedAtLabel}"` (drop the missing
clause when `MissingCount == 0` — a `string MetaLine` computed prop is simplest); synopsis toggle
`Content="{Binding SynopsisToggleLabel}"` → `"less"`/`"more"`; full-width divider under the action
row; trim the inter-section margins.
**Verify:** build; `ReadingScreenViewModelTests` (Step 11).

## Step 10 (v2): sidebar polish + header ＋ button + New-List dialog
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml`, `Models/ReadingListSummary.cs` (chip
caption helper if wanted), `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (new overlay flag +
open/close + `onCreated` wiring + Escape ordering), `src/Paperbunkr.App/ViewModels/NewReadingListViewModel.cs`
(new), `src/Paperbunkr.App/Views/NewReadingListOverlay.axaml` (+ `.axaml.cs`, new),
`src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs` (`CreateNew(string? name = null)`).
**What:**
- Sidebar list row: 32×48 cover (placeholder `▤` when `CoverIssueId` null), name
  `TextWrapping="Wrap" MaxLines="2"`, `ProgressBar Height="5"` + a caption `TextBlock`
  (`"{ReadCount} / {TotalCount} read"` / `"Not started · {TotalCount}"` — add
  `string ProgressCaption` to `ReadingListSummary`), delete `Button` only visible on row
  `:pointerover`, selected = 3px amber left border + `PbSurface2` tint, ~9px padding.
- Remove the bottom "＋ New Reading List" bordered item. Add a small `＋` icon `Button` in the
  "READING LISTS" header row (right-aligned) → `Command="{Binding OpenNewReadingListDialogCommand}"`
  on `MainViewModel`.
- `NewReadingListViewModel`: `[ObservableProperty] string _name = "New Reading List"`; an enum/
  string `SelectedMethod` (`Blank`/`Import`/`Arc`/`Event`) with `Is*` bools; for Arc, reuse
  `ArcSourceOptions` + a local `ArcSearchResults`/`SearchArc`/`UseArc` (or delegate to
  `ReadingScreenViewModel`'s — simplest: give this VM the same source-resolve + builder calls, it
  only needs `PaperbunkrDb` + `_filePicker`); for Event, `ObservableCollection<StoryEventOption>`
  loaded from `context.StoryEvents`. `CreateCommand` routes by method:
  Blank → `context` add `ReadingList{Name, Type=User}` ; Import → `_filePicker` .cbl/.csv →
  `Cbl/CsvReadingListIO.Import` → rename if `Name` != default ; Event → new list from
  `event.Members` (ordered, roles carried, `Type=Event`, `StoryEventId`). Arc "Use" →
  `ArcReadingListBuilder.CreateFromArcAsync`. Each ends by invoking `_onCreated(list.Id)`.
- `MainViewModel`: `[ObservableProperty] bool _isNewReadingListDialogOpen`; `NewReadingList`
  property (the VM instance, constructed with `_onCreated` = close + `Reading.LoadReadingList` +
  `Reading.RefreshSidebar`); `OpenNewReadingListDialog`/`CloseNewReadingListDialog` commands;
  slot it into the app-wide `Escape` handler ahead of the other overlays.
- `MainWindow.axaml`: render `<views:NewReadingListOverlay>` in a dimmed backdrop `Border`
  (`IsVisible="{Binding IsNewReadingListDialogOpen}"`), same block shape as the Reading List
  properties overlay.
**Verify:** build (`AVLN` watch — new `.axaml` ships with `.axaml.cs`); Step 11 + 12.

## Step 11 (v2): tests
**Files:** `src/Paperbunkr.App.Tests/ReadingScreenViewModelTests.cs`,
`src/Paperbunkr.App.Tests/NewReadingListViewModelTests.cs` (new)
**What:** row `Position` is 1-based and continuous across groups; `ProgressLabel` for
none/partial/all-read; `ContinueLabel == "Start reading"` when nothing read.
`NewReadingListViewModel`: blank → list with given name + `onCreated` fires; event → items seeded
from members with roles + `Type=Event` + `StoryEventId`; (import/arc: light "callback fires with
new id" using a fake picker returning a fixture .csv, or skip arc which needs network).
**Verify:** `dotnet test --filter "ReadingScreenViewModelTests|NewReadingListViewModelTests"`.

## Step 7 → renumber to Step 12: build, full suite, manual pass
**What:** `dotnet build` clean (delete `obj/Debug/net10.0/Paperbunkr.App.dll`+`.pdb` on an XAML
post-CoreCompile failure). Full `dotnet test` (App.Tests + Data.Tests, not filtered). Launch the
app: list renders + scrolls under a fixed top band; Continue lands on the right issue; hover
reveals row management; Add-issues popover appends; each Manage action routes; empty-state; a
hand-built list shows the placeholder cover + "add a description"; sidebar covers + progress bars
render; `ToggleRead` from a row updates progress live.
**Depends on:** all
**Verify:** green build + suites; manual checklist.
