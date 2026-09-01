# Books Reader Ergonomics + Annotations & Export — Design

## Background

The user asked for the Books (novels/EPUB/PDF) section to be brought up to par with dedicated
e-reader apps, after reviewing a Gemini-assisted research doc comparing **Thorium Reader**
(EDRLab, Readium CSS, WCAG/OPDS-focused desktop reader) and **eBoox Reader** (mobile-first,
gesture-driven, universal-format reader). The research doc groups its recommendations into five
domains: format ingestion, layout ergonomics/typography, catalog federation (OPDS + cloud sync),
accessibility/TTS, and annotations/export.

Paperbunkr's Books section is not a blank slate: `EpubBookSource`/`PdfBookSource` (Engine),
`BookReaderScreenViewModel` + `BookPaginator` (fit modes, zoom, rotation, continuous scroll,
fullscreen, position tracking — largely shared lineage with the comic reader), `BookFolderScanService`,
`BookCoverThumbnailService`, `BooksScreenViewModel` (search/sort/group chrome), `BookDetailScreenViewModel`,
and `BookBookmark` (point position via chapter index + character offset, reflow-stable) all exist and
ship today. `BookReaderSettings` (font size, 3 font families, 3-level line spacing, 4 themes) exists
but is session-only — never persisted (`docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md`
§5 explicitly deferred persistence).

Paperbunkr is a single-user local Windows desktop app (Avalonia/.NET, SQLite under `%AppData%`) with
no server, account, or cloud-sync infrastructure. **Catalog federation (OPDS) and cross-device cloud
sync are out of scope for this spec** — they're the two biggest architectural asks in the research doc,
assume a networked/mobile usage model that doesn't obviously fit how this app is used today, and would
warrant their own dedicated spec if ever pursued. **Universal format ingestion (MOBI/AZW3/FB2/DOCX/
RTF/ODT) and accessibility/TTS (screen-reader DOM enhancement, "Where am I?" trail, text-to-speech)
are also out of scope for this round** — deferred to the Beta backlog, not dropped.

This spec covers the two domains the user chose to pursue now: **reader ergonomics/typography** and
**annotations & export**, both scoped to the existing EPUB (reflowable) and PDF (page-image) readers.

**CE note:** ComicRack CE has no prose-reading concept — nothing to verify against for the Books
section, per the existing Novels design spec.

## Decisions

