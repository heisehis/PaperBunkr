# Library Redesign — Lenses, Two-Row Command Bar, One-Badge Covers, Inspector

## Background

The Library screen (`Views/LibraryScreen.axaml`, `LibraryScreenViewModel.cs`, `Views/LibraryToolbar.axaml`,
`Views/LibraryPreviewPanel.axaml`) is functionally rich but visually and structurally crowded. Reviewing a
screenshot of today's screen (`docs/assets/library.png`) and a state audit on 2026-10-04 found:

- The toolbar is one dense row (back/forward, Workspace dropdown, search + mode, Filter, View & Sort) with a
  second chip row that mixes filter chips with "Sorted: / Grouped:" summaries.
- There is no quick way to ask "what am I in the middle of?" or "what have I not started?". The nearest thing,
  the built-in "Currently reading" Workspace, is really "unread only, sorted by last opened".
- Series tiles can carry up to six overlays (publisher chip, language badge, unread count, dog-ear, numeric
  rating, continue button) with no formal precedence. The 2026-09-14 spec
  (`2026-09-14-library-visual-redesign-design.md`) promised a corner-slot system; a grep for `CornerBadgeKind`
  shows it never shipped.
- Library has no continue-reading surface; Home and Reading Lists do.
- A hardcoded `#B814161B` remains on `Button.continueReading` (`LibraryScreen.axaml:203`).

What the 2026-09-14 spec **did** ship and this spec builds on: three `LibraryViewMode` values (`PosterGrid`, `List`,
`DetailsTable`) with Panorama/Tiles as a cover-fit setting, and the `LibraryPreviewPanel` third column (preview
panel v2, `2026-09-26-library-preview-panel-v2-design.md`) with series / issue / idle / no-results states.

This design was reached by a grilling pass on 2026-10-04 (23 numbered decisions, all in this document) after the
user approved an interactive mockup. The mockup is a concept with generated placeholder covers; this document is
the authority where they differ. The mockup is at https://claude.ai/artifact/JmsuQFjQYsZqq5HeMZ24Fp.

## Goals

1. Add four **lenses** — All, Reading, Unread, Read — as a tab strip with live counts, so reading state is one
   click away.
2. Rebuild the toolbar as a **two-row command bar**: tools on row 1, state (lenses, filter chips, result summary)
   on row 2.
3. Give every cover **one badge by default** and a **fixed corner-slot system** for the opt-in extras, with a thin
   progress bar and a stack effect for multi-issue series.
4. Restyle the existing preview panel as an **inspector**: better resume target, series synopsis, an issue strip of
   number chips with a cover popup.
5. Add a **Continue reading strip** to the Library and restyle the contextual sidebar.
6. Do all of this with **no new skin-schema tokens**, no new input actions, and no lost user settings.

## Non-goals

- Smart or rule-based collections (separate thread, unchanged).
- New status colours or a status stripe on covers (declined; status shows only in the inspector's existing
  `SeriesStatusChip`).
- Gap detection (missing issue numbers) and Wanted/catalog "missing" issues in the inspector. Only
  files-missing-on-disk is shown; gaps and wanted items stay in Wanted and Library Health.
- Pinning a smart list as a lens tab. The tab strip is built so this is a later extension; it is not in scope.
- The Books screen. Sidebar changes to Reading Lists and Smart Lists screens.
- Changing bulk actions (`LibraryActionCatalog`, selection-actions row) beyond where the bar sits.
- New keyboard actions or `InputActionIds`. Everything uses existing ones (see Keyboard).
- True push-off sticky headers (would need a custom panel or a flat-row refactor).

## Decisions (grilling log)

