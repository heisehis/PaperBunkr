# Context Menu Rebuild — shared mechanism + Library adoption

**Date:** 2026-08-29
**Status:** Design approved, pending spec review
**Scope:** One spec. Ships a screen-agnostic context-menu mechanism and adopts it fully on the
Library screen. Every other screen's context menu (comic Detail, Manga Detail, Book Detail, Books,
Reader, Reading) is a short follow-up spec that writes a builder and drops in the new host — not
covered here.

## Problem

Right-clicking a cover / tile in the Library does nothing at all — no menu appears.

Root cause: the `<ContextMenu>` blocks live inside `DataTemplate`s in `LibraryScreen.axaml`'s
`UserControl.Resources`, and every command binds through
`{Binding $parent[UserControl].((vm:LibraryScreenViewModel)DataContext).SomeCommand}`. A
`ContextMenu` opens in its **own popup visual tree**, so the `$parent[UserControl]` ancestor walk
resolves to `null`; as a compiled binding against a template declared `x:DataType="models:IssueListRow"`
that failure can also abort the menu build entirely, which is why nothing opens.

Secondary problems:

- The same ~40-60-line `<ContextMenu>` is copy-pasted **10 times** across the display-mode
  templates (Panorama / Poster / Tiles / List / Details, issue and series variants) — ~500 lines
  of duplicated markup that drift apart on every edit.
- The menu is unstyled (stock Fluent), not the app's dark/amber identity.
- The item set predates multi-select and the metadata model; it has four sibling `Set …` submenus
  cluttering the top level and no CE-parity pass.

## Goals

- Right-click works, reliably, on every Library display mode.
- One themed menu definition for the whole app; zero per-template menu markup.
- Menu contents are plain data — unit-testable without Avalonia.
- Contents reworked against ComicRack CE's comic-browser menu
  (`_reference/ComicRackCE/ComicRack/Views/ComicBrowserControl.Designer.cs`, `contextMenuItems`)
  plus Paperbunkr's own metadata features.
- Selection-aware: right-clicking within a selection acts on (and labels for) the whole selection.

## Non-goals

- Other screens' menus (separate follow-up specs, one per screen).
- "Open in new tab" — CE has it; Paperbunkr's shell has no tab model, so it is intentionally omitted.
- Empty-space right-click menu beyond an optional Select All (return `null` if nothing useful).
- Virtualized item hosting / any change to how tiles are laid out.

## Architecture

Four new screen-agnostic pieces under `Paperbunkr.App`, then Library consumes them.

### 1. `ContextMenuEntry` — `ContextMenus/ContextMenuEntry.cs`

Immutable record describing one menu row. No Avalonia types.

```csharp
public sealed record ContextMenuEntry
{
    public string? Header { get; init; }
    public Symbol? Icon { get; init; }              // FluentIcons.Common.Symbol
    public ICommand? Command { get; init; }
    public object? CommandParameter { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }            // radio/checkbox tick for "current value"
    public string? InputGesture { get; init; }      // right-aligned hint text, e.g. "Ctrl+I"
    public bool IsDanger { get; init; }             // Delete etc. — red hover wash
    public IReadOnlyList<ContextMenuEntry>? Children { get; init; }
    public bool IsSeparator { get; init; }

    public static readonly ContextMenuEntry Separator = new() { IsSeparator = true };
    public static ContextMenuEntry Item(string header, ICommand? command, object? parameter = null, /* … */);
    public static ContextMenuEntry SubMenu(string header, IEnumerable<ContextMenuEntry> children, Symbol? icon = null, bool isVisible = true);
}
```

Builders skip an entry entirely rather than emit an invisible one, so there is no `IsVisible` on
the record — callers filter. `SubMenu`'s `isVisible: false` returns `null` and the caller drops it.

### 2. `IContextMenuProvider` — `ContextMenus/IContextMenuProvider.cs`

```csharp
public interface IContextMenuProvider
{
    IReadOnlyList<ContextMenuEntry>? BuildContextMenu(object? target);
}
```

`target` is the DataContext of the right-clicked element (an `IssueListRow`, a `SeriesCardSample`,
or `null` for empty space). `null` return ⇒ no menu shown.

### 3. `ContextMenuHost` — `Controls/ContextMenuHost.cs`

Attached behavior. One property:

```xml
<UserControl ctl:ContextMenuHost.Provider="{Binding}">
```

- On the property changing to a non-null value, adds a **bubbling** `PointerReleased` handler
  (`handledEventsToo`, since a tile `Button` may mark the right-release handled) on the host.
- Handler (right button only): from `e.Source as Visual`, walk up the visual tree collecting
  distinct non-null `DataContext` values. Hand each (nearest first) to `Provider.BuildContextMenu(dc)`;
  also try `null` last. Use the first non-empty result.
