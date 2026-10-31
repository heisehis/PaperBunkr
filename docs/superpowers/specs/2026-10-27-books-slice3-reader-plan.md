# Books Slice 3 (reader: 3a, 3b, 3c) — Implementation Plan
*Implements: `2026-10-27-books-slice3a-reader-jumpback-session-design.md`, `…3b-reader-typography-aids-design.md`, `…3c-reader-annotations-design.md`*

Surveyed 2026-10-27. Facts the plan relies on:
- `BookReaderScreenViewModel` (1,342 lines) owns `_position`, a chapter `_history` stack (page turns) and the navigation commands `GoToChapter`, `GoToHighlight`, `GoToBookmark`, `GoToSearchResult`, `NextPage`, `PreviousPage`; `OnPositionCaptured(blockId, fraction, excerpt)` fires after every scroll/turn **and once right after a chapter loads**; `PositionRestoreRequested(blockId)` scrolls the page; `EndReadingSession()` calls `_activity.Stop()` (active seconds) and `UpdateSessionPages`.
- Book reader input: `InputActionIds.Book*` (`InputAction.cs`), declared in `InputActions.cs` under `BookReaderGroup`, handled in `BookReaderScreen.axaml.cs` `OnBookInputAction` (a `switch` on the action id) through an `AttachedInputRegistration` in scope `InputScope.BookReader`. The comic reader's jump-back (`ReaderScreenViewModel`: chip + 6 s timer, `GetShortcutHint`) is the pattern for the chip.
- Typography CSS is assembled inline in `PushTypographyCss` (`BookReaderScreen.axaml.cs`); settings live in `BookReaderSettings` + `AppSettings.BookReader*` + `Book.*Override`, seeded in `LoadBook` under `_isSeedingSettings`. Highlight notes: `BookHighlight.Note`, edited through `HighlightPopupNote`; the highlight payload is built in code for the page script (`HighlightScript`, `pb-color-*` classes).
- The page injects one script carrying the click/key bridge (`window.pbBoundKeys`); WebView behaviour cannot be unit-tested, so logic goes in pure helpers.

## Rules
Same as the earlier slices: design-language skill for XAML, code-behind with every new `.axaml`, touched-class tests only, patch scripts via the Write tool with raw strings, no launch of the dev exe against the real DB, and honest "not verified on screen" notes.

## 3a
1. **`BookJumpTracker` + `BookSessionSummary`** (pure, `Paperbunkr.App/Services/Reader/`) with tests. Same-chapter rule refined from the spec: different chapter = large; same chapter with both block ids numeric = large at ≥ 10 blocks apart; otherwise a fraction change > 15 %.
2. **View model:** chip state (`HasJumpBack`, `JumpBackLabel`, 6 s `DispatcherTimer`), `NoteJump(from,to)` called from the four jump commands, `JumpBack` command (restores without recording, via `PositionRestoreRequested`), clearing on `NextPage`/`PreviousPage` and on a position capture **after a 1.5 s settle window** (the capture that follows the jump itself must not dismiss the chip); injectable clock for tests. Session summary in `EndReadingSession` through an optional `showToast` (set in `MainViewModel`), goal line from `GoalResolver.Build`.
3. **Input + view:** `InputActionIds.BookJumpBack` (default Alt+Left, Book reader group, handled only while the chip shows), chip in `BookReaderScreen.axaml` (same look as the comic chip), shortcut hint in the tooltip.
4. **Tests:** tracker, summary, VM (chip per jump type, clears on turn/scroll after settle, return, toast threshold + goal line), input catalogue.

## 3b
1. **Verify** what the normalized chapter HTML keeps of the book's styling (decides what "book's own styling" can do; recorded in the spec's risk).
2. **`BookReaderCss`** extracted from `PushTypographyCss` with a test pinning today's default output; then add justify, hyphenation (`lang` from `Book.LanguageISO`), publisher-styles, custom font.
3. **Settings + migration** `AddBookReaderTypographyAndAids` (6 AppSettings columns, 2 Book columns, `BookFontFamilyOption.Custom`), seeding with overrides, persistence, migration test.
4. **Sheet UI:** Text group switches + extended font picker (system fonts) in `ReaderSettingsSheet`.
5. **Aids:** `BookRulerMath`, `BookAutoScrollMath` (pure + tests); injected ruler overlay and rAF auto-scroll in the page script; VM state (`IsRulerOn`, `IsAutoScrolling`, `AutoScrollSpeed`), floating pill; five actions (`BookRulerToggle`, `BookRulerLineUp/Down`, `BookAutoScrollToggle/Faster/Slower`) with defaults chosen against the BookReader scope; auto-scroll `NoteInput` only on user speed changes.
6. **Tests:** CSS, maths, VM settings/aids, migration, input catalogue; lint trio.

## 3c
1. `pb-has-note` in the highlight payload/script + marker CSS; click hit target.
2. Highlights drawer row: muted note line; `HasNote` on `BookHighlightSummary`.
3. **Tests:** payload flag, summary flag; on-screen checklist.

## Wrap-up
Lint trio and every touched class (one `dotnet` at a time, `MainViewModelTests` once), `docs/paperbunkr-todo.md` and the memory note, on-screen checklist for the WebView-side pieces, no launch against the real DB.

## Risks
Chip dismissal races the post-jump position capture (settle window); the page script is one shared file of injected JS (keep additions small and namespaced `pb*`); `BookReaderScreen.axaml.cs` and the VM are large and touched by all three slices: keep each step green.
