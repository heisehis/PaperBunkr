# Books section redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-27-books-section-redesign-design.md*

**How this plan works.** Paths are under `src/Paperbunkr.App/` unless they start with `src/`. Each slice ends with the user's on-screen check
at 1366×768; the next slice does not start until it passes.

**Standing rules for every step:**
- **Skills:** load `paperbunkr-design-language`, plus the named `avalonia` subskill read off disk.
- **New views:** a new `.axaml` gets its code-behind in the same step.
- **Row and popup removals:** removing a row, or closing a popup from inside it, is deferred one dispatcher tick.
- **No hex:** colours are tokens only. The `ThemeHardcodedColorLintTests` baseline may only go down.
- **Builds:** build with `-p:OutDir=<scratch>` (the app is often running).
- **Tests:** run only the targeted test classes (never the wide suite), then `dotnet build-server shutdown`.
- **Real database:** don't launch the dev exe against the real DB without asking. The new migration would apply to `%AppData%`.

**Lint command** (used at every "lint" mention below):
`dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"`

**Precedents to mirror:**
- **Library pinned group header:** `Views/LibraryScreen.axaml:1926` and `LibraryScreen.axaml.cs:84-176`.
- **Library grouped virtualized grid:** an `ItemsControl` of groups, each with an inner `ItemsControl` on a `VirtualizingWrapPanel`
  (`LibraryScreen.axaml:1540-1560`).
- **Render-test harness:** `InsightsTodayRenderTests` (built on `FoundationStyleTests.WithFoundation`).

---

## Slice 1: Books screen and shared controls

### Step 1: Book-title font token and type modifier
**Files:**
- `Styles/AppTokens.axaml` (edit)
- `Styles/Typography.axaml` (edit)
- `src/Paperbunkr.App.Tests/XamlConsistencyLintTests.cs` (edit, only if a rule rejects the new class)

**What:**
- Add `PbBookTitleFontFamily` = `avares://Paperbunkr.App/Assets/Fonts/#Source Serif 4` next to `PbDisplayFontFamily`. It is fixed and
  never set by `ThemeService`'s font override.
- Add the modifier style `TextBlock.bookTitle` (FontFamily = that token, FontWeight SemiBold) for combining with the existing size
  classes (`pbTextBody`, `pbTextSection`, `pbTextHeading`, `pbTextHero`).

**Depends on:** none.
**Verify:**
- Lint.
- A render smoke test asserting `bookTitle` + `pbTextHeading` resolves to Source Serif at 22 even with a body-font override applied.

### Step 2: `Book.LastExcerpt`
**Files:**
- `src/Paperbunkr.Data/Entities/Book.cs` (edit)
- `src/Paperbunkr.Data/Migrations/<ts>_AddBookLastExcerpt.cs` (+ Designer and snapshot, generated with `dotnet ef migrations add` against
  a scratch DB path)