| # | Decision |
|---|---|
| Q1 | One spec, four slices, each with its own plan. |
| Q2 | Three view buttons — Covers, List, Details. Poster/Panorama cover-fit and List density stay as toggles. |
| Q3 | Series \| Issues granularity stays as a segmented control in the command bar; lenses apply within it. |
| Q4 | Lenses are fixed and built in. Workspaces and Smart Lists are untouched. Pinning a smart list is a later extension. |
| Q5 | Sidebar stays shell-level and holds only All Series and Collections. Content type becomes a filter chip. |
| Q6 | One badge by default. The existing opt-in toggles remain, governed by the corner-slot system. |
| Q7 | Hover quick actions are mouse-only. Keyboard and gamepad use the context menu and the inspector's Continue. |
| Q8 | Lens names follow CE: All / Reading / Unread / Read. "Reading" means some read, some left. |
| Q9 | No status stripe. Status appears only in the inspector. |
| Q10 | Continue strip: comics only, up to 6 cards, unfiltered All lens only, scrolls with the grid. |
| Q11 | Inspector marks files-missing-on-disk only. |
| Q12 | Inspector synopsis is `Series.Summary`, falling back to the cover issue's summary. |
| Q13 | Command bar layout A (two rows). |
| Q14 | Issue strip option C (number chips plus cover popup). |
| Q15 | Lenses are mutually exclusive; counts sum to All. A "Has unread" chip keeps the old "has anything left" meaning. |
| Q16 | Old "Unread only" loads as the Has unread chip. "Currently reading" is redefined as Reading lens. User Workspaces untouched. Workspaces save the lens. |
| Q17 | Lens counts reflect scope + filters + search; zero-count tabs dim, not hide. |
| Q18 | Keyboard model below. No new actions. |
| Q19 | Responsive breakpoints are defaults to tune on screen. |
| Q20 | Existing installs keep every saved badge toggle. New installs: unread/finished badge and publisher label on, the rest off. Fixed corner slots. |
| Q21 | Slice order: 1 command bar and lenses, 2 tile/cover system, 3 inspector, 4 continue strip and sidebar. |
| Q22 | Sticky letter headers: one floating pinned header, no push-off, built last in slice 2 and droppable. |
| Q23 | A thin bottom progress bar replaces today's progress ring for in-progress series. |

## CE parity

Per the standing rule, checked against `_reference/ComicRackCE` (2026-10-04). CE-compatible: stack grouping,
alphabet group headers, docked preview/info panes, and the Read/Reading/Unread "Show only" filter semantics.
**Deliberate deviations:** lens tabs (CE exposes the same filter as a menu), the Library continue-reading strip
(CE has none; Quick Open is already a documented deviation), single-badge covers with corner slots (CE stacks
several overlays), the jump rail (CE has headers only), and the inspector's form. `docs/ce-feature-inventory.md`
has no rows for most of these, so slice 1 and slice 2 each add rows stating the deviation.

## Architecture

### Slice 1 — Command bar and lenses

**Row 1 (tools):** back/forward, Workspace dropdown, search box with mode dropdown (modes unchanged), Series |
Issues, the three-way view switch, Sort, Refresh, Add. The existing "View & Sort" popup remains the home of sort
axes, group axes, density, cover fit and the badge toggles; the **Sort** button opens its Sort tab.

**Row 2 (state):** lens tabs with counts, a divider, filter chips, and a right-aligned summary
(`N series · Title ↑ · A–Z`). The summary replaces the old "Sorted: / Grouped:" chips. The chip row scrolls
horizontally and never wraps.

**Filter chips:** Content type, Publisher, Has unread, Missing, Tracked. The old Filter dropdown is removed; its
Library-source list stays reachable through the View & Sort popup or a chip, to be settled in the plan (it holds no
decision, only placement).

**Lens semantics.** Series granularity, mutually exclusive:
- **Unread:** no issue of the series has been opened.
- **Read:** every issue is finished.
- **Reading:** everything else (some read, some left).

Issues granularity, per issue: **Unread** (not opened), **Reading** (opened, not finished), **Read**.
All is the sum of the three.

*Implementation check for the plan:* `SeriesCardSample.UnreadCount` today counts issues with
`LastPageRead` null or 0, so an opened-but-unfinished issue counts as "read" there. The lens predicate must use the
same notion of *finished* as `IsFinished`/`ReadFraction`. If `UnreadCount` cannot express it, add a finished count to
the card rather than redefining `UnreadCount` (the unread badge depends on it).

