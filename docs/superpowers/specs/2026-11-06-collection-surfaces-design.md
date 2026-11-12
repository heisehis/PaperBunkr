# Collection surfaces — Design

Slice 3 of [cosmetics pitch 3](2026-11-06-cosmetics-pitch-3.md): items 9 (banner header, with the markdown description dropped from Slice 1), 13 (membership badges),
17 (empty state) and 18 (Home hero card). Grilled 2026-11-06; all eight recommended answers accepted. Status: **design for review - nothing built.**
Builds on Slices [1](2026-11-06-collection-dialog-polish-design.md) and [2](2026-11-06-collection-identity-design.md).

## Summary

A collection currently has no face on the screens where you use it: opening one just filters the grid. This slice gives it a **banner header** in the Library (cover, glyph, name,
description, stats, Edit/Pin), an **empty state** that explains how to fill it, optional **membership dots** on Library covers, and a larger **Continue hero card** for the
collection you read from most recently on Home. One new `AppSettings` column; everything else is computed at load.

## Goals / non-goals

Goals: the four items below, reusing `LibraryTile`, `CollectionProgressResolver`, `MarkdownView`/`MarkdownLite`, `TileCosmeticsOverlay` and the Home `Collections` shelf.

Non-goals (decided in the grilling round): **item 16 (collection tabs) is dropped** - there are no Members/Gaps/Related tabs on a collection (Gaps live in Preferences > Library
Health, Related in the Edit dialog), so there is nothing to underline; **item 15 (group by collection) is deferred** - the Library has no collection group axis and a series can
sit in several collections, so grouping would repeat items; no drag-to-add onto a collection; no page-count stat; no continue chip for books; no new setting for the hero.

## CE parity

ComicRack CE has no collection header, badges or Home shelf (its library tree has lists/folders only), so these are deliberate Paperbunkr features; nothing to verify against `_reference/ComicRackCE`.

## Existing anchors (verified 2026-11-06)

- `Views/LibraryScreen.axaml`: `RootGrid` has `RowDefinitions="Auto,*"`; row 0 is `LibraryToolbar` (spans 3 columns), row 1 holds one scroller per view mode (`PosterGridScrollViewer`, list, table, bookshelf) plus
  `CollectionGridScrollViewer` for a mixed-membership collection (`IsCollectionView`); the preview panel is column 2. There is **no** collection header anywhere.
- `LibraryScreenViewModel`: `_activeCollectionId`, `_allCollections`, `_collectionMemberCounts`, `_activeCollectionMembers`, `HasAnyResults`, `Collections` (`CollectionSummary` with glyph/pin/progress from Slice 2),
  `OpenCollectionPropertiesCommand`, `TogglePinCollectionCommand`, `SelectCollectionCommand`.
- `Controls/MarkdownView` renders `MarkdownLite` blocks; `LinkCommand="{x:Static services:ExternalLinks.OpenWebCommand}"` is the existing link handler (changelog, `DetailBand`).
- `Views/TileCosmeticsOverlay`: one custom control, no template, no per-tile bindings, reads its `DataContext` as `ITileProgressSource` (`ReadFraction`, `IsFinished`, `IsRightToLeft`); implemented by `IssueCardSample` / `SeriesCardSample`.
- `Models/HomeCollectionCard`, `Views/Home/CollectionsSection.axaml` (a `StackPanel` of `PosterTile`s), `HomeScreenViewModel.OpenCollection`; `HomeFeedResolver.GetHomeCollections` (pinned first since Slice 2).
- `Issue.OpenedTime`, `Issue.LastPageRead`; `CollectionResolver.GetMembers`; `CollectionProgressResolver`.

## Decisions

