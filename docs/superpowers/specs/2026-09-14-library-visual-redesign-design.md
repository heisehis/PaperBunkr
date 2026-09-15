# Library Screen — Master-Detail Visual Redesign

## Background

The Library screen (`Views/LibraryScreen.axaml` + `LibraryScreenViewModel.cs`) shipped its current
shape across UI-rework Phase 4a/4b (2026-08-27, see `project_paperbunkr_library_browsing_4b`
memory): a two-zone toolbar (`Views/LibraryToolbar.axaml`) + optional filter-chips row + optional
selection-actions row, above 5 `LibraryViewMode` values (`PosterGrid`, `PanoramaGrid`, `List`,
`Details`, `Tiles` — `Paperbunkr.Data/Entities/LibraryViewMode.cs`) × 2 granularities
(Series/Issue) × grouped/ungrouped, all virtualized. A left sidebar (Collections / Reading Lists /
Smart Lists / browse groups) sits beside the content grid.

This redesign was triggered by uncommitted, in-flight work adding 4 "cosmetic thumbnail" toggles
(`docs/superpowers/specs/2026-09-13-preferences-cosmetic-toggles-{design,plan}.md`:
`FadeInThumbnails`, `DogEarThumbnails`, `ShowToolTips`, `NumericRatingThumbnails`) plus a 5th CBL
export toggle. Reviewing that diff surfaced two real problems this doc addresses:

1. **Corner contention.** `PosterGridIssueTemplate`/`PosterGridSeriesTemplate` (and their Panorama
   equivalents — 4 templates total) each stack up to 6 overlays on one tile: plugin-draw hook,
   publisher chip (top-left), language badge (bottom-left), unread dot (top-right), and — as of the
   in-flight work — a dog-ear peek image and a numeric rating badge, both bottom-right,
   hover-revealed, alongside the pre-existing selection `CheckBox` (also bottom-right,
   hover-revealed). Three elements now compete for one corner with no formal precedence between
   them.
2. **Hardcoded, non-skin-reactive color.** The publisher and language chips both hardcode
   `Background="#B814161B"` (6 occurrences across the 4 templates) instead of a `DynamicResource` —
   the same class of defect flagged as real (not a style nit) in an earlier detail-screens polish
   pass (`reference_avalonia_pro_max.md`). Separately, the skin schema
   (`Assets/Skins/default/theme.json`, wired into `App.axaml` as `Pb*Color`/`Pb*Brush` resources)
   defines `PbAccentBrush`, `PbBadgeBrush`/`PbBadgeTextBrush`/`PbBadgeSoftBrush`, `PbSuccessBrush`,
   `PbGlowBrush` — a real palette that Library barely touches, leaning almost entirely on neutral
   surfaces instead.

Reached via `/grilling` (2026-09-14) per this project's CLAUDE.md override of the `brainstorming`
skill's one-at-a-time clarification step, plus the visual companion for every genuinely spatial
question (layout paradigm, pane arrangement, tile corner states, preview panel content, color
comparison). The user originally named two threads — this visual/layout overhaul, and a Collections
capability question — and explicitly chose to split them into two separate design docs, this one
first; the Collections thread (likely smart/rule-based collections reusing the SmartList engine,
the one item left "deferred, no concrete plan" per `project_paperbunkr_collections` memory) is
out of scope here and untouched by anything below.

## Goals

- Move Library from a single grid+sidebar screen to a 3-pane **Master-Detail** layout: sidebar |
  list | live preview panel — chosen over two other companion-mocked paradigms (see Approaches).
- Consolidate `LibraryViewMode` from 5 values to 3: **Grid** (absorbs Poster/Panorama as a
  cover-fit toggle), **List** (absorbs List/Tiles as a density toggle), **Details/Table** (kept
  as-is conceptually — the power-user sortable-column view).
- Replace the ad-hoc bottom-right overlay stack with a single, priority-ordered corner-slot system.
- Relocate the language badge and rating badge off the bottom-right corner entirely; give the live
  preview panel and the existing hover-tooltip popup the job of surfacing that information instead.
- Fix the `#B814161B` hardcoded-hex defect and, in the same edit, spend more of the existing skin
  palette (`PbBadgeBrush`/`PbBadgeTextBrush`/`PbAccentBrush`/`PbGlowBrush`) that Library currently
  under-uses, plus tie each Collection's own stored `AccentColor` into its sidebar row.
- Restyle the toolbar's "Overlay" checkbox group (grew to 4 rows via the in-flight cosmetic-toggles
  work) as part of the same pass, since it's the toolbar surface most directly affected by the
  motivating change.

## Non-goals

- No change to the existing full Detail screens (comic/manga/book, `DetailScreenViewModel` family)
  — the new preview panel is a lighter, separate surface; double-click/Enter still navigates to the
  real Detail screen exactly as today.
