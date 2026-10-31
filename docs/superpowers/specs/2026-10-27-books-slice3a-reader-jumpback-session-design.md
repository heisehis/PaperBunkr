# Books improvement arc — Slice 3a: reader jump-back and session summary (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Builds on Slices 1–2.
Pitch items 23 (resume / jump-back) and 30 (session stats). Decisions: grilling round 1 for Slice 3 (2026-10-27), all recommendations accepted. **Assumed** = not asked.

## 1. Goal

Make long jumps in a book reversible and make the end of a reading session say something. Resume itself is already precise (the reader stores `LastChapterIndex`, `LastBlockId` and `LastProgressionFraction`; bookmarks and highlights anchor to a block), so nothing about resume changes.

## 2. Jump-back

Mirrors the comic reader (`ReaderScreenViewModel` "Jump-back chip": one stored position, no history stack, a chip for a few seconds, remappable `Reader.JumpBack`).

- **What counts as a jump:** a navigation that is not a page turn or scroll — TOC chapter, in-book search result, bookmark, highlight (drawer), and a Notes jump from Book Detail that lands in an already-open book. A jump is *large* when it changes chapter, or stays in the chapter but moves more than 15 % of the chapter (**Assumed** threshold, a constant `JumpBackFractionThreshold`). Small jumps record nothing.
- **State:** `BookJumpTracker` (pure): `Record(from, to)` returns the position to offer or null; `Clear()`. One stored `BookPosition` (chapter, block id, fraction).
- **Chip:** "Back to Ch. 3" (or "Back to where you were" when the chapter title is unknown), shown for 6 seconds like the comic chip, cleared by any normal page turn or scroll (the same rule the comic reader uses: any position change that is not the return itself). Same visual as the comic chip (`floatingPanel` + `clusterPill`), positioned above the bottom chrome.
- **Action:** a new `InputActionIds.BookJumpBack` in the Book reader group, default **Alt+Left**, scope `InputScope.BookReader`, context Always. It handles only when a chip is showing and otherwise declines (so Alt+Left keeps its other meanings elsewhere). Shown in Preferences > Keyboard Shortcuts automatically. The chip's tooltip shows the bound key (`GetShortcutHint`).
- **Return:** jumps to the stored position through the reader's existing position-restore path, without recording a new jump-back (no ping-pong), and clears the chip.

## 3. Session summary

When a reading session ends (`BookReaderScreenViewModel.EndReadingSession`, called on leaving the reader and on opening another book) and it lasted **5 minutes or more of active time** (`ReaderActivityTracker.Stop()` already returns the active seconds), show one toast through the app's toast channel:

> Reading session · 32 min · 41 pages · *Daily pages: 62 of 50*

- `BookSessionSummary.Format(activeSeconds, pages, goalLine)` (pure): minutes rounded, "1 hr 5 min" from 60 minutes up, pages omitted when 0, a goal fragment only when given. Returns null under the threshold.
- **Goal fragment:** `GoalResolver.Build` is read once at session end; the first goal that is Active (not complete, not scope-missing) is shown as "{name}: {current} of {target}" (**Assumed**: the first by the goal list's own order; no scoring of which goal the session moved). A completed goal reads "{name}: done". No goal → no fragment.
- **Plumbing:** `BookReaderScreenViewModel` gains an optional `Action<ToastRequest>? showToast` (set from `MainViewModel`, same pattern as the other VMs); null in tests means no toast. Severity Info, no actions.
- A session shorter than 5 minutes, or one with no active time recorded, shows nothing.

## 4. Data and settings

No schema change. No new settings (the 5-minute threshold is a constant, **Assumed**). `ReadingEvent` writing is unchanged.

## 5. Tests and verification

- `BookJumpTrackerTests`: same chapter small move → null; same chapter > threshold; different chapter; return does not record; Clear.
- `BookSessionSummaryTests`: threshold, rounding, hour format, pages omitted, goal fragment variants.
- `BookReaderScreenViewModelTests`: jump from TOC/search/bookmark/highlight sets the chip with the right label; a page turn clears it; `BookJumpBack` returns and clears; session end raises a toast only at ≥ 5 min and carries a goal line when one exists.
- Input: the new action appears in the catalogue with its default and is in the Book reader group (`InputActions` tests).
- On screen (WebView): the jump really lands where it was, the chip position clears the bottom chrome at 1366×768, light/dark/sepia.

## 6. Out of scope

A history stack of positions, jump-back in the PDF reader, a goal ring inside the reader, changing what a "session" is.