| Topic | Decision |
|---|---|
| Banner header | A band between the toolbar and the grids, shown whenever a collection is active (mixed grid and every normal view-mode grid), never for "All Series". About 120 px tall: the collection cover blurred and accent-tinted behind, glyph + name (`pbTextHeading`), the description, stat chips, and Edit / Pin `iconButton`s. Scrolls with nothing - it is fixed above the scrollers, like the toolbar. Collapses to a single line (name + chips) when the window is shorter than 700 px. |
| Placement | Add a row to `RootGrid` (`Auto,Auto,*`) and move the row-1 content to row 2 mechanically; the banner is a new `Views/CollectionBanner` UserControl (code-behind in the same change) in row 1. The preview-panel column and splitter keep spanning the content row. |
| Description | Rendered with `MarkdownView` (bold, italic, links via `ExternalLinks.OpenWebCommand`), capped at three lines with a "more / less" toggle; empty description hides the block. Home, tooltips and the dialog counter stay plain text. |
| Stats | Chips "N series · M issues · K books" (only the non-zero kinds) and "62% read"; the percentage comes from `CollectionProgressResolver.GetProgress` for the one active collection, **even when the sidebar ring setting is off**. No page counts. Computed in the Library load for the active collection, not on the UI thread. |
| Membership badges | New `AppSettings.ShowCollectionBadges` (default **false**; Preferences > Appearance toggle). When on, Poster and Panorama covers show up to three small accent dots (the collection accent, or the generic accent when unset) at the cover's bottom-left, "+N" beyond three, drawn inside `TileCosmeticsOverlay`. Not shown while a collection is the active filter. Tooltip lists the collection names. Membership (manual items and rule matches, for series and for issues) is built once per Library load, only when the setting is on, reusing the member resolution already done for sidebar counts. A series tile uses its own memberships; an issue tile uses its own plus its series'. |
| Overlay data | A new `ITileCollectionSource` (`IReadOnlyList<CollectionBadge> CollectionBadges`, `string? CollectionBadgeTooltip`) implemented by `IssueCardSample` and `SeriesCardSample`; the overlay reads it from its `DataContext` like `ITileProgressSource`. No new bindings or template parts per tile. |
| Empty state | When the active collection has **no members at all** (not "the filters hide everything"): a centred card with the glyph tinted by the accent, "Nothing in {name} yet", a line "Right-click a series or issue in the Library and choose Add to Collection, or give this collection a rule.", and two buttons: "Edit collection…" and "Go to All Series". The existing filtered-empty message is unchanged. No drag target. |
| Home hero | The first card of the Collections shelf is a larger card for the collection with the newest `OpenedTime` among its members. A "Continue" chip opens the next unread issue: the first manual member, in collection order, that has one (a series uses its first unread issue by number; a standalone issue counts if unread; books are skipped). With no activity or nothing unread there is no hero and the shelf looks as today. The rest of the cards follow in the existing order. No new setting. |
| Hero view | New `Views/Home/CollectionHeroCard` UserControl (code-behind in the same change): cover (single/mosaic/fan per the collection's style), glyph + name, `N items`, read-progress ring when the Slice 2 setting is on, and the Continue chip as a real `Button` so its press does not reach the card's open handler. Clicking the card elsewhere opens the collection as today. |
| Persistence | One migration adding `AppSettings.ShowCollectionBadges` (no-op `Down()`); never regenerate a migration the user has launched. |

## Behaviour details

- **Banner cover:** reuses the collection's resolved cover (manual file, chosen member, or first member via `GetCoverHint`/`LibraryTile`); blur is a fixed-radius effect on a clipped image at low opacity under a scrim token so
  text contrast holds on every theme; with no cover, a flat `PbSurface2` with the accent tint. Reduced motion: no animation either way.
- **Banner actions:** Edit opens the existing dialog; Pin toggles via `TogglePinCollectionCommand`; both carry names. A smart collection shows the Wand marker beside the name.
- **Stats freshness:** recomputed on every Library reload (the same trigger that refreshes the sidebar), so reading an issue and returning updates "% read".
- **Badge cost:** `ShowCollectionBadges` off = no extra work and no overlay change. On = one dictionary lookup per tile realization (no allocation: the badge list is prebuilt per load and shared).
- **Badge limit:** dots are 6 px, 3 px apart, bottom-left, clear of the bottom-right rating/dog-ear and the read-progress bar; they sit inside the existing overlay's bounds.
- **Hero selection:** newest `Issue.OpenedTime` over (series members' issues + standalone issue members); ties break by collection order. Computed in `HomeFeedResolver`/a small `CollectionContinueResolver` once per Home load.
- **Continue chip target:** `OpenResume`-style reader open for the issue id, same path as the Continue Reading row.
- **Empty state vs filters:** shown only when `_activeCollectionMembers`/manual items and rule matches are both empty; a collection that has members but zero results under the filters shows the existing message.

## Approaches considered

1. **A fixed banner row above the scrollers + a code-only overlay extension (recommended).** One new row, one new control, minimal change to scroll behaviour; badges stay inside the single overlay.
2. **Banner as the first child inside each scroller.** It would scroll away, but needs adding to every view-mode grid (and each grid's pinned-header logic walks the first Panel's children); rejected.
3. **Badges as template parts on each card.** Simpler XAML but undoes the scroll-smoothness pricing of the tile overlay; rejected.

## Avalonia / design-language constraints

- `paperbunkr-design-language`: type classes, tokens only (scrims/blur through named tokens, no hex), 4 px grid, 24 px gutter, shared families (`iconButton`, `ghost`, `pbChip`/`countPill` for chips), `PbMotionEase`, squircle chips, 1366x768 minimum (the banner must not push the grid below a usable height).
- Load the `avalonia` layout/styling/data-template/accessibility subskills from disk before XAML; run `avalonia-pro-max/review-checklist` at the end.
- New `.axaml` views (`CollectionBanner`, `CollectionHeroCard`) ship with code-behind in the same change; verify `CompileAvaloniaXaml` ran.
- A control inside the Continue chip/Edit/Pin must not remove or rebuild its own row from inside the routed click: defer one dispatcher tick (CLAUDE.md). A `Button` swallows a left press: the hero's open handler is on the card, the chip is a Button.
- No hotkeys are added.

## Accessibility

Banner is a landmark with the collection name; Edit/Pin/more-less have names; the stats are real text; badge dots are decorative with the names in the tile tooltip and in the tile's accessible description; the empty-state buttons are named; the hero's Continue chip names the issue ("Continue Batman #12").

## Verification

Data.Tests: migration (default false); `CollectionContinueResolver` (newest opened wins, next unread by collection order, series first-unread by number, nothing unread = no chip, books skipped, ties). App.Tests: Library view-model tests for the banner state (active collection, stats incl. the % with the sidebar setting off, empty-collection flag vs filtered-empty), badge membership map (manual + rule, series vs issue, off = empty), `ITileCollectionSource` on both samples, Home hero selection and chip target; headless renders of the Library with a collection active (banner with/without cover and description, empty state), of tiles with badges, and of the Home hero (default/daylight/matrix); lint trio. Then the user's screen check at 1366x768.

## Risks

- Renumbering `Grid.Row` in a 2,000-line `LibraryScreen.axaml` that also hosts scroll-restore and pinned-header code: do it mechanically, then re-run the Library view-model and the keyboard tests for the screen.
- Blur cost behind the banner: one small clipped image; measure on the user's machine before adding any animation.
- Badge membership for large smart collections reuses member resolution; keep it once per load and only when enabled.
- The hero card must not disturb the Collections shelf's existing keyboard order (hero first, then cards).

## Implementation notes (2026-11-06)

Built per the plan. Deviations: badge data lives in a static `CollectionBadgeStore` (push-cache) instead of properties set on each sample; the Library's issue tile type is
`IssueListRow` (not `IssueCardSample`); `CollectionContinueResolver` is in the App layer (issue number ordering is there); the banner's progress/cover are read with one small query
when it rebuilds; the banner spans all three columns. Item 16 dropped and item 15 deferred, as decided. Not yet seen on screen.