- No new skin-schema tokens. The color pass spends what `theme.json`/`App.axaml` already define;
  adding `warning`/`danger`-style status-color tokens for `Series.Status` was explicitly considered
  and explicitly declined by the user as its own smaller follow-up, not part of this doc.
- No change to Collections/Reading Lists/Smart Lists data models, CRUD, or the context-menu "Add
  to Collection" flows shipped in `project_paperbunkr_collections` — only where and how the sidebar
  displays them (still its own pane column) and how the active row is tinted.
- No change to bulk-edit/bulk-action command logic or its rendering surface
  (`LibraryContextMenuBuilder`, the existing selection-actions row) — it stays exactly where it is
  today, just spanning the toolbar's new full width; the preview panel does not take this over (see
  §4 and the Corrections note).
- The Collections capability thread (smart/rule-based collections) is a separate design doc per the
  user's explicit decomposition decision — not addressed here.
- CE-parity is not a constraint on any decision in this doc. `_reference/ComicRackCE` is a WinForms
  TreeView-based app with no master-detail live-preview precedent and no corner-badge-priority
  precedent to check against; every decision here is a deliberate Paperbunkr-native deviation, the
  same status this project's existing view-mode/toolbar rework (4a/4b) already has.

## Approaches considered

Three overall layout paradigms were mocked in the visual companion as wireframes:

1. **Refined Grid** — keep today's sidebar+toolbar+grid skeleton, consolidate view modes and clean
   up tile visuals in place. Lowest risk, but does nothing about the actual cost that motivated this
   redesign: checking what a series *is* still requires leaving the grid (opening Detail) or
   memorizing what a handful of corner badges mean.
2. **Master-Detail** (chosen) — sidebar | list | live preview panel. The panel updates on selection,
   showing exactly the information (title, synopsis, language, rating, quick actions) that today's
   tile corners were trying to cram onto a ~150px cover. This is what actually let the corner-badge
   count shrink, rather than just reshuffling the same badges.
3. **Faceted Browse** — replace the sidebar with a persistent filter/facet rail (status, publisher,
   collection as chip groups), toolbar shrinks to search + view switch. Rejected: biggest departure
   from the current navigation model, and it doesn't address the corner-contention problem at all —
   the grid tiles are unchanged under this option.

## Architecture

### 1. Pane layout & toolbar

**Correction (implementation-time, 2026-09-14):** this section originally described the
Collections/Reading Lists/Smart Lists sidebar as one of `LibraryScreen.axaml`'s own Grid columns.
That's wrong — reading the actual file shows the sidebar is shell-level chrome
(`Border.contextualSidebar`, `Views/MainWindow.axaml:287`), `DockPanel.Dock="Left"` in the main
window, populated per-screen via `IsVisible="{Binding IsLibrary}"` (line 296) and already
independently width-animated/collapsible via `MainViewModel.ShowContextualSidebar` — it is not
markup this redesign owns or restructures, and `LibraryScreen.axaml`'s own root `Grid` today is a
single-column `RowDefinitions="Auto,*"` with no sidebar sibling in it at all. "3-pane Master-Detail"
is still the right description of the resulting *experience* (sidebar, list, preview all visible
together), but structurally it's the existing sidebar pane (untouched, already there) plus two new
columns *within* `LibraryScreen.axaml` for list + preview. Corrected below.

