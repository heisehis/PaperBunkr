# Continuity screen redesign — design

*Status: **built 2026-09-28, all five phases (the old screen is deleted), not committed, not yet seen on screen** - see "Implementation notes" at the end. Approved by the user 2026-09-28 (grilling rounds 1–3 plus the F1–F4 defaults, approach C, then design sections 1–6 one at
a time); the user asked for it to be written up and implemented straight away. Mockups from the brainstorming companion:
`.superpowers/brainstorm/486860-1790550014/content/round1…3*.html` (not shipped).*

## Goal

The Story Events screen ("Continuity" on the rail) grew one feature at a time: continuities, events, the Event Map, the continuity
map, the smart connector, the Story Event resolver and GCD series bonds. None of the recent work is visible on the pages
themselves - no event chronology, no series continuity, no read progress, no stats, and the loose ends (duplicate events, connector
suggestions, outside series) sit in sidebar blocks far from what they concern. Rebuild the whole section - sidebar, continuity pages,
event pages, timeline - around what the data can now say.

## Facts this rests on (verified 2026-09-28)

- **Today's screen:** `EventsScreen.axaml` (729 lines) plus the sidebar block in `MainWindow.axaml` (~lines 732–921), bound to
  `EventsScreenViewModel` split over seven partials (main 886 lines, Continuities 728, Timeline 399, Identity 226, EventGraph 202,
  Map 201, StoryEventSuggestions 78) - about 2,700 lines.
- **Outside contract:** `MainViewModel` uses about ten members (`LoadEvent`, `LoadContinuity`, `EnsureEventLoaded`,
  `ActiveEventId`, `ActiveContinuityId`, the sidebar refreshes, the two Select commands, `Reader`, `ChannelEventPublisher`);
  `EventsCardContextMenuBuilder` uses the two Select commands; the `MainWindow` sidebar binds to the lists. Plugins don't touch it.
- **Tests on the old view model:** `EventsScreenViewModelTests` (1,075 lines), `EventsScreenIdentityTests`, `EventsScreenMapTests`,
  `RoleDetectionUiTests`, parts of `ReadingScreenViewModelTests`, `NewEventOrContinuityViewModelTests` and
  `ContinuityMapViewModelTests`.
- **Remembered tabs:** `AppSettings.LibraryHealthTab` is a string column; the sidebar switch follows it.
- **Eras:** `ComicAge` has CE's five values (Platinum, Golden, Silver, Bronze, Modern).
- **Skins:** `ThemeService` maps `theme.json` colours to `Pb…Color` resources; `heroGradientStart`/`heroGradientEnd` already exist
  and the detail-screen heroes use them.
- **ComicRack CE** has no continuity or story-event concept (only `MetronInfo.cs` mentions arcs), so there's no parity to keep here;
  this whole screen is a Paperbunkr deviation.
- **Avalonia:** no built-in sticky headers (hence no pinned era headers); `ItemsControl` needs a `VirtualizingStackPanel` for long
  lists; gradient stops take `DynamicResource` colours per stop, so the hero fade can follow the skin.

## Decisions

### Scope (Q1, R1)

The whole section: the shared sidebar and its suggestion blocks, continuity pages and all their tabs, event pages, the New/Edit
dialogs, compare and merge, and the timeline.

### Sidebar (R2, R3, E7, E8)

- A **Continuities | Events** switch at the top, in that order, with counts. It remembers the last tab (`AppSettings.
  ContinuitySidebarTab`) and opens on Continuities the first time. Opening an event from a continuity page flips it to Events, and
  opening a continuity from an event flips it back.
- **Continuity rows:** the publisher logo (`BrandMark`, publisher family), the name, and the issue count faint on the right. The count
  is distinct issues across member series.
- **Event rows:** the name, with "2007 · 32" (start year · issue count) faint on the right. No logo, since events span publishers.
- **Kept:** the header's **＋** menu (New continuity / New event), the active-row accent bar, delete-on-hover with its two-click
  confirm, the right-click menu (`EventsCardContextMenuBuilder`, re-pointed), the empty-list text.
