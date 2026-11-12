# Collection identity — Design

Slice 2 of [cosmetics pitch 3](2026-11-06-cosmetics-pitch-3.md): items 4 (glyph), 5 (pick the automatic cover), 10 (cover styles), 11 (pinning), 12 (progress
ring) and 14 (sidebar density, dot shape, drag reorder). Grilled 2026-11-06; all eight recommended answers accepted. Status: **design for review - nothing built.**
Builds on [Slice 1](2026-11-06-collection-dialog-polish-design.md), whose dialog hosts most of the new controls.

## Summary

A collection is currently a name, a colour dot and an automatic cover. This slice gives it a look of its own: an optional **glyph**, a **pin** that floats it to the
top, a choice of **cover style** (Auto / Single / Mosaic / Fanned stack), a **"use as cover"** member pick, an opt-in **read-progress ring**, and two sidebar
cosmetics (**row density**, **dot shape**) plus **drag reorder** in the sidebar. One new migration; no change to what membership means.

## Goals / non-goals

Goals: the six items below, reusing `ProgressRing`, `LibraryTile.FromMember`, the dialog from Slice 1 and the sidebar row of `MainWindow.axaml`.

Non-goals (decided in the grilling round): cover **crop / focal point** (deferred), **spine-row** cover style (deferred), a glyph on Home cards, dragging between the
pinned and unpinned groups, per-collection density/shape (they are global), the markdown description (already folded into pitch item 9).

## CE parity

ComicRack CE has no collections with glyphs, pins or cover styles (its closest idea is a library list/folder with an optional icon-less tree row); these are
deliberate Paperbunkr features, so there is nothing to verify against `_reference/ComicRackCE`.

## Existing anchors (verified 2026-11-06)

- `Collection` entity: `AccentColor`, `CoverImagePath`, `IsAutoCover`, `SortOrder`, the three smart-list slots. No glyph / pin / style / cover-member field.
- Sidebar row: `Views/MainWindow.axaml` ~L297-419 (`Border.colRow`, 8 px `Ellipse`, `Wand` icon for smart collections, `sideItemButton`, hover "More" flyout with
  Edit / Set a goal / Move up / Move down, two-step delete). Summaries are built in `LibraryScreenViewModel.cs` ~L1412 from `_allCollections`, which is loaded
  `OrderBy(SortOrder)` at ~L1205; reorder is `ReorderCollection` ~L2024 -> `CollectionService.Reorder`.
- Home shelf: `HomeFeedResolver.GetHomeCollections` (`OrderBy(SortOrder)`, limit 10, non-empty only) -> `HomeCollectionCard.FromCollection`, which already builds a
  2x2 mosaic when there are at least three distinct covers (`ReadingListCoverMosaic.PickCoverKeys`) and `CollectionResolver.GetCoverHint` for the single cover.
- `Controls/ProgressRing` (drawn; `Value` 0..1, brushes from the caller). Series read fraction: `SeriesCardSample.ReadFraction` = (IssueCount - UnreadCount) / IssueCount.
- The series ring setting is `AppSettings.ProgressRing` (default **true**).
- Buttons swallow a left press (CLAUDE.md), so a sidebar drag cannot start on the row button; `HomeSectionRowDrag` / `CollectionMemberRowDrag` are the pattern.
- Books screen builds its own `CollectionSummary` list (`BooksScreenViewModel.cs` ~L866), ordered by `SortOrder`.

## Decisions