`LibraryScreen.axaml`'s root `Grid` changes from `RowDefinitions="Auto,*"` (toolbar row + single
content row) to a toolbar row spanning full width, above a 3-column content row:
`ColumnDefinitions="*,Auto,Auto"` — list (the consolidated view-mode content, unchanged from today
structurally, reclaims this column's full width whenever the panel is hidden) | `GridSplitter` |
preview panel (§4). The splitter and the panel column are two separate `Auto` columns, not one —
collapsing the panel means hiding both, not just the panel: both the `GridSplitter` and the preview
panel container bind `IsVisible` to the same `IsLibraryPreviewPanelVisible` property (and,
independently, both are also hidden while `DetailsTable` mode is active, per §2), so a hidden panel
leaves no stranded splitter and no dead `Auto`-width column — the list column's `*` genuinely
reclaims that space rather than just showing an invisible panel next to a live splitter.
`LibraryToolbar.axaml` itself is unchanged internally — same search/filter/sort/group/view
controls — only its container now spans all 3 columns (`Grid.ColumnSpan="3"`) instead of sitting
above a single content area. The existing sidebar (`MainWindow.axaml`) needs no structural change
here — it already has its own header/actions independent of Library's toolbar.

### 2. View-mode consolidation

`Paperbunkr.Data/Entities/LibraryViewMode.cs` reduces from 5 values to 3:

```csharp
public enum LibraryViewMode
{
    Grid,
    List,
    DetailsTable,
}
```

Two new settings carry what the removed values used to mean:

- `LibraryGridCoverFit` (enum: `Poster` / `Panorama`) — a toggle within Grid mode, not a separate
  mode. Poster keeps today's uniform tile size; Panorama keeps today's per-cover aspect ratio via
  the existing `VirtualizingVariableWrapPanel`.
- `LibraryListDensity` (enum: `Comfortable` / `Compact`) — a toggle within List mode. Comfortable
  matches today's `List` row height; Compact matches today's `Tiles` row height.

Migration: existing persisted `AppSettings.LibraryViewMode` values need remapping —
`PosterGrid`→`Grid`+`CoverFit=Poster`, `PanoramaGrid`→`Grid`+`CoverFit=Panorama`,
`List`→`List`+`Density=Comfortable`, `Tiles`→`List`+`Density=Compact`, `Details`→`DetailsTable`.
This follows the exact precedent already proven safe in this codebase: the `LibraryPosterGridConsolidation`
migration (4a) did the same raw-SQL remap of a persisted legacy enum string, hand-edited into a
freshly-scaffolded migration rather than inserted retroactively into the chain.

Flipping `LibraryGridCoverFit` or `LibraryListDensity` rebuilds the virtualized panel
(`VirtualizingWrapPanel`/`VirtualizingVariableWrapPanel`) with different item sizing, which resets
scroll offset today unless addressed. Capture and restore scroll position across the toggle flip —
but **not** via `LibraryBrowseHistory`: that record (`LibraryBrowseState`) only ever carries
`ActiveContentType`/`ActiveCollectionId`/`SearchQuery`, i.e. navigation targets, and doesn't track
view-mode/display settings at all today. Folding a cosmetic toggle's scroll offset into it would
make Back/Forward start undoing display toggles instead of navigation, which it has never done.
Keep this as a transient, non-history `LibraryScreenViewModel` field (or a small view-level layout
helper) instead — scoped to "restore scroll after this specific rebuild," not part of any
undo/history stack.

`Details`/Table mode: kept conceptually as-is (configurable `DetailsColumns`,
`Models/DetailsColumn.cs`, click-to-sort headers) — no changes to its column system. The one
behavior change: while `DetailsTable` is active, the preview panel (§4) is hidden and the list
column reclaims its width, since a wide sortable table and a live preview panel compete for the
same screen real estate for no benefit — table mode is a "scan many rows" mode, and double-click
still opens the full Detail screen exactly as it does in Grid/List. This does **not** cost table
mode access to bulk actions: per §4, bulk actions live in the toolbar-anchored selection bar, not
the preview panel, so hiding the panel here has no effect on multi-select functionality.

### 3. Tile corner-slot system

Replace the current 3-way independently-bound `Border`/`CheckBox`/`Image` overlays at bottom-right
with one computed slot, and give rating its own independent bottom-left binding — these are two
separate slots, not one shared priority list.

**Bottom-right** — per-row/per-tile enum `CornerBadgeKind { None, Selected, DogEar }`, exactly one
renders:

1. **Selection checkbox** — item selected, or multi-select mode active.
2. **Dog-ear peek** — pointer currently hovering the tile, `DogEarEligibility.IsEligible(...)` true
   (unchanged gate: `PageCount > 1 && !FileIsMissing && !HasCustomCover`), no selection.

**Bottom-left** — a plain `IsVisible` binding, not part of the priority enum above (nothing else
ever competes for this slot, so no priority is needed): rating badge shows whenever
`NumericRatingThumbnails` is on and `HasRating` is true, regardless of selection or hover state.

The per-row hover flag already exists (the in-flight work added `PointerEntered`/`PointerExited`
handlers on the cover `Border` for the tooltip/dog-ear peek) — the corner-slot computation reuses
it rather than adding a second hover-tracking mechanism.

Full corner map after this change:

| Corner | Content | Change from today |
|---|---|---|
| Top-left | Publisher chip | Unchanged position; background token fixed (§6) |
| Top-right | Unread dot | Unchanged |
| Bottom-left | **Rating badge** | Was: language badge. Persistent when `HasRating`, no hover-gating needed — nothing else competes here now |
| Bottom-right | Selection > Dog-ear (only these two) | Was: selection + dog-ear + rating, no precedence |
| *(removed)* | Language badge | Moves to the preview panel (always shown) and the `ShowToolTips` hover popup (§4) |

The center "Continue Reading" play-button overlay on series tiles (`Button.continueReading`) is
unaffected — different slot, no contention with the corner system.

This touches all 4 grid templates (`PosterGridIssueTemplate`, `PosterGridSeriesTemplate`, and their
Panorama-mode equivalents, which fold into the same templates under the `LibraryGridCoverFit`
toggle from §2) — the corner block becomes one shared piece of markup/converter logic instead of
4 near-duplicated copies.

### 4. Live preview panel

New component, `Views/LibraryPreviewPanel.axaml` (+ code-behind), living in the third grid column,
resizable via a `GridSplitter` between the list and panel columns (`MinWidth`/`MaxWidth` bounds),
width persisted to a new `AppSettings.LibraryPreviewPanelWidth` — a fixed, non-resizable width is
hostile to ultrawide monitors, and this app already persists other per-user layout choices the same
way (`LibraryDetailsColumns`, saved workspaces). The panel's root content is wrapped in a vertical
`ScrollViewer` (`HorizontalScrollBarVisibility="Disabled"`) — the series-preview state in particular
stacks cover, chips, synopsis, an issue rail, and quick actions, which can exceed a 1080p panel
height at narrower window sizes; per this app's own layout-patterns convention, only the scrollable
region is wrapped, not the whole panel, and quick actions must never be clipped off unreachable.

Bound to a new `LibraryScreenViewModel` property (e.g. `SelectedPreview`) that updates on selection
change — single click or arrow-key navigation in the list, not requiring a separate pin action.
Four content states:

- **Series selected**: cover, name, issue count / unread count, publisher/language/status chips,
  synopsis excerpt, a horizontal issue-cover rail (reuses the existing `PosterRail` control
  verbatim — already `Stretch="UniformToFill"` + `ClipToBounds` and already used for mixed-format
  covers on Detail screens' Related/Continuity/Collection rails, so mixed comic/manga aspect ratios
  are already a solved problem here, not new work), and quick actions (Continue, Mark read, Add to
  Collection).
- **Issue selected**: cover, series + number, publisher/language, read state, rating, writer/artist
  credit line, file size/format, quick actions (Read, Mark unread, Edit metadata).
- **Nothing selected, items exist** (fresh load, or after a selection is cleared): an idle empty
  state ("Select a series or issue to preview").
- **Nothing selected, zero results** (an active search/filter yields nothing to select): a distinct
  "No results for this search" state — must not show the same copy as the idle state above, which
  would misleadingly imply something is selectable.

**Multi-select does not get its own panel state.** Bulk actions stay where they already work today
— the existing toolbar-anchored selection bar (`LibraryContextMenuBuilder`'s bulk edit / mark read
/ add-to-collection / delete), now spanning the full toolbar width alongside the rest of the
toolbar (§1) rather than only above the grid — so bulk actions remain available identically in
Grid, List, *and* DetailsTable mode. The preview panel simply keeps showing the last single
focused/anchor item's preview (or the idle state, if nothing was previewed yet) while a multi-select
is active; it never needs to render a bulk summary itself, which is also what keeps it un-broken by
DetailsTable mode hiding the panel (see §2 and the Corrections note below).

Content adapts to the existing Series/Issue granularity toggle — selecting in Series-granularity
mode always produces the series preview even if the underlying row is an issue-level row, and vice
versa, matching how the rest of the screen already respects that toggle.

Double-click or Enter on a selected row is unchanged: it still opens the real Detail screen
(comic/manga/book) — the preview panel never replaces that navigation, only defers it.

**Manual collapse in Grid/List.** The `GridSplitter` alone doesn't give a one-action way to reclaim
full grid width — dragging to near-zero every time is friction a dedicated toggle avoids. New
`AppSettings.IsLibraryPreviewPanelVisible` (bool, default `true`), a toolbar toggle button/icon,
bound to `Ctrl+B` (verified unbound anywhere in `Paperbunkr.App` today — no existing `Ctrl+B`/`Key.B`
binding — and matches the panel-toggle convention several other apps use, e.g. VS Code's sidebar
toggle). This toggle is independent of, and unaffected by, `DetailsTable` mode's own unconditional
panel-hide (§2) — that one isn't user-toggleable, this one is; both routes end up hiding the same
splitter + panel columns per §1.

**Focus order.** The panel's own quick-action buttons (Continue/Mark Read/Add-to-Collection, etc.)
stay fully keyboard-focusable and Tab-reachable — they're the panel's real interactive surface.
The embedded `PosterRail`'s item buttons (`Button.railCard`, `PosterRail.axaml:70` — genuine
`Button` controls in a plain `ItemsControl`, so they are part of the default Tab order today) get
`IsTabStop="False"` in this context specifically, so Tab-cycling through the screen doesn't have to
pass through N issue-cover buttons before reaching the panel's actual actions; they remain fully
mouse-clickable regardless (`IsTabStop` only affects keyboard Tab traversal, not pointer
activation). Note this is a Tab-order refinement, not an arrow-key concern: Avalonia's directional
navigation keeps arrow-key handling scoped to whichever list/grid currently has focus, so arrow-key
browsing of the main list was never at risk of jumping focus into the preview panel — nothing here
addresses a problem that exists on that axis.