**Counts.** One pass over the in-memory cards, after scope (collection, content-type chip, other chips) and the
search text, tallies all four lenses at once. The lens itself is not applied to its own count. A tab at zero stays
visible, dimmed. The pass reuses the existing search debounce and must not add a database query.

**Persistence and migration.**
- New persisted setting `LibraryLens` (default All), added to the auto-persisted Library settings and to
  `LibraryWorkspaceState`. A Workspace saved without a lens loads as All.
- The existing `FilterUnreadOnly` setting is **kept** and becomes the storage for the **Has unread** chip. No value
  conversion is needed, so old saved state and old Workspaces behave exactly as before. This makes the migration
  lossless.
- The built-in "Currently reading" Workspace (`WorkspaceService.EnsureBuiltInsSeeded`) is redefined as
  `Lens = Reading`, sort Opened descending, `FilterUnreadOnly = false`. The seeder must update the existing built-in
  row on upgrade, not only insert; the plan must verify how it treats existing `IsBuiltIn` rows. It is the only
  built-in whose meaning changes. User-created Workspaces are never modified.
- If `LibraryLens` is stored as a new `AppSettings` column, the data change needs an EF migration, a Data.Tests
  migration test, and must follow the migration up-down-up testing guidance already in the project memory.

**View modes.** Toolbar buttons become Covers, List, Details. Panorama vs Poster and Comfortable vs Compact stay as
toggles inside the View & Sort popup. The derived `IsPanoramaGrid`/`IsTilesView` bindings are retired from the
toolbar.

**Keyboard.** Lens tabs use the `tab` class with `active`, so `TabStrip.Step` drives them from the bumpers and
Ctrl+PageUp/PageDown. They register no handlers of their own.

### Slice 2 — Tiles and covers

**Corner slots (fixed, per tile):**

| Corner | Content | Default (new installs) |
|---|---|---|
| Top-left | Publisher label (replaced by the selection checkbox while selecting) | on |
| Top-right | Unread count, or a check when finished | on |
| Bottom-left | Language badge | off |
| Bottom-right | Numeric rating, then dog-ear (hover-revealed) | off |

Existing installs keep every saved toggle value; only layout changes for them. The existing Preferences cosmetic
toggles and the "Continue reading button" toggle remain. That last toggle now controls the hover **Read** button.
Language is a cover-issue approximation (`SeriesCardSample.LanguageIso`), as today.

**Progress bar.** A thin bar along the bottom edge shows read fraction for in-progress series (Reading lens). It is
drawn in `TileCosmeticsOverlay` beside the binding spine, from `ITileProgressSource.ReadFraction`, inside the cover
clip. It replaces the hover/finished progress ring; finished is carried by the check badge. It is drawn above the
bottom of the cover and must not collide with the dog-ear.

**Stack effect.** Series with more than one issue show two peek cards behind the cover, drawn as **sibling Borders
before the cover Border** (4px and 8px offset, reduced opacity, no image or bindings), kept strictly inside the
existing 5px gutter or with `PosterCardHeight` grown by exactly the peek offset. They must never sit inside the cover
Border (its clip would cut them and the glow `BoxShadow` would fight them). They must not change measured tile
height, to avoid the "card too short, cover pushed out of bounds" failure recorded for the poster glow ring.
Single-issue tiles and Issues granularity draw no stack.

**Hover quick actions.** Read and Add-to-collection buttons appear on hover, built as `LazyPart`s with
`IsActive` bound to the cover's `IsPointerOver`, copying the `Button.continueReading` pattern. Mouse-only: not
reachable by keyboard (the rule against focusable controls inside cards). The card's `OnSeriesTilePointerPressed` and
`OnCardKeyDown` handlers must ignore events whose source is inside a nested Button; this is a required check.

**Colour.** Remove the remaining hardcoded `#B814161B`; badges and labels use `PbBadgeBrush`/`PbBadgeTextBrush`,
`PbSuccessBrush` and existing chip resources. No new skin tokens.

