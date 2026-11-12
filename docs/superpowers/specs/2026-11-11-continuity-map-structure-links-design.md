# Continuity map M5 "Structure links" — design

*Status: approved by the user 2026-11-11 (grilling round 4, Q38–Q42, with a visual-companion screen). Source: slice M5 of the
[continuity map pitch](2026-11-11-continuity-map-pitch.md) (items 16–19). Builds on [M1](2026-11-11-continuity-map-room-design.md) and uses
the Series structure work ([D1–D3](2026-11-10-series-structure-and-discovery-design.md)). **Prerequisite: D1–D3 committed** — they are
untracked in the working tree as of 2026-11-11. Implemented in the working tree at base HEAD `28e60a1` on top of the still-uncommitted
D1–D3 code (2026-11-12, at the user's request); on-screen review pending.*

## Goal

Bring the new character, creator and arc data onto the map: filter it by a character or team, show character faces on cards, colour
cards by writer or artist with creator-run brackets, draw story arcs inside lanes, and link the entity pages to the map.

## Facts this rests on (verified 2026-11-11, working tree)

- **Indexes:**
  - `CharacterAppearance { CharacterId, IssueId }` and the matching `TeamAppearances`, `LocationAppearances` and `CreatorCredits` (with
    Role).
  - `EntityIndexBulk.Sync` keeps them current.
  - There is no "issues featuring X" helper; the pattern is `context.CharacterAppearances.Where(a => a.CharacterId == id).Select(a =>
    a.IssueId)` (`EntityPageResolver.cs:52`).
- **Names → ids:** `EntityNameLookup.Find(context, kind, name)` and `FindOrIndex` (`Metadata/EntityNameLookup.cs:7, 35`).
- **Images:**
  - `EntityChipImages.Get(kind, name)` / `Resolve(...)` are keyed **by name** (Character, Team, Creator), with an `event Changed`.
  - `EntityImageResolver.Choose(...)` gives custom > provider > first-appearance cover > initials.
  - `EntityImageCache.Load(path)` loads a cached image.
- **`MainCharacterOrTeam`** is an Issue text field (`Entities/Issue.cs:265`), **not indexed** as an entity.
- **Detectors:**
  - `CreatorRunDetector.Find(IEnumerable<Issue> seriesIssues, IEnumerable<CreatorCredit> credits, string role) → CreatorRun(CreatorId,
    CreatorName, Role, Issues, CreditedCount)`, needing 6+ credited issues, allowing up to 2 fill-ins, placeholders ignored.
  - `SeriesArcDetector.Find(IEnumerable<Issue> seriesIssues, ContentType) → SeriesArc(Name, Source, Issues)`.
- **Entity pages:**
  - `EntityPageResolver.Build(context, kind, id, continuityId?)` produces `EntityPageContinuity(Id, Name, Count)` and
    `EntityPageEvent(Id, Name, CoverUrl, Year, Continuity, Count)`.
  - `MetadataEntityDetailScreenViewModel` maps them to `EntityPageLink(..., OpenCommand, ...)`, with `OpenContinuity` / `OpenEvent`
    hooks.
  - The continuity Overview's `KeyCharacters` / `KeyTeams` are `OverviewEntity(Kind, Id, Name, Count, ImagePath, CoverKey)` with an
    `OpenEntity` command.
  - Navigation: `MainViewModel.GoMetadataEntityDetail(kind, id, continuityId?)` and the `WireSeriesStructure()` wiring.
- ComicRack CE has character/team/arc fields and groupers but no pages or map; this is a deviation.

## Decisions

| # | Decision |
|---|---|
| Q38 | A **Featuring** picker of the map's characters and teams, most frequent first; dims the rest, with a "Hide others" switch. |
| Q39 | Faces come from the issue's `MainCharacterOrTeam`, looked up as a character then as a team; no face when neither resolves; shown at Standard and Covers. |
| Q40 | **Colour by** Lane \| Writer \| Artist \| Read. The top 6 creators get the lane palette, the rest are muted. Run brackets show with Writer/Artist. |
| Q41 | Arc brackets at Standard and up: on by default on continuity maps, off on event maps. |
| Q42 | "Open on map" from entity pages (Continuities and Events cards) and the Overview's Key characters/teams strips, opening with Featuring set. |

## Design

### 1. Featuring

**Data (pure loader, `EventMapEntityLoader.Load(context, issueIds) → EventMapEntities`):**
- For the map's issue ids, count `CharacterAppearances` and `TeamAppearances` per entity.
- Return `(Kind, Id, Name, Count, IssueIds)` sorted by count, then name, plus the creator credits for Colour by (§3).
- It runs off the UI thread with the map load, and again only when the issue set changes.

**UI:**
- **Picker:** a "Featuring" `SuggestBox` (strict) in the toolbar, listing "Wolverine · 12" entries with characters first, then teams, and
  a cached 20 px squircle image when `EntityChipImages` has one. Clearing it turns the feature off.
- **Hide others:** a `Button.secondary` toggle next to it, shown only while Featuring is set.
- **Effect:**
  - Cards whose issue doesn't feature the entity get the M1 dimmed state (opacity 0.25, the same animation).
  - Under Hide others, they're removed before layout, like a filter: freed columns close up, and the Matrix and List (M3/M6a) follow.
  - The status line adds "featuring Wolverine · 12 issues".
- **Lifetime:** the session, per map. Arriving through "Open on map" (§5) sets it.

### 2. Character faces

- **Source:**
  - Take the issue's `MainCharacterOrTeam`, the text before the first `,` or `;`, trimmed.
  - Resolve it with `EntityNameLookup.Find(context, Character, name) ?? Find(context, Team, name)` in one batch per map load (off the UI
    thread).
  - Then pick the image path with `EntityImageResolver.Choose`, without the first-appearance-cover and initials fallbacks: a face appears
    only when there is a real character/team image.
- **No fetching:** nothing is downloaded from the map. Images that `EntityChipImages.Changed` reports later are picked up and invalidate
  the canvas.
- **Drawing:**
  - Standard: an 18 px squircle (`PbRadiusSm`) at the card's top-right, inside the frame.
  - Covers: 24 px at the bottom-left over the cover, with a 1 px `PbBgBrush` ring.
  - Not drawn below Standard.
  - The face is part of the card's peer name ("…, featuring Wolverine").
- **Memory:** decoded at 2× the drawn size and kept in the M1 cover source's LRU, sharing its cap.

### 3. Colour by and creator runs

**The control:** a "Colour by" `SuggestBox` (strict, 140 px) in the map toolbar, after the filters: Lane (default) / Writer / Artist /
Read state. It lasts for the session, per scope. Below 1000 px window width it moves into the toolbar's More menu as a submenu.

**Writer / Artist:**
- *Credits:* from `CreatorCredits` for the map's issues. Writer is role Writer. Artist is Penciller or Artist combined, the same rule as
  D2's combined artist role.
- *Colours:*
  - A card's colour is its *first credited* creator in that role.
  - The six creators with the most cards get the lane palette (`EventMapPalette`, the six lane brushes in order).
  - Everyone else, and uncredited issues, get `PbTextFaintBrush` bars (icons-and-dividers token, so allowed for a bar).
  - The lane fills keep their alternating backgrounds, so lanes stay readable.
- *Legend:* a one-line legend under the toolbar ("■ Claremont 42 · ■ Byrne 18 · … · ■ others"), each entry a hit target that sets
  Featuring-style dimming for that creator. It's a `WrapPanel` of `pbChip`s with colour squares drawn as 10 px `Border`s.

**Read state:** the bars use `PbSuccessBrush` for read, `PbAccentBrush` for in progress and `PbBorderBrush` for unread. There's no legend
(the read glyph already says it).

**Run brackets** (Writer/Artist only):
- `CreatorRunDetector.Find(laneIssues, credits, role)` runs per lane on the lane's own member issues.
- On event maps lanes are short, so runs (6+ issues) mostly appear on continuity maps, where lanes are whole member series. This is
  intended.
- A run draws as a 2 px bracket in the creator's colour, 6 px under the cards it spans, labelled "Claremont · #129–#143" at Standard and
  up (trimmed). When the run's cards are not contiguous in columns (other blocks in between), it draws one bracket per contiguous stretch
  and labels the first.
- Brackets are hit-testable: hovering shows "Chris Claremont · writer · 15 issues, 9 read"; clicking sets the legend dimming for that
  creator.

### 4. Arc brackets

- **Source:** `SeriesArcDetector.Find(laneIssues, contentType)` per lane: Story Arc field first, then title patterns for comics.
- **Drawing:**
  - A 1.5 px `PbTextMutedBrush` bracket under the lane's cards (below run brackets when both show), labelled with the arc name and
    "· from titles" when it was inferred from titles.
  - Standard and up only; labels trim.
  - Non-contiguous stretches draw as separate brackets, the label on the first.
  - Hovering shows "{Arc} · #129–#138 · 4 of 10 read".
- **Toggle:** "Arcs" (`Button.secondary` toggle) in the toolbar. Default on for continuity scope, off for event scope. It lasts for the
  session, per scope.
- **Display only:** nothing is written to `StoryArc`, matching D3.

### 5. Entry from entity pages

- **Entity page cards:** each row in the Continuities and Events cards gets a secondary action, "Open on map" (`iconButton sm`,
  FluentIcon Map, named "Open {name} on the map").
  - It calls a new `MainViewModel.GoContinuityMapFeaturing(scope, scopeId, kind, entityId)`, which opens the Continuity screen on that
    continuity or event, sets the detail view to Map and sets Featuring.
  - It reuses the existing open-continuity / open-event paths, then the map view model's `SetFeaturing(kind, id)` once the map has
    loaded, deferred through `Dispatcher.UIThread.Post`.
- **Continuity Overview:** each Key characters / Key teams tile gets the same action in its context menu (shared `MenuFlyout` mechanism)
  and as a small overlay button on hover/focus. The existing click still opens the entity page.
- **History:** the navigation entry records the featuring entity, so Back/Forward restores it, like the entity page's own continuity
  selection.

## Testing

**Pure:**
- `EventMapEntityLoader`:
  - counts per character and team
  - sort order
  - the issue sets
- Face resolution:
  - first segment of `MainCharacterOrTeam`
  - character before team
  - no face without a real image
- Colour by:
  - top-6 assignment
  - first credited creator
  - artist = penciller ∪ artist
  - the uncredited bucket
- Run and arc brackets:
  - contiguous stretches split across blocks
  - the label on the first stretch

**Headless:**
- Featuring:
  - dims non-featuring cards
  - Hide others closes columns
  - the status text
- Faces appear when `EntityChipImages.Changed` fires later.
- The legend click dims the other creators.
- Bracket hover tooltips.
- The Arcs toggle defaults per scope.
- "Open on map" from an entity page and from a Key character tile lands on the right map with Featuring set (after `Drain()`), and Back
  restores it.

**UI checks:** the lint set; renders of Colour by Writer with run brackets and arc brackets on a continuity map, and faces at Standard and
Covers, in three themes; the review checklist.

## Risks

- **Depends on uncommitted D1–D3.** The entity index must be built (`EntityIndexVersion` current) or Featuring is empty. When the index
  isn't ready, the picker shows "Character index still building" and stays disabled.
- **`MainCharacterOrTeam` is free text.** Faces simply don't appear when it doesn't resolve; nothing is guessed from other fields.
- **Colour clutter.** Colour by is off (Lane) by default, and the "others" bucket keeps it to seven visible colours.

## Implementation notes (2026-11-12)

Built directly from this design on top of the uncommitted Series structure D1–D3 code. Code: `Services/EventMap/EventMapStructure.cs`
(`EventMapStructureLoader`: appearances, first-credited writer and artist, faces, runs and arcs per series, loaded with each map),
`ViewModels/EventMapViewModel.Structure.cs`, `Controls/EventMap/EventMapCanvas.Structure.cs`, the toolbar/legend in `EventMapView.axaml`,
"Open on map" in `MetadataEntityDetailScreenView.axaml` (Continuities and Events rows, characters and teams only) and on the Overview's
Key characters / Key teams tiles (`ContinuityOverviewView.axaml`, shown on hover and keyboard focus). Where the build differs:

- **The Featuring picker shows "Name · count" labels**; the status line text sits beside it in the toolbar rather than in the slim header.
- **Colour by** applies at Chips and larger; Dots keep lane colours (Featuring dimming does reach Dots). It isn't moved into the More menu
  on narrow windows; the toolbar wraps instead.
- **Legend chips are `filterChip`s**; clicking one (or a run bracket) highlights that creator by dimming everyone else; again to clear.
- **Bracket labels:** a run's label sits at its start; an arc is labelled only on lanes with no run, the rest show their name on hover.
- **No context-menu "Open on map"** on the Overview tiles: the overlay button covers mouse and keyboard.
- **History doesn't record Featuring.** Back returns to the map without it.
- **Faces** decode at 48 px per canvas from the cached image files; they refresh when `EntityChipImages.Changed` fires.
- **Verified:** `EventMapStructureTests` (10: face name, Featuring dim / Hide others / clear, Colour by top six + others + read state +
  lane, highlight via legend and run bracket, bracket stretches and labels, arcs off on event maps, Featuring set before load applies after,
  faces and the index-building note, and a throwaway-database loader test of appearances, first credits and a seven-issue writer run).
  Not verified: the entity-page and Overview entry points by test or on screen, the look of brackets and faces on screen.
