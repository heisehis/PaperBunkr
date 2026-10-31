# Books section redesign — design

Date: 2026-10-27 · Status: **built, uncommitted** (all three slices, confirmed 2026-10-28; reader and Detail fixes user-verified on screen) · Scope: Books screen, Book Detail, EPUB/FB2/MOBI reader, PDF reader

## 0. Why and how this was decided

The Books section (browse → Detail → reader) still has the look it had before the 2026-10-19/20 UI polish and the newer redesigns of the
Library, Reading Lists, Home and Insights. The user asked for "a totally new spin" that fits the current design language
(`.claude/skills/paperbunkr-design-language`). The design was settled in four grilling rounds with the visual companion. The mockups
are in `.superpowers/brainstorm/1636-1793119543/content/` (`directions.html`, `mix.html`, `details.html`, `clusters.html`); that folder
is git-ignored and local only.

**What this redesign builds on:** this is a restyle, not a rebuild. Every feature from Books Slices 1–4 and the paged/scroll reader modes
(all built 2026-10-27; specs `2026-10-26-books-slice1-*`, `2026-10-27-books-slice2a-*`, `-slice2b-*`, `-slice3-*`, `-slice4-*`,
`-books-reader-paged-scroll-modes-*`) stays. This spec decides where each feature lives and what it looks like. It adds no reading
features of its own.

**ComicRack CE parity:** CE has no prose reader, so the standing CE-parity rule does not apply (this was already confirmed for the Novels
work in 2026-08).

## 1. Identity: the Reading Room with shelves

The direction is a mix of two concepts from round 1:

- **A, the Reading Room.** The section is about *where you are in a long read*: a "Now reading" spread with the line you stopped at, a Detail
  page that reads like a book jacket, and a quiet paper reader.
- **B, the Shelf.** Physical cues: face-out covers and spines on boards, a wash in the book's spine colour, the series shown as spines, and
  page edges that show how far through the book you are.

Rules:

- **Book titles are serif.** A new token, `PbBookTitleFontFamily`, is defined in `Styles/AppTokens.axaml` as
  `avares://Paperbunkr.App/Assets/Fonts/#Source Serif 4`. It is fixed like `PbDisplayFontFamily` and **does not** follow the Preferences
  body-font override. A shared modifier class `bookTitle` in `Styles/Typography.axaml` sets this family plus SemiBold, and is combined with
  the existing size classes (`pbTextBody` 13 on cards, `pbTextHeading` 22 on the spread, the 42 hero on Detail). No new font sizes; the
  mockups' 28/32 px titles were off-scale and become 22/42.
- Bebas (`pbTextHeading` / display) stays for the screen title "Books" and for big numbers.
- Everything else follows the design language: `Pb*Brush` tokens only, `DynamicResource` radii, the shared control families, the 4 px grid,
  the 24 px gutter, `DialogCard` for dialogs, `PopupFit` and a ScrollViewer for dropdowns, the motion roles and FluentIcons only.

## 2. New shared controls

Each control is built in the first slice that needs it (approach 1, "restyle in place").