**Sticky letter header (last item, droppable).** One floating header overlays the top of the scroll area, bound to a
VM `TopVisibleGroupHeader` computed from `ScrollViewer.Offset` and the realized group containers (reusing the
letter-to-group mapping in `ScrollToLetterGroup`). The real header hides while pinned. There is no push-off. If the
hand-off flickers on screen, drop it and keep today's inline headers plus the existing jump rail.

### Slice 3 — Inspector

The existing `LibraryPreviewPanel` is restyled, not replaced. It keeps its four states, its persisted width
(`LibraryPreviewPanelWidth`), its visibility setting (`IsLibraryPreviewPanelVisible`, default true) and its rule of
hiding in Details view (`ShowPreviewPanelColumn`).

Series state shows: hero cover, name, content type, `SeriesStatusChip`, publisher, language; a progress bar with
"N of M read"; a **Continue** button; synopsis; facts; the issue strip; "Open full detail page".

- **Continue** resumes the most recently opened in-progress issue, falling back to the first unread. This reuses the
  logic already in `HomeFeedResolver.GetContinueReading`. The card gains a resume issue id and page
  (`Issue.OpenedTime`, `IsInProgress()`, `LastPageRead`), computed in `SeriesCardSample.FromSeries`.
- **Synopsis** is `Series.Summary` when set, else the cover issue's summary. The card carries the series summary.
- **Issue strip** is number chips that wrap (no sideways scroll). Read and next-to-read are styled distinctly;
  a file-missing issue gets a dashed border **and** an accessible name ("file missing"), so meaning is not carried
  by colour alone. Data comes from `IssueList.Rows` in memory (`Issue.FileIsMissing`).
- **Chip popup.** Hover or keyboard focus on a chip shows a cover and title using one **shared** Popup, repositioned
  per chip from code-behind, following `ComicHoverTooltipPopup` (a second shared popup, since that one is bound to
  `IssueListRow`). Closing it from inside a chip event must be deferred one dispatcher tick.
- Focusing a card or a strip card updates the inspector (current behaviour). Esc in the inspector returns focus to
  the card it came from.

### Slice 4 — Continue strip and sidebar

**Strip.** Comics only; up to 6 cards on one row, horizontal scroll if more. Each card shows a small cover, title,
"Issue N of M" (plus page when mid-issue), a progress bar and a play button. It is shown only when the lens is All,
there is no search text and no chip is active; it scrolls away with the grid and is not pinned. Source: the comics
half of `HomeFeedResolver.GetContinueReadingMixed` (series with an in-progress issue by `OpenedTime`, excluding
Dropped), cached and refreshed on library change, never run per tile. Enter on a card continues reading; the
context-menu key opens the series menu.

**Sidebar.** It stays the shell-level `ContextualSidebar` in `MainWindow.axaml` and is not restructured. It keeps
the header, All Series and Collections (accent dot, count, hover actions). The Content type group is **removed**
and replaced by the Content type chip in row 2. Restyle only: spacing and the accent tint, using existing tokens.

## Keyboard and gamepad

No new `InputActionIds`. Following the input-service design:

- Lens tabs: bumpers and Ctrl+PageUp/PageDown through `TabStrip.Step`.
- The strip's cards are normal focusable items; Left/Right move within the strip.
- Down from the lens tabs goes to the strip if visible, otherwise the grid. Up from the grid's first row goes to the
  nearest strip card by x position. This directional move is taken on the **tunnel**, as `WantedScreen` does,
  because `ItemsControl` handles arrow keys itself and drops focus on a virtualizing list.
