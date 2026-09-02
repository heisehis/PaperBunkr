# Books Reader HUD Redesign — Design

## Background

The EPUB/FB2/MOBI reflow reader (`BookReaderScreen.axaml`/`BookReaderScreenViewModel`) and the PDF
page-image reader (`PdfPageReaderScreen.axaml`/`PdfPageReaderScreenViewModel`) each have their own,
independently-built chrome: top/bottom toolbar bars, overlay drawers/sheets, icon buttons. They
share no code and have drifted into two visibly different systems — different color sources
(`DynamicResource Pb*` skin tokens vs. entirely separate hardcoded local brushes), different icon
style (emoji glyphs, two different sets), different button shapes (circular hover-pill vs. flat),
different overlay directions (left drawer / top dropdown / bottom sheet, inconsistently), and
different show/hide behavior (tap-to-hide vs. always-visible).

**Scope: the chrome/HUD layer only** — the toolbar bars, overlay drawers/sheets, buttons, icons, and
their visual language. **Not in scope:** either reader's actual content-rendering engine. The EPUB
reflow reader's WebView2-based rendering rewrite (`2026-09-02-books-reflow-reader-webview-redesign-
design.md`) shipped immediately before this spec and is untouched here; the PDF reader's rasterized
page-image pipeline (`PageCanvas`/`PdfBookSource`/PDFium) is likewise untouched — this spec only adds
a theme setting that tints PDF's canvas backdrop, not its rendering pipeline.

**CE note:** no CE equivalent — Books (EPUB/PDF novels) is a Paperbunkr-original feature area beyond
ComicRack CE's scope, same standing note as every prior Books spec.

## Decisions

| Area | Decision |
|---|---|
| **Unification** | One shared `ReaderChrome` control (top bar + bottom bar) embedded in both screens, replacing their hand-rolled `Border`/`Grid` bars. Every possible button (TOC/Search/Bookmarks/Highlights/Export/Captures/CaptureToggle/FontTheme/Close) is a named nullable-`ICommand` property on the control; a button hides itself when its bound command is `null`. EPUB's ViewModel binds 7 of them; PDF's binds 4 (`Captures`/`CaptureToggle`/`FontTheme`/`Close`). |
| **Icons** | FluentIcons.Avalonia (already the app-wide icon system — `LibraryToolbar.axaml`, Home, Detail screens) replaces every emoji glyph (☰🔍🔖✎⇩ / ←🖼✂◀▶) in both readers. One icon family, no new package. |
| **Button style** | Circular hover-pill (`chromeIcon`-style: transparent at rest, soft `#22FFFFFF`-equivalent circle on hover, tinted-fill when active/toggled) — EPUB's current pattern, extended to PDF's buttons. |
| **Show/hide behavior** | Kept different, made an explicit `ReaderChromeMode` enum (`TapToHide` / `AlwaysVisible`) rather than an implicit per-screen difference. EPUB stays `TapToHide` (chrome starts hidden, tap the page to reveal). PDF stays `AlwaysVisible` (page number/zoom/theme are reference info glanced at continuously, not chrome to dismiss). |
| **Chrome color** | Theme-tinted, not a fixed dark overlay and not the app's own skin. Chrome background + icon-foreground brushes are computed from the active `BookTheme`, extending the existing `BookReaderSettings.Background`/`Foreground` per-theme mapping pattern with a chrome-specific (translucent) variant. |
| **PDF theme** | PDF gains a real theme setting for the first time: Light/Dark/Sepia (a 3-value subset of `BookTheme` — no MatchAppSkin/OledBlack/HighContrast, no font/spacing sliders, since there's no reflowable text to restyle). Reuses **existing, already format-agnostic** storage — `Book.ThemeOverride` (nullable `BookTheme`) and `AppSettings.BookReaderTheme` already live on `Book`/`AppSettings`, not EPUB-specific — so no new column or migration. The theme tints the canvas/backdrop behind the page image and the chrome bars; it does **not** recolor the rasterized PDF page pixels themselves (that would be a real image-processing feature, explicitly out of scope here). |
| **Bottom bar progress** | Unified on a thin progress-bar-plus-label (PDF's current shape), replacing EPUB's percent-only text. Each reader feeds its own fraction + label: EPUB shows "Chapter 6 · 42%" (chapter-start fraction, per the WebView redesign's own simplification), PDF shows "12 / 300" (exact page fraction). |
| **Overlay panel direction** | Split by content shape, made a deliberate rule instead of today's accidental mix: **lists** (TOC, Bookmarks, Highlights, Search, and PDF's Captures) open as **left drawers**; **controls** (Font & Theme) open as a **bottom sheet**. This resolves two existing inconsistencies: Search currently drops from the top (moves to a left drawer, search box pinned at its top instead of centered on a full-width overlay), and PDF's Captures drawer currently opens from the **right** (moves to left, matching every other list drawer in both readers). |
| **Font & Theme sheet layout** | Redesigned, not reskinned: today's 8 flat stacked ALL-CAPS-labeled sections become 2-3 grouped mini-cards — **Typography** (size, font family, line spacing), **Spacing & margins** (character/word/paragraph spacing, page margin), **Theme** (swatches + auto-hide toggle). EPUB shows all three cards; PDF shows only the Theme card (3 swatches, no auto-hide toggle since its chrome doesn't hide). |

## Components

1. **`ReaderChrome`** (new `UserControl`, `src/Paperbunkr.App/Views/ReaderChrome.axaml`/`.cs`) — top
   bar (title + feature-gated icon button row) and bottom bar (prev/progress-track+label/next).
   `Mode` (`ReaderChromeMode`) property; `ChromeBackground`/`ChromeForeground` brushes bound from the
   host screen's resolved `BookTheme`. Named nullable-`ICommand` properties per button, `IsVisible`
   driven by a `command != null` converter (an existing pattern in this codebase, not a new one).
2. **`ReaderListDrawer`** (new `UserControl`) — shared left-drawer scaffold (scrim `Popup` +
   `ShouldUseOverlayLayer="False"` panel, per the WebView-airspace note already established in
   `BookReaderScreen.axaml`, + header `TextBlock` + optional pinned search `TextBox` + an
   `ItemsControl`/`ContentPresenter` slot). Used for TOC, Bookmarks, Highlights, Search (EPUB) and
   Captures (PDF). Item templates stay per-screen (the five list item shapes —
   `BookChapterSummary`/`BookBookmarkSummary`/`BookHighlightSummary`/`BookSearchResult`/
   `BookAnnotationImageSummary` — are unrelated types), only the drawer scaffold itself is shared.
3. **`ReaderSettingsSheet`** (new `UserControl`) — shared bottom-sheet scaffold with the three
   mini-card sections. `HasTypographyControls` bool gates the Typography/Spacing cards (`true` for
   EPUB, `false` for PDF). Theme card's swatch set comes from a bindable collection so EPUB's 6 and
   PDF's 3 both render off the same template.
4. **Chrome/sheet tint helper** — a small static mapping (alongside `BookReaderSettings`'s existing
   `Background`/`Foreground` switch) producing the translucent chrome-bar brush and icon-foreground
   brush per `BookTheme`. Single source of truth so `ReaderChrome` and `ReaderSettingsSheet` tint
   identically.
5. **ViewModel changes**:
   - `BookReaderScreenViewModel`: rewire its existing commands (`OpenTocCommand` etc.) onto
     `ReaderChrome`'s named properties; no command logic changes.
   - `PdfPageReaderScreenViewModel`: gains `BookReaderSettings`-backed theme state (reusing the same
     type EPUB uses — it already tolerates fields a caller never touches, so PDF simply never sets
     `FontSize`/`CharacterSpacing`/etc., only `Theme`), read from/written to `Book.ThemeOverride`/
     `AppSettings.BookReaderTheme` the same way `BookReaderScreenViewModel.LoadBook`'s resolution
     chain already does. Gains `OpenFontThemeCommand`/`IsFontSheetOpen`; `Captures`/`CaptureToggle`
     commands move onto `ReaderChrome`/`ReaderListDrawer` instead of the screen's own hand-rolled
     buttons.
6. **Icon swap** — every `Button.Content="<emoji>"` across both screens' XAML replaced with
   `<fi:SymbolIcon Symbol="..."/>` (exact symbol names confirmed against the installed
   FluentIcons.Avalonia package during implementation, not guessed here).

## Risks / Open Questions

- **AutomationId/AutomationProperties.Name continuity**: `BookReaderAccessibilityTests` (shipped
  2026-09-02) and the existing `TocToggleButton`/`SearchToggleButton`/`BookmarksToggleButton`/etc.
  AutomationIds must land on `ReaderChrome`'s equivalent buttons unchanged, not silently dropped —
  this cycle already had one real FlaUI control-view-filter surprise, so this needs explicit
  verification, not an assumption that "the control still has a button so the ID must still work."
- **Chrome-tint contrast**: translucent theme-tinted chrome over six different content themes (plus
  PDF's new three) needs a real contrast check, especially High Contrast and OLED Black — the prior
  accessibility spec's "verify, don't assume" discipline applies here too.
- **FluentIcons symbol coverage**: most glyphs (hamburger/TOC, search, bookmark, highlight/pen,
  download/export, close, chevrons, capture/scissors) have obvious Fluent equivalents; the "Aa"
  typography glyph may not have a direct stock symbol and might need `PathIcon`/text fallback —
  resolve during implementation, not assumed here.
- **`ReaderListDrawer`/`ReaderSettingsSheet` width tuning**: TOC/Bookmarks/Highlights currently use
  300-320px; Search's pinned search box and PDF's Captures thumbnail grid may want different widths —
  an implementation-time tuning detail, not a blocking design decision.
- **Sparse PDF sheet**: PDF's Font & Theme sheet is just one mini-card (Theme, 3 swatches) — worth an
  on-screen look to confirm it doesn't read as broken/empty rather than deliberately minimal.

## Testing

- **Unit**: `ReaderChrome` button-hidden-when-command-null logic; chrome/sheet tint-per-`BookTheme`
  mapping determinism; `PdfPageReaderScreenViewModel` theme read/write round-trip through
  `Book.ThemeOverride`/`AppSettings.BookReaderTheme`.
- **Manual/on-screen** (chrome/layout changes aren't meaningfully unit-testable): both readers'
  chrome visually consistent (icons, button shape, spacing, theme tint); EPUB tap-to-hide vs. PDF
  always-visible both still behave correctly; all list drawers open from the left uniformly
  (including Search's new shape and Captures' new side); Font & Theme sheet's grouped-card layout
  renders correctly for both the full (EPUB) and Theme-only (PDF) configurations; PDF theme swap
  actually retints its canvas backdrop and chrome; `BookReaderAccessibilityTests` still passes
  (reader reachable, AutomationIds intact) after the button/control rewiring.
