# Library preview panel v2

Date: 2026-09-26. Follows the panel built from §4 of
[2026-09-14-library-visual-redesign-design.md](2026-09-14-library-visual-redesign-design.md). That
spec settled *where* the panel lives (third grid column, splitter, focus-driven, Ctrl+B toggle,
Details-table hiding) and those decisions stand. This spec replaces its **content and styling**
(§4's "four content states" bullets) and fixes two defects found on screen.

## Why

Viewed on screen on 2026-09-26, the panel is visibly unfinished:

1. **The series issue rail renders as thin coloured slivers.** `LibraryScreenViewModel.OnPreviewSeriesChanged`
   builds each `PosterRailItem` with only `CoverBrush` (the placeholder gradient) and never a
   `CoverImage`; `PosterRail` was written for Detail screens, which supply decoded bitmaps, and its
   cover `Border` has no width of its own, so without an intrinsic image it collapses to a bar.
2. **The hero cover is cropped.** `Border.previewCover` is a fixed 220px high with
   `Stretch="UniformToFill"` inside a ~320px-wide column, so a portrait cover is cut to a landscape
   strip.
3. **Most available data is not shown.** `SeriesCardSample` already carries status, reading status,
   direction, content type, total size, last added/opened and read fraction; `IssueListRow` carries all
   credits, characters/teams/locations, story arc, page count, dates, age rating and file path. The
   panel shows publisher, language, a synopsis excerpt and a few actions.
4. **Actions are incomplete against the v1 spec.** Series state has only Continue (v1 §4 lists
   Continue, Mark read, Add to Collection); there is no status chip.

## CE parity check (standing rule)

Verified against `_reference/ComicRackCE` on 2026-09-26. CE has **no built-in field-rendering info
panel**: View → Info Panel (Shift+F9) is an empty tabbed host that only user scripts populate
(`ComicExplorerView.cs:205-217`, `ComicPageContainerControl.cs:33-66`, `ScriptUtility.cs:200-272`),
and CE ships no default fields. The built-in equivalents are:

- The small **Preview pane** (`SmallComicPreview.cs`): page 0 of the *first selected book only*, and
  the text "Preview is only available for Books" when nothing is previewable.
- The **Book Info dialog** (`ComicBookDialog`, tabs Summary / Details / Plot & Notes / Catalog /
  Custom / Pages / Colors, fields set in `ComicBookDialog.cs:324-410`). This is the reference for
  which fields exist and how they group; the credit roles below are its Writer, Penciller, Inker,
  Colorist, Letterer, CoverArtist, Editor, Translator.
- **Multi-selection:** CE aggregates nothing in the preview; the Info command opens
  "Edit multiple Books" with tri-state "values differ" checkboxes (`MultipleComicBooksDialog.cs`).

So the panel is free to be designed rather than ported. **Deliberate deviations from CE:** a
series-level summary (CE has none anywhere) and a persistent, always-visible panel instead of
script-only. The multi-select behaviour (keep showing the focused item) *matches* CE's preview
pane, which uses only the first selected book.

## Scope

In: content and styling of the series and issue states, the two bug fixes, pinned action bars,
rail drill-in, collapsible sections, width bounds, backdrop and cross-fade, the multi-select strip.

Out (YAGNI): inline editing (star rating, tags) inside the panel; aggregated multi-select summary;
aggregated series credits (needs a per-series query and duplicates the Detail screen); tabs;
Books (EPUB/PDF) library, which has its own screen; CE's Catalog/Custom/Pages/Colors tabs.

## Design

### 1. Structure (unchanged from v1)

`Views/LibraryPreviewPanel.axaml` keeps its four mutually-exclusive states (idle, no-results,
series, issue) gated by the VM booleans `ShowIdlePreview` / `ShowNoResultsPreview` /
`ShowSeriesPreview` / `ShowIssuePreview`. It still updates on card **focus**
(`LibraryScreen.axaml.cs` `OnCardGotFocus`), not on selection. What changes is the layout inside the
series and issue states and one new VM concept (§5, the drill-in).

Each of the two content states is a two-row grid: a scrolling region (`ScrollViewer`,
`HorizontalScrollBarVisibility="Disabled"`) above a **pinned action bar** that is outside the
scroller, so the actions are never clipped or scrolled away (v1 §4 requirement, now enforced
structurally instead of by luck).

### 2. Series state (mockup option C, "backdrop hero")

Top to bottom:

- **Backdrop hero.** A ~170px band showing the cover, blurred, fading into the panel surface. The
  cover overlaps its lower edge at its natural aspect ratio (`CoverAspectRatio`, default 2:3), width
  about 35% of the panel, `Stretch="Uniform"` — never `UniformToFill`, never a fixed height. Title
  (display font, `PbDisplayFontFamily`) and the "N issues · M unread" meta line sit beside the
  cover's bottom edge.
- **Chip row:** content type (accent chip), publisher, language, series status
  (`SeriesStatusLabel`), reading direction (only when RTL). Chips use `PbRadiusChip`
  (never `CornerRadius 999`, per the squircle rule).
- **Progress:** a 4px bar bound to `ReadFraction`, with "6 of 10 read" beneath.
- **About** section: synopsis (`RepresentativeRow.SummaryExcerpt`); section hidden when empty.
- **Issues** section: the rail (§4), with read markers.
- **Details** section (collapsible, §6): total size, last opened, last added.
- **Pinned bar:** primary **Continue #N** (label becomes **Read from start** when nothing is read),
  **Mark all read / unread** (label follows `IsFinished`), **Add to Collection**, and a `⋯` overflow
  with **Open series page**.

### 3. Issue state (mockup option B, "collapsible sections")

Same header treatment as §2, with the issue's own cover. Top to bottom:

- **Back link** "← <Series>" — shown only when the panel was reached by drilling in from the rail
  (§5); absent when reached by grid focus.
- **Header:** series name (display font), "#7 · <Title>", "Mar 2026 · 32 pages".
- **Chips:** read state, publisher, language, age rating, rating (★ n.n). Empty ones are hidden.
- **Progress bar** bound to `ReadFraction`.
- **Summary** (open) — full `Summary`, not the truncated `SummaryExcerpt`, in a max-height region that
  scrolls with the panel.
- **Credits** (open) — key/value grid of Writer, Penciller, Inker, Colorist, Letterer, CoverArtist,
  Editor, Translator. Roles with no value are omitted; the section hides if all are empty.
- **Story** (collapsed) — StoryArc, Characters, Teams, Locations, Genre. Collapsed state shows a
  one-line hint (arc name or first character).
- **File** (collapsed) — Format, page count, file size, released date, file name, plus a **Reveal**
  link (`RevealIssueCommand`, already present at `LibraryScreenViewModel.RevealIssue`). Collapsed state
  shows "CBZ · 84 MB · <file name>".
- **Pinned bar:** primary **Read**, **Mark read / unread**, **Edit metadata**, and `⋯` overflow with
  **Add to Collection**, **Open Detail**, **Reveal in Explorer**.

### 4. Issue rail — Approach A: extend `PosterRail`

Chosen over a dedicated in-panel rail control (duplicates ~100 lines) and over a `ListBox` of covers
(more work, no gain).

- `PosterRailItem` gains `string? CoverKey` and `bool IsRead`. Both optional and additive; every
  existing Detail-screen rail leaves them at default and behaves exactly as today.
- `PosterRail`'s item template renders the cover through `AsyncCoverImage.SourceId="{Binding CoverKey}"`
  when `CoverKey` is set (the exact pattern every grid template and the hero already use), falling
  back to the existing `CoverImage` bitmap otherwise.
- `Border.railCover` gets an explicit `Width` (76) to match its `Button.railCard` — this is the fix
  for the collapsed slivers, and is correct for Detail rails too (their cards are already 76 wide).
- When `IsRead`, the cover shows a small ✓ overlay and reduced opacity.
- `OnPreviewSeriesChanged` sets `CoverKey = row.CoverKey` and `IsRead = row.IsRead` on each item.

### 5. Rail interaction and the drill-in

Clicking a rail cover **swaps the panel to that issue's preview** instead of opening the reader. Add
`LibraryScreenViewModel.PreviewDrillIssue` (nullable `IssueListRow`) and `PreviewDrillSeries` (the
series to return to):

- Rail click sets `PreviewDrillIssue`; the issue state renders `PreviewDrillIssue ?? PreviewIssue`,
  and shows the "← <Series>" back link.
- Back link clears `PreviewDrillIssue`.
- **Any grid focus change** (`OnCardGotFocus`) clears the drill-in, so the panel always returns to
  following the grid and never shows something the grid isn't focused on for longer than one
  interaction.
- Double-click on a rail cover, or the **Read** button, opens the issue via the existing
  `IssueList.OpenIssueCommand`.
- Series-granularity: the drill-in shows the issue state even though `IsSeriesGranularity` is true;
  `ShowIssuePreview` becomes `PreviewDrillIssue is not null || (IsIssueGranularity && PreviewIssue is not null)`,
  and `ShowSeriesPreview` becomes `PreviewDrillIssue is null && IsSeriesGranularity && PreviewSeries is not null`.
- The rail's own buttons stay `IsTabStop="False"` (v1 §4 focus-order rule is unchanged).

### 6. Collapsible sections

A small reusable `Controls/PanelSection` (header with hairline divider and chevron + content) used by
Details, Credits, Story and File. Open/collapsed state is persisted as **one** new
`AppSettings.LibraryPreviewCollapsedSections` string (comma-separated section keys, e.g.
`"story,file"`), shared by both states, default `"story,file,details"`. It is read once when the panel
is created and written on each toggle. This needs an EF migration; it must be created and verified
against the migration up-down-up antipattern and stale-`Designer.cs`-snapshot notes in memory before
merging. Fallback if the migration is unwanted: hold the state in the view model for the session only.

### 7. Multi-select strip

While `Selection.Count > 1` (or the series equivalent), a strip at the very top of the panel reads
"N selected · showing focused" with "Esc to clear". It is informational only; bulk actions stay in the
toolbar selection bar (v1 §4, unchanged). The strip is a third row above the scroller, so it never
scrolls away.

### 8. Width, backdrop and motion

- `AppSettings.LibraryPreviewPanelWidth` keeps its column and persistence. The default rises from 320
  to **340**; `MinWidth` **280**, `MaxWidth` **480**. Existing users keep their persisted value; only a
  fresh profile sees 340. The cover scales with the panel width.
- The backdrop is the same decoded cover with `Effect="blur(…)"`. **No blur when the cover is a
  placeholder** (no `CoverKey` decoded yet) — use the flat surface with the placeholder gradient at low
  opacity instead.
- On selection change the hero cross-fades in ~120 ms. **No animation during rapid focus changes**:
  if a second focus change arrives within the fade window, snap instead of restarting the animation,
  so the panel never lags the grid during arrow-key browsing.

### 9. Restyle

Every colour and radius comes from the existing tokens (`PbSurface*`, `PbBorderBrush`, `PbText*`,
`PbAccent*`, `PbRadius*`); no hex literals. Section headers use the shared uppercase-overline +
hairline pattern already used elsewhere in the app. Body text keeps the panel's existing
`previewSynopsis` size. After building, run `avalonia-pro-max/review-checklist`.

## Data notes

- Every field named above exists on `SeriesCardSample` / `IssueListRow` today (verified 2026-09-26);
  no new query is needed. `Summary` (full text) is on `IssueListRow`; the rail and per-issue data come
  from `IssueList.Rows`, already in memory.
- "Mark all read/unread" for a **single series** and "Add to Collection" for a **single series or
  issue** are used by the v1 spec but I did not find single-item commands by those names in
  `LibraryScreenViewModel` (the toolbar selection bar has the bulk versions, and
  `MarkIssueReadCommand` / `MarkIssueUnreadCommand` exist per-issue). The plan must confirm and either
  reuse the bulk path with a one-item set or add thin wrappers. This is a plan-level detail, not a
  design change.

## Testing

- View-model tests: `ShowSeriesPreview` / `ShowIssuePreview` truth table including the drill-in;
  drill-in cleared by a focus change; back link clears it; rail items carry `CoverKey`/`IsRead`;
  multi-select strip visibility; collapsed-section persistence round-trip.
- The headless test app loads no theme, so item controls cannot be rendered there. **The visual
  result — rail covers, hero crop, backdrop, cross-fade, pinned bars at narrow heights — needs an
  on-screen check by the user** and must be reported as unchecked until then.

## Risks

- `PosterRail` is shared with Detail screens. The change is additive, but the fixed cover `Width`
  should be checked on the Related / Continuity / Collection rails on screen.
- The blurred backdrop is one extra effect layer per selection; it reuses the already-decoded cover,
  but confirm no scroll-perf regression in the grid (see the 2026-09-19 scroll-smoothness work).
- Nested `Popup`/collection-mutation bug (CLAUDE.md, routed-event detach): the drill-in mutates a VM
  property from a rail button's own click. That only changes bound state and does not remove the
  clicked control, but the `⋯` overflow menu and any Add-to-Collection flyout must follow the
  `Dispatcher.UIThread.Post` pattern if they close themselves from a row inside them.

## Implementation notes (2026-09-26)

Where the built version differs from the design above, and why:

- **Panel width default stays 320** (spec §8 said 340). The column keeps `HasDefaultValue(320.0)`; changing a default on an
  existing column makes SQLite rebuild `AppSettings`, for a cosmetic 20 px. Only `MinWidth` moved (260 → 280).
- **No double-click on a rail cover.** The first click of a double-click already drills in, which hides the series state and its
  rail, so the second click never lands on the rail. Opening the issue is the issue state's **Read** button (spec §5 also listed
  double-click).
- **No `PanelSection` control.** The four collapsible sections use a header `Button` bound to `TogglePreviewSectionCommand` plus
  per-section open flags on the view model (`IsPreviewCreditsOpen` etc.) - simpler than a templated control for four uses. The plan's
  Step 3 was dropped.
- **Hero cover size:** fixed 118 px wide, height from the cover's own aspect ratio (`HeroCoverHeight`, derived from
  `PanoramaWidth`), so the whole cover shows. The spec's "~35% of the panel width" was not scaled with the panel.
- **Reload safety (not in the design):** after every reload the previewed row/card/drilled issue is re-pointed at its reloaded twin
  (`ReResolvePreview`); their state is init-only, so without this Mark read left the panel showing the old state.
- **Panel buttons act on the exact previewed item.** The tile commands route through `Selection.UnionForAction` and would also
  mark an unrelated multi-selection, so `MarkPreview*` commands were added.
- **Reduced motion:** the cross-fade is skipped when `MotionTokens.IsReducedMotion()`.
