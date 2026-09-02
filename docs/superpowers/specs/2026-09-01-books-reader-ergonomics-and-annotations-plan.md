# Books Reader Ergonomics + Annotations & Export — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md*

Cross-spec note: Step 4 below builds `ParagraphViewAutomationPeer` in the same pass as `ParagraphView`
itself, per the design's Cross-Spec Dependency section — this is pulled forward from the (not yet
separately planned) accessibility spec because shipping `ParagraphView` without it would silently break
screen-reader access to every book.

## Step 1: Data model + migration

**Files:**
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit) — add 9 columns: `BookReaderFontSize` (double),
  `BookReaderFontFamily`/`BookReaderLineSpacing`/`BookReaderTheme` (enums, reusing
  `BookFontFamilyOption`/`BookLineSpacingOption`/`BookTheme` from `Paperbunkr.App.Models` — note these
  enums currently live in the App project; `Paperbunkr.Data` can't reference `Paperbunkr.App`, so this
  step also moves `BookFontFamilyOption`, `BookLineSpacingOption`, `BookTheme` from
  `src/Paperbunkr.App/Models/BookReaderSettings.cs` into `Paperbunkr.Data.Entities` (their own files,
  matching `BookFormat.cs`'s one-enum-per-file convention), leaving `BookReaderSettings` in App
  referencing them via `using Paperbunkr.Data.Entities;` — same direction every other
  `AppSettings`-backed enum in this codebase already goes), `BookReaderCharacterSpacing`/
  `BookReaderWordSpacing`/`BookReaderParagraphSpacing`/`BookReaderPageMargin` (double),
  `BookReaderAutoHideChrome` (bool, default true).
- `src/Paperbunkr.Data/Entities/Book.cs` (edit) — add nullable override columns:
  `FontSizeOverride: double?`, `FontFamilyOverride: BookFontFamilyOption?`,
  `LineSpacingOverride: BookLineSpacingOption?`, `CharacterSpacingOverride: double?`,
  `WordSpacingOverride: double?`, `ParagraphSpacingOverride: double?`, `PageMarginOverride: double?`,
  `ThemeOverride: BookTheme?`.
- `src/Paperbunkr.Data/Entities/BookHighlightColor.cs` (new) — enum `Yellow`/`Green`/`Blue`/`Pink`.
- `src/Paperbunkr.Data/Entities/BookHighlight.cs` (new) — `Id`, `BookId`, `Book?`, `ChapterIndex`,
  `StartOffset`, `EndOffset`, `Color: BookHighlightColor`, `Note: string?`, `Excerpt: string`,
  `CreatedTime`, mirroring `BookBookmark.cs`'s shape/doc-comment style.
- `src/Paperbunkr.Data/Entities/BookAnnotationImage.cs` (new) — `Id`, `BookId`, `Book?`, `PageIndex`,
  `RectX`/`RectY`/`RectWidth`/`RectHeight: double`, `ImagePath: string`, `Note: string?`, `CreatedTime`.
- `src/Paperbunkr.Data/Entities/Book.cs` (edit, same file as above) — add
  `List<BookHighlight> Highlights` / `List<BookAnnotationImage> AnnotationImages` navigation
  collections, same shape as the existing `Bookmarks` collection.
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit) — `DbSet<BookHighlight>`, `DbSet<BookAnnotationImage>`;
  `OnModelCreating` config for the new enum columns using the established
  `HasConversion<string>().HasMaxLength(32).HasDefaultValue(X).HasSentinel(X)` pattern (see the
  existing `BooksSortField` config, ~line 926, as the template).
- `src/Paperbunkr.Data/Migrations/` (new, via `dotnet ef migrations add AddBookReaderErgonomicsAndAnnotations`).

**What:** One migration covering every schema change this spec needs. Moving the three `Book*Option`/
`BookTheme` enums out of `Paperbunkr.App.Models` is a real, load-bearing prerequisite here (not
optional cleanup) — `AppSettings` needs to reference them and lives in `Paperbunkr.Data`, which cannot
depend on `Paperbunkr.App`.

**Depends on:** none.

**Verify:** `dotnet ef migrations add` produces a clean up/down; `dotnet build` on the full solution
succeeds after the enum move (fixes any `Paperbunkr.App` files that referenced the old namespace); a
new `AddBookReaderErgonomicsAndAnnotationsMigrationTests.cs` in `Paperbunkr.Data.Tests` mirroring
`AddBooksBrowseStateMigrationTests.cs` (apply migration to a fresh in-memory/temp SQLite db, assert
new columns/tables exist with expected defaults).

---