| Control | Where | What it does |
|---|---|---|
| `Controls/Books/BookCoverCard` | Books grid, Detail Series mode, Detail "More by", nightstand | Cover (`SharedElement` key `book:{id}`) with the **one-badge rule**: a progress bar along the bottom edge while Reading, otherwise one corner badge (Finished ✓ in `PbSuccess`, Want to read and Abandoned in `PbBadge*`). Hover shows `StarRating`; the select checkbox appears on hover and stays visible while anything is selected. Under the cover: the title (`bookTitle` 13, two lines), the author (`pbTextCaption`), and a **status caption** ("37% · 2 h left", "Finished · ★★★★★", "Want to read"). A ghost variant ("#4 missing", dashed, can't be focused). Replaces BooksScreen's and BookDetailScreen's local copies of `bookCover`, `ghostCard` and `tileSelect`. |
| `Controls/Books/SpineShelf` | Bookshelf view, Detail series shelf | Spines on a board (`PbSurface3` board with a fixed shadow layer). Spine colour comes from the existing `book:{id}` spine-colour key, falling back to `PbAccentSoftBrush`. Ghost = dashed outline spine (fixes today's bug where Bookshelf-by-Series draws ghosts as real spines). Reading progress = a fill from the bottom of the spine. Hover pulls the cover forward (Hover role, `PbMotionSpring` lift on the artwork, never on the hit-tested element). |
| `Controls/Books/ChapterRibbon` | Detail jacket; Series mode (one segment per book) | A horizontal row of segments sized by length (chapter word count; for a series, one equal segment per book). Read segments are filled with `PbAccent`, the current one is partly filled, the rest are `PbSurface3`, and ghosts are dashed. Clicking a segment opens the reader at that chapter (or opens that book's Detail in Series mode). An overline caption above it, e.g. "9 of 24 chapters · about 2 h 10 min left". PDF and books without chapter data show a single continuous bar. |
| `Controls/Books/CoverWash` | Spread, Detail jacket | A soft radial wash in the book's spine colour behind content. On light themes it uses `Controls/HeroWash` (as Reading Lists does) so text contrast holds; on Matrix it uses the theme accent instead of the cover colour. A named token, never hex. |
| `Controls/Reader/ReaderClusters` | EPUB/FB2/MOBI reader, PDF reader | Three floating clusters (§5.2). One control, used by both readers. |

## 3. Books screen (`Views/BooksScreen.axaml`)

### 3.1 Layout, top to bottom (default view, "All" tab)

1. **Header row** (24 px gutter): "Books" (`pbTextHeading`), a count caption ("214 books · 3 reading"), flexible space, the search box
   (`searchBox`, about 260 px), and **Add books ▾** (`secondary`). Its dropdown (`Border.dropdown` / `menuRow`) holds: Scan now, Manage folders…
   (opens Preferences › Libraries › Book Folders), Import from Calibre…, Browse catalogs…. Scan leaves the selection bar and the toolbar.
2. **Spread + nightstand**: a two-column Grid (`*` and 320).
   - **Spread** (`CoverWash` inside a `PbRadiusLg` card):
     - a 120×180 cover;
     - the overline "NOW READING" (accent text);
     - the title (`bookTitle` 22) and author · series #n;
     - the **pull quote** (§6.1), italic serif, with a 2 px accent rule on its left;
     - a meta line "Ch. 9 · 37% · about 2 h 10 min left" (existing pace and time-left data from Slice 2b);
     - **Continue reading** (`primary`), which opens the reader.

     Clicking the cover or title opens Detail.
   - **Nightstand**: an overline "ON YOUR NIGHTSTAND", up to three face-out covers (66×99) standing on a small board, filled first with other
     Reading books, then topped up with next-in-series books. Below it, one `pbTextCaption` line: "This week · 4 h 20 read · 212 pages · 1
     finished", computed from `ReadingEvents` (book events only, the last 7 days). Clicking that line navigates to Insights.
   - **When the spread shows:** only on the All tab with no search, shelf or author filter. Any of those collapses the whole row so the
     grid gets the height (the user's window is 768 px tall).
   - **When nothing is in progress:** the spread shows the next book to start, in this order: next in a series you are reading, then the
     top of Want to read, then the newest book added. The overline becomes "UP NEXT", the button "Start reading", and the quote falls back
     as in §6.1. With an empty library, the row is replaced by the existing empty state (with copy fixed, §7.4).
3. **Tabs row** (pinned, §3.3):
   - the status tabs (`Button.tab`, `Spacing="20"`, with `countPill`s): All · Reading · Want to read · Unread · Finished · Abandoned, generated
     from an ItemsControl rather than six hand-written buttons;
   - flexible space;
   - `ghost` buttons Shelves ▾ (today's popup, unchanged in content), Group ▾ and Sort ▾. The value shows in the label only when it isn't the
     default ("Group: Series");
   - a Workspace `iconButton` (saved workspaces plus the List layouts section, today's popup);
   - the view switch `segGroup` (Covers | Bookshelf, the `.on` state).

   Shelves and Add books move out of the old tab row. The author filter chip (`pbChip`, from Detail's "More by") sits after the tabs when
   it is set.
4. **Content**: the grid or the Bookshelf view (§3.4), with the A–Z rail on the right.

### 3.2 Selection

While anything is selected, a **selection bar replaces the tabs row** (same height, `PbAccentSoftBrush` tint, accent bottom rule), as the
Library does. It holds:

- "3 selected", "of 214";
- `ghost` buttons Set status ▾, Rate ▾, Add to ▾ (collections), Match metadata, Edit…;
- flexible space;
- Delete… (`Button.danger`, which replaces hex `#E06060`);
- a Clear `iconButton`.

The local `Button.selectionAction` restyle goes. Shift-click and Shift+arrow range selection, and the `Books.*` input actions, are
unchanged.

### 3.3 Pinning

Avalonia has no native sticky positioning. The header row sits outside the content ScrollViewer. The spread and the tabs row sit inside it. A
pinned copy of the tabs row (the same view model, a second visual) is overlaid under the header and shown once the scroll offset passes
the spread's height. This is the pattern the Library's pinned group header uses. The current group header pins the same way under the
tabs row: the pinned group header that Slice 2a left unbuilt is built here, for both views.

### 3.4 Content views

- **Covers (default):** `BookCoverCard`s (128×192 covers, 24 px gaps) in a **virtualizing** wrap panel (today's flat grid is a plain
  WrapPanel), grouped or flat. Group headers are a serif title (`bookTitle` + `pbTextSection`), a caption count ("3 of 4 · 1 missing"
  for a series), and a hairline rule. They look the same in both views (today the grid uses `pbTextHeading` and the shelf view
  `pbTextSection`). Series headers stay clickable (they open Series mode); other headers are inert. Ghost cards follow the existing rule
  (grouped by Series, no tab, shelf, author or search filter).
- **Bookshelf:** `SpineShelf` rows with a board under every group (ungrouped: one long shelf that wraps onto new boards). Tooltips show
  title, author and stars as today.
- **A–Z rail:** shown in both views when `BooksAlphabet.Applies` (today it is grid-only). Letters with no books are faint; it's restyled
  onto tokens and keeps `OnAlphabetIndexLetterClick`.
- **Empty and no-match states:** unchanged in behaviour, restyled. The empty-state copy changes (§7.4).

### 3.5 Narrow windows

At 1366×768 everything fits. Below about 1100 px of content width, Group and Sort become `iconButton`s with accessible names. Below
900 px the search box becomes an icon (the Library's rule) and the nightstand column hides, so the spread takes the full width.

## 4. Book Detail (`Views/BookDetailScreen.axaml`)

### 4.1 Book mode

- **Back link** as today.
- **Jacket** (replaces the shared `DetailHero` and `DetailBand` for books; the comic and manga screens keep theirs). It sits in a
  `CoverWash`.
  - Left: a 220×330 flat cover with a visible spine edge (a darker strip in the spine colour, plus a fixed shadow layer). It is the
    `SharedElement` target.
  - Right, in a `*` column:
    - an overline "NOVEL · EPUB · 2019";
    - the title (`bookTitle`, 42 hero);
    - "by {Author}" (an accent link to the author filter, which replaces "More by…" in the old Your Shelf card) · *Series* #n (a link to
      Series mode);
    - a row with the editable `StarRating` and a status `SuggestBox` (both moved up from the Overview card);
    - flap copy: the synopsis in serif `pbTextBody` at `PbTextMutedBrush`, 4 lines plus "more";
    - actions: **Continue · Ch. 9 · 37%** (or Start / Read again, `primary`), Edit (`secondary`), and a ⋯ `iconButton` menu with Match
      metadata…, Reveal in Explorer and Delete book… (`danger` menu row with an inline confirm, which replaces the `#E06060` ghost button).
  - Under the actions: the `ChapterRibbon` (max 520 px), with the series `SpineShelf` (small: current book outlined in accent, ghosts
    dashed) to its right when the book is in a series.
- **Pinned compact bar:** once the jacket scrolls out of view, a 44 px bar pins under the top. It holds a 24×36 cover, the title, the
  progress caption and Continue (same overlay technique as §3.3).
- **Tabs** (`Button.tab`, `Spacing="20"`; panels fade with `controls:Replace.On`):
  - **Overview:**
    - an "About this edition" card (Publisher, Published, Language, ISBN, Format with the book-page count, Community rating);
    - a "Genres & tags" card (`pbChip`s) plus "More by {Author}" (three small `BookCoverCard`s and "All n ›", which opens the Books author
      filter);
    - "Your latest highlight" (a serif quote, the chapter, and "n notes · see all in Notes ›").
  - **Contents** (count pill): rows grouped by **part titles** (spec 2b §5 asked for this; today the list is flat). Each row has a state icon
    (✓ read / ▶ current / ○), the number, the title, the current percentage, and a 64 px length bar. Clicking a row opens the reader at that
    chapter. The "unavailable" card stays for missing data.
  - **Notes** (count pill): as today (kind chips, colour toggles, rows, two empty states), restyled. **Export… lives only here.** It is
    removed from the jacket (old hero action) and from the reader's top bar (moved to the Notes panel, §5.3).
  - **Your reading:** the Slice 2b stat tiles (Sessions, Time read, Last read, First read, Pace, Time left) as `card` tiles.

  The local `sectionTitle` restyle goes; card overlines use `pbTextOverline`.

### 4.2 Series mode

- **Jacket variant:**
  - a fanned stack of the first three covers (rotations −3°/0°/+6°, fixed);
  - an overline "SERIES · 4 BOOKS · 1 MISSING";
  - the series name in `bookTitle` 42 and "by {Author}";
  - actions: Continue · Book 2 · 37% (`primary`, the book you're on), Edit series, and ⋯ (Edit all books);
  - a `ChapterRibbon` in per-book mode with the caption "1 of 3 read · next: {title}".
- **Body:** the series books in order as `BookCoverCard`s (with status, progress and rating, which they lack today) plus ghost cards (the
  shared ghost replaces the inline ghost style).

## 5. Reader

### 5.1 The page is drawn by the page

The WebView (`NativeWebView` in `Views/BookReaderScreen.axaml`) loses its `Margin="0,56,0,56"` and fills the screen. Everything that looks
like paper is page HTML/CSS, generated in `Services/Reader/BookReaderCss.cs` and the page script:

- **Desk and paper:** the body background is a desk colour (the book theme's background, slightly darker). The text column is a paper sheet
  (the theme page colour) with a soft shadow. **OLED Black** and **High Contrast** have no desk, shadow or edges: the page fills the screen.
- **Page edges (paged mode only):** thin repeating-stripe stacks on the column's left and right. Their widths are proportional to the pages
  before and after the current one (from `BookPageMap`), clamped to 2–24 px. In scroll mode there are no edges.
- **Folio:** a faint page number at the foot of the paper, the only thing visible at rest.
- **Margin notes:** when the window is at least 1200 px wide, notes for blocks on the current page are drawn in the right margin as short
  cards (quote fragment and note, with a highlight-colour rule). Below 1200 px they become small dots after the highlighted text. Either
  way, clicking one opens the highlight popup.
- **In-page pills** (jump-back chip, auto-scroll pill, read-aloud pill, ruler bands) are restyled through CSS variables that C# pushes in from
  the app theme: `--pb-accent`, `--pb-radius`, `--pb-radius-chip`, `--pb-ui-font`, plus the book-theme `--pb-chrome-bg/fg`. They become
  squircles in the cluster style instead of today's inverse colours and system-ui font.
- **Highlight colours** become four named tokens (`PbHighlightYellow/Green/Blue/Pink` colour plus brush) in `AppTokens.axaml`. The page CSS
  gets them as variables, and the highlight popup swatches and the Notes list use the brushes. This replaces the hex copies in
  `BookReaderScreen.axaml` and `HighlightColorConverter`.

### 5.2 Floating clusters (`ReaderClusters`)

The same three clusters, in the same places, as the comic reader:

- **Navigate (top-left):** Back, Contents, "**Book** › Chapter".
- **Actions (top-right):** Search, Bookmark (active state), Notes, Aa, and ⋯ (read aloud, auto-scroll, line ruler, Export, and for PDF the
  Captures and Capture region items).
- **Page (bottom-centre):** Previous, "p. 214", a scrubber (part ticks, draggable thumb), "640 · 2 h 10 left", Next. In scroll mode the
  label is a percentage.

**Look:** a `PbRadiusLg` card with a fixed shadow layer. Background, foreground and accent come from the **book theme** (`BookThemeBrushes`
gains `ClusterBackground/Foreground/Border/Accent`; MatchAppSkin reads `PbReaderChrome*`). Buttons are `iconButton`s. The hover colour
comes from the theme's foreground at low alpha, which fixes the `#22FFFFFF` that barely shows on Light and Sepia.

**Showing and hiding:**

- **Shown by:** a tap in the middle third of the page; the pointer within 80 px of the top or bottom edge; or any reader input action other
  than page turns. The page script already forwards clicks, and it gains a throttled `pointer-edge` message for the edge case.
- **Hidden after:** 3 s idle (Reveal role in and out).
- **Setting:** "Keep toolbar visible" in the Aa sheet is the existing `AppSettings.BookReaderAutoHideChrome`, relabelled and inverted in
  the view. No schema change. `Reader.ToggleChrome` toggles the clusters.

**Hosting (airspace):** Avalonia can't draw over the native WebView in the same window.

- **(a) Chosen:** **one small owned transparent window per cluster** (`OverlayHostWindow`), sized to the cluster's desired size and
  positioned at its corner of the reader's root Grid. It follows the owner's `PositionChanged` and the root's `LayoutUpdated`, as
  `OverlayWindowController` does. A new `ClusterWindowController` generalizes it with an anchor (TopLeft / TopRight / BottomCenter) and a
  margin of 16. The window opacity animates for Reveal. Keys pressed while a cluster window has focus are forwarded to the main window's
  `IInputService`. Each window carries an automation name. Closing a panel returns focus to the button that opened it.
- **Spike first** (reader slice, step 1): show the three windows over a real book. Check:
  - they follow window move, resize, maximize and minimize with no visible lag;
  - transparency and rounded corners work under the Win32 composition mode (`Program.cs` chain);
  - clicks go to the page everywhere outside the clusters;
  - focus, Tab and keys behave.
- **(b) Fallback:** if the spike fails, draw the clusters as page HTML (the jump-back-chip pattern), styled from the same CSS variables,
  with actions sent through `invokeCSharpAction`. The PDF reader keeps the Avalonia `ReaderClusters` either way.
- **PDF reader:** `PageCanvas` is Avalonia, so `ReaderClusters` sits in-window over it, with no owned windows.

### 5.3 Panels

The reader has four panels: Contents, Notes, Search and Aa (the settings sheet).

- **Notes** merges today's Bookmarks and Highlights drawers. It has the same kind chips as Detail's Notes tab (All / Highlights /
  Bookmarks) and the moved **Export…**.
- The Actions cluster's Bookmark button still toggles a bookmark on the current page.

All four become **floating cards** in the cluster style:

- **Placement:** they open below their cluster with a 16 px margin and fill the height down to 16 px from the bottom. Width is 340 px for
  lists and 360 px for Aa.
- **Colours:** the book theme's colours, for every panel. This fixes today's mismatch, where the list drawers use the app's `PbChromeBrush`
  and the sheet uses the book theme.
- **Hosting:** they stay in the existing full-size `OverlayWindowController` window (it has a scrim, so swallowing clicks is right there).
  The scrim becomes a named token (it is hex `#B0000000` today) and is lighter on light book themes.
- **Content:** otherwise unchanged (go to page, part groups, search, and the settings cards). The Aa sheet's theme swatches get an
  active ring (a known gap today), and its Paged | Scroll `segItem`s use `.on`, matching the Books screen.

### 5.4 Highlight and dictionary popup

The same owned-window popup, restyled as a cluster-style card: token swatches (§5.1), the note box, Remove highlight as `danger` `ghost`,
Cancel `ghost`, Look up `secondary`. The dictionary mode is unchanged.

### 5.5 PDF reader (`Views/PdfPageReaderScreen.axaml`)

- **Clusters:** the same `ReaderClusters` in-window. The title is the book title, there's no Contents, Search or Bookmarks, and ⋯ holds
  Captures and Capture region.
- **Panels:** the same floating-card panels (Captures, Aa with the theme card only).
- **Page edges:** drawn in Avalonia around the page in fit-page mode, from page index and count.
- **Desk:** the PDF themes (Light, Dark, Sepia) get the desk treatment.

No other PDF features change.

## 6. Data

### 6.1 `Book.LastExcerpt`

A new nullable `string` column, at most 200 characters, plus one migration (`AddBookLastExcerpt`).

- **Written:** `BookReaderScreenViewModel.PersistPosition` (`ViewModels/BookReaderScreenViewModel.cs:1241`) fills it with the plain text of
  the block at the saved position (`LastBlockId`), whitespace-collapsed and cut at a word boundary to about 160 characters with an
  ellipsis. Only EPUB/FB2/MOBI; PDF leaves it null.
- **Read:** the spread shows `LastExcerpt`; if empty, the newest `BookHighlight` text; if none, the synopsis's first sentence; if none,
  no quote (the meta line moves up).
- **Already-read books:** they show the fallbacks until the next time they are opened. There is no backfill pass.

### 6.2 Nothing else

`BookReaderAutoHideChrome` is reused (§5.2). Spine colours, pace, time left and reading events already exist.

## 7. Across the section

### 7.1 Themes

All three surfaces must look finished in default (dark), Daylight and Matrix:

- **Washes:** `HeroWash` on light themes and the accent on Matrix (§2).
- **Reader:** the clusters and panels follow the **book** theme, not the app theme. MatchAppSkin uses the `PbReader*` roles, so a skin
  author still controls it.

### 7.2 Motion

| Where | Role | Notes |
|---|---|---|
| Grid, spread or nightstand cover → Detail jacket cover | Travel (`SharedElement` key `book:{id}`, existing `NavigationTransitionCoordinator`) | |
| Detail → reader | Reader route fade | existing route intent |
| Detail tabs | Replace (`controls:Replace.On`) | |
| Spine pull-out, cover hover | Hover (lift on artwork, `PbMotionSpring`) | |
| Clusters, panels, popup | Reveal (window opacity / `controls:Reveal.IsOpen`) | close is instant for popups |
| Tabs row / compact bar pin | none | appears without animation |
| Page edges, scrubber, ribbon | none | no count-ups or sweeps |

Reduced motion zeroes the move tokens as usual. Code-driven fades ask `MotionPolicy`.

### 7.3 Input and accessibility

- **Input:** no new hotkeys are needed. Any new action goes through `InputActionIds` and `InputActions.Core`. The Books and Detail screens keep
  their `ScreenInput.Attach` handlers.
- **Focus:** the new focusable controls (spread buttons, nightstand covers, ribbon segments, jacket links, cluster buttons) need
  keyboard reach. Ribbon segments are focusable and arrow-navigable; nightstand covers are focusable; anything decorative inside a card
  isn't.
- **Accessible names:** every icon-only button has one, and so does each cluster window.
- **Tests:** keyboard behaviour is tested in `RealWindowKeyboardTests`.

### 7.4 Defects fixed in their slice

| Defect | Slice |
|---|---|
| Bookshelf view grouped by Series draws ghosts as real spines (`BookSpineTemplate` has no ghost branch) | 1 |
| Books header subtitle and empty-state copy say "EPUB and PDF" (FB2 and MOBI are supported too). New: "Novels and non-fiction: EPUB, PDF, FB2, MOBI" | 1 |
| Hex `#E06060` Delete (selection bar, Detail) | 1, 2 |
| Local copies of the cover, ghost, select, alpha-letter and selection-action styles | 1, 2 |
| Home "Next in your book series" / "New books" rows open the reader; they should open Detail (`HomeScreenViewModel.OpenBookRow`). Continue Reading still opens the reader. | 2 |
| Export in three places | 2, 3 |
| List drawers (app theme) vs settings sheet (book theme) mismatch | 3 |
| Theme swatches have no active ring | 3 |
| Reader button hover `#22FFFFFF` invisible on Light and Sepia | 3 |
| Hex highlight swatches and drawer scrim | 3 |

## 8. Slices and verification

1. **Books screen and shared controls:** `PbBookTitleFontFamily` / `bookTitle`, `BookCoverCard`, `SpineShelf`, `CoverWash`, `LastExcerpt`
   (migration plus the `PersistPosition` write, needed by the spread), header, spread, nightstand, tabs row, pinning, selection bar,
   virtualized grid, group headers, Bookshelf view, A–Z rail, narrow behaviour, slice-1 defects.
2. **Detail:** `ChapterRibbon`, jacket, compact bar, tabs, Contents part groups, Series mode, Home row routing, slice-2 defects.
3. **Reader:** **clusters spike first** (§5.2), then page CSS (desk, edges, folio, margin notes, pills, highlight tokens), `ReaderClusters`,
   floating panels, popup, the PDF reader, slice-3 defects.

Each slice, before the user's on-screen check:

- **View-model tests** for new behaviour:
  - spread selection and fallback order;
  - nightstand fill;
  - "This week" figures;
  - `LastExcerpt` cut and fallbacks;
  - pin thresholds as pure math;
  - ribbon segment proportions;
  - cluster anchor math.
- **Renders:** `FoundationStyleTests.WithFoundation` for default, Daylight and Matrix (with `PB_RENDER_OUT`), plus 1.25 scale for the
  spread and jacket.
- **Lint:** `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`. The hex baseline may only go down.
- **Keyboard:** `RealWindowKeyboardTests` for new focusable controls (targeted classes only, never the wide suite).
- **Review:** the `avalonia-pro-max/review-checklist`.
- **On screen:** the user checks at 1366×768. The next slice starts only after that check.

Build gotchas carried from CLAUDE.md:

- New `.axaml` files get their code-behind in the same step (AVLN2000 / un-woven assembly).
- A Delete or Remove button inside a row or popup defers its removal one dispatcher tick.
- Never launch the dev exe against the real DB without asking: the new migration would apply to `%AppData%`.

## 9. Out of scope

- The dialogs: Calibre import, OPDS catalogs, Match metadata, Book/Bulk/Series properties. They are already `DialogCard`s; only their entry
  points move (Add books ▾, the ⋯ menu). Possible follow-up.
- A restyle of Home's book rows, beyond the routing fix.
- New reading features, a two-page spread (considered in round 2, not chosen), and the B-style 3-D tilted cover (round 2).
- Comic and manga Detail screens: they keep `DetailHero` and `DetailBand`.

## 10. Risks

- **Owned cluster windows** may lag or misbehave under some composition modes. Mitigation: the spike first, then the HTML fallback (§5.2).
- **Pinned rows** are overlays duplicating a visual. The two copies must share one view model and stay in sync; test the threshold math.
- **Virtualizing the grouped grid** with pinned headers is the Library's hardest pattern. Reuse its panel and pinned-header code rather than
  writing new code.
- **Margin notes** need the page script to know block positions on the current page. The paged-mode edge measurement already computes
  these, and scroll mode uses the visible range.