- **Suggestions & checks:** one row below the list, shown on both tabs, with a badge counting everything pending (story-event
  suggestions, possible duplicates, shared-universe suggestions). It replaces today's three sidebar blocks.

### Suggestions & checks panel (E9)

A full panel in the main area; opening it clears the sidebar selection.

- Three sections - story-event suggestions, possible duplicates, shared-universe suggestions - each with a heading and count, hidden
  when empty.
- Rows keep today's actions (Accept/Dismiss; Merge/Not the same/Check again), restyled as cards with the reason text. Accepting a
  story-event or shared-universe suggestion opens what it created.
- Header buttons **Check story events** and **Check Wikidata**, with their progress and last-result lines; both still report through
  the Activity Center.
- Empty state: an illustration, "Nothing to review", and the two check buttons.
- Row removal after Accept/Dismiss/Merge is deferred one dispatcher tick (CLAUDE.md routed-event rule).

### Hero band (R4, F4, E5)

One shared `HeroBand` control for both page types:

- A collage of up to 8 covers (member series for a continuity, core issues for an event), dimmed and blurred, fading into the page
  on the left through `PbHeroGradientStart/EndColor`. Covers load in the background; without them it's a plain surface.
- Title row: (publisher logo for continuities) name, and the **Overview | Map | Timeline** switch on the right.
- Stats line, chips, a description clamped to 3 lines with more/less, and a button row (primary **▶ Continue · <issue>**, an add
  button, **⋯ Manage**).

### Continuity page (Q2–Q7, R5, R7, R8, R9, F1–F3)

- **Tabs:** Overview (default, replaces Series) | Map | Timeline.
- **Stats:** "2000–2015 · 18 series · 514 issues · 20 events · 41% read". Years use GCD on-sale dates where matched, cover dates
  otherwise. A **GCD** chip shows when any member series is matched.
- **Buttons:** **▶ Continue** opens the first unread issue in continuity-map order and hides when everything is read; the reader
  then carries on in that issue's own order (F3). **＋ Add series** opens today's search-and-multi-select as a panel under the hero.
  **⋯ Manage:** Edit details, Compare & merge, Create reading list (publication order), Delete continuity.