## Step 2: `BookReaderSettings` model extensions + OpenDyslexic font asset

**Files:**
- `src/Paperbunkr.App/Models/BookReaderSettings.cs` (edit) — add `CharacterSpacing`, `WordSpacing`,
  `ParagraphSpacing`, `PageMargin` observable double properties (defaults matching current visual
  behavior: `CharacterSpacing=0`, `WordSpacing=0`, `ParagraphSpacing=10` — the existing hardcoded
  paragraph `Margin="0,0,0,10"` value — `PageMargin=40`, the existing `ScrollViewer` padding); add
  `OpenDyslexic` to `BookFontFamilyOption` and `OledBlack`/`HighContrast` to `BookTheme` (now in
  `Paperbunkr.Data.Entities` per Step 1) with their `ResolvedFontFamily`/`Background`/`Foreground`
  switch-expression arms (`OledBlack` → true `#000000` bg; `HighContrast` → max-contrast pairing, e.g.
  pure black/white).
- `src/Paperbunkr.App/Assets/Fonts/OpenDyslexic-Regular.otf` (new) +
  `src/Paperbunkr.App/Assets/Fonts/OFL-OpenDyslexic.txt` (new) — sourced from the OpenDyslexic project's
  OFL release, same bundling convention as the existing `SourceSerif4-Regular.ttf`/`OFL-SourceSerif4.txt`.

**What:** Pure model/asset addition, no rendering changes yet — `ResolvedFontFamily` for
`OpenDyslexic` follows the same `FontFamily.Parse("OpenDyslexic,...")` pattern as the other three
options, with a same-family web-safe fallback chain.

**Depends on:** Step 1 (enums now live in `Paperbunkr.Data.Entities`).

**Verify:** `Paperbunkr.App.Tests` build succeeds; a quick manual check that the font file loads (no
`FontFamily.Parse` exception at runtime) — full visual verification happens once Step 6 wires it into
the font sheet.

---

## Step 3: Spike — validate the `TextLayout` word-spacing mechanism

**Files:** none shipped — a throwaway console/unit-test experiment (delete or fold into Step 4's real
tests once resolved), e.g. a scratch method in a temporary test file under `Paperbunkr.App.Tests`.

**What:** Per the design's Risks section, this must be resolved before Step 4 is built on top of it,
not assumed. `MeasureParagraphHeight` in `BookReaderScreenViewModel.cs` already proves basic
`TextLayout` construction works in this codebase (font/size/wrapping/line-height), but word-spacing
needs inserting extra advance-width specifically after space characters — validate which of the two
candidate mechanisms from the design (per-run `TextRunProperties` overrides on space-delimited
`TextCharacters` runs, vs. explicit zero-width spacer runs between words) actually produces correct,
stable layout and hit-testing via `Avalonia.Media.TextFormatting.TextLayout`. Record the chosen
mechanism in `ParagraphView`'s own doc comment when Step 4 lands (no separate design-doc update needed
— this is an implementation detail the design already flagged as deferred to this point).

**Depends on:** none (can run in parallel with Steps 1-2, but must complete before Step 4 starts).

