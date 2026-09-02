# Books Reader HUD Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md*

## Survey notes (grounding for the steps below)

- **FluentIcons.Avalonia symbols confirmed** (reflected directly off the installed
  `fluenticons.common/2.1.337` assembly, not guessed): `List` (TOC), `Search`, `Bookmark` (has an
  `IsFilled` bool on `SymbolIcon` for the bookmarked/active state, same as the library's existing
  usage elsewhere in this app), `Highlight`, `ArrowExport` (export annotations), `TextFont`
  (font/theme sheet trigger), `Dismiss` (close), `ChevronLeft`/`ChevronRight` (page-turn), `Crop`
  (capture-region toggle — no literal "scissors" symbol exists), `Image` (captures drawer trigger).
- **Command shape differs between the two ViewModels and that's fine** — `BookReaderScreenViewModel`
  has `NextPageCommand`/`PreviousPageCommand` (`ProgressPercent`, 0-100) while
  `PdfPageReaderScreenViewModel` has `GoLeftCommand`/`GoRightCommand` (`ProgressFraction`, 0-1). Since
  `ReaderChrome` binds each screen's *actual* command/property names from its own XAML (not a shared
  interface), no ViewModel renaming is needed — only the binding paths per screen differ.
- **EPUB's chrome show/hide state (`IsChromeVisible`, the auto-hide `DispatcherTimer`,
  `NotifyPointerActivity`/`NotifyKeyActivity`) already lives entirely in
  `BookReaderScreenViewModel`**, not in the XAML. `ReaderChrome` doesn't need to own any of that
  logic — it just exposes a bindable `IsChromeVisible` the host sets. PDF simply never toggles it
  (stays `True`), which is the entire behavioral difference the design's `Mode` decision described —
  kept here as a real `ReaderChromeMode` enum property per the approved design, but it has no internal
  branching logic of consequence yet; it documents intent for both readers and is the natural place
  to hang future mode-specific behavior.
- **`CloseAllOverlays`/`PersistSettingsOverride`-on-close pattern stays in each ViewModel** — the
  shared drawer/sheet controls are visual scaffolding only; open/close state, persistence-on-close,
  and the "closing the font sheet writes the override" behavior are unchanged ViewModel logic, just
  rebound to new control names.
- **PDF's `ReaderChromeBrush`/`ReaderBorderBrush`/etc.** (`PdfPageReaderScreen.axaml.Resources`) are
  local, hardcoded, and skin-unaware — retired once the screen no longer references them (Step 8).
- **Existing AutomationIds to preserve** (not asserted directly by `BookReaderAccessibilityTests`
  today, but real automation surface worth not silently dropping): `TocToggleButton`,
  `SearchToggleButton`, `BookmarksToggleButton`, `HighlightsToggleButton`, `ExportAnnotationsButton`,
  `FontSheetToggleButton`, `CloseReaderButton`, `PreviousPageButton`, `NextPageButton`,
  `BookReaderWebView`, `ReadingPositionLiveRegion` (untouched — separate always-in-tree `TextBlock`,
  not part of chrome).

## Step 1: Shared chrome/overlay styles resource dictionary

**Files:** `src/Paperbunkr.App/Styles/ReaderChromeStyles.axaml` (new)
**What:** Move the `chromeIcon`/`tocRow`/`resultRow`/`deleteRow`/`sheetOption`/`themeSwatch` style
selectors verbatim out of `BookReaderScreen.axaml`'s `<UserControl.Styles>` block into this shared
dictionary (no visual changes — a relocation, not a redesign, so this step is pure mechanics).
`BookReaderScreen.axaml` merges it back via `<StyleInclude Source="avares://Paperbunkr.App/Styles/
ReaderChromeStyles.axaml"/>` in place of the inline block. `PdfPageReaderScreen.axaml` is left alone
for now (it doesn't use these classes yet — wired in Step 8).
**Depends on:** none
**Verify:** `dotnet build`; EPUB reader's existing chrome renders pixel-identical to before (same
selectors, new file).

## Step 2: Chrome/sheet tint helper (`BookTheme` → chrome brushes)