| Topic | Decision |
|---|---|
| Glyph | `Collection.IconGlyph` (string, nullable) = a FluentIcons `Symbol` name from a curated list of ~24 (stack, star, heart, skull, rocket, flame, crown, bookmark, ...). Empty = the current dot. Picker is a grid in the dialog's Appearance card. Shown in the sidebar row (replaces the dot, tinted with the accent), the dialog preview and the Detail chips; **not** on Home cards. An unknown name falls back to the dot. |
| Pinning | `Collection.IsPinned` (bool, default false). Dialog toggle plus a sidebar hover action. Pinned first, then unpinned, each ordered by `SortOrder` (sidebar, Books screen list, Home shelf). Pinned rows show a small pin icon; a hairline divider sits between the groups. Move up/down and drag stay inside the group. |
| Cover style | `Collection.CoverStyle` (int enum, default `Auto`): `Auto` (today's rule: mosaic from 3+ distinct covers, else single), `Single`, `Mosaic` (2x2, falls back to Single below 3 distinct covers like the rule), `Fan` (3 overlapping covers, slight rotation, falls back to Single below 2). Applies to Home cards and the dialog preview. A manual cover file (`IsAutoCover` false) always overrides the style. Spine-row deferred. |
| Pick the automatic cover | `Collection.CoverMemberItemId` (int?, FK-less pointer to a `CollectionItem.Id`). A "Use as cover" icon button on each **manual** member row of the dialog (rule-matched rows have no item id and cannot be chosen); picking the current one clears it. The Auto button reads "Auto (Title)". If the item is removed or its id no longer exists, fall back to the first member. `GetCoverHint` honours it. Buffered with Save, like the other edits. |
| Progress ring | Fraction of read units across the members: a series counts issues read / issues total, a standalone issue counts 1 when read, a book counts 1 when finished. Rule-matched members count too. Opt-in new setting `AppSettings.ShowCollectionProgress` (default **false**; deliberately separate from `ProgressRing`, which defaults to true and would otherwise turn the ring on for everyone at once). Drawn with `ProgressRing` beside the count in the sidebar row and in the Home card's corner. Computed in the same load that builds the summaries, never per row on the UI thread. 0 members or 0 units = no ring. |
| Density | `AppSettings.CollectionRowDensity` (enum, default `Comfortable`; `Compact` = tighter row padding and a smaller dot/glyph). Collections section of the sidebar only. Preferences > Appearance cosmetics. |
| Dot shape | `AppSettings.CollectionDotShape` (enum, default `Circle`; `Squircle` = `PbRadiusSm`-rounded square). Never an oval pill. Ignored when a glyph is set. Same Preferences place. |
| Sidebar drag | A hover-only grip at the row's left starts the drag (not the row button); an accent drop line shows the target; drops work only within the pinned group or within the unpinned group. Move up / Move down stay in the hover menu as the keyboard path and respect the same boundary. |
| Persistence | One new migration `AddCollectionIdentity`: `Collection` gains `IconGlyph`, `CoverStyle`, `IsPinned`, `CoverMemberItemId`; `AppSettings` gains `ShowCollectionProgress`, `CollectionRowDensity`, `CollectionDotShape`. The `AppSettings` part gets a no-op `Down()`. Never regenerate a migration the user has launched. |

## Behaviour details

- **Order everywhere:** a small helper `CollectionOrdering.Apply(IQueryable<Collection>)` = `OrderByDescending(IsPinned).ThenBy(SortOrder)` used by the Library sidebar load, Books
  screen, Home shelf and `ShowInList`. `CollectionService.Reorder` still assigns `SortOrder` from the full displayed id list, so pinned/unpinned relative order is preserved.
- **Pin toggle** changes only `IsPinned`; it does not renumber `SortOrder`. A collection keeps its place inside its group when unpinned later.
- **Move up/down across the boundary** is a no-op (the first unpinned row cannot move above the last pinned one).
- **Progress is aggregated in one pass:** one grouped query for series (read issues, total issues) + counts of read standalone issues and finished books per collection id,
  merged into the summary list; the Home cards reuse the same helper. The result is cached with the existing sidebar load and refreshed with it.
- **Fan style:** three cover images drawn by one custom control (no per-image template parts) at fixed offsets and rotations (-6, 0, +6 degrees), ClipToBounds; no animation.
- **Glyph list:** a static array of (symbol name, display label); the picker is a `WrapPanel` of `iconButton`s with `AutomationProperties.Name` and a "None" button; the
  selected one gets the same ring state style as the swatches.
- **Sidebar row with a glyph:** the glyph replaces the dot; the smart-collection Wand stays as the secondary marker. A pinned smart collection can show pin, glyph and wand;
  the row keeps trimming the name first.
- **Density:** `Compact` changes padding/margins and icon sizes through a `.compact` class on the Collections `StackPanel` set from the setting; no fixed heights.
- **Hover actions:** the existing `colAction` fade pattern gets a Pin button; the drag grip uses the same hover/focus-visible rule.

## Approaches considered

1. **Add the columns and extend the existing sidebar row and dialog (recommended).** Smallest change, matches Slice 1 and the existing row mechanics.
2. **A separate `CollectionAppearance` table.** Cleaner separation but an extra join everywhere for six scalar fields; rejected.
3. **Store glyph/style/pin in a JSON blob column.** Avoids columns but hides them from queries (pinned ordering needs a real column); rejected.

## Avalonia / design-language constraints

- `paperbunkr-design-language`: type classes, tokens only, 4 px grid, shared families (`iconButton` for pin/grip/use-as-cover), `DialogCard` stays the frame, motion on `PbMotion*` tokens, no oval pills.
- Load the `avalonia` layout/styling/data-template/accessibility subskills from disk before XAML; run `avalonia-pro-max/review-checklist` before calling it done.
- New views need their code-behind in the same change; prove `CompileAvaloniaXaml` ran. Prefer code-only controls (like `HsvColorPicker`) for the fan cover.
- Any removal or reorder started inside a routed event is deferred one dispatcher tick (CLAUDE.md).
- A button swallows a left press: the sidebar grip is a non-button `Border`; the drag is not started from the row `Button`.
- No hotkeys are added; nothing here needs an `InputAction`.

## Accessibility

Glyph, pin, grip and "use as cover" buttons carry names; the selected glyph and the cover member expose selected state; the ring has text via the row tooltip
("62% read"); pinned state is an icon plus a tooltip, never colour alone; the drag is never the only way to reorder (Move up/down remain).

## Verification

Data.Tests: migration test (columns added, defaults), `CollectionOrdering` (pinned first, stable within groups), `Reorder` keeping group order, `GetCoverHint` with a chosen
member and with a stale id, progress aggregation (series/issue/book mix, rule-matched members, empty). App.Tests: view-model tests for the dialog (glyph, pin, cover style,
use-as-cover buffering and Cancel), sidebar summary (`IsPinned`, glyph, progress), move-up/down boundary, a headless render of the sidebar block and the dialog in
default/daylight/matrix (`FoundationStyleTests.WithFoundation`), the lint trio. Then the user's screen check at 1366x768.

## Risks

- Progress aggregation cost on a large library: keep it one grouped query; measure on the user's DB before enabling by default.
- The migration touches both `Collection` and `AppSettings`: scaffold with `dotnet ef migrations add ... --project src/Paperbunkr.Data`; confirm `Up` is not empty and the
  `AppSettings` `Down()` is a no-op; watch the known rollback-chain test failures from other uncommitted migrations.
- Pinned ordering must be applied in every collection list (sidebar, Books, Home, ShowInList) or the lists disagree.
- Fan control with few covers must fall back cleanly (never draw an empty slot).

## Implementation notes (2026-11-06)

Built per the plan. Deviations: the dialog preview card shows the single/auto cover only (Mosaic and Fan appear on Home cards); Compact density trims only the row's own
margin; the Books screen got the pinned ordering but not glyph/pin marks; the Preferences settings have no dedicated view-model test. Not yet seen on screen.
