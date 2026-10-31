# Books improvement arc — Slice 3b: typography and reading aids (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Builds on Slices 1–3a.
Pitch items 26 (typography power-ups) and 27 (reading aids). Decisions: grilling round 1 for Slice 3 (2026-10-27), all accepted; the mock (typography sheet + ruler/auto-scroll HUD) was approved. **Assumed** = not asked.

Dropped by that round: named typography profiles (global default + per-book override already is the profile concept) and bionic reading.

## 1. Goal

Give the book reader the text controls other readers have (justify, hyphenation, the book's own styling, any installed font) and two aids for steady reading (a line ruler and auto-scroll).

## 2. New settings

| Setting | Scope | Where |
|---|---|---|
| Justify text | global default + per-book override | `AppSettings.BookReaderJustify` (bool), `Book.JustifyOverride` (bool?) |
| Hyphenation | global default + per-book override | `AppSettings.BookReaderHyphenate` (bool), `Book.HyphenateOverride` (bool?) |
| Use the book's own styling | global only | `AppSettings.BookReaderUsePublisherStyles` (bool) |
| Font from this machine | global; a book can choose "Custom" | `BookFontFamilyOption.Custom` (new enum value, stored by name), `AppSettings.BookReaderCustomFont` (string?) |
| Ruler on | global, remembered | `AppSettings.BookReaderRulerEnabled` (bool) |
| Auto-scroll speed | global, remembered (the scrolling itself always starts off) | `AppSettings.BookReaderAutoScrollSpeed` (int 1–8, default 3) |

Resolution follows the existing chain (`Book.…Override ?? AppSettings.BookReader…`, seeded in `LoadBook` under `_isSeedingSettings`). One migration `AddBookReaderTypographyAndAids` (six `AppSettings` columns, two nullable `Book` columns); defaults all off except speed 3.

## 3. Typography

- **Extract** the reader's typography CSS (today assembled inline by `PushTypographyCss` in `BookReaderScreen.axaml.cs`) into a pure `BookReaderCss.Build(BookReaderSettings, BookThemeBrushes, language)` so every rule below is unit-testable without the WebView. Behaviour for existing settings must stay byte-identical (a regression test pins the current output for the default settings).
- **Justify:** `text-align: justify` on `#pb-content` paragraphs; off = the current left alignment.
- **Hyphenation:** `hyphens: auto` on `#pb-content`, and the chapter document's `lang` set from `Book.LanguageISO` (Slice 1) falling back to `en`. The WebView hyphenates only languages it has dictionaries for; others render unhyphenated (no error). Off = `hyphens: manual`.
- **The book's own styling:** when on, the reader stops forcing font family, size, line height, character/word/paragraph spacing, justify and hyphenation on the body text, so the chapter's own markup styling shows; theme colours and page margins still apply. **Depends on the normalized chapter HTML keeping the book's inline styles and classes: plan step 1 verifies what survives normalization.** If the book's stylesheet is already dropped, the switch is limited to "don't force the reader's family/size/spacing" and the sheet's help line says so.
- **Font from this machine:** a font list from `FontManager.Current.SystemFonts`, shown as the last section of the Font picker; picking one stores `BookReaderCustomFont` and sets the family option to `Custom`. CSS uses `font-family: "<name>", <serif|sans fallback>`; a font that is no longer installed falls back to the Serif stack.
- **Sheet:** `ReaderSettingsSheet` gains the controls in the mock: a Text group with the three switches, the extended Font picker, and a one-line help text ("Justify and hyphenation can differ per book. The styling switch is global.").

## 4. Reading aids

### Ruler
- A dim band over everything except about two lines of text, placed by the pointer; the band height is `2 × line height`, the dim colour is the theme's page colour at ~78 % opacity (from `BookThemeBrushes`, no hard-coded colour). Two overlay elements injected into the page by the same script that already carries the click/keyboard bridge; they never intercept pointer events.
- Geometry in a pure `BookRulerMath.Band(pointerY, lineHeightPx, viewportHeight)` (clamped so the band stays inside the viewport).
- Keyboard: with the ruler on, new remappable actions `BookRulerLineUp` / `BookRulerLineDown` move it one line. **Assumed:** no gamepad binding (the pad drives focus and scroll, not a free-floating band).
- Toggle from the sheet and from a new action `BookRulerToggle`.

### Auto-scroll
- A `requestAnimationFrame` loop in the page scrolls `#pb-content`'s container by `BookAutoScrollMath.PixelsPerSecond(speed, lineHeightPx)` (`lineHeightPx × 0.15 × speed` lines per second, so it follows font size and line spacing). It stops at the end of the chapter (it does not turn the chapter), and any wheel, key, click or tap stops it.
- A small floating pill (same visual family as the 3a jump-back chip: `floatingPanel` + `clusterPill`) while running: pause/play, slower, faster, and the speed number. Hidden when stopped.
- Actions `BookAutoScrollToggle`, `BookAutoScrollFaster`, `BookAutoScrollSlower` (remappable; defaults chosen in the plan against the existing `BookReader` scope to avoid conflicts, and none that collide with text-entry keys since the service already ignores keyboard actions in text boxes).
- The reader's activity tracker counts auto-scrolling as reading time (the screen is showing and the user is present), so `NoteInput` is called when the user adjusts speed, not on every scrolled frame; the tracker's own idle cut-off still ends a session if nobody touches anything for its cut-off (**Assumed**, consistent with how a long idle read is treated today).

## 5. Tests and verification

- `BookReaderCssTests` (pure): default output pinned; justify, hyphenation (with and without a language), custom font (installed and fallback), the-book's-own-styling dropping the forced rules, and theme variables.
- `BookRulerMathTests`, `BookAutoScrollMathTests`: clamping, speed scaling with line height, speed bounds 1–8.
- `BookReaderScreenViewModelTests`: settings seeding with per-book overrides for justify and hyphenation; the global-only switch; speed persisted; ruler state persisted; auto-scroll starts off on every open.
- Migration test (`AddBookReaderTypographyAndAidsMigrationTests`): defaults, round trip, reversible.
- Input: the five new actions exist in the Book reader group, with no default-binding conflicts in their scope (`InputActions` tests).
- Lint trio for the sheet and pill XAML.
- On screen (WebView, light/dark/sepia/OLED at 1366×768): justify and hyphenation visibly change line breaks; a custom font applies after a chapter turn; the ruler tracks the pointer and arrow actions; auto-scroll is smooth, stops at the chapter end and on input; none of it shifts the saved reading position.

## 6. Out of scope

Typography profiles, bionic reading, per-language hyphenation dictionaries beyond what the WebView has, font installation from the app, PDF reader changes, a gamepad binding for the ruler.