**Verify:** the spike itself — construct a `TextLayout` with the candidate mechanism, render/measure
it, and confirm (a) visually correct extra spacing between words at a few different `WordSpacing`
values and (b) `HitTestPoint`/`HitTestTextRange` still resolve correctly against the adjusted layout
(a hit-test that's off by the accumulated spacing offset would silently break selection later). If
neither mechanism produces reliable hit-testing, fall back per the design's own risk note and flag
that explicitly rather than shipping broken selection.

---

## Step 4: `ParagraphView` control + `ParagraphViewAutomationPeer`

**Files:**
- `src/Paperbunkr.App/Views/ParagraphView.cs` (new) — `Control`-derived, per design Component 2:
  `TextLayout`-based rendering (text + word-spacing per the Step 3 spike result), `Render(DrawingContext)`
  drawing persisted-highlight fills + live-selection fill + text, `PointerPressed`/`PointerMoved`/
  `PointerReleased` handlers resolving drag-selection via `HitTestPoint`, `SelectionCompleted`/
  `HighlightTapped` events (paragraph-relative offset range + bounding `Rect`). Takes paragraph text,
  resolved `BookReaderSettings`, and the subset of highlights in-range as constructor/property inputs.
- `src/Paperbunkr.App/Views/ParagraphViewAutomationPeer.cs` (new) — `ControlAutomationPeer` subclass
  per the accessibility spec's Component 2: `GetNameCore()` returns the paragraph's plain text,
  `GetAutomationControlTypeCore() => AutomationControlType.Text`. `ParagraphView.OnCreateAutomationPeer()`
  returns it.
- `src/Paperbunkr.App.Tests/ParagraphViewTests.cs` (new) — headless-Avalonia tests (this codebase
  already has `Avalonia.Headless` wired into `Paperbunkr.App.Tests`; follow whatever existing headless
  test in this project sets up the headless app lifetime, per this codebase's own noted headless-test
  gotchas from earlier reader work) for: offset↔point hit-testing round-trips, highlight-range
  rendering doesn't throw for out-of-bounds ranges, and `ParagraphViewAutomationPeer.GetNameCore()`
  returns the expected plain text.

**What:** The single largest, highest-risk piece of this plan. Builds directly on the Step 3 spike's
chosen mechanism — if that spike revealed hit-testing problems, this step's design needs to account
for them rather than proceeding as originally scoped.

**Depends on:** Step 3 (spike result), Step 2 (settings shape it consumes).

**Verify:** `ParagraphViewTests` green; a manual on-screen check once Step 6 wires it in (this control
can't be fully verified in isolation — its real test is rendering real book paragraphs).

---

## Step 5: Settings persistence (global default + per-book override resolution)

**Files:**
- `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit) — in `LoadBook`, after loading
  `_book`, seed `Settings` from `context.GetOrCreateAppSettings()` (the existing helper used elsewhere
  in this codebase, e.g. `PdfPageReaderScreenViewModel.LoadBook`) then apply any non-null `_book.*Override`
  columns on top (mirrors the `Issue.PageFitModeOverride ?? AppSettings.DefaultPageFitMode` chain).
  Each `BookReaderSettings.On*Changed` (already wired via the constructor's
  `Settings.PropertyChanged += (_, _) => RecomputeCurrentPage()`) needs a second subscriber that writes
  the changed value back to the current `_book`'s override column and saves — add a
  `PersistSettingsOverride()` private method (same "open a fresh context, write, SaveChanges" shape as
  `PersistPosition()`) invoked from a new `Settings.PropertyChanged` handler, distinguishing it from
  the existing recompute-only handler.

**What:** Closes the "session-only settings" gap the design's Background section calls out. No global
Preferences UI entry point is added in this step or spec — per the design's own explicit deferral,
global defaults are only ever set implicitly (there is no book open at the very first app run, so the
constructed-default `BookReaderSettings` values become the initial global default the first time
`GetOrCreateAppSettings()` is called with unset columns, via the sentinel/default pattern from Step 1).

**Depends on:** Step 1 (columns exist), Step 2 (properties exist on `BookReaderSettings`).

**Verify:** new `BookReaderScreenViewModelTests` cases (extending the existing test file) — changing a
setting while a book is open persists to that book's override row; reopening a different book without
an override falls back to the global `AppSettings` value; reopening the same book restores its override.

---

## Step 6: Wire `ParagraphView` into the reader + font sheet UI additions

**Files:**
- `src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit) — replace the `TextBlock` inside
  `ItemsControl.ItemTemplate` (currently lines ~107-117) with `ParagraphView`, bound the same way
  (paragraph text + `Settings` + in-range highlights, the last of which is empty until Step 8 wires
  real highlight data through); add four new sliders (Character Spacing, Word Spacing, Paragraph
  Spacing, Page Margin) to the font sheet's existing `StackPanel`, same visual pattern as the current
  "TEXT SIZE" slider row; add `OpenDyslexic` as a fourth button in the FONT row; add `OledBlack`/
  `HighContrast` as two more swatches in the THEME row, same `Border.themeSwatch` pattern as the
  existing four.
- `src/Paperbunkr.App/Views/BookReaderScreen.axaml.cs` (edit, if `ParagraphView` needs a code-behind
  hookup beyond pure XAML binding — e.g. if `ItemsControl` virtualization requires per-item event
  wiring that XAML alone can't express).

**What:** This is where `ParagraphView` actually replaces `TextBlock` in the live reader and becomes
visually verifiable for the first time — word spacing, character/paragraph spacing, and page margin
should all be checkable on-screen after this step, even before highlighting (Step 8) works.

**Depends on:** Step 4 (`ParagraphView` exists), Step 5 (settings resolve correctly), Step 2 (new
font/theme options exist).

**Verify:** on-screen check (per this project's UI-work convention) — open a book, adjust every new
slider and confirm visible effect, switch through all 7 themes and both new fonts;
`BookReaderScreenViewModelTests` unaffected (this step is view-layer only).

---

## Step 7: Chrome auto-hide

**Files:**
- `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit) — `IsChromeVisible` already
  exists (currently toggled only by `OnContentPointerPressed`); add a `DispatcherTimer` (2.5s) that,
  when `Settings` — actually `AppSettings.BookReaderAutoHideChrome` (a global toggle, not per-book) —
  is enabled, sets `IsChromeVisible = false` after the interval, reset on every pointer-move; suppress
  auto-hide while any drawer is open (extend the existing `IsTocOpen || IsFontSheetOpen ||
  IsBookmarksOpen || IsSearchOpen` check with `|| IsHighlightsOpen` once Step 8 adds it).
- `src/Paperbunkr.App/Views/BookReaderScreen.axaml.cs` (edit) — new `PointerMoved` handler on
  `RootGrid` calling a new `vm.NotifyPointerActivity()` method that both shows chrome (if hidden by
  auto-hide, not if the user explicitly closed it) and resets the timer.
- `src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit, minor) — add the toggle checkbox for
  `AppSettings.BookReaderAutoHideChrome` to the font sheet (or a small settings row near it).

**What:** Additive to the existing tap-to-toggle behavior, not a replacement — auto-hide only fires
while chrome is currently visible from an explicit tap.

**Depends on:** Step 1 (the `AppSettings` column).

**Verify:** manual on-screen timing check (2.5s fade after no pointer movement, reappears on
pointer-move-to-top-edge or keypress); this is exactly the kind of gesture/timing behavior this
codebase's own testing notes flag as not unit-testable.

---

## Step 8: Highlights drawer + drag-select flow

**Files:**
- `src/Paperbunkr.App/Models/BookHighlightSummary.cs` (new) — mirrors `BookBookmarkSummary.cs`, plus
  `Color: BookHighlightColor`, `Note: string?`.
- `src/Paperbunkr.App/ViewModels/HighlightPopupViewModel.cs` (new) — color choice (4 swatches),
  note text, Save/Delete commands, positioned via a bounding `Rect` passed in from the triggering event.
- `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit) — `Highlights:
  ObservableCollection<BookHighlightSummary>` loaded in `LoadBook` alongside `Bookmarks` (extend the
  existing `context.Books.Include(b => b.Bookmarks)` to `.Include(b => b.Highlights)`); `IsHighlightsOpen`
  bool + `OpenHighlights`/`CloseHighlights` commands (same shape as `OpenBookmarks`/`CloseBookmarks`,
  added to `CloseAllOverlays()`); handlers for `ParagraphView.SelectionCompleted` (opens
  `HighlightPopupViewModel` for a new highlight) and `HighlightTapped` (opens it pre-filled for
  edit/delete); `CreateHighlight`/`DeleteHighlight` methods following the existing
  `ToggleBookmark`/`DeleteBookmark` "open a fresh context, write, SaveChanges" shape.
- `src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit) — new Highlights drawer (same
  `Grid`/scrim/`Border` structure as the Bookmarks drawer, listing `Highlights` with color swatch +
  excerpt + note + delete button); a `Popup` for `HighlightPopupViewModel`, anchored via
  `Placement="Pointer"` or an explicit position derived from the event's bounding rect (exact Popup
  placement API to confirm against this Avalonia version during implementation); new toolbar icon
  alongside the existing TOC/Search/Bookmarks/Font icons.