**Files:** `src/Paperbunkr.App/Views/ReaderChromeTint.cs` (new), `src/Paperbunkr.App.Tests/
ReaderChromeTintTests.cs` (new)
**What:** A small static class (`ReaderChromeTint.Background(BookTheme)`/`.Foreground(BookTheme)`)
returning the translucent chrome-bar background + icon-foreground `IBrush` per theme, following the
exact per-theme switch shape `BookReaderSettings.Background`/`.Foreground` already use (light themes
→ dark translucent-on-light chrome text, dark/OLED/high-contrast themes → light text). Single source
of truth `ReaderChrome`/`ReaderSettingsSheet`/PDF's canvas backdrop all consume, so they always tint
identically for a given theme.
**Depends on:** none
**Verify:** New unit tests — deterministic brush per `BookTheme` value, one assertion per enum member
(6 values: Light/Dark/Sepia/MatchAppSkin/OledBlack/HighContrast).

## Step 3: `ReaderChrome` control (top bar + bottom bar)

**Files:** `src/Paperbunkr.App/Views/ReaderChrome.axaml` (new), `src/Paperbunkr.App/Views/
ReaderChrome.axaml.cs` (new — added in the same step per this project's own AVLN2000 build gotcha),
`src/Paperbunkr.App.Tests/ReaderChromeTests.cs` (new, Avalonia headless)
**What:** New `UserControl` with:
- `Mode` (`ReaderChromeMode` enum: `TapToHide`/`AlwaysVisible`, new small enum alongside it or in
  `Paperbunkr.App.Models`).
- `Title` (string), `IsChromeVisible` (bool), `ChromeBackground`/`ChromeForeground` (`IBrush`,
  fed by Step 2's helper from the host's bound theme).
- Nullable `ICommand` `StyledProperty`s, one per possible button: `TocCommand`, `SearchCommand`,
  `BookmarksCommand` (+ `IsBookmarked` bool for `Bookmark` symbol's `IsFilled`), `HighlightsCommand`,
  `ExportCommand`, `CapturesCommand`, `CaptureToggleCommand` (+ `IsCaptureMode` bool), `FontThemeCommand`,
  `CloseCommand` — each button's `IsVisible` bound via a `command != null` converter so an unbound
  button simply doesn't render.
- `PreviousCommand`/`NextCommand` (bottom bar; PDF and EPUB both wire real commands here — page-turn's
  WebView-JS-first logic for EPUB stays in `BookReaderScreen.axaml.cs`'s existing Click handlers,
  which still call `NextPageCommand`/`PreviousPageCommand` as their fallback, so `ReaderChrome`'s
  `NextCommand`/`PreviousCommand` bind to those same VM commands for PDF and to no-ops/omitted for
  EPUB if the Click-handler wiring stays code-behind-driven — resolved concretely in Step 4).
- `ProgressFraction` (double, 0-1) + `ProgressLabel` (string) feeding the thin track+fill+label.
- Circular hover-pill button chrome (`chromeIcon` class from Step 1), FluentIcons `SymbolIcon`s per
  the survey notes' confirmed symbol names.
**Depends on:** Steps 1, 2
**Verify:** `dotnet build`; new headless test instantiating `ReaderChrome` directly — a bound command
renders its button, a null command hides it; `ProgressFraction`/`ProgressLabel` render as bound.

## Step 4: Wire `ReaderChrome` into `BookReaderScreen.axaml` (EPUB)

**Files:** `src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit — replace the two hand-rolled
top/bottom `Border` bars with one `<views:ReaderChrome Mode="TapToHide" .../>`)
**What:** Bind `TocCommand={Binding OpenTocCommand}`, `SearchCommand={Binding OpenSearchCommand}`,
`BookmarksCommand={Binding OpenBookmarksCommand}` + `IsBookmarked={Binding IsCurrentPositionBookmarked}`,
`HighlightsCommand={Binding OpenHighlightsCommand}`, `ExportCommand={Binding ExportAnnotationsCommand}`,
`FontThemeCommand={Binding OpenFontSheetCommand}`, `CloseCommand={Binding CloseCommand}`,
`Title={Binding ChapterTitle}`, `IsChromeVisible={Binding IsChromeVisible}`,
`ChromeBackground`/`ChromeForeground` from `Settings.Theme` via Step 2's helper (a converter binding,
not a code-behind assignment, so it stays live as the theme changes). Bottom-bar Previous/Next stay
wired via the existing `OnPreviousPageButtonClick`/`OnNextPageButtonClick` code-behind `Click`
handlers (unchanged logic — WebView-scroll-first, VM-command fallback), so `ReaderChrome`'s
`PreviousCommand`/`NextCommand` are left unbound here and its buttons instead get `Click` handlers the
same way today's do. `ProgressFraction={Binding ProgressPercent, Converter=...}` (÷100),
`ProgressLabel` = a new small formatted-string binding ("{ChapterTitle} · {ProgressPercent:F0}%") or a
tiny VM-side computed property if XAML multi-binding gets awkward. Preserve every existing
`AutomationProperties.AutomationId` on the moved buttons.
**Depends on:** Step 3
**Verify:** `dotnet build`; `dotnet test --filter BookReaderScreenViewModelTests` stays green (no VM
changes); on-screen — EPUB chrome unchanged in function, now FluentIcons + circular-pill + theme-tinted.

## Step 5: `ReaderListDrawer` control; retrofit TOC/Bookmarks/Highlights/Search onto it

**Files:** `src/Paperbunkr.App/Views/ReaderListDrawer.axaml` (new), `ReaderListDrawer.axaml.cs` (new),
`src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit)
**What:** Shared left-drawer scaffold: scrim `Border` + `Popup`/`ShouldUseOverlayLayer="False"` panel
(same airspace-safe pattern `BookReaderScreen.axaml` already established for the WebView) with a
header `TextBlock`, an optional pinned search `TextBox` slot (`SearchBoxText`, `IsSearchBoxVisible`),
and an items slot (`ItemsSource`/`ItemTemplate` passthrough, since the four consumers'
item types — `BookChapterSummary`/`BookBookmarkSummary`/`BookHighlightSummary`/`BookSearchResult` —
are unrelated). Replace `BookReaderScreen.axaml`'s four near-identical `Popup` blocks (TOC, Bookmarks,
Highlights, Search) with four `<views:ReaderListDrawer>` instances, same bound collections/commands as
today. Search's overlay shape changes from a centered top dropdown to a left drawer with its search
box pinned at the top — no `SearchQuery`/`SearchResults`/`RunSearch` ViewModel logic changes.
**Depends on:** Step 1
**Verify:** `dotnet build`; on-screen — all four drawers open/close from the left; Search still
filters and navigates results correctly.