- Hover quick actions and the chip popups are mouse-only conveniences; every action has a keyboard route (Enter,
  context-menu key, the inspector's Continue).
- The controller works unchanged (app-wide D-pad focus movement).
- Reaching a text box (search) by arrows only browses; Ctrl+F lands ready to type, as today.

## Responsive

Defaults to tune on screen, not requirements to the pixel:

- Below about **1100px** the inspector hides and a toolbar button reopens it as an overlay over the grid. Esc closes it.
- Below about **900px** the contextual sidebar collapses (existing `ShowContextualSidebar`), and row 1 drops, in
  order: Refresh, Forward, the Workspace button, then the search mode label, then moves Series | Issues into View &
  Sort. Row 2 stays and its chips scroll. The strip shows fewer cards rather than shrinking them.
- No minimum window width was found in `MainWindow.axaml`; the plan must check the real minimum before settling
  these numbers. Implementation is either `ContainerQuery` or size classes set from the window, per the
  `avalonia-pro-max/layout-patterns` guidance; the plan picks one and matches the repo's existing responsive code.

## Avalonia constraints checked

From `avalonia-pro-max/layout-patterns` and `review-checklist` and the 2026-10-04 feasibility check:

- Master-detail with an inline right panel is a supported pattern; collapse it on narrow windows.
- No animation of `Width`/`Height`/`Margin`; the inspector overlay uses opacity/transform only, hover and press
  feedback under 150 ms, and the reduced-motion path must be tested.
- No information by colour alone (missing files, status, finished state all pair colour with an icon, text or accessible name).
- No hardcoded hex; Light and Dark both verified; icon-only buttons need `AutomationProperties.Name` and a hit area
  of at least 36×36.
- Avalonia adorners clip to the adorned control; the focus-ring rules in `CLAUDE.md` apply to the new strip cards and
  chips. Do not add a second hand-rolled inner border on `:focus-visible`.
- A Button swallows Enter/Space and (Avalonia 12) left presses; strip cards that open on Enter keep a bound
  `Command` or tunnel from an ancestor.
- Any Remove/Close that runs inside a routed event from a row inside a Popup or list is deferred one dispatcher tick.
- Run `avalonia-pro-max/review-checklist` before calling each UI slice done.

## Testing

- **View model:** lens predicates and counts in both granularities (including opened-but-unfinished issues and the
  zero-count case); counts reflect scope and search; `LibraryLens` persists and is saved/applied by Workspaces;
  a legacy Workspace JSON without a lens loads as All and an old `FilterUnreadOnly` still filters; "Currently
  reading" is re-seeded to Reading without touching user Workspaces; resume target picks the most recent
  in-progress issue.
- **Data/migration:** if a column is added, a Data.Tests migration test with the up-down-up shape the project already
  warns about.
- **Headless view:** two-row bar present, one badge by default, corner-slot precedence, stack drawn only for
  multi-issue series, strip visible only on the unfiltered All lens, chip and strip card focus.
- **Real window** (`RealWindowKeyboardTests`): lens switching from the bumpers, strip-to-grid and grid-to-strip
  Up/Down, focus returning from the inspector. This class is needed because the arrow-key behaviour only
  reproduces in the real `MainWindow`.
- **UI automation (FlaUI):** give the lens tabs and chips `AutomationId`s and extend `LibraryToolbarDriver`.
  Running FlaUI repros needs the user's permission first.
- Existing Library test classes will need updating; the plan lists them. `Speed=Slow` classes stay tagged.

## Documentation

On landing each slice: update `wiki/The-Library.md`, `docs/paperbunkr-todo.md` (status, commit ref, what was verified),
and add `docs/ce-feature-inventory.md` rows for the deviations above.

## Out of scope / follow-ups

Pinnable smart-list lenses; status-colour tokens and a cover stripe; gap and wanted-issue marking in the inspector;
`Series.Language` as a real series-level field (language is a cover-issue approximation today); push-off sticky
headers.

## Addendum, 2026-10-04: the "View & Sort" popup was split up

After running the build the user found the three-tab popup redundant against the row-1 switches. Agreed in chat and built the same day:
the row-1 button is an icon-only **Display options** panel (cover style, cover size, "On the cover" switches, no tabs); **Sort** and
**Group** are popups on their row-2 chips, with the group chip always shown; Fade in covers, Cover tooltips and Smooth scrolling moved to
Preferences > Appearance. This supersedes the command-bar description above where they differ. Status and details:
[paperbunkr-todo.md](../../paperbunkr-todo.md), "2026-10-04 follow-up".