- `src/Paperbunkr.App/Views/BookReaderScreen.axaml.cs` (edit) — wire `ParagraphView.SelectionCompleted`/
  `HighlightTapped` events (raised per-item inside the `ItemsControl`) to the view model.

**What:** The interactive centerpiece of the annotations half of this spec — everything here depends
on `ParagraphView` (Step 4/6) actually being live in the reader first.

**Depends on:** Step 4, Step 6, Step 1 (`BookHighlight` entity).

**Verify:** new `BookReaderScreenViewModelTests` cases (create/delete highlight persists correctly,
`Highlights` collection updates); UI automation (`Paperbunkr.App.UiTests`) for the full drag-select →
color popup → persisted highlight flow, per the design's own testing section flagging this as a
gesture regression risk unit tests won't catch.

---

## Step 9: PDF area capture

**Files:**
- `src/Paperbunkr.App/Views/PageCanvas.cs` (edit) — add one new public read-only method (e.g.
  `GetCurrentImageBounds(): Rect`) returning the currently-rendered image's bounds in control-local
  coordinates, computed from whatever internal fields `Render()` already uses for zoom/pan/fit
  placement. **Deliberately minimal and additive** — `PageCanvas` is a large (~1800-line), heavily
  shared control (also used by the comic reader), so this step must not touch its existing rendering
  or gesture logic, only expose a value it already computes internally.
