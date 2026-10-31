# Books improvement arc — Slice 4d: read aloud (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Decisions: Slice 4 grilling rounds 1 and 2, all accepted. **Assumed** = not asked.

## 1. Goal

Have the reflow reader read the chapter aloud with the voices installed in Windows, following along on the page.

## 2. Engine

`System.Speech` (NuGet, Windows only; the app targets plain `net10.0`, so the newer WinRT speech API would force a Windows-specific target for the whole app and is not used). Wrapped by an `ISpeechEngine` (`Speak(text)`, `Pause`, `Resume`, `Stop`, `Rate`, `VoiceName`, `Completed` event, `GetVoices()`) so everything else is testable with a fake. Only voices the machine reports are offered; if there are none the reader's read-aloud control is hidden. Cloud voices are out of scope.

## 3. What it reads

- A pure `BookReadAloudPlan` turns the chapter's blocks (`pb-p<n>` ids and plain text, already available from the chapter source) into an ordered list starting at the block currently at the top of the page; skips empty blocks and image-only blocks; stops at the chapter's last block (it never advances to the next chapter).
- Granularity is the **paragraph (block)**: the engine speaks one block at a time, and on completion the page highlights and scrolls to the next. Per-sentence highlighting needs a text-to-DOM offset mapping on top of the highlight spans and is deferred. **Assumed**: a long block is spoken whole.

## 4. In the page and the view model

- An in-page pill (HTML, like the auto-scroll pill, because the WebView paints above Avalonia): play/pause, stop, slower, faster, and the speed number; the block being spoken gets a `pb-speaking` class (soft background tint from the theme variables) and is scrolled into view.
- `BookReaderScreenViewModel` state: `IsSpeaking`, `IsSpeakingPaused`, `SpeechRate` (−5…+5, remembered as `AppSettings.BookReaderSpeechRate`), `SpeechVoice` (remembered as `AppSettings.BookReaderSpeechVoice`, picked in a new "READ ALOUD" card in the font sheet next to the reading aids) — one migration `AddBookReaderSpeech`.
- Actions in the Book reader group: `BookSpeakToggle` (default **T**), `BookSpeakFaster` / `BookSpeakSlower` (defaults **Shift+.** and **Shift+,**, acting only while speaking). Speaking stops when the book closes, another chapter loads (TOC, search, bookmark, highlight, page turn into a new chapter) or the reader jumps; jump-back is unaffected.
- **Reading activity:** speaking counts as reading time (each spoken block notes input, so the session timer runs while it plays); the existing idle cut-off ends it if the machine is left alone for longer than a block takes.

## 5. Tests and verification

- `BookReadAloudPlanTests` (start block, skipping, chapter end), a fake engine driving the view model (start, pause, resume, stop, rate bounds, voice persistence, stops on navigation and on close, next block on completion, last block ends cleanly), input catalogue and key-set pin updates, migration defaults.
- The engine wrapper itself and the page highlight cannot be unit-tested; on screen: voices listed, speak a chapter, pause/resume, speed change mid-speech, scrolling follows, stops at the end, light/dark/sepia.

## 6. Out of scope

Per-sentence highlighting, cloud voices, audio export, reading across chapters, PDF reading aloud (image pages have no text layer), audiobook linking (dropped earlier).