- `ViewModels/BookReaderScreenViewModel.cs` (edit `PersistPosition`, line ~1241)
- `Services/Books/QuoteExcerpt.cs` (new)
- `Views/BookReaderScreen.axaml.cs` (edit: the page script's excerpt slice goes from 140 to 200, lines ~872/877)

**What:**
- Add a nullable column `LastExcerpt` (`MaxLength(200)`).
- `QuoteExcerpt.Cut(string text, int max = 160)`: collapse whitespace, cut at the last word boundary at or before `max`, and append `…`
  when cut.
- `PersistPosition` writes `book.LastExcerpt = QuoteExcerpt.Cut(_currentBlockExcerpt)` for non-PDF books when `_currentBlockExcerpt` is
  non-empty, and leaves the old value otherwise.

**Depends on:** none.
**Verify:**
- `QuoteExcerptTests` (new): short text unchanged, word-boundary cut, whitespace collapse, empty input.
- Add a `BookReaderScreenViewModelTests` case: the position message, then `PersistPosition`, writes the excerpt.
- A Data.Tests migration test in the existing style (`context.Database.Migrate()` plus an ORM seed, not the bare-INSERT trick).
- Remember the up-down-up antipattern note.

### Step 3: Spread and nightstand data (`BooksHomeShelf`)
**Files:**
- `Services/Books/BooksSpreadResolver.cs` (new)
- `Models/BookSpreadModel.cs` (new)
- `ViewModels/BooksScreenViewModel.cs` (edit)

**What:** a pure resolver over `_allCards` plus a small DB read.

- **Spread book:**
  - the most recently opened Reading book;
  - otherwise, in order: the next book in a series you are reading (the lowest `SeriesIndex` that is unread or want-to-read, in a series
    with a Reading or Finished book), then the top of Want to read (by `AddedTime`), then the newest added;
  - with a mode of `NowReading` or `UpNext`.
- **Quote:** `LastExcerpt`, otherwise the newest `BookHighlight.Excerpt`, otherwise the synopsis's first sentence, otherwise null.
- **Meta line:** chapter, percentage and time left, reusing Slice 2b's pace and time-left calculation (the helper behind the
  `BookDetailScreenViewModel.Tabs.cs` stat tiles; extract it if it's private).
- **Nightstand:** up to 3 other Reading books by `LastOpenedTime`, topped up from next-in-series.
- **This week:** a sum over `ReadingEvents` where `ItemType == Novel` and `TimestampUtc` is within the last 7 days: `ActiveSeconds`
  → time, `PagesRead` → pages, `Kind == Finished` → finished count.
- **View-model properties:** `Spread`, `Nightstand`, `ThisWeekLine` and `ShowSpread` (true only when `ActiveStatusTab == All`, there's no
  search, no shelf, no author filter, and the library is non-empty).
- **Callbacks:** `ContinueSpreadCommand` (reader), `OpenSpreadDetailCommand`, and `GoInsights` (a new optional `Action? goInsights` ctor
  parameter, passed from `MainViewModel.cs:226`).
- **Recompute:** in `Rebuild()` / `LoadFromDatabase()`.

**Depends on:** Step 2 (the quote source).
**Verify:** `BooksSpreadResolverTests` (new) covers:
- each fallback level;
- the nightstand fill order and the cap of 3;
- the this-week window edges;
- `ShowSpread` across tab, search, shelf and author combinations.

### Step 4: `BookCoverCard` control
**Files:**
- `Controls/Books/BookCoverCard.axaml` and `.axaml.cs` (new)
- `Models/BookCardSample.cs` (edit)

**What:**
- **The card:** a `UserControl` bound to `BookCardSample`, made of:
  - the cover: `AsyncCoverImage`, the `SharedElement` key `book:{id}`, and the `posterScrim`;
  - the one-badge slot: a `pbar` progress bar when `ShowProgressRing` (renamed `ShowProgressBar`), otherwise a status badge (Finished ✓ on
    `PbSuccessBrush`, Want to read and Abandoned on `PbBadge*`);
  - a hover `StarRating`;
  - the select `CheckBox`, which keeps today's tile-select behaviour and the `TileSelectionController` binding;
  - text below the cover: `bookTitle pbTextBody` (2 lines, trimmed), `pbTextCaption` author, and the `StatusCaption` line.
- **Ghost variant:** `IsGhost` → a dashed outline with `GhostLabel`, not focusable.
- **Card size:** set by a `CoverWidth` property (default 128, height ×1.5).
- **New `BookCardSample` properties:**
  - `StatusCaption`: "37% · 2 h left" / "Finished · ★★★★★" / "Want to read" / "Abandoned" / empty. Time-left needs `CharacterCount`
    plus the pace; fall back to "37%" when the pace is unknown.
  - `StatusBadgeKind`.
- **Styles:** Hover lift on the cover artwork (`PbMotionSpring`); no local restyle of families.

**Depends on:** Step 1.
**Verify:**
- `BookCardSampleTests` (new or extended) for `StatusCaption` and `StatusBadgeKind`.
- `BookCoverCardRenderTests` (new) in default, Daylight and Matrix, written to `PB_RENDER_OUT`.
- Lint.

### Step 5: `SpineShelf` control
**Files:**
- `Controls/Books/SpineShelf.axaml` and `.axaml.cs` (new)
- `Views/SpineTile.cs` (edit: add a ghost and a progress-fill render path)

**What:**
- **The shelf:** an `ItemsControl` on `VirtualizingVariableWrapPanel UseSpineWidths` (today's Bookshelf panel), with a board drawn under
  every row. The board is a `PbSurface3` strip plus a fixed-shadow layer; draw it in the panel's row arrange or as an adorner layer per row.
  The spike decides which, and the simpler one wins.
- **`SpineTile` additions:**
  - `IsGhost`: a dashed outline in `PbTextFaintBrush` with no fill. This fixes the ghost bug: `BookCardSample` already implements
    `ISpineSource`, so pass `IsGhost` through.
  - `ProgressFraction`: a bottom-up fill at `PbAccentSoft` alpha.
- **Hover:** pulls the face-out cover forward (the existing hover behaviour, restyled to the Hover role).
- **Two sizes:** `Default` (Bookshelf view) and `Compact` (Detail series shelf, about 70 px tall).

**Depends on:** none.
**Verify:**
- `SpineTileRenderTests` (extend) for ghost and progress.
- `BookshelfViewTests` (extend): Bookshelf-by-Series emits ghosts that render as ghosts.

### Step 6: `CoverWash` control
**Files:**
- `Controls/Books/CoverWash.cs` (new)
- `Styles/AppTokens.axaml` (edit, only if a new named colour is needed)

**What:**
- **The control:** a `Decorator` that paints a radial wash behind its child.
- **Colour:** from `SpineColorCache` for the `CoverKey` it's given. If the cache has none, it uses `PbAccentSoftBrush`; on Matrix it always
  uses the accent.
- **Light themes:** read the theme mode the way `HeroWash` does and use `HeroWash`'s light treatment, so text contrast holds.
- **Colours:** token-only.

**Depends on:** none.
**Verify:** `CoverWashRenderTests` (new) in the three themes, with a contrast assertion on a text sample over the wash in Daylight.

### Step 7: Books screen layout — header, spread, nightstand, tabs row
**Files:**
- `Views/BooksScreen.axaml` (edit, major)
- `Views/BooksScreen.axaml.cs` (edit)
- `ViewModels/BooksScreenViewModel.cs` (edit)
- `Models/BooksStatusTabItem.cs` (edit, if needed)

**What:** rebuild the top of the screen per spec §3.1. The old three-band header/toolbar/tab structure goes.

- **Header row:** title, count caption, `searchBox`, and Add books ▾ (`Border.dropdown` + `PopupFit` + ScrollViewer with Scan now,
  Manage folders…, Import from Calibre…, Browse catalogs…). This absorbs today's Add menu and the Scan icon button. Manage folders
  calls `_goLibrarySettings`.
- **Inside the content ScrollViewer:**
  - the spread row: `CoverWash` card plus nightstand. Its covers are `SharedElement`-keyed; the nightstand's "This week" line is a
    `Button.ghost` that runs `GoInsights`.
  - the tabs row: status tabs from an `ItemsControl` over `StatusTabs`, plus Shelves ▾, Group ▾, Sort ▾ (`ghost`, label shows the value
    only when not the default), the Workspace `iconButton` (today's workspace popup content moved under it) and the view `segGroup`.
  - the author `pbChip` after the tabs.
- **View-model changes:** `GroupLabel` / `SortLabel` return null for the default value. Remove the duplicate "Workspace:" label button.
- **Empty and no-match copy:** "Novels and non-fiction: EPUB, PDF, FB2, MOBI".

**Depends on:** Steps 1, 3, 4, 6.
**Verify:**
- `BooksScreenViewModelTests` (extend: labels, `ShowSpread` wiring).
- `BooksScreenFocusTests` and `BooksTabsViewModelTests` (keep green, update selectors).
- Render tests of the full screen in three themes at 1366×768 with a seeded library.
- Lint.

### Step 8: Pinning (tabs row and group header)
**Files:**
- `Views/BooksScreen.axaml` (edit)
- `Views/BooksScreen.axaml.cs` (edit)
- `Services/Books/PinMath.cs` (new)

**What:**
- **Pinned tabs row:** a second tabs-row visual, overlaid under the header (same `DataContext`), shown when the ScrollViewer's offset
  passes the in-flow tabs row's top. The in-flow one keeps layout space and is hidden from keyboard and hit-test while the pinned copy
  shows, so focus never lands on an invisible duplicate.
- **Pinned group header:** ported from `LibraryScreen.axaml.cs:84-176` (`UpdatePinnedGroupHeader`, hand-off 44), positioned below the
  pinned tabs row.
- **`PinMath`:** holds the threshold maths as pure functions.

**Depends on:** Step 7.
**Verify:**
- `PinMathTests` (new).
- `RealWindowKeyboardTests` (targeted new test): Tab and arrow keys from the grid up into the pinned tabs row reach the visible copy.

### Step 9: Content views — virtualized grid, group headers, Bookshelf, A–Z rail, narrow widths
**Files:**
- `Views/BooksScreen.axaml` (edit)
- `Views/BooksScreen.axaml.cs` (edit)
- `ViewModels/BooksScreenViewModel.cs` (edit)

**What:**
- **Grid:** the flat grid and every group's inner list use `VirtualizingWrapPanel` (ItemWidth 128 + 24 spacing; ItemHeight from the
  `BookCoverCard` height plus the three text lines), with `BookCoverCard` as the item template.
- **Group headers:** `bookTitle pbTextSection` + `pbTextCaption` count (series: "3 of 4 · 1 missing") + a hairline, the same in both views.
  Series headers stay Buttons that open Series mode.
- **Bookshelf view:** uses `SpineShelf`.
- **A–Z rail:** shown in both views (drop the `IsGridView` gate), restyled with tokens.
- **Local styles deleted:** `bookCover`, `ghostCard`, `tileSelect`, `alphaLetter`, `selectionAction`.
- **Narrow widths** (from the screen's `SizeChanged`):
  - below 1100 px of content width, Group and Sort become `iconButton`s with names;
  - below 900 px, search becomes an icon and the nightstand hides.

**Depends on:** Steps 4, 5, 7.
**Verify:**
- `BooksScreenFocusTests` (arrow navigation across virtualized groups, which is where `ItemsControl` arrow handling drops focus; take the
  directional move on the tunnel if needed, per CLAUDE.md).
- `BookshelfViewTests`.
- Render tests at 1366 and 1000 px widths.

### Step 10: Selection bar
**Files:**
- `Views/BooksScreen.axaml` (edit)
- `ViewModels/BooksScreenViewModel.cs` (edit)

**What:**
- **Placement:** while `HasSelection`, a selection bar replaces both the in-flow and the pinned tabs row (spec §3.2).
- **Contents:** "n selected · of N"; Set status ▾, Rate ▾, Add to ▾ (submenus reuse `BooksContextMenuBuilder`'s items); Match metadata;
  Edit…; flexible space; Delete… (`Button.danger`) with a confirm flyout; Clear `iconButton`.
- **Bulk commands:** add `SetSelectionStatusCommand(BookReadingStatus)` and `RateSelectionCommand(float?)`, mirroring the single-book
  context-menu commands. Defer any list-mutating reload one tick.

**Depends on:** Step 7.
**Verify:** `BooksScreenViewModelTests` (bulk status and rate), the existing selection tests, and lint.

### Step 11: Slice 1 review
**Files:** —
**What:**
- Run the `avalonia-pro-max/review-checklist`.
- Run targeted tests: Books*, BookCover*, Spine*, CoverWash*, PinMath*, QuoteExcerpt*, BooksSpreadResolver*, plus lint.
- Ask the user before launching the dev build: it applies `AddBookLastExcerpt` to the real DB, so offer a DB backup first.
- Update `docs/paperbunkr-todo.md` with what was verified.

**Verify:** the user's on-screen check at 1366×768 in dark and Daylight.

---

## Slice 2: Book Detail

### Step 12: `ChapterRibbon` control
**Files:**
- `Controls/Books/ChapterRibbon.axaml` and `.axaml.cs` (new)
- `Models/RibbonSegment.cs` (new)
- `Services/Books/RibbonMath.cs` (new)

**What:**
- **Segments:** `RibbonSegment { Weight, FillFraction, IsGhost, Label, Tag }`.
- **`RibbonMath`:**
  - Chapters: weights from per-chapter character counts (from `BookChapterSummary`; add `CharacterCount` there if it's missing), floored
    so that tiny chapters stay clickable (minimum 6 px).
  - Series: equal weights, one per book.
- **Segment buttons:** each segment is a focusable Button with an automation name ("Chapter 9, Nine Winters, 40% read"). Left and Right
  move focus between segments; Enter opens. The overline caption is bound.
- **Single-bar mode:** used when there are no segments (PDF).

**Depends on:** none.
**Verify:**
- `RibbonMathTests` (new): weights, minimum width, fills.
- A render test.
- `RealWindowKeyboardTests`: arrow keys between segments.

### Step 13: The jacket and the compact bar
**Files:**
- `Views/BookDetailScreen.axaml` (edit, major)
- `Views/BookDetailScreen.axaml.cs` (edit)
- `ViewModels/BookDetailScreenViewModel.cs` (edit)

**What:**
- **Replace `DetailHero` / `DetailBand`** in Book mode with the jacket (spec §4.1), inside a `CoverWash`.
  - **Cover:** flat, with the spine edge (a Border in the darkened spine colour from `SpineColorCache`) and the `SharedElement` key.
  - **Text column:** overline, `bookTitle pbTextHero` title, author link (`AuthorFilterCommand`, reusing "More by"), series link, rating and
    status `SuggestBox` moved from the Your Shelf card, and flap copy (4 lines plus a "more" toggle).
  - **Actions:** primary Continue / Start / Read again, Edit, and a ⋯ menu (Match metadata…, Reveal in Explorer, Delete book… as a `danger`
    `menuRow` with an inline confirm). Export leaves the hero.
- **Under the actions:** the `ChapterRibbon` (segments from `Chapters`; click → `OpenAtChapterCommand`, which exists for the Contents rows,
  so reuse it) and a `SpineShelf Compact` for the series (ghosts via the same `AddGhosts` logic, with the current book outlined).
- **Pinned compact bar:** shown past the jacket's bottom, using `PinMath`.
- **`IDetailHeaderSource`:** stays implemented, because other code may read it. It no longer drives the Books view.
- **Hex removed:** `#E06060`.

**Depends on:** Steps 5, 6, 8 (`PinMath`), 12.
**Verify:**
- `BookDetailScreenViewModelTests` (extend: action labels, series shelf items, ghost count).
- `BookDetailScreenFocusTests` (update selectors; Tab order jacket → ribbon → tabs).
- Render tests in three themes.

### Step 14: Detail tabs
**Files:**
- `Views/BookDetailScreen.axaml` (edit)
- `ViewModels/BookDetailScreenViewModel.Tabs.cs` (edit)
- `Models/BookTocGroup.cs` (edit or reuse)

**What:**
- **Tab strip:** `Overview · Contents (count) · Notes (count) · Your reading`, using `Button.tab` with `Spacing="20"`. Panels fade via
  `controls:Replace.On`.
- **Overview:**
  - About this edition: `fieldLabel`/value grid.
  - Genres & tags: `pbChip`s, plus More by: three `BookCoverCard`s at `CoverWidth=48` with captions hidden, and "All n ›".
  - Latest highlight: newest `BookHighlight`, plus "n notes · see all ›", which switches to the Notes tab.
- **Contents:** rows grouped by part using `BookTocGroup` (built by `BookTocBuilder` for the reader; reuse it here) with state icons and
  length bars. A click opens the reader at that chapter.
- **Notes:** today's content restyled; it is the only place Export lives.
- **Your reading:** the stat tiles as `card`s in a `ResponsiveColumnsPanel`.
- **Local restyle deleted:** `sectionTitle`.

**Depends on:** Step 13.
**Verify:** `BookDetailTabsTests` (extend: part grouping, latest-highlight selection), `BookDetailScreenFocusTests`, lint.

### Step 15: Series mode
**Files:**
- `Views/BookDetailScreen.axaml` (edit)
- `ViewModels/BookDetailScreenViewModel.cs` (edit)

**What:**
- **Jacket variant:** a fanned stack (the first three `SeriesBooks` covers, rotations −3/0/+6), overline, name, author, Continue at the
  book in progress, Edit series, ⋯ (Edit all books), and a per-book `ChapterRibbon` with its caption.
- **Body:** `BookCoverCard`s plus ghost cards for `SeriesBooks`. Delete the inline ghost style.

**Depends on:** Steps 4, 12, 13.
**Verify:** `BookDetailScreenViewModelTests` (Series: Continue target, ribbon fills, ghosts), and render tests.

### Step 16: Home book rows open Detail
**Files:**
- `ViewModels/HomeScreenViewModel.cs` (edit: ctor gets a new optional named `Action<int>? goBookDetail = null`; `OpenBookRow` at ~491 uses
  it and falls back to the reader when it's null)
- `ViewModels/MainViewModel.cs` (edit: line 219 passes `goBookDetail: GoBookDetailForBook`)
- `Views/Home/BookRowsSection.axaml` (edit: comment only)

**What:** the "Next in your book series" and "New books" rows open Detail. Continue Reading is unchanged and still opens the reader.

**Depends on:** none.
**Verify:** a `HomeScreenViewModelTests` case. Add it to an existing class with a named argument; don't touch the ~16 positional call
sites.

### Step 17: Slice 2 review
**Files:** —
**What:** run the review checklist, targeted tests (BookDetail*, RibbonMath*, Home*Book*) and lint. Update `docs/paperbunkr-todo.md`.
**Verify:** the user's on-screen check.

---

## Slice 3: Reader

### Step 18: Cluster windows spike
**Files:**
- `Views/Reader/ClusterWindowController.cs` (new)
- `Views/OverlayHostWindow.cs` (edit, if focus or activation options are needed)
- A throwaway test cluster in `Views/BookReaderScreen.axaml.cs`, behind a debug flag removed at the end of the step

**What:**
- **`ClusterWindowController`:** generalizes `OverlayWindowController` with `Anchor` (TopLeft / TopRight / BottomCenter), `Margin` (16) and
  `SizeToContent`. It tracks the owner's `PositionChanged`, the root's `LayoutUpdated` and window-state changes, hides while the owner is
  minimized, and sets `ShowActivated = false`.
- **Show three test clusters over a real book.** Check:
  - they follow window move, resize, maximize and minimize without lag;
  - transparency and rounded corners work under the Win32 composition chain in `Program.cs`;
  - clicks outside the clusters reach the page;
  - a key pressed while a cluster has focus reaches `IInputService`: forward `KeyDown` / `KeyUp` from the cluster window into
    `InputHost` for the main window.
- **Record the result in the plan.** **Gate:** on failure, switch Step 20 to the HTML fallback (spec §5.2 b).

**Depends on:** none.
**Verify:**
- Manual, with the user watching. Ask before launching the dev exe. UI automation (FlaUI) needs explicit permission.
- `ClusterAnchorMathTests` (new) for the placement maths.

### Step 19: Page CSS — desk, paper, edges, folio, margin notes, pill variables, highlight tokens
**Files:**
- `Services/Reader/BookReaderCss.cs` (edit)
- `Views/BookReaderScreen.axaml.cs` (edit: page script)
- `Views/BookReaderScreen.axaml` (edit: remove the WebView `Margin="0,56,0,56"`)
- `Styles/AppTokens.axaml` (edit)
- `Views/HighlightColorConverter.cs` (edit)
- `Models/BookThemeBrushes.cs` (edit)

**What:**
- **New input:** `BookReaderCssInput` gains `DeskColor`, `ShowPaper` (false for OLED Black and High Contrast), `AccentColor`, `Radius`,
  `RadiusChip`, `UiFontStack` and `HighlightColors[4]`.
- **CSS:** emits the body desk, a `.pb-paper` sheet with shadow, `.pb-edge-left/right` (widths set by script from `BookPageMap`:
  `before/total` and `after/total` scaled to 2–24 px, paged only), and `.pb-folio`.
- **Margin notes:** `.pb-margin-notes` cards when `innerWidth ≥ 1200`, else `.pb-note-dot`. The script places them from block rects already
  computed for paged edges, or the visible range in scroll mode. A click posts the existing highlight-tap message.
- **Pills:** jump-back, auto-scroll, read-aloud and ruler move from inverse colours to the CSS variables.
- **Tokens:** `PbHighlightYellow/Green/Blue/Pink` (Color + Brush). `HighlightColorConverter` and the popup swatches use them.
- **Book-theme colours:** `BookThemeBrushes` gains `Desk(theme)` and the cluster brushes `ClusterBackground/Foreground/Border/Accent`.
  Their hex values live only in `BookThemeBrushes` as today, or move to tokens where a matching role exists.

**Depends on:** none (independent of the spike).
**Verify:**
- `BookReaderCssTests` (extend: variables present, no edges in scroll mode, no paper for OLED and HC).
- Test the page script in the built-in browser per the Books-arc memory recipe: a standalone HTML under `obj/`, a temporary
  `py -m http.server` entry in `.claude/launch.json`, checked with `javascript_tool`. Remove the entry and the file afterwards.

### Step 20: `ReaderClusters` and showing/hiding
**Files:**
- `Views/Reader/ReaderClusters.axaml` and `.axaml.cs` (new)
- `Views/BookReaderScreen.axaml` and `.axaml.cs` (edit)
- `ViewModels/BookReaderScreenViewModel.cs` (edit)
- `Styles/ReaderChrome.axaml` (edit)
- `Views/ReaderChrome.axaml` and `.axaml.cs` (retire once nothing uses them)

**What:**
- **One control, three parts:** `Navigate`, `Actions` and `Page`. It exposes the same command properties `ReaderChrome` has today plus
  `NotesCommand`, `MoreItems` (⋯: Read aloud, Auto-scroll, Line ruler, Export…, Captures and Capture region for PDF), `ScrubFraction`
  (two-way), `PartTicks` and `TimeLeftLabel`.
- **Look:** `iconButton`s on a `PbRadiusLg` card with a fixed shadow layer; colours from the `BookThemeBrushes` cluster brushes. The
  `#22FFFFFF` hover is replaced with a foreground-alpha hover.
- **Hosting in the EPUB reader:** the three parts sit in `ClusterWindowController`s (or as HTML, if the spike failed).
- **Showing:**
  - the page script adds a throttled `pointerEdge` message (pointer within 80 px of the top or bottom edge);
  - `contentTap` in the middle third of the page;
  - any reader input action other than page turns;
  - `Reader.ToggleChrome`.
- **Hiding:** after a 3 s idle timer.
- **Opacity:** a Reveal-role fade via `MotionPolicy`.
- **"Keep toolbar visible":** the Aa sheet's toggle relabelled, bound inverted to `BookReaderAutoHideChrome`.
- **Accessibility:** automation names on each cluster window; focus returns to the opening button when a panel closes.

**Depends on:** Steps 18, 19.
**Verify:**
- `BookReaderScreenViewModelTests` (idle timer, edge message → visible, toggle).
- `ReaderClustersRenderTests` (Sepia and Dark).
- `RealWindowKeyboardTests` (targeted: Tab into clusters, Esc closes a panel, focus returns).

### Step 21: Floating-card panels and the merged Notes panel
**Files:**
- `Views/ReaderListDrawer.axaml` and `.axaml.cs` (edit, becomes `ReaderPanel`)
- `Views/ReaderSettingsSheet.axaml` and `.axaml.cs` (edit)
- `Views/BookReaderScreen.axaml` (edit: Bookmarks and Highlights drawers merge into Notes with kind chips; Export moves into Notes)
- `ViewModels/BookReaderScreenViewModel.cs` (edit: `IsNotesOpen`, `NotesKindFilter`; `IsBookmarksOpen` / `IsHighlightsOpen` become aliases
  for input actions)
- `Styles/AppTokens.axaml` (edit: `PbReaderScrimColor`, plus a light-theme variant)

**What:**
- **Cards:** panels become cards anchored under their cluster (16 px margin), 340 px wide (360 for Aa), with book-theme background and
  foreground for all of them. They're hosted in the existing `OverlayWindowController` window, and the scrim colour becomes a token.
- **Aa sheet:** the theme swatches get an active ring; Paged | Scroll uses `.on`.
- **Notes panel:** All / Highlights / Bookmarks chips reuse Detail's filter logic (`BookDetailScreenViewModel.Tabs.cs`), lifted into a shared
  `BookNotesFilter` if it's private. The Actions cluster's Bookmark button still toggles the current page.

**Depends on:** Step 20.
**Verify:**
- `BookReaderScreenViewModelTests` (notes filter, Export from Notes).
- The existing drawer tests, updated.
- Render tests (Sepia and Dark).
- Lint.

### Step 22: Highlight / dictionary popup restyle
**Files:**
- `Views/BookReaderScreen.axaml` (popup card, ~275-357)
- `Views/BookReaderScreen.axaml.cs` (~1190-1213)

**What:**
- **Card:** the cluster-style card with token swatches.
- **Buttons:** Remove highlight as `danger` `ghost`, Cancel as `ghost`, Look up as `secondary`.
- **Removed:** the local transparent-background restyles.
- **Dictionary mode:** content unchanged.

**Depends on:** Steps 19, 20.
**Verify:** a render test and lint.

### Step 23: PDF reader
**Files:**
- `Views/PdfPageReaderScreen.axaml` and `.axaml.cs` (edit)
- `ViewModels/PdfPageReaderScreenViewModel.cs` (edit)
- `Views/Reader/PageEdges.cs` (new, a small Avalonia control)

**What:**
- **Clusters:** `ReaderClusters` in-window, with ⋯ holding Captures and Capture region and no Contents, Search or Notes.
- **Panels:** floating-card panels (Captures; Aa with the theme card only).
- **Desk:** a desk background.
- **Page edges:** `PageEdges` around the page image in fit-page mode (widths from page index and count via the same 2–24 px maths, shared
  as `PageEdgeMath`).

**Depends on:** Steps 20, 21.
**Verify:**
- `PageEdgeMathTests` (new).
- The existing PDF reader view-model tests.
- A render test.

### Step 24: Slice 3 review and close-out
**Files:**
- `docs/paperbunkr-todo.md` (edit)
- Memory: `project_paperbunkr_books_section_redesign.md` (edit)

**What:**
- Run the review checklist, targeted tests (BookReader*, ReaderClusters*, PageEdge*, Pdf*) and lint.
- Delete `ReaderChrome` if nothing references it.
- Remove the spike flag.
- Update the todo doc and the memory with what was verified on screen.

**Verify:** the user's on-screen check of the EPUB reader (Sepia, Dark, OLED Black; paged and scroll) and the PDF reader.

---

## Test strategy summary
- **Pure maths and data units** (new test classes): `QuoteExcerpt`, `BooksSpreadResolver`, `PinMath`, `RibbonMath`, `ClusterAnchorMath`,
  `PageEdgeMath`.
- **View-model tests** extend the existing Books / BookDetail / BookReader / Home classes. New constructor parameters are optional and named,
  so old call sites keep compiling.
- **Renders:** `FoundationStyleTests.WithFoundation` in default, Daylight and Matrix (plus Sepia and Dark book themes for reader pieces),
  written to `PB_RENDER_OUT`. Check them by eye before each on-screen check.
- **Lint:** after every XAML step.
- **Keyboard:** targeted `RealWindowKeyboardTests` cases for pinned rows, ribbon segments and clusters.
- **What only the screen shows:** the cluster windows (Step 18), page CSS in WebView2, and the motion feel. These are the user's on-screen
  checks at the end of each slice. Never use computer-use, and use FlaUI only with explicit permission.