- Build `MenuItem` / `Separator` controls from the entry tree recursively:
  - `IsSeparator` ⇒ `Separator`
  - otherwise `MenuItem` with `Header`, `Command`, `CommandParameter`, `IsEnabled`;
    `Icon` ⇒ a `SymbolIcon`; `IsChecked` ⇒ `ToggleType = CheckBox` + `IsChecked` (overrides `Icon`);
    `InputGesture` ⇒ `MenuItem.InputGesture` (display only - Avalonia does not accelerate it).
    The one gesture that needs to actually fire, **Ctrl+I → Edit Properties**, is a real
    `KeyBinding` on the `LibraryScreen` root → `BulkEditSelectionCommand` (acts on the selection;
    no-ops when empty). CE parity (`miProperties.ShortcutKeys`);
    `IsDanger` ⇒ `Classes="danger"`; `Children` ⇒ recurse into `MenuItem.Items`.
- Show the items in a fresh **`MenuFlyout`** via `flyout.ShowAt(host, showAtPointer: true)`; mark
  `e.Handled`.

**Why `MenuFlyout`, not `ContextMenu`:** verified on-device that a plain `ContextMenu` popup does
not render at all in this Avalonia 12 + FluentAvalonia build — its `Opening` event fires and items
populate, but nothing appears. That is the *same* failure the old in-template `Button.ContextMenu`
menus hit (so "menu never opened" was two bugs: dead ancestor bindings *and* an invisible popup).
`MenuFlyout` renders correctly.

A single `Provider` on the screen root covers every tile in every display mode with no per-template
markup, because `PointerReleased` bubbles.

### 4. `Styles/Menu.axaml`

`StyleInclude`d in `App.axaml` after the other `Styles/*.axaml`. Selectors target
`MenuFlyoutPresenter` and its `MenuItem`s (plus the matching `MenuFlyout*` Fluent resource keys).
The app has no menu bar; other `MenuFlyout` surfaces are rare and inherit the same skin.