| Area | Decision |
|---|---|
| **Settings persistence** | Global defaults on `AppSettings` (singleton row, same pattern as `DefaultPageFitMode`/`MouseWheelSpeed`): `BookReaderFontSize`, `BookReaderFontFamily`, `BookReaderLineSpacing`, `BookReaderCharacterSpacing`, `BookReaderWordSpacing`, `BookReaderParagraphSpacing`, `BookReaderPageMargin`, `BookReaderTheme`, `BookReaderAutoHideChrome`. |
| **Per-book override** | Nullable override columns on `Book` (`FontSizeOverride`, `FontFamilyOverride`, `ThemeOverride`, etc.), falling back to `AppSettings` when null — mirrors the existing `Issue.PageFitModeOverride ?? AppSettings.DefaultPageFitMode` resolution chain. Changing a setting while reading a book writes that book's override; there's no separate "reset to global" UI in v1 (clearing an override isn't a common enough need to justify one yet). |
| **Character & paragraph spacing** | `TextBlock.LetterSpacing` (character) and per-paragraph `Margin` (paragraph) — both natively supported, cheap. |
| **Page margin** | Controls the reading column's outer padding (replacing the current fixed `MaxWidth="640"` centered column with a setting-driven value). |
| **Word spacing** | Avalonia has no native word-spacing property. Implemented via the new `ParagraphView` custom control (see Components) inserting extra glyph advance-width after each space character during layout — not a `TextBlock` property. |
| **Accessible fonts** | Add `OpenDyslexic` to `BookFontFamilyOption`, bundled under `Assets/Fonts/` (OFL-licensed, same pattern as the existing `SourceSerif4`/`BebasNeue` bundled fonts). |
| **Themes** | Add `OledBlack` (true `#000000`) and `HighContrast` to `BookTheme`, alongside existing Light/Dark/Sepia/MatchAppSkin. |
| **Chrome auto-hide** | Toolbar/drawer-toggle row fades after ~2.5s of no pointer movement over the reading canvas, reappears on pointer-move-to-top-edge or any key press. New `IsChromeVisible` bool + `DispatcherTimer`, reset on `PointerMoved`. Toggle in the font sheet (`BookReaderAutoHideChrome`), default on. |
| **Selectable/highlightable text** | `TextBlock` is replaced by a new custom `ParagraphView : Control` (see Components) built on Avalonia's `TextLayout` API — the same layer `SelectableTextBlock`/`TextBox` are built on. This single control backs word spacing, character/paragraph spacing, drag-selection, and highlight-range rendering together, rather than four separate half-solutions. |
| **Highlight colors** | 4 fixed colors (yellow/green/blue/pink — matches the common convention in reading/annotation apps), plus an optional note per highlight. |
| **Highlight selection UX** | Click-drag over paragraph text → live selection fill while dragging → on release, a `Popup` anchored to the selection's bounding rect (via `TextLayout.HitTestTextRange`) shows the 4 color swatches + a note button. Tapping an *existing* highlight reopens the same popup for edit/delete instead of starting a new selection. |
| **Highlights drawer** | New "Highlights" entry in the existing drawer mechanism (same `IsTocOpen`/`IsBookmarksOpen` pattern → `IsHighlightsOpen`), listing all highlights for the book with excerpt/color/note, tap-to-jump, delete. |
| **PDF area capture** | New "Capture Region" tool on `PdfPageReaderScreen` — click-drag draws a rectangle over the current page's rendered `Bitmap`; stored as fractions of page width/height (zoom-independent) in a new `BookAnnotationImage` row, cropped to PNG under `%AppData%\Paperbunkr\annotations\`. A "Captures" panel (same drawer pattern) lists them with thumbnail + optional note + delete. |
| **Export** | New "Export Annotations" action on `BookDetailScreenViewModel` and mirrored in the reader's menu, scoped to a single book. Gathers `BookBookmark` + `BookHighlight` + `BookAnnotationImage` for that book and writes to one of: **Markdown** (chapter headings, blockquoted excerpts/notes, `![capture](relative-path)` image links — default, Obsidian-friendly), **CSV** (flat rows, no images, for spreadsheet review), **JSON** (full structured dump including image paths, for programmatic reuse). Format picked via dropdown in the save-file dialog; captured images are copied alongside the exported file, not embedded. Bulk/library-wide export is explicitly out of scope for v1 — per-book only. |

## Components

### 1. Data model (one EF migration)

- `AppSettings`: 9 new columns per the Decisions table above (font size, family, line/character/word/
  paragraph spacing, page margin, theme, auto-hide-chrome), enum columns using the existing
  `HasConversion<string>()` + sentinel pattern (`LibrarySortField` etc.).
- `Book`: nullable override columns mirroring the global settings (`FontSizeOverride: double?`,
  `FontFamilyOverride: BookFontFamilyOption?`, `LineSpacingOverride`, `CharacterSpacingOverride`,
  `WordSpacingOverride`, `ParagraphSpacingOverride`, `PageMarginOverride`, `ThemeOverride`).
- New `BookHighlight` entity (`Paperbunkr.Data.Entities`), parallel to `BookBookmark` but a range:
  `Id`, `BookId`, `Book?`, `ChapterIndex`, `StartOffset`, `EndOffset`, `Color` (enum: Yellow/Green/
  Blue/Pink), `Note: string?`, `Excerpt: string`, `CreatedTime`.
- New `BookAnnotationImage` entity: `Id`, `BookId`, `Book?`, `PageIndex`, `RectX/RectY/RectWidth/
  RectHeight` (doubles, fractions of page size 0–1), `ImagePath: string`, `Note: string?`, `CreatedTime`.
- `Book.Highlights` / `Book.AnnotationImages` navigation collections, same `Include()` pattern
  `BookReaderScreenViewModel.Load` already uses for `Bookmarks`.

### 2. `ParagraphView` — custom text control (`Paperbunkr.App/Views/ParagraphView.cs` + no XAML, pure `Control`)

- Constructed per-paragraph from `CurrentPageParagraphs` (replaces the `TextBlock` inside
  `BookReaderScreen.axaml`'s `ItemsControl.ItemTemplate`), taking the paragraph text, resolved font/
  spacing/theme settings, and the subset of `Book.Highlights` falling within that paragraph's
  chapter+offset range.
- Builds an `Avalonia.Media.TextFormatting.TextLayout` from the paragraph text; word spacing applied
  by inserting extra advance-width at each space-character boundary during layout construction
  (exact mechanism — explicit per-run advance override vs. zero-width spacer runs — to be validated
  by an implementation-time spike; see Risks).
- `Render(DrawingContext)`: draws persisted-highlight fills (via `HitTestTextRange` per highlight,
  ordered under the text), the live drag-selection fill (if active), then the text layout itself.
- `PointerPressed`/`PointerMoved`/`PointerReleased`: `HitTestPoint(Point)` resolves pointer position
  to a character offset; drag start/current offset become the live selection range. On release with
  a non-empty range, raises a `SelectionCompleted` event (paragraph-relative offset range + bounding
  rect) that `BookReaderScreenViewModel` turns into the color-palette `Popup` + eventual `BookHighlight`.
- Tapping inside an existing highlight's range (no drag) raises `HighlightTapped` instead, for the
  edit/delete popup.

### 3. `BookReaderSettings` + persistence

- `BookReaderSettings` (existing model) gains `CharacterSpacing`, `WordSpacing`, `ParagraphSpacing`,
  `PageMargin` observable properties and the two new `BookTheme` values.
- `BookReaderScreenViewModel.Load` seeds `BookReaderSettings` from `AppSettings` defaults, then
  applies the current `Book`'s override columns where non-null (mirrors the `Issue`/`AppSettings`
  fallback chain elsewhere in the codebase). Each `On*Changed` in `BookReaderSettings` writes back to
  the `Book`'s override column (not `AppSettings` — a mid-session change is book-scoped by design per
  the Decisions table); a still-open question for the plan phase is whether a first-run/global-default
  change needs a separate explicit entry point (e.g. from Preferences) versus only ever being set
  implicitly the first time someone changes a setting with no book open.