### 5. Language relocation

The language badge is removed from all 4 grid templates. It becomes:

- A field in the preview panel's chip row (§4) — always visible whenever something is selected.
- A new field in the existing `ComicHoverTooltipPopup` content (`ShowToolTips` toggle,
  currently Title / Writer+Penciller / Summary excerpt / FileSize / Format) — one more bound field
  in the same popup, not a new tooltip mechanism.

If `ShowToolTips` is off (its default) and nothing is selected, language is simply not visible
per-tile — acceptable because, unlike before this redesign, the preview panel is a permanent screen
fixture the moment anything is selected, not an occasional detail-screen visit.

### 6. Color pass

Two edits land together, since they touch the same lines:

- `Background="#B814161B"` (publisher chip, 4 occurrences after template consolidation) and the
  relocated rating badge's background both become `{DynamicResource PbBadgeBrush}` with
  `{DynamicResource PbBadgeTextBrush}` foreground — fixes the non-skin-reactive hardcoded-hex defect
  and, in the same change, moves both chips from a flat near-black scrim to the skin's actual gold
  badge color.
- Tile hover state gains a subtle `{DynamicResource PbGlowBrush}`-based `BoxShadow`, replacing a
  flat lighter-neutral border-only hover state — same visual trick already used elsewhere in the
  app (hero cards).
