# Entity pages redesign — Character, Team, Location, Creator, Publisher

Date: 2026-11-12 · Status: design approved in brainstorming (visual companion screens `entity-layout`, `entity-tile`), awaiting spec review

## 1. Why

The entity page (`MetadataEntityDetailScreenView` / `MetadataEntityDetailScreenViewModel`, built on `EntityPageResolver`) is one layout for five
kinds. Seen on screen 2026-11-12 (Castle Doom, a creator page, Earth 6160's Key characters):

- The two-column card grid has uneven card heights, which leaves holes ("First appearance" beside a tall Series card, Runs alone at the bottom).
- Series, Events, Appearances by year and Runs are each capped at 320 px and scroll inside the page scroll.
- Bugs: an empty **Runs** card on a location page; "2013 · · 1 issue" when an event has no continuity; "1 appearances"; roles shown raw
  ("CoverArtist"); event rows indent differently when one row has an image; a dead gap under the actions (empty loading/error text blocks
  plus stack spacing).
- Continuity appears twice (the switch and a card), and each segment is its own `segGroup`, so the switch reads as separate pills.
- The portrait is small (112×140) and falls back to a crop of the first cover, so the Continuity page's Key characters / Key teams strips look like
  a row of comics. Those tiles change height when a name wraps, and their hover map button sits on the art.

**CE parity:** ComicRack CE has no entity pages. Characters, teams and locations are text fields you can stack by (`_reference/ComicRackCE`,
`ComicBookDialog`, `ComicListField`). This is a Paperbunkr-only screen, so there's no CE behaviour to match.

## 2. Decisions (from the grilling rounds)

| # | Decision |
|---|---|
| Q1 | One page for all five kinds. Character and Team share one variation. Location, Creator and Publisher each get their own (§4). |
| Q2 | The Continuity page's Key characters / Key teams strips move to the new shared entity tile (§5.1). Issue tiles reuse `CoverThumb`. |
| Q3 | Reading comes first: the header's main action is "Continue · <next unread>" for characters and teams. |
| Q4 | Structure A: a header plus tabs. The page's `ScrollViewer` is the only scroller; no list scrolls inside it. |
| Q5 | The continuity switch is one `segGroup` with counts ("Earth 616 · 30"), or the `SuggestBox` above 5 choices. The Continuities card is removed. |
| Q6 | New: **Often appears with** (characters and teams that share the most issues), top 12. |
| Q7 | Appearances by year becomes a compact column chart: not clickable, no sweep animation. |
| Q8 | Creator roles become readable filter chips. Runs get a full-width career timeline. |
| Q9 | The header reuses the Continuity page's `HeroBand` (cover collage behind the hero scrim), with a bigger image and the actions inside the header. |
| Q10 | The bugs listed in §1 are fixed as part of this work. |
| Q11 | Appearances tab: `CoverThumb` grid, sorted Reading order (default) / Newest / Series, with an **Unread only** toggle (default off). |
| Q12 | Series tab: a poster grid with read-progress bars, shared with the Publisher page. |
| Q13 | Events tab: rows in reading order. Every row has a thumbnail slot (event cover or monogram), so rows line up. "Open on the map" is in the row's context menu. |
| Q14 | Collage: the 5 most recent appearance covers in the selected continuity. Publisher: one cover from each of the 5 series you own the most issues of. |
| Q15 | The ⋯ Manage menu holds Change image, Reset image, Open on the map (character/team), Refresh profile. "Show in Library" stays a visible button. |
| Q16 | No "parent place" on location pages: `Location` has no parent field and no provider fills one. |
| Q17 | Creator counts are distinct **issues** credited, not credit rows. Each role chip shows the number of issues for that role. |
| Q18 | Publisher: Characters tab = its 48 most-seen characters (shared tile). Continuities tab = rows (brand mark, name, count) that open the continuity. |
| Q19 | On the issue Detail page, clicking a character, team, location or creator chip opens its entity page. "Search library" moves to the chip's context menu. |
| Q20 | The page always opens on the kind's lead tab. Back returns to the tab you left (the kept `RouteHost` view already does this). |
| Tile | Portrait card (3:4) at a fixed height. With no portrait: a monogram with the first-appearance cover inset. The map button moves to the context menu. |

## 3. Page shell

```
┌ HeroBand (collage + PbHeroScrimColor) ─────────────────────────────────────────┐
│ [image]  OVERLINE (kind)                                                        │
│          NAME (pbTextHeading)                                                   │
│          aliases · counts line (pbTextMeta)                                     │
│          description, 2 lines + More/Less (pbTextBody)                          │
│          [primary action] [secondary…] [Show in Library] [⋯ Manage]            │
│          [ All · 32 | Earth 616 · 30 | Earth 6160 · 2 ]   (or role chips)      │
└─────────────────────────────────────────────────────────────────────────────────┘
  Tab · Tab · Tab                         (Button.tab, StackPanel Spacing=20)
  ─────────────────────────────────────────
  tab content (one panel visible at a time, controls:Replace.On)
```

- **Back:** the `ghost backLink` button stays above the hero band, as today.
- **Image slot per kind:**

  | Kind | Size and shape |
  |---|---|
  | Character / Team | 3:4 portrait, 120×160 |
  | Location | 16:9, 240×135, with the scrim fading downward |
  | Creator | 120×120 squircle (`PbRadiusLg`) |
  | Publisher | `BrandMark` at MarkSize 64 instead of an image, and no title text (the mark is the name) |

  The fallback order is unchanged (`EntityImageResolver`): custom image → provider image → monogram with the inset cover (§5.1).
- **Counts line:**
  - Character / Team / Location: "32 appearances · 9 series · 4 read". Pluralised: "1 appearance", "1 series" (fixes §1).
  - Creator: "135 issues · 18 series · 2009–2026".
  - Publisher: "412 series · 6,980 issues · 1961–2026 · 18% read".
- **Profile note:** the existing "Couldn't fetch the picture and bio…" note stays, below the description.
- **No dead gap:** "Loading…" and the error text collapse when empty (`IsVisible` bound, not just empty text).
- **Tabs:**
  - `Button.tab` buttons with the sliding `TabIndicator`, so `TabStrip.Step` drives them from the bumpers and Ctrl+PageUp/PageDown with no extra code.
  - A tab with no content is hidden; Events is hidden when there are none.
  - Each tab label carries its count where useful ("Appearances 32").
- **One scroller:** list tabs put a plain `ItemsControl` with `controls:VirtualizingWrapPanel` straight into the page `ScrollViewer`. The panel
  virtualizes from `EffectiveViewportChanged` (`VirtualizingWrapPanel.cs` line 102), so a 1,000-issue character still realizes only what's on
  screen. No `ListBox`/`ScrollViewer` inside a tab (avalonia-pro-max/layout-patterns: "one vertical scroll axis per view").
- **Narrow windows (1366×768 with the sidebar):**
  - The action row wraps (`WrapPanel`).
  - The description stays 2 lines collapsed.
  - The continuity `segGroup` switches to the `SuggestBox` above 5 choices, as today.
  - The counts line trims, never overlaps.

## 4. Per-kind variations

### 4.1 Character and Team (shared)

| | |
|---|---|
| Overline | "Character" / "Team" |
| Main action | **Continue · <next unread label>** (first unread in reading order; "Read in order" when everything is read) |
| Secondary | Save as reading list · Show in Library · ⋯ Manage |
| Filter | Continuity `segGroup` with counts |
| Tabs | **Overview** · Appearances · Series · Events |

**Overview** (fixed and short; its size doesn't depend on how many issues there are):
1. **Often appears with:** a strip of shared entity tiles, top 12, caption "18 together".
   - Character: other characters and teams, ranked together by shared issues.
   - Team: characters first (its roster in practice), then other teams.
2. A two-column row:
   - **Events**: the first 3 rows in reading order, then "All N events →", which switches to the Events tab.
   - **Appearances by year** (§5.4) with **First appearance in your library** under it, as one row.

### 4.2 Location

| | |
|---|---|
| Overline | "Location" |
| Main action | **Read in order** |
| Secondary | Save as reading list · Show in Library · ⋯ Manage |
| Filter | Continuity `segGroup` |
| Tabs | **Overview** · Appearances · Series |

**Overview:**
1. **Who's been here:** characters and teams seen in this location's issues, top 12, caption "19".
2. **Events set here:** the event rows (§5.3), first 3 plus "All N →", which expands in place. There's no Events tab for a location.
3. **Seen by year:** the chart from §5.4.

### 4.3 Creator

| | |
|---|---|
| Overline | "Creator" |
| Main action | **Read in order** |
| Secondary | Save as reading list · Show in Library · ⋯ Manage |
| Filter | **Role chips** (`Button.filterChip`): All · Writer 48 · Cover artist 56 · Penciller 46… They narrow every tab. No continuity switch. |
| Tabs | **Career** · Credits · Series |

- **Role names** are readable, from one shared mapping: "CoverArtist" → "Cover artist", "Penciller", "Inker", "Colorist", "Letterer",
  "Editor", "Writer"; unknown values are split at their capitals.

**Career:**
1. **Runs** as a timeline:
   - One row per run (from `CreatorRunDetector`): the series name, then a bar across the creator's active years.
   - Bar colour is by role: `PbAccentBrush`, plus one secondary token for cover-only runs.
   - Clicking a bar opens a small flyout (`Border.dropdown`, `PopupFit.ToWindow`) with the run's range, "N of M read", and **Read this run** / **Save as reading list**.
   - A creator with no runs shows the plain list of series instead.
2. **Often works with:** collaborating creators, top 12, using the shared tile with a squircle crop, caption "48 issues".

- **Credits** is the Appearances tab under another name: the same `CoverThumb` grid, sorts and Unread toggle.
- **Series** is the shared poster grid.

### 4.4 Publisher

| | |
|---|---|
| Overline | "Publisher" |
| Main action | **Show in Library** (no Read in order: thousands of issues) |
| Secondary | ⋯ Manage (Refresh only, if anything; hidden when empty) |
| Filter | **Imprint chips**, from `Series.Imprint`, under the tab strip on the Series tab: All imprints · Marvel 380 · MAX 18… Hidden when no imprint data. |
| Tabs | **Series** · Continuities · Characters |

- **Series:** the shared poster grid (§5.2), then **Published by year** (§5.4) under it.
- **Continuities:** rows of the continuities its series belong to (brand mark, name, series count); a row opens the continuity.
- **Characters:** its 48 most-seen characters (by appearances in its issues), as shared entity tiles in a wrapping grid.

## 5. Shared pieces

### 5.1 Entity tile (`EntityTile`, new `UserControl`)

The one tile for every character/team/creator strip and grid. It's used on the entity page (Often appears with, Who's been here, Often works
with, a publisher's Characters) and on the Continuity page (Key characters, Key teams). The Event Map draws its faces on its own
surface and is not changed.

- Layout: art 76×100 (3:4, `PbRadius`) · name `pbTextMeta`, `MaxLines=2`, in a row **always two lines tall**, so every tile is the same
  height · count `pbTextCaption`. A `Shape="Squircle"` variant (76×76, `PbRadiusLg`) for creators.
- No border box. Hover lifts the art only (`translateY(-3px) scale(1.02)`, `PbMotionHoverMove` + `PbMotionSpring`), never the
  hit-tested Button.
- **No portrait:** a monogram (initials, `pbTextTitle`, accent text on `PbAccentSoftBrush`), with the first-appearance cover as a 26×34
  inset bottom-right (`PbRadiusSm`, the `PbCoverShadeColor` shadow). No cover either: monogram only.
- **Context menu** (the shared mechanism: `controls:ContextMenuHost.Provider` on the screen root, and the screen VM implements
  `IContextMenuProvider` and builds the entries for an `EntityTileItem` target): Open · Open on the map (character/team, when the host has a
  map) · Show in Library.
  The hover map button is removed.
- Keyboard: one focusable Button per tile; Enter opens it, the context-menu key opens the menu. The pad's Y is the `App.ContextMenu` action
  (no tile code needed).
- Bound to a small record `EntityTileItem(Kind, Id, Name, Caption, ImagePath, CoverKey, Initials)`. `OverviewEntity` and the new relation rows
  map to it.

### 5.2 Series poster grid (`EntitySeriesGrid`)

A `VirtualizingWrapPanel` of poster tiles: cover, series name (2 lines), a read-progress bar ("4 of 12 read" on the tooltip). It reuses the
poster pieces the Library already has (`PosterTile` / `CoverThumb`), not a new local style. It's used by Character/Team/Location/Creator Series tabs
and by Publisher.

### 5.3 Event row

`Grid ColumnDefinitions="36,*,Auto"`. The 36×44 thumbnail slot is always there: event cover if one is cached, else a monogram on
`PbAccentSoftBrush`, so every row lines up. Name `pbTextBody`, detail `pbTextCaption`: "2013 · Earth 616 · 3 issues". Empty parts are dropped
before joining, which fixes "2013 · · 1 issue". Context menu: Open event · Open on the map (character/team).

### 5.4 Year column chart (`YearColumnChart`)

- Columns of equal width, one per year that has appearances, filling the card width.
- 64 px tall: bars `PbAccentBrush` with `PbRadiusSm` top corners; first and last years as captions under the axis.
- A column's tooltip shows "2009 · 4".
- Not interactive, no animation (motion rules: no chart sweeps).
- Undated issues are left out of the chart, and the caption says "N undated".

## 6. Data

`EntityPageResolver.Build` stays the core query: name, profile, appearances, continuities, series, events, years. New relation queries sit
beside it in `Paperbunkr.Data/Metadata/EntityRelations.cs`:

| Query | Used by | Shape |
|---|---|---|
| `CoAppearances(kind, id, issueIds, take: 12)` | Character, Team | characters + teams in the same issues, ranked by shared-issue count, excluding self |
| `SeenHere(issueIds, take: 12)` | Location | characters + teams in the location's issues |
| `Collaborators(creatorId, issueIds, take: 12)` | Creator | other creators credited on the same issues, by shared-issue count |
| `CreatorCareer(creatorId, roleFilter)` | Creator | distinct issues per role (chips), runs with first/last year (timeline) |
| `PublisherCatalogue(publisherId, imprint)` | Publisher | series with owned/read counts and imprint, imprint counts, continuities, year counts, top 48 characters |

- Every relation query takes the **already-filtered** issue id list (continuity or role filter applied once), so the tabs and strips agree.
- They run in the same worker `Task.Run` as the page build, with a fresh context, inside the existing generation guard.
- No schema change and no migration.
- The resolver's `Roles` keep the raw value; the readable name is added in the view model.

## 7. View model

`MetadataEntityDetailScreenViewModel` stays the screen VM, keeping its routing, generation guard, profile completion, image picker and
reading actions. What changes:

- **`IEntityPageKind` profile** (one small class per variation: `CharacterTeamKind`, `LocationKind`, `CreatorKind`, `PublisherKind`) supplies:
  - the overline;
  - the image shape;
  - the counts line;
  - the main action (label + command);
  - which secondary actions show;
  - the filter mode (continuity / roles / imprints / none);
  - the ordered tab list.

  The view binds to these instead of `IsPublisher`-style flags.
- **Tabs:** `Tabs` (list of `EntityPageTab { Id, Label, Count, IsVisible }`) and `SelectedTab`. Each tab panel binds `IsVisible` and
  `controls:Replace.On` to its own shown flag.
- **New collections:** `OftenWith` / `SeenHere` / `Collaborators` (as `EntityTileItem`), `CareerRuns`, `RoleChips`, `ImprintChips`,
  `PublisherContinuities`, `PublisherCharacters`. `ByYear` becomes `YearColumns`.
- **Appearances:** `AppearanceSort` (ReadingOrder / Newest / Series) and `UnreadOnly`, applied in memory over the loaded appearances (no re-query).
- **Continue:** `ContinueLabel` / `ContinueCommand` pick the first unread issue in reading order within the active filter.
- **`HasRuns` fix:** it is computed from the creator kind *and* a non-empty list, and `CreatorRuns` is cleared on every kind change, not just on a full clear.
- **Collage:** `CollageKeys` feeds `HeroBand`, using the Q14 rules.

## 8. Elsewhere in the app

- **Continuity page** (`ContinuityOverviewView`): `EntityTileTemplate` becomes `EntityTile`. The `entityTileMap` hover button and its style
  are removed, and Open on the map moves to the tile's context menu (it was already wired to `OpenEntityOnMapCommand`).
- **Issue Detail** (`DetailBand`, `DetailTabs`): surveyed while planning, the entity chips **already** open the entity page
  (`DetailBandViewModel.EntityPill` and `DetailTabsViewModel.OpenCreator` route through `OpenEntity` → `GoMetadataEntityByName`), so Q19's
  click change is already in place. What's left is the menu: `TagPillContextMenuBuilder` gives a pill with an `EntityKind` the rows
  **Open <name>** and **Search library**, plus the weight rows when it can be reweighted. Chips for fields with no entity page keep their
  current menu.

## 9. Input, motion, theming

- **Input:**
  - Tabs come free through `TabStrip.Step`.
  - Shortcuts on the screen's existing `ScreenInput.Attach`: `Refresh` (F5) reloads the page. No new hotkeys.
  - Tiles and rows handle Enter on the focused item as their own key (CLAUDE.md "what stays a control's own key handler").
  - Tile-grid arrow navigation: if a virtualizing grid drops focus on arrows, take the directional move on the tunnel, as `WantedScreen` does, and test in `RealWindowKeyboardTests`.
- **Motion:**
  - Tab panels fade with `controls:Replace.On`.
  - The hero image and collage take no entrance of their own (the route transition covers them).
  - Grids pulse `PlayEntranceAnimation` only when the filter, sort or continuity changes.
  - Hover lift on tile and poster art.
  - The flyout gets `PopupMotion` automatically.
- **Theming:**
  - Tokens only. The scrim is `PbHeroScrimColor`, the inset cover shadow `PbCoverShadeColor`, and chart bars `PbAccentBrush`.
  - Readable text is `PbTextBrush` / `PbTextMutedBrush`.
  - It must look finished on default dark, Daylight and Matrix.
- **Type and spacing:**
  - The type classes from the design language; the 24 px gutter; cards pad 16.
  - Chips are `PbRadiusChip` squircles; no oval pills.

## 10. Error, loading, empty

- **Loading:**
  - The hero shows its name as soon as the page record arrives.
  - Tab content shows a one-line "Loading appearances…" (`pbTextMeta`) until the build returns.
  - A continuity, role or imprint switch keeps the old content dimmed (`IsEnabled=false`) until the new results land. This is the current behaviour.
- **Error:** `ErrorMessage` shows under the tab strip. The hero stays.
- **Empty:**
  - Zero appearances (a character only on placeholders): the hero, a note "No issues in your library feature <Name> yet", and the Series tab hidden.
  - Profile completion still runs.

## 11. Testing

- **Data (`Paperbunkr.Data.Tests`):**
  - Each `EntityRelations` query on a seeded context: ranking, self-exclusion, the `take` limit, and that the continuity/role filter flows through.
  - Creator counts are distinct issues.
  - Imprint counts ignore null or blank imprints.
- **View model (`Paperbunkr.App.Tests`):**
  - Each kind profile's tabs, main action and filter mode.
  - `HasRuns` is false on a location after a creator was shown (regression for §1).
  - Pluralisation of the counts line, and the event detail joining with no "· ·".
  - Readable role names.
  - Continue picks the first unread in the filter.
  - Unread-only and sort over the appearances.
- **View (headless):**
  - Tab switching keeps one scroller: there's no `ScrollViewer` descendant inside a tab panel.
  - A 1,000-appearance grid realizes fewer than 100 containers.
  - `EntityTile` heights are equal for 1- and 2-line names.
- **Render:** `FoundationStyleTests.WithFoundation` PNGs for each kind on default, Daylight and Matrix at 1366×768 (`PB_RENDER_OUT`).
- **Lint:** `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`.
- **Checklist:** `avalonia-pro-max/review-checklist`.
- **On screen:** the user checks Storm, Castle Doom, a creator and Marvel on their own library.

Run only the targeted classes (CLAUDE.md "Running the App tests"); the full suite stays for release.

## 12. Out of scope

- A parent-place relation for locations (Q16).
- Clickable chart columns, and entity relationships beyond co-appearance (family, rivals, team rosters from a provider).
- Restyling the Detail chips themselves (only their click target changes).
- Any change to profile completion providers or the image cache.

## 13. Working-tree note

`MetadataEntityDetailScreenView.axaml`, `MetadataEntityDetailScreenViewModel.cs` and `ContinuityOverviewView.axaml` carry uncommitted work from
the series-structure-and-discovery and continuity-map sessions (Runs card, map buttons). This redesign builds on that working copy and keeps
its behaviour: runs with Read/Save, and Open on the map. It doesn't revert it.