### 4. Font sheet drawer (`BookReaderScreen.axaml`)

- Extends the existing `IsFontSheetOpen` drawer: new sliders for Character Spacing, Word Spacing,
  Paragraph Spacing, Page Margin (same slider style as the existing Text Size row); `OpenDyslexic`
  added to the Font radio group; `OledBlack`/`HighContrast` added to the Theme swatch row.

### 5. Chrome auto-hide

- `BookReaderScreenViewModel`: new `IsChromeVisible` bool, `DispatcherTimer` (2.5s), reset on a
  `PointerMoved` handler wired to the reading canvas root in `BookReaderScreen.axaml.cs` (same file
  that already wires other reader input). Any drawer being open suppresses auto-hide (chrome must stay
  visible while TOC/Bookmarks/Highlights/Search/FontSheet is open — reuses the existing
  `IsTocOpen || IsFontSheetOpen || IsBookmarksOpen || IsSearchOpen` check pattern, extended with
  `IsHighlightsOpen`).

### 6. Highlights drawer + drag-select flow

- New `IsHighlightsOpen` bool + `OpenHighlights`/`CloseHighlights` commands, same shape as
  `OpenBookmarks`/`CloseBookmarks`.
- New `Highlights: ObservableCollection<BookHighlightSummary>` (mirrors `BookBookmarkSummary`),
  loaded in `Load` alongside `Bookmarks`.
- `SelectionCompleted`/`HighlightTapped` events from `ParagraphView` drive a new lightweight
  `HighlightPopupViewModel` (color choice + note text + save/delete), shown via a `Popup` positioned
  from the event's bounding rect.

### 7. PDF capture tool (`PdfPageReaderScreen`/`PdfPageReaderScreenViewModel`)

- New `IsCaptureMode` toggle; while active, click-drag over the page `Image` draws a selection
  rectangle (simple `Border`-based overlay, not `ParagraphView` — this is image cropping, not text).
- On release: crop `CurrentPage` (the rendered `Bitmap`) to the selected rect, save PNG via
  `Paperbunkr.Common.Drawing` (extending the existing `PdfImages` helper), insert `BookAnnotationImage`.
- New "Captures" drawer entry, same pattern as Highlights, showing thumbnail + note + delete.

### 8. Export

- New `AnnotationExportService` (`Paperbunkr.Engine` or `Paperbunkr.App/Services`, TBD at plan time
  based on where similar export logic like CBL export currently lives) with one method per format,
  taking a `Book` (with `Bookmarks`/`Highlights`/`AnnotationImages` loaded) and an output path.
- Entry points: a button on `BookDetailScreenViewModel` and a menu item in `BookReaderScreenViewModel`,
  both opening a save-file dialog with a format dropdown (Markdown default), then invoking the service.

## Risks / Open Questions

- **`TextLayout` word-spacing mechanism**: Avalonia's `TextLayout` doesn't expose a direct
  "extra advance after this character" knob in its high-level API; the exact approach (custom
  `TextRun`/`TextCharacters` with per-run properties vs. splitting each paragraph into word-length
  runs with explicit spacer runs between them) needs a short implementation-time spike before the full
  `ParagraphView` is built, so the plan should sequence that spike first and treat the rest of
  `ParagraphView` as blocked on its result.
- **Hit-testing accuracy inside a virtualized/wrapped multi-line paragraph** — `HitTestPoint`/
  `HitTestTextRange` are the right APIs in principle (they're what Avalonia's own text controls use
  internally) but haven't been exercised in this codebase yet; the spike above should also confirm
  drag-selection across a line-wrap boundary behaves correctly.
- **OpenDyslexic font file** — needs to be sourced and its OFL license file added under
  `Assets/Fonts/`, same as the existing bundled fonts; not yet downloaded.
- **Per-book override reset UX** — deferred per the Decisions table; may need a follow-up if users
  find themselves wanting to "unstick" a book from an accidental override.

## Testing

- Unit tests: `ParagraphView` hit-testing math (offset↔point round-trips) and word-spacing layout
  output; `BookHighlight`/`BookAnnotationImage` CRUD mirroring existing `BookBookmarkSummary`/
  `BookFolderScanServiceTests`-style patterns; one export-format test per format (golden-file style,
  mirroring how other structured exports in this codebase are tested); an EF migration test mirroring
  `AddBooksBrowseStateMigrationTests`.
- UI automation (`Paperbunkr.UiTests`, FlaUI): drag-select → color popup → persisted highlight flow;
  PDF capture-rectangle → saved annotation flow; chrome auto-hide/reveal timing. Flagged because
  drag-gesture and hit-testing regressions are exactly what unit tests miss in this codebase (noted
  precedent: the reader-polish rounds found several real hit-testing/gesture bugs only via on-screen
  or UI-automation verification).