- The sidebar's active-row highlight (`Border.colRow`/`Classes.active`, `Views/MainWindow.axaml:358`
  — shell-level markup, not part of `LibraryScreen.axaml`), currently one flat neutral tint for
  every row regardless of which Collection/Reading List/Smart List is selected, changes to use that
  specific row's own
  `Collection.AccentColor` (already a stored, user-set field, already partially consumed for the
  sidebar accent-bar/dot per `project_paperbunkr_collections`) — but **not** as a raw, full-opacity
  background fill, which would fight text contrast against an arbitrary user-picked hex. The left
  accent border uses `AccentColor` at full opacity (a thin 3px bar, not a text-bearing surface, so
  full saturation is fine there). The row's background tint uses the same `AccentColor` reduced to
  a fixed ~16-18% alpha — matching the alpha this codebase's own `PbAccentSoftColor`
  (`#29C9803F`, ~16%) / `PbBadgeSoftColor` (`#2ED7AC4C`, ~18%) tokens already use for exactly this
  "tinted surface, not a solid fill" purpose. Implementation: extend the existing
  `AccentColorToBrushConverter` (already built for `CollectionPropertiesOverlay`'s swatch picker)
  with an alpha-parameter overload, or add a sibling
  `AccentColorToBackgroundTintConverter` — either way, text foreground stays a normal
  `PbText`/`PbTextMuted` token, never derived from `AccentColor`. A fixed alpha is sufficient here,
  not a luminance-adaptive one: `AccentColor` isn't a free-form color picker, it's a bounded
  6-swatch palette (`CollectionPropertiesScreenViewModel.AccentSwatches`: `#C9803F`, `#5FA889`,
  `#D7AC4C`, `#7C93C9`, `#C97C9E`, `#8F7CC9`) already curated for this dark skin — one of the six
  (`#D7AC4C`) is literally `PbBadgeColor`, already used at ~18% alpha elsewhere in this app with no
  contrast issue. There's no neon/oversaturated value in the reachable set for this to fail against.
  Reading Lists/Smart Lists rows, which have no `AccentColor` field, keep the existing flat
  `PbAccentBrush`-based highlight unchanged.
- No new tokens are added to `theme.json`/`App.axaml` — every brush referenced above
  (`PbBadgeBrush`, `PbBadgeTextBrush`, `PbGlowBrush`, `PbAccentBrush`) already exists.

## ViewModel changes (summary)

- `LibraryViewMode` (Data entity): 5 values → 3 (`Grid`/`List`/`DetailsTable`); new
  `LibraryGridCoverFit` and `LibraryListDensity` enums/settings.
- New migration remapping the old 5-value persisted setting into the new 3-value + 2-toggle shape
  (pattern: `LibraryPosterGridConsolidation`).
- New `AppSettings.LibraryPreviewPanelWidth` (persists the `GridSplitter`-adjusted panel width) and
  `AppSettings.IsLibraryPreviewPanelVisible` (bool, default `true`; manual collapse in Grid/List via
  toolbar toggle + `Ctrl+B`, independent of `DetailsTable`'s own unconditional hide — both routes
  hide the same splitter + panel columns, per §1).
- `LibraryScreenViewModel`: new `SelectedPreview` property (or equivalent) exposing the 4-state
  preview content described in §4 (series / issue / idle-empty / no-results); recomputed on
  selection change and on granularity-toggle change. Does **not** carry a multi-select
  state — bulk actions stay on the existing selection-bar commands, unaffected by this property.
- Per-row/tile: new computed `CornerBadgeKind { None, Selected, DogEar }` (bottom-right only,
  2-way) replacing the current 3-way `IsVisible` bindings there. Rating badge (bottom-left) is a
  separate, simple `IsVisible` binding, not part of this enum.
- New alpha-aware accent-tint conversion for the sidebar's active-row background (extend the
  existing `AccentColorToBrushConverter` with an alpha parameter, or add a sibling converter) —
  background tint at ~16-18% alpha, left border at full opacity, per §6.