## Step 6: `ReaderSettingsSheet` control (grouped mini-cards); retrofit EPUB's Font & Theme sheet

**Files:** `src/Paperbunkr.App/Views/ReaderSettingsSheet.axaml` (new), `ReaderSettingsSheet.axaml.cs`
(new), `src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit)
**What:** Shared bottom-sheet scaffold with three mini-card sections — **Typography** (font size
slider, font-family buttons, line-spacing buttons), **Spacing & margins** (character/word/paragraph
spacing sliders, page-margin slider), **Theme** (swatch buttons + an optional auto-hide-toggle slot).
`HasTypographyControls` bool gates the first two cards (`True` for EPUB). Theme swatches come from a
bindable collection so EPUB's 6 and PDF's eventual 3 (Step 7) render off one template. Replace
`BookReaderScreen.axaml`'s flat 8-section Font & Theme `Popup` body with
`<views:ReaderSettingsSheet HasTypographyControls="True" .../>` bound to the same existing
`Settings.*`/`SetFontFamilyCommand`/`SetLineSpacingCommand`/`SetThemeCommand`/`AutoHideChromeToggle`
properties — no ViewModel changes.
**Depends on:** Step 1
**Verify:** `dotnet build`; on-screen — every slider/swatch/button still updates `Settings` and
persists via the existing `CloseFontSheetCommand` → `PersistSettingsOverride` path, now grouped into
cards instead of 8 flat sections.

## Step 7: PDF gains a Light/Dark/Sepia theme

**Files:** `src/Paperbunkr.App/ViewModels/PdfPageReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/PdfPageReaderScreenViewModelTests.cs` (edit — new theme round-trip test)
**What:** Add a `BookTheme _theme` observable property (UI restricts the font/theme sheet to offering
only `Light`/`Dark`/`Sepia`, but the stored type is the existing shared `BookTheme` enum — no new
type). `LoadBook` reads `book.ThemeOverride ?? appSettings.BookReaderTheme` (same resolution chain
`BookReaderScreenViewModel.LoadBook` already uses — no schema change, these columns are already
format-agnostic on `Book`/`AppSettings`). Add `SetThemeCommand`, `IsFontThemeOpen`,
`OpenFontThemeCommand`/`CloseFontThemeCommand` (the close handler persists `book.ThemeOverride =
Theme`, mirroring `BookReaderScreenViewModel.PersistSettingsOverride`'s write-on-close shape — no
per-tick DB writes). Add a `CanvasBackground` computed property (`ReaderChromeTint.Background(Theme)`
from Step 2) for the page-canvas backdrop.
**Depends on:** Step 2
**Verify:** New/edited unit test — `Book.ThemeOverride` unset falls back to
`AppSettings.BookReaderTheme`; setting `Theme` then closing the sheet persists `ThemeOverride`;
existing `PdfPageReaderScreenViewModelTests` stay green.

## Step 8: Wire the shared controls into `PdfPageReaderScreen.axaml`

**Files:** `src/Paperbunkr.App/Views/PdfPageReaderScreen.axaml` (edit)
**What:** Replace the hand-rolled top toolbar `Border` + bottom scrubber `Border` with
`<views:ReaderChrome Mode="AlwaysVisible" .../>` bound to `CapturesCommand={Binding OpenCapturesCommand}`,
`CaptureToggleCommand={Binding ToggleCaptureModeCommand}` + `IsCaptureMode={Binding IsCaptureMode}`,
`FontThemeCommand={Binding OpenFontThemeCommand}`, `CloseCommand={Binding CloseCommand}`,
`PreviousCommand={Binding GoLeftCommand}`, `NextCommand={Binding GoRightCommand}`,
`ProgressFraction={Binding ProgressFraction}`, `ProgressLabel` = "{PageNumber} / {PageCount}",
`Title={Binding BookTitle}`, chrome tint from the new `Theme`/`CanvasBackground` (Step 7). Replace the
right-side Captures `Grid` overlay with `<views:ReaderListDrawer>` (left-aligned, matching every other
drawer). Add `<views:ReaderSettingsSheet HasTypographyControls="False">` bound to Step 7's theme
commands. Remove the now-dead local `ReaderBgBrush`/`ReaderChromeBrush`/`ReaderBorderBrush`/
`ReaderTextBrush`/`ReaderTextMutedBrush`/`ReaderTrackBrush` resource block once nothing references
them. Bind the page area's background to `CanvasBackground`.
**Depends on:** Steps 3, 5, 6, 7
**Verify:** `dotnet build`; `dotnet test --filter PdfPageReaderScreenViewModelTests`; on-screen — PDF
chrome now visually matches EPUB's (icons, button shape, theme tint, progress bar), Captures drawer
opens from the left, Font & Theme sheet shows only the Theme card, Light/Dark/Sepia swap retints both
the canvas backdrop and the chrome.

## Step 9: Full-suite regression pass + dead-code cleanup

**Files:** none new — a verification/cleanup pass across files touched above
**What:** Grep both screens for any leftover emoji-glyph `Button.Content="..."` or now-unreferenced
brush resources and remove them. Confirm no dangling references to the old inline Popup blocks this
plan replaced.
**Depends on:** Steps 1-8
**Verify:** `dotnet build Paperbunkr.sln` — 0 errors; `dotnet test Paperbunkr.sln --filter
"FullyQualifiedName!~UiTests"` — full App/Data/Plugins suites green; `dotnet test
src/Paperbunkr.App.UiTests --filter BookReaderAccessibilityTests` — still passes (reader reachable,
chapter text discoverable) after the button/control rewiring; final manual on-screen pass comparing
both readers side-by-side for visual consistency — the actual goal of this spec.