- **Overview order (F1):** hero → Needs attention → Series → Events in order.
- **Needs attention** (only when something's there), each item a button:
  - "2 possible duplicate events" → Suggestions & checks at duplicates.
  - "3 event connections to review" → a flyout listing them with Accept/Dismiss.
  - "4 series from its events aren't in this continuity" → a flyout of those series, each with **Add**.
- **Series · N** with **Order: Automatic ▾ / Custom** in the section header (F2).
  - **Automatic:** runs. A run is series joined by Continuation relations (GCD and yours), oldest first, one row per run: a label
    ("Hulk · 1968–2026 · 5 series") then posters joined by → arrows. A continuation you don't own is a dimmed placeholder poster
    ("Hulk (2008) · Not in your library") that opens its comics.org page. Series in no run go under **Standalone**, by start year.
  - **Custom:** a flat poster wall in the saved order, rearranged by dragging (the arrow buttons go). This order drives map lanes and
    the reading-list export, as today.
  - **Posters (96 px):** read-progress ring, name, years. Hover: open, note, remove. The multi-select checkbox and bulk "Remove from
    continuity" stay.
- **Events in order · N** in the smart connector's order: years · name · "14 issues · 9 read" with a thin progress bar · **Map**.
  Clicking the row opens the event. Between linked neighbours a faint line says "↓ prequel of" / "↓ continued by".
- **Compare & merge** (Manage): an overlay with this continuity left, the chosen other right, shared series in the middle; a picker
  lists overlapping continuities with shared counts; **Merge into <other>…** keeps today's two-click confirm.
- **New/Edit dialogs:** same fields; publisher becomes a `SuggestBox` of the library's publishers with their logos.
- **Map tab:** today's continuity map, unchanged.

### Event page (E1–E6)

- **Tabs:** Overview (renamed from Members) | Map | Timeline.
- **Stats:** "2007–2008 · 32 issues (5 core · 24 tie-ins) · 9 series · 28% read", a **source** chip (*ComicVine*, *Metron*,
  *ComicVine + Metron* from its stored arc ids, or *Yours*) and an **in Earth-616** chip opening that continuity (first one plus
  "+2" with the rest in a flyout).
- **Buttons:** **▶ Continue · <issue>** (next unread in event order; the reader already stays in the event), **＋ Add issues**
  (today's search panel under the hero), **⋯ Manage** (Edit details, Detect roles, Delete event).
- **Overview, top to bottom:**
  1. **Follows ← This event → Followed by:** cards (name, years, read tick) built from Prequel/Sequel/Continuation relations with the
     existing direction rules; several on a side wrap; the middle card is this event in the accent colour. Hidden with no neighbours.
     A card opens its event.
  2. **Needs attention:** "3 role suggestions to review" → the **Needs review** filter; "6 issue suggestions" → expands and scrolls
     to Issue suggestions; "1 connection to review" → expands Related events; "Possible duplicate: X" → Suggestions & checks at that
     pair.
  3. **Reading list:** filter chips with counts - All · Core · Hide optional · Unread, plus **Needs review** when role suggestions
     exist. Rows as today (position, cover, title, "series · Aug 2007", role chip, inline role-suggestion Accept/Dismiss, ⋯ with Set
     role / Move up / Move down / Remove); the cover gets a read tick, or a progress ring when partly read. The multi-select bar
     (Remove, Set role, Clear) stays.
  4. **Related events** (folding): crossovers and other non-directional links with their source chip, **Connect an event…**, the
     Event chain. Directional links live in the strip above and aren't repeated.
  5. **Issue suggestions** (folding): as today, with the Dismissed sub-list and Restore.
- **Map tab:** today's Event Map, unchanged.

### Timeline (Q8 reversed by the user's "more cosmetics", R6)

`ContinuityTimelineViewModel`, logic moved over unchanged, for both page types:

- **Year histogram** across the top: a slim bar per year with the issue count; hover "2007 · 42 issues"; click scrolls to the first
  issue of that year. Empty years keep a slot so the axis stays true to time.
- **Era headers:** "Modern Age · 2000–2015" (plus the commonly cited range where it differs, as today), "142 issues · 58 read" and a
  progress bar.
- **Era colours:** each header gets a soft tint, a left edge and a timeline dot in its era colour. The dot keeps its meaning: filled
  while the era has unread issues, an outline once all are read.
- **Folding:** clicking a header folds its covers; remembered for the session, reset on switching continuity or event. No pinned
  headers (no native sticky headers in Avalonia).
- **Unchanged:** "Review inferred ages", the unread dot and "?" badge on covers, opening an issue on click.

### Colour tokens

- `PbEra{Platinum,Golden,Silver,Bronze,Modern}Color`, with `…Brush` and `…SoftBrush`. Defaults in `App.axaml` with light and dark
  sets (platinum grey, gold, silver, bronze; Modern follows the accent).
- Optional `theme.json` keys `eraPlatinum` … `eraModern`, read by `ThemeService` like `heroGradientStart`. Skins without them get the
  defaults; no skin needs editing.
- Everything via `DynamicResource`.

## Architecture (approach C: a new screen)

A new `ContinuityScreen` view and `ContinuityScreenViewModel` replace `EventsScreen` / `EventsScreenViewModel`. The rail entry and
`MainViewModel.Events` point at it. Instead of one 2,700-line view model it's a tree:

| Piece | Job |
|---|---|
| `ContinuityScreenViewModel` | Shell: the sidebar tab, what's selected, navigation, the open map, hosting Suggestions & checks. Keeps the members `MainViewModel` calls. |
| `ContinuitySidebarViewModel` | Continuities / Events lists with logos, counts, years; the Suggestions & checks count. |
| `ContinuityPageViewModel` | One continuity: Overview (hero, attention, runs, events), custom order, add/remove series, notes, compare/merge. |
| `EventPageViewModel` | One event: Overview (hero, neighbours, attention, reading list and filters), roles, add issues, related events and connecting, issue suggestions. |
| `SuggestionsChecksViewModel` | Story-event suggestions, possible duplicates, shared-universe suggestions, the two check buttons. |
| `ContinuityTimelineViewModel` | The age timeline plus era progress, colours, histogram, folding. |
| `ContinuityOverviewBuilder`, `EventOverviewBuilder` | Pure functions from database snapshots to runs, placeholders, stats, neighbours and attention items. |
| `HeroBand` | Shared templated/user control for the hero. |

The Event Map (`EventMapViewModel` / `EventMapView`) and the data layer are reused unchanged.

**Porting rule:** working logic (role detection, suggestions, connecting events, identity checks, merge, notes, the routed-event
deferrals) moves over with its behaviour intact; only UI and state are rebuilt. Every old test's intent is carried to the new view
models; none are dropped silently. The old view and view model are deleted only once the new screen passes everything.

### Data added

- `AppSettings.ContinuitySidebarTab` (string, nullable; "continuities" / "events") - migration `AddContinuitySidebarTab`.
- Nothing else: runs, stats, neighbours and attention are computed live.

## Loading, errors, speed

- Each page's heavy part - the overview (stats, runs, events in order, attention, Continue target, collage) - is computed off the
  UI thread through a runner seam (synchronous in tests). The editable lists (members, series, suggestions) load as they do today,
  so every edit still reflects immediately.
- **Automatic / Custom** is a display choice for the Series section, remembered per continuity for the session. The stored series
  order (`ContinuityMembership.SortOrder`) keeps driving map lanes and the reading-list export; dragging in Custom writes it.
- A load failure shows an inline error with **Retry** and raises an Activity Center alert, as the Event Map does.
- Partial data degrades quietly: no covers → plain hero; no GCD data → no placeholders, no GCD chip, cover-date years; no events →
  the Events section hides and Continue follows publication order.
- Stats and counts come from aggregate queries, not loading every issue; sidebar counts are one grouped query per list.
- The Standalone wall and the reading list virtualize at 100+ items.

## Testing

- **Builders (pure):** runs (chains, branches, cycles, placeholders with and without a GCD store), stats, attention items,
  neighbours, source label.
- **View models:** the sidebar switch saves and restores the tab and counts are right; Suggestions & checks keeps its deferred
  removals; the page view models cover Continue, filters, custom order and compare/merge.
- **Parity list:** every test in `EventsScreenViewModelTests`, `EventsScreenIdentityTests`, `EventsScreenMapTests`, the screen parts of
  `ContinuityMapViewModelTests`, `RoleDetectionUiTests` and the `ReadingScreenViewModelTests` bits is mapped to where its intent lands
  in the plan.
- **Headless views:** hero with and without covers, the sidebar switch, the timeline histogram jump.
- **Migration:** `AddContinuitySidebarTab`, forward-only (per the up-down-up antipattern note).
- **Review checklist:** `avalonia-pro-max/review-checklist` on the new views before calling it done.

## Rollout

One plan, five phases; the app keeps working at each:

1. Shell, sidebar, Suggestions & checks, builders.
2. Continuity page.
3. Event page. At the end of phase 3 the rail switches to the new screen; the user checks it on screen.
4. Timeline polish.
5. After the user's OK: delete `EventsScreen.axaml`, the seven `EventsScreenViewModel` files and their old tests; update the wiki
   page, the roadmap and the notes in the older specs this replaces.

## Out of scope

- A reader mode that pages through a whole continuity (F3).
- Pinned era headers.
- Changes to the Event Map, the continuity map layout, the resolver, the connector or GCD matching.

## Implementation notes (2026-09-28)

Built from 2026-09-28-continuity-screen-redesign-plan.md. The user asked for every phase in one go ("don't stop until you're done
with phase 5"), so the on-screen check at the end of phase 3 didn't happen before the old screen was removed; it's still owed.

- **Where things live:** ViewModels/ContinuityScreenViewModel.cs (shell), ContinuitySidebarViewModel, SuggestionsChecksViewModel,
  ContinuityPageViewModel, EventPageViewModel, ContinuityTimelineViewModel, ContinuityScreenNavigation;
  Services/ContinuityScreen/ (builders and loaders); Controls/HeroBand.cs + Styles/ContinuityChrome.axaml; views
  ContinuityScreen, ContinuityHero, EventHero, ContinuityOverviewView, EventOverviewView, SuggestionsChecksView,
  ContinuityCompareOverlay (on the shared OverlayShell), ContinuityTimelineView; the sidebar stays inline in MainWindow.axaml
  so its right-click menu keeps MainViewModel as provider.
- **Deviations and choices made while building:**
  - Directional links (Prequel / Sequel / Continuation) leave Related events for the Follows / Followed by strip, so the strip's
    cards carry **Unlink** (right-click) - otherwise a wrong inferred link couldn't be removed.
  - Manage → Delete (continuity and event) now uses the same two-click confirm as the sidebar row (the review checklist flags
    unconfirmed destructive actions; the old menu deleted on one click).
  - New continuity via the dialog reuses an existing one with the same name, case-insensitively (the intent of the old sidebar
    command's test; the dialog used to create a duplicate).
  - Row-button reloads (add/dismiss/restore a suggestion, move/remove a member, unlink) now wait one dispatcher tick - the old
    screen did several synchronously, the CLAUDE.md detach-crash pattern.
  - The Standalone and Custom poster walls use the Library's VirtualizingWrapPanel (fixed 108×222 cells); runs keep a WrapPanel.
  - Custom order is remembered per screen instance for the session (a static version leaked between instances).
  - The old series-family / whole-library / character-aware timeline scopes are gone (unreachable since 2026-08-28).
- **Deleted:** EventsScreen.axaml(.cs), EventTimelineView.axaml(.cs), the seven EventsScreenViewModel*.cs, EventsScreenMode,
  TimelineScope, and the old tests EventsScreenViewModelTests, EventsScreenIdentityTests, EventMap/EventsScreenMapTests, plus
  the three screen tests in ContinuityMapViewModelTests. EventsScreenViewModel.Identity.cs, .Map.cs and the two identity/map
  test files had never been committed, so they can't be restored from git; their behaviour and tests were ported first.
- **Verified:** App tests for everything touched - 423/423 after the deletion (new screen 87 incl. 4 headless view tests, theme,
  dialog, role detection, Event Map / continuity map, context menu, MainViewModel, Quick Open, Reading). A full App run before the
  deletion: 4,660/4,679; the 19 failures were outside this work (tests that locate the repo or load pdfium from the output folder,
  which a scratch OutDir breaks, and five timing tests that passed when re-run). Data: the migration test, 2/2.
- **On screen (the user, 2026-09-28):** the continuity and event pages, Map, Timeline, the sidebar and Suggestions & checks render and
  work. Fixes that came out of it:
  - The hero was far taller than its text: the cover collage's natural height sized it. The collage now sits in a zero-size Canvas.
  - Publisher logos printed their name beside the mark (`ShowText` defaults on) - now off; and a dark logo (Image) vanished on the
    dark sidebar - logos now use `EnsureContrast` (a plate picked from their own pixels), shown only when a real logo exists.
  - **Crash:** opening Related events / Needs review overflowed the stack (0xc00000fd, no crash log). `Run.Text` binds TwoWay by
    default, so `<Run Text="{Binding SourceLabel, StringFormat=' · {0}'}"/>` wrote the formatted text back and reformatted it forever.
    Every formatted Run is now `Mode=OneWay` (also CollectionPropertiesOverlay and CompareScreen, which had the same trap).
    Regression test: `ContinuityScreenViewTests.EventPage_OpeningRelatedEvents_AndTheNeedsReviewFilter_LaysOut`.
  - The attention flyouts were clipped: Fluent's flyout caps content near 456px, so they're now 400px wide, wrap, and virtualize.
  - The year histogram's bars widen for short spans (48px up to 5 years … 7px past 60) and year labels thicken to match.
- **Not verified:** drag-to-reorder, the compare overlay, era colours in a light skin, and keyboard-only / screen-reader passes.