- New transient (non-persisted, non-history) `LibraryScreenViewModel` field capturing scroll offset
  immediately before a `LibraryGridCoverFit`/`LibraryListDensity` toggle rebuild, restored after —
  deliberately **not** routed through `LibraryBrowseHistory`, which only tracks navigation targets
  (`ActiveContentType`/`ActiveCollectionId`/`SearchQuery`) and must stay that way so Back/Forward
  keeps meaning "previous collection," not "previous display setting."
- `CosmeticThumbnailSettings`/`DogEarEligibility`/`DogEarThumbnailCache` (in-flight work): unchanged
  in shape — `DogEarEligibility.IsEligible` becomes an input to `CornerBadgeKind` rather than
  directly gating its own `Border.IsVisible`. `NumericRatingThumbnails` continues to gate the
  bottom-left rating binding exactly as today, minus the now-unnecessary `!HasSelection` condition
  (nothing else competes for that corner anymore).
- `ComicHoverTooltipPopup`'s bound content model gains a Language field.
- Toolbar's "Overlay" checkbox group (`Views/LibraryToolbar.axaml`): copy/labels reviewed against
  the new corner map (e.g. "Dog-ear preview" and "Numeric rating badge" toggle text should still
  read correctly now that rating moved to bottom-left) — no new toggles, no removed toggles.
- Toolbar's existing selection-bar row (bulk edit / mark read / add-to-collection / delete):
  unchanged in commands, now rendered consistently across Grid/List/DetailsTable rather than only
  above the grid.

## Corrections after review (2026-09-14)

An external review of the first draft caught two genuine internal contradictions, fixed above:
the bottom-right `CornerBadgeKind` priority list still listed Rating as a bottom-right contender
after the corner-map table had already moved it to bottom-left (§3); and bulk-action rendering was
assigned to the preview panel (§4) while the panel is hidden in `DetailsTable` mode (§2), which
would have made bulk actions unreachable in table view. Both are fixed by narrowing
`CornerBadgeKind` to Selected/DogEar only, and by keeping bulk actions on the existing
toolbar-anchored selection bar instead of the preview panel. The review's contrast concern was
valid as a documentation gap (the design itself, as approved in the visual companion, already used
a low-alpha tint) and is now stated explicitly in §6. Its GridSplitter, scroll-position, and
no-results-state suggestions were accepted as genuine gaps and folded into §4/§2/Testing.

Two of the review's claims were checked against this codebase and rejected as not applicable:
a "Select All causes UI-thread freeze" concern, based on an assumption that missing-file/unread
counts require live I/O — verified false: `Issue.FileIsMissing` is a plain DB-cached bool populated
by the background `LibraryHealthService`, and the existing `TileSelectionController`/
`SelectAllVisibleIssues` path is already O(1)/in-memory with no per-item disk access, so no
debounce or `Task.Run` offload is needed here. And a concern that the preview panel's issue-cover
rail would look jagged mixing comic/manga aspect ratios — verified false: the shared `PosterRail`
control already enforces `Stretch="UniformToFill"` + `ClipToBounds` and is already used for
mixed-aspect covers on Detail screens today, so §4 already inherits a solved problem rather than
introducing a new one.

**Second review round (2026-09-14, same day):** caught one more genuine bug — the scroll-position
note (§2) proposed extending `LibraryBrowseHistory` to carry cosmetic toggle state, which would
have made Back/Forward start undoing display settings instead of navigating; fixed by keeping
scroll restoration in a transient, non-history field instead (confirmed via `LibraryBrowseState`'s
actual fields — it never carried view-mode/display state to begin with, so this would have been new
scope-creep onto that record, not a natural extension of it). Its ScrollViewer and manual
panel-collapse-toggle suggestions were accepted as genuine gaps (§4) — though the suggested `Ctrl+P`
binding is already taken by `OpenQuickOpenCommand`, so `Ctrl+J` is the candidate instead.

Two more claims from this round were checked and rejected: a luminance-adaptive alpha requirement
for the accent-tint converter, based on an assumption `AccentColor` could be an arbitrary neon
hex — verified false, it's a bounded 6-swatch palette already curated for this skin (§6), so the
flat ~16-18% alpha already specified has no unreachable failure case to guard against. And a
mandatory ~50ms debounce on `SelectedPreview` updates during arrow-key navigation, based on an
assumption that rapid selection changes could pile up stale image-decode work — verified false,
`AsyncCoverImage` already coalesces concurrent decodes per cover-stem (`s_inflight`) and drops
stale results via a per-instance generation token, so rapid arrow-key traversal already can't
accumulate wasted decode work today. Adding a debounce would add complexity against a cost that
doesn't exist in this codebase; revisit only if on-screen verification actually shows jank, not
pre-emptively.