| Element | Token |
|---|---|
| Popup surface | `PbSurface3Brush` (#1B1E24), `PbBorderBrush` 1px, `PbRadius` (7), soft drop shadow |
| Row text | `PbTextBrush`, 13px |
| Row hover / focus / submenu-open | fill `PbAccentSoftBrush`; icon → `PbAccentTextBrush` |
| Icon column | 16px, `PbTextMutedBrush` (FluentIcons `SymbolIcon`) |
| Check glyph | `PbAccentTextBrush` |
| Disabled row | `PbTextFaintBrush`, icon dimmed |
| Separator | `PbBorderBrush`, horizontally inset |
| Input-gesture hint | right-aligned, `PbTextFaintBrush` |
| `MenuItem.danger` hover | muted red wash (`#3A1D17`) |
| Submenu chevron | `PbTextMutedBrush` |

## Library adoption

### View model

`LibraryScreenViewModel : …, IContextMenuProvider`. The interface method delegates to a standalone
builder so the 1,900-line VM does not grow a menu tree:

```csharp
IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target)
    => new LibraryContextMenuBuilder(this).Build(target);
```

`LibraryContextMenuBuilder` — `ViewModels/LibraryContextMenuBuilder.cs` — takes the VM and reads
its existing commands, `Selection` / `SeriesSelection`, `HasPluginHost`, reading-list flyout
source, `DeleteConfirmLabel` / `DeleteSeriesConfirmLabel`.

### Comic / issue tile — `target is IssueListRow row`

Order (separators shown as `—`):

1. **Open** — `IssueList.OpenIssueCommand`, param `row`, gesture "Enter"
2. —
3. **Edit Properties…** — `EditIssuePropertiesCommand`, param `row.Id`, gesture "Ctrl+I"
4. **Quick Rate…** — single item, `OpenQuickRateCommand`, param `row.Id`. Opens the existing
   Quick Rating + Review overlay. No star submenu: the VM has no rate-to-value command and adding
   one is out of scope for this pass.
5. **Mark as ▸** — Read (`MarkIssueReadCommand`) / Unread (`MarkIssueUnreadCommand`), param `row.Id`
6. **Add to Reading List ▸** — one child per existing list
   (`AddSelectionToReadingListCommand`, param list id) + `—` + **New List…**
   (`CreateReadingListAndAddSelectionCommand`)
7. —
8. **Go to Series** — `GoToSeriesCommand`, param `row.SeriesId`
9. **Series ▸** — folds the four former top-level submenus:
   - **Content Type ▸** — Comic / Manga / Manhua / Manhwa
     (`SetSeriesContentType{…}Command`, param `row.SeriesId`); ✓ from `row.ContentTypeLabel`
   - **Reading Direction ▸** — Left to Right / Right to Left
     (`SetSeriesReadingMode{…}Command`); **omitted entirely** unless `row.IsMangaFamily`;
     ✓ from new `row.ReadingDirectionLabel`
   - **Publication Status ▸** — Unknown / Ongoing / Completed / Cancelled / Hiatus
     (`SetSeriesStatus{…}Command`); ✓ from new `row.SeriesStatusLabel`
   - **Reading Status ▸** — Unknown / Planned / Reading / Completed / Paused / Dropped / Re-reading
     (`SetSeriesReadingStatus{…}Command`); ✓ from new `row.ReadingStatusLabel`
10. —
11. **Show in Explorer** — `RevealIssueCommand`, param `row.Id`, `IsEnabled = row.HasFile`
12. **Find Duplicates** — `RunLibraryPluginsCommand`, param `row.Id`; **omitted** unless
    `HasPluginHost`
13. —
14. **Select All** — new `SelectAllVisibleIssuesCommand` (selects `GetOrderedVisibleIssueRows()`);
    **Clear Selection** — `ClearSelectionCommand` (exists), `IsEnabled = HasSelection`
15. —
16. **Delete… ▸** — `IsDanger`; single child = `DeleteConfirmLabel` bound text invoking
    `DeleteIssueCommand`, param `row.Id` (keeps today's two-step confirm-in-submenu pattern)

**Selection-aware headers:** when `Selection.IsSelected(row) && Selection.Count > 1`, the count
`n = Selection.Count` rewrites: "Mark _n_ as Read/Unread", "Delete _n_ comics" / confirm child
"Yes, delete _n_ comics", "Add _n_ to Reading List". Single-target labels otherwise. Series-level
submenu items are not pluralized (they act on the series, not the issue set).

### Series card — `target is SeriesCardSample card`

1. **Open Series** — the series-card's existing open command, param `card`
2. —
3. **Content Type ▸** / **Reading Direction ▸** (if `card.IsMangaFamily`) /
   **Publication Status ▸** / **Reading Status ▸** — same children/commands as above,
   param `card.SeriesId`, ✓ from `card.ContentTypeLabel` + the new label fields
4. —
5. **Show in Explorer** — series reveal command, `IsEnabled = card.HasFile`
6. —
7. **Delete Series… ▸** — `IsDanger`; child = `DeleteSeriesConfirmLabel` invoking
   `DeleteSeriesCommand`, param `card.SeriesId`

Selection-aware headers use `SeriesSelection` the same way.

### Empty-space — `target is null`

Return a one-item menu: **Select All** (issues or series per current granularity). If that is
awkward to express, return `null`. Not worth iterating on.

### Model additions

`IssueListRow` and `SeriesCardSample` each gain nullable strings mirroring the existing
`ContentTypeLabel`:

- `SeriesStatusLabel` — "Ongoing", "Hiatus", …
- `ReadingStatusLabel` — "Reading", "Planned", …
- `ReadingDirectionLabel` — "Left to Right" / "Right to Left"

Populated in the existing library load queries / projection
(`LibraryScreenViewModel` load path + `SeriesCardSample` projection). If a value is not already
joined in and adding it is non-trivial, that one submenu ships without a ✓ this pass — noted, not
blocking.

### XAML / code-behind

- `LibraryScreen.axaml`: delete all 10 `<Button.ContextMenu>…</Button.ContextMenu>` blocks. Add
  `xmlns:ctl="using:Paperbunkr.App.Controls"` and `ctl:ContextMenuHost.Provider="{Binding}"` on
  the root `UserControl`.
- `LibraryScreen.axaml.cs`: no change — right-click does not mutate selection; the existing
  `Selection.UnionForAction(id)` inside each command already gives "just this tile, or the whole
  selection if it's part of one".
- `App.axaml`: add `<StyleInclude Source="avares://Paperbunkr.App/Styles/Menu.axaml" />`.

## Testing

- **`ContextMenuEntryTests`** — factory helpers, `Separator` sentinel, `SubMenu` visibility drop.
- **`LibraryContextMenuBuilderTests`** (plain xUnit, no Avalonia) — build a VM with a small
  in-memory library and assert, for `IssueListRow` / `SeriesCardSample` / `null` targets:
  - entry order and headers
  - every leaf entry's `Command` is non-null and matches the expected VM command instance
  - `Show in Explorer` `IsEnabled` tracks `HasFile`
  - Reading Direction submenu absent when target is not manga-family, present when it is
  - `Find Duplicates` absent without a plugin host
  - ✓ lands on the entry matching `ContentTypeLabel` (and status labels when populated)
  - `Quick Rate…` is a single leaf (no children)
  - selection-aware headers: with a 3-item selection that includes the target, "Delete 3 comics"
    etc.; with the target outside the selection, singular labels
- **`ContextMenuHost`** — behavioural verification is on-device (headless UI tests are flaky here
  and `MenuFlyout` needs a real top level). Confirmed live: right-click on Library covers opens the
  series / issue menu at the pointer.

## Follow-up specs (not this one)

Each: write a `<Screen>ContextMenuBuilder`, implement `IContextMenuProvider` on its VM, add
`ContextMenuHost.Provider`, delete the old in-template `<ContextMenu>`, CE-parity pass on contents.

- comic Detail (`DetailTabs` issues list + `DetailBand`)
- Manga Detail
- Book Detail
- Books screen
- Reader (page context menu — CE's page/reader menu)
- Reading (reading-list rows)
