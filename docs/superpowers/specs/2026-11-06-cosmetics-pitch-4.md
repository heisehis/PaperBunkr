# Cosmetics pitch 4 — reading, finding, people, moments (20 items)

Pitch only; nothing built. These are surfaces that pitches 1–3, the 2026-09-28 Home cosmetics and the 2026-10-18 cosmetics
package did not touch. A quick code survey (2026-11-06) backs each premise; the file references below are what it found. Earlier
pitches kept finding half-built features, so each item still gets a proper survey (and a CE check where CE has the feature) before
it is specced. Everything uses tokens (no hex, no oval pills), `PbMotionEase`, the reduced-motion gate and the shared control families.

Ideas dropped during the survey because they already exist: reader end-of-issue card (`ReaderScreen.axaml` `chapterCard` /
`ChapterTransitionState.EndCard`) and page-turn styles (`PageTransitionStyle` in reader profiles).

## A. Reading (1–5)

1. **Scrubbable progress bar** — the comic reader's bottom bar is a plain 4 px `ProgressBar` (`ReaderChrome.axaml:106`). Make it
   a scrubber: drag or click to jump, hover shows the page thumbnail (reuse the page-dot `ReaderThumbnailSample`), and small ticks
   mark bookmarks, detected ad pages (`AdPageDetection`) and double-page spreads.
2. **Page-number flash** — with the chrome hidden, a page turn briefly shows a small "12 / 24" chip in a corner that fades out
   after ~1 s. Off by default; position follows reading direction (RTL puts it on the left).
3. **Chapter openings in the Books reader** — drop cap plus small-caps first line on the first paragraph of each chapter, coloured
   from the book theme. Pairs with the chapter ornaments from the 2026-10-18 package (#11). Opt-in per book theme.
4. **Warmth filter** — one slider (off → candle) that lays a warm tint over the page in both readers, with an optional "after
   sunset" schedule. A colour overlay only; page pixels and screenshots/Save Page As stay untouched.
5. **Chapter progress hairline (Books)** — a 2 px accent line along the top edge showing progress through the current chapter,
   with "8 min left in chapter" on hover, estimated from the reading-speed data Insights already logs.

## B. Finding and moving around (6–10)

6. **Quick Open match highlighting and covers** — `QuickOpenOverlay.axaml` renders plain Primary/Secondary text and a symbol icon.
   Bold the matched characters, show a small cover for issue/series results, and render the footer hints ("↑↓ navigate…") as
   keycaps (#19).
7. **Search-term highlight in Library rows** — while a search is active, tint the matched text in list/details rows and tile
   titles, so it is obvious why each result matched (especially for the Library search modes that match metadata fields).
8. **History peek on Back** — right-click or long-press Back (and the mouse back button held) opens a short list of recent screens
   with a thumbnail and name each, from `NavigationHistoryService`; pick one to jump there.
9. **Drag-and-drop affordances** — the Library drop zone deliberately has no drag-over affordance "same as CE"
   (`LibraryScreen.axaml:1494`). Deviate on purpose: a soft accent outline and a "Drop to import 12 files" card with type icons
   while dragging files in, and a stacked-cover drag ghost with a count when dragging issues onto collections or reading lists.
10. **Custom title bar (opt-in)** — `MainWindow` uses the native title bar. Extend the client area so the title bar carries the
    current screen name, uses the theme surface (and Mica on Windows 11 when the skin allows it), while keeping Snap Layouts on
    the maximise button. Opt-in because drag regions and snap behaviour are fiddly.

## C. Detail and people (11–14)

11. **Creator avatars on credits** — credits on Detail are plain text buttons (`DetailTabs.axaml` ~line 739). Add a small
    generated initials squircle per creator (colour hashed from the name, theme tokens only) and a hover card: "14 books in your
    library, 9 read", with a link to search.
12. **Arc position strip** — on the Detail hero, a segmented bar for the issue's story arc ("Part 3 of 6"): one segment per part,
    filled for read, outlined for owned-unread, hatched for missing (ties into Wanted). Click a segment to open that issue.
13. **Run density sparkline** — on series Detail, a thin bar per cover-date year showing owned vs. missing issues, so gaps in a
    long run are visible at a glance. Reuses `TrendSparkline` drawing from Insights.
14. **Cover lightbox** — click the Detail cover to open it full size over a backdrop tinted by its dominant colour
    (`CoverPalette`), with arrows through variant covers and alternate cover images when the metadata has them.

## D. Moments and feedback (15–20)

15. **Finish celebration** — finishing a series, a reading list or a reading goal plays a short burst of paper scraps in the
    accent colours (static badge under reduced motion) and logs a quiet entry to the Activity Center, not a separate popup.
16. **"On this day" card on Home** — from the `ReadingEvent` log: "A year ago you finished *Saga #54*", with the cover; hidden
    when there is nothing to show. One card at most, dismissable, setting to turn it off.
17. **Toast polish** — `PbToastView` has a severity stripe and icon. Add a hairline that runs down to show auto-dismiss time,
    pause on hover, and collapse a burst into "+2 more" instead of a tall stack.
18. **Skeleton placeholders** — no skeleton states exist today. Show tile- and row-shaped placeholders (static, or a slow pulse
    when motion is allowed) while Library, Detail and Insights load, instead of an empty area or a spinner.
19. **Keycap rendering** — shortcut chips (`pbChip` in `KeyboardShortcutsSection.axaml`) become keycaps: each key its own small
    squircle with a darker bottom edge. Reused in Quick Open's footer, the Welcome tour and button tooltips ("Refresh  F5").
20. **Wanted "this week" strip** — on the Wanted screen, a seven-day strip with covers on their release day, an "Out today"
    ribbon, and a countdown on the next release. Built from the pull-list dates already loaded.

## Suggested slicing

- **Slice 1 (small, view-only):** 2, 6, 7, 17, 19.
- **Slice 2 (reader):** 1, 3, 4, 5.
- **Slice 3 (detail):** 11, 12, 13, 14.
- **Slice 4 (app shell and moments):** 8, 9, 15, 16, 18, 20.
- **Slice 5 (stretch, riskiest):** 10.

Likely new settings: page-number flash, warmth level and schedule, chapter openings per book theme, title bar mode, On this day,
celebrations. Remember: add new migrations (never regenerate one the user has launched); AppSettings migrations get a no-op `Down()`.