**Third review round (2026-09-14, same day):** caught one more real gap — §1/§4 never said what
happens to the `GridSplitter` and its column when the panel is collapsed, which could leave a
stranded splitter and dead column width instead of the list actually reclaiming space; fixed with
an explicit column layout (at the time, described as 4 columns including the sidebar — see the
implementation-time correction in §1: the sidebar turned out to live in `MainWindow.axaml`, not
here, so the real count inside `LibraryScreen.axaml` is 3 — the splitter/panel visibility-binding
fix itself was correct and unaffected by that recount). `Ctrl+B` was locked in (checked: genuinely
zero `Ctrl+B`/`Key.B` bindings anywhere in `Paperbunkr.App` today), replacing the earlier tentative
`Ctrl+J`.

The "focus trap" claim was partially right and partially wrong, and the spec now reflects the
correct part only. Checked `PosterRail.axaml:70`: its rail items are real `Button` controls in a
plain `ItemsControl`, so they are genuinely part of the default Tab order — worth trimming so
Tab-cycling reaches the panel's real actions faster (`IsTabStop="False"` on the rail's buttons
specifically, not "all child controls," since the panel's own Continue/Mark Read/Add-to-Collection
buttons must stay keyboard-reachable — blanket-disabling focus across the whole panel as originally
worded would have been a real accessibility regression, not a fix). But the stated mechanism — that
rapid *arrow-key* navigation in the main grid could "periodically drop focus" into the rail — isn't
how Avalonia's keyboard navigation works: arrow-key handling stays scoped to whichever control
currently has focus; it doesn't hop to an unrelated sibling control. Tab-order hygiene is worth
doing; treating it as an arrow-key trap is not accurate and the spec doesn't claim it fixes one.

## Testing

- Migration test for the `LibraryViewMode` remap (mirrors `LibraryPosterGridConsolidationTests`
  pattern) — every old value lands on the correct new value + toggle combination, existing rows
  with no prior value get the documented defaults.
- `CornerBadgeKind` computed-property unit tests: selected wins over hovering-with-dog-ear-eligible,
  dog-ear wins when hovering and not selected, `None` when neither applies. Separate test(s) for the
  bottom-left rating binding confirming it renders independently of selection/hover state.
- Preview panel: one test per content state (series/issue/idle-empty/no-results) confirming the
  right fields are populated, plus a granularity-toggle test confirming a selection re-renders the
  correct variant when the toggle flips without changing the underlying selection. A multi-select
  regression test confirming the panel keeps showing the last single-focused item's preview (not a
  crash or blank state) while multiple items are selected.
- Selection-bar bulk-action test confirming bulk edit/mark-read/add-to-collection/delete remain
  reachable in all 3 view modes, including `DetailsTable` with the preview panel hidden.
- Accent-tint converter test: a sample `AccentColor` produces a background brush at the documented
  ~16-18% alpha and a border brush at full opacity from the same input color.
- Scroll-position test: selecting a `LibraryGridCoverFit`/`LibraryListDensity` toggle mid-scroll
  restores the same approximate position after the virtualized panel rebuilds, and a companion test
  confirming `LibraryBrowseHistory`'s entries are unchanged by the toggle (still only
  content-type/collection/search, no view-mode/scroll fields leaking in).
- Manual collapse test: toggling `IsLibraryPreviewPanelVisible` hides/shows the panel in Grid/List
  without affecting `DetailsTable`'s own independent auto-hide, the setting persists across a
  restart, and the list column's measured width actually grows to fill the freed space (not just
  panel content disappearing behind a still-occupied column).
- Tab-order test: Tab-cycling from the main list reaches the preview panel's quick-action buttons
  without first stepping through every `PosterRail` card button.
- Existing `LibraryToolbarDriver`-based FlaUI UI tests updated for the toolbar's new full-width
  container — verifies the toolbar itself needs no behavioral changes, only re-verifies layout.
- On-screen verification (no unattended GUI automation, standing project caveat): 3-pane layout at
  a few window widths including ultrawide (`GridSplitter` behavior), corner-badge states
  (idle/hover-multipage/hover-singlepage/selected) against real cover art, preview panel for a real
  series and a real issue, an empty-search no-results state, sidebar accent-color tinting across at
  least 2 differently-colored Collections (checking text legibility, not just presence of color),
  and bulk actions working correctly in `DetailsTable` mode specifically.

## Implementation-time shape corrections (plan-writing pass, 2026-09-14)