- `src/Paperbunkr.App/Views/CaptureOverlay.cs` (new) — simple `Control`-derived (or `Border`-based, if
  that proves sufficient) transparent overlay drawn on top of `PageCanvas` in `PdfPageReaderScreen`,
  active only while `IsCaptureMode` is on; click-drag draws a selection rectangle; on release, converts
  the drawn screen-space rectangle to page-fraction coordinates using `PageCanvas.GetCurrentImageBounds()`
  and raises a `RegionCaptured` event with the resulting `RectX/RectY/RectWidth/RectHeight`.
- `src/Paperbunkr.Common/Drawing/PdfImages.cs` (edit) — add a crop-and-save-PNG helper taking the
  rendered page `Bitmap` + a fractional rect + a destination path.
- `src/Paperbunkr.App/Models/BookAnnotationImageSummary.cs` (new) — mirrors `BookBookmarkSummary.cs`.
- `src/Paperbunkr.App/ViewModels/PdfPageReaderScreenViewModel.cs` (edit) — `IsCaptureMode` toggle
  command; `AnnotationImages: ObservableCollection<BookAnnotationImageSummary>` loaded in `LoadBook`;
  `RegionCaptured` handler that crops+saves via the `PdfImages` helper to
  `%AppData%\Paperbunkr\annotations\`, inserts a `BookAnnotationImage` row; `IsCapturesOpen` +
  open/close/delete commands, same drawer shape as the reflow reader's Bookmarks.
- `src/Paperbunkr.App/Views/PdfPageReaderScreen.axaml` (edit) — `CaptureOverlay` added over
  `PageCanvasControl`; a capture-mode toggle button in the top toolbar; a Captures drawer (same
  scrim/`Border` pattern as `BookReaderScreen`'s drawers, though this screen currently has no drawer
  precedent of its own to copy — mirror the reflow reader's structure instead).

**What:** Separate control path from `ParagraphView` — this is image cropping over a `Bitmap`, not
text layout. The `PageCanvas.GetCurrentImageBounds()` addition is the one piece of shared-control
surface area this step touches; keeping it to a single new accessor method (not modifying existing
zoom/pan/render code) is the specific risk-mitigation for working in a file this central to the app.

**Depends on:** Step 1 (`BookAnnotationImage` entity).

**Verify:** on-screen check across different zoom/pan states (confirms `GetCurrentImageBounds()` is
actually correct, not just correct at 100% zoom); UI automation for the capture-rectangle → saved
annotation flow, same rationale as Step 8's highlight-flow UI test.

---

## Step 10: Export

**Files:**
- `src/Paperbunkr.Data/Books/AnnotationExportService.cs` (new) — static class, same shape as
  `CblReadingListIO`'s `Export(context, id, filePath)` precedent: `ExportMarkdown`/`ExportCsv`/
  `ExportJson(PaperbunkrDbContext context, int bookId, string filePath)`, each loading the book with
  `Bookmarks`/`Highlights`/`AnnotationImages` included and writing per the design's per-format
  Decisions (Markdown: chapter headings + blockquoted excerpts/notes + relative-path image links,
  copying annotation images alongside the output file; CSV: flat rows, no images; JSON: full structured
  dump including image paths).
- `src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs` (edit) — new `ExportAnnotationsCommand`
  opening a save-file dialog via the existing `FilePickerService` (format dropdown: Markdown default,
  CSV, JSON) then invoking the matching `AnnotationExportService` method.
- `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit) — mirrored menu command, same
  invocation shape.

**What:** The one component with no interactive/gesture surface — straightforward service + two thin
UI entry points.

**Depends on:** Step 1 (all three source entities), Step 8/9 for there to be real highlight/capture
data to export (though the service itself can be built and tested against fixture data independent of
those UI flows landing first).

**Verify:** `AnnotationExportServiceTests` in `Paperbunkr.Data.Tests` — one golden-file-style test per
format against a fixture book with a bookmark, a highlight, and an annotation image; confirms image
files are actually copied alongside Markdown/JSON output, not just referenced.

---

## Cross-cutting test/verification pass

After all steps land: full `dotnet test` across `Paperbunkr.Data.Tests`/`Paperbunkr.App.Tests` green;
a `Paperbunkr.App.UiTests` (FlaUI) pass covering the three flagged gesture-risk flows (drag-select →
highlight, PDF capture-rectangle, chrome auto-hide/reveal timing) per the design's own Testing section;
a manual on-screen pass through every new font-sheet control, both readers, and an end-to-end export
of a real book with mixed annotation types.