Read the real code before planning (per this project's own convention). Three places where the
actual shape simplifies what's written above — intent unchanged, mechanism corrected:

- **No `CornerBadgeKind` enum/pipeline needed.** Hover state (dog-ear peek, tooltip) is entirely
  control-level code-behind today (`LibraryScreen.axaml.cs`'s `OnCoverPointerEntered`/
  `TryShowDogEarPeek`, manipulating an `Image.IsVisible` directly) — no row/card model has ever
  carried an `IsHovered`-style property, and `IssueListRow.DogEarEligible` already exists as a
  computed property. "Selection wins over dog-ear" is a one-line change to `TryShowDogEarPeek`'s
  existing gate (also require `!row.IsSelected`), not new per-row state plumbing.
- **No new "anchor/last-selected" tracking needed on `TileSelectionController<T>`.** It's a pure
  selection-set API (`SelectedIds`/`Count`/`Toggle`/`ReplaceSelection`) shared with
  `DetailTabsViewModel`, with no public single-item concept — and it shouldn't gain one just for
  this. The preview panel's "last single-focused item" instead comes from the same `item` parameter
  already passed into the existing `ReplaceSelection`/`Toggle` call sites at the moment of a
  click/arrow-key interaction, tracked as a small new `LibraryScreenViewModel` field, independent of
  `TileSelectionController<T>` itself.
- **`LibraryViewMode`'s consolidated Grid-family member keeps the C# identifier `PosterGrid`, not
  `Grid`.** Every reference above to a `Grid` enum value means this member. Renaming it during
  implementation broke two things at once: any code path that lets EF fall back to the column's
  physical DB default (every "verify this migration in isolation" test in this project deliberately
  does that) failed to parse the old stored string `"PosterGrid"` into a renamed member; and fixing
  that with an `AlterColumn` reintroduced the exact `project_paperbunkr_migration_rollback_orphan_
  column_bug` this codebase already spent a session fixing once. Keeping the identifier unchanged
  needs zero schema change for the default value and avoids both problems - see `LibraryViewMode.cs`'s
  doc comment for the full account. VM-level property names (`IsGridView`, `ShowPreviewPanelColumn`,
  etc.) are unaffected and still read naturally.
- **No new preview content model needed.** `SeriesCardSample` and `IssueListRow` already carry
  everything §4 lists (`SeriesCardSample.Publisher`/`LanguageIso`/`SeriesStatusLabel`/`IssueCount`/
  `UnreadCount`/`RepresentativeRow.SummaryExcerpt`; `IssueListRow.Writer`+`Penciller`/`LanguageIso`/
  `Rating`+`HasRating`/`FileSizeDisplay`/`HasFormat`/`IsRead`) — the preview panel binds to these
  existing models directly, the same way the hover-tooltip popup already does, rather than
  projecting into a new DTO.

## Deliverable: description checklist

- [ ] 3-pane Master-Detail layout ships; toolbar spans full width above all 3 columns.
- [ ] `LibraryViewMode` reduced to `Grid`/`List`/`DetailsTable`; `LibraryGridCoverFit` and
      `LibraryListDensity` toggles ship; migration remaps existing persisted values correctly;
      scroll position survives the toggle flip.
- [ ] Tile corner-slot system ships across all 4 grid templates: bottom-right `CornerBadgeKind`
      (Selected/DogEar only), bottom-left rating as an independent binding; language badge removed
      from tiles.
- [ ] Live preview panel ships with all 4 content states (series/issue/idle-empty/no-results),
      resizable via `GridSplitter` with persisted width, manually collapsible in Grid/List via
      toolbar toggle + `Ctrl+B` (splitter and panel columns both hide together, list reclaims full
      width), wrapped in a `ScrollViewer` so quick actions never clip, granularity-aware, updates on
      single-click/arrow-key selection, does not attempt to own multi-select/bulk rendering, and
      keeps its own action buttons Tab-reachable while the embedded `PosterRail`'s cards are
      `IsTabStop="False"`.
- [ ] Scroll position across a `LibraryGridCoverFit`/`LibraryListDensity` toggle flip is restored
      via a transient ViewModel field, not `LibraryBrowseHistory`.
- [ ] Double-click/Enter still opens the real Detail screen; `DetailsTable` mode hides the panel
      without affecting bulk-action availability.
- [ ] Bulk actions (edit/mark-read/add-to-collection/delete) reachable identically in all 3 modes
      via the toolbar-anchored selection bar.
- [ ] `#B814161B` hardcoded hex removed; publisher/rating chips use `PbBadgeBrush`/
      `PbBadgeTextBrush`; tile hover uses `PbGlowBrush`; sidebar active-row uses each row's own
      `Collection.AccentColor` at ~16-18% alpha for background / full opacity for the border, with
      verified text contrast.
- [ ] Toolbar Overlay-group copy reviewed against the new corner map; no toggle added or removed.
- [ ] `App.Tests`/`Data.Tests` green; on-screen verification per the Testing section completed.
