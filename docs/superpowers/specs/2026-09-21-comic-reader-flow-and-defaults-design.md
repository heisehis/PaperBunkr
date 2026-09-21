# Comic reader — Flow & defaults (slice A) design

Date: 2026-09-21. Status: approved and built 2026-09-21 (uncommitted; on-screen check outstanding). See "Implementation notes" at the end for where the code differed.
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`. This is **slice A of eight**; the other slices are
listed under "Slice map" and each gets its own grilling → spec → plan cycle.

## Scope

In: #4 series-level reader defaults, #5 end-of-issue card, #10 jump-back chip, #27 event/reading-list context
strip, and the shared `ReadingOrderResolver` they need.

Out (and why):
- #16 slider markers — the comic reader has no page slider. Blocked until the Cosmetics pitch's page-thumbnail
  scrubber (#23) exists.
- #19 command palette — deferred to slice F; more useful once reader profiles (#15) exist.
- Books/EPUB/PDF reader — deliberately excluded from the whole pitch.

## Slice map (rest of the pitch)

| Slice | Items | Note |
|---|---|---|
| A. Flow & defaults | 4, 5, 10, 27 | this spec |
| B. Page intelligence | 6, 9, 12, 13 | 6 is CE parity (CE filters navigation by page type; default All). 13 is a deliberate deviation (CE counts ads). Also fix the 0-based vs CE `(LastPageRead+1)` read-percentage mismatch here |
| C. Image quality | 2, 3, 25 | 2 is mostly done (see corrections). 3 is new, not CE parity. Pixel-level filters need `PageId` to carry a filter hash |
| D. Panel / zoom | 1, 14 | |
| E. Info & compare | 28, 7, 11, 29 | CE has no in-reader info panel |
| F. Input & comfort | 15, 19, 20, 22, 30, 17, 18 | 15, 20, 28, 30 are Paperbunkr deviations; CE has none |
| G. Performance | 23, 24 | 24 kept as a small later item |
| H. OCR / translate | 8 | deferred, own project |

Deferred as "needs its own project": 26 (upscaling), 21 (pen layer), 8 (OCR/translate).

## Corrections to the pitch (verified 2026-09-21)

- #2: per-issue and global brightness/contrast/saturation/gamma already exist (additive, as in CE), applied as an
  `SKColorFilter` in `ReaderPageVisualHandler`, outside the decode cache. Only night/sepia (CE has none) and
  auto-levels/sharpen remain.
- #3: CE has no auto-crop. It is a new feature.
- #5: a chapter-transition card, prev/next buttons and a review-on-finish prompt already exist. This slice extends them.
- #4: reading mode already inherits issue → series → global; fit mode and auto-rotate go issue → global only.
- #6: paging does not consult page tags today.
- #16: no page slider exists.
- #27: the reader's next-issue logic ignores continuity.

## 1. `ReadingOrderResolver`

Lives in `Paperbunkr.Data`, next to `ContinuityResolver`. Replaces the reader VM's private `TryResolveAdjacentIssue`
and `TryGetAdjacentIssuePreview`; the VM becomes a thin consumer.

Input: the issue, and an optional opened-from context (reading list id, or Event id).
Output: previous and next issue, position ("3 of 12"), and a source label ("Reading list: Absolute Universe").
Missing files are skipped, as today.

Two entry points:
- **Paging order** (used by paging off the end, auto-advance, the end card): opened-from context if there is one,
  otherwise series order (`series.Issues.OrderByNumber()`). Opening from the Library never silently switches to
  event order.
- **Context for the strip**: opened-from context wins. Otherwise the highest-priority membership: Event first, then
  the most recently opened reading list. At most one is returned. Returns none if the issue has no membership.

## 2. #4 series-level reader defaults

- New nullable columns on `Series`: `PageFitModeOverride`, `AutoRotateOverride`, `PageLayoutModeOverride` (same types
  as the `Issue` fields). One new EF migration with a **no-op `Down()`** (SQLite rebuild drops orphaned columns).
  Never edit earlier migrations. Add with `dotnet ef migrations add ... --project src/Paperbunkr.Data`.
- One resolution method, `issue ?? series ?? global`, used by both `ReaderScreenViewModel.Load` and the detail
  screens so they cannot drift. Reading mode's existing chain is folded into the same method.
- Colour adjustments do not get a series level (a dark scan is an issue trait).
- Changing fit in the reader still writes only the issue override (`SetFitMode`). A small "Apply to series"
  affordance writes the series column.
- The comic and manga detail screens share one control that shows and clears the series values. The same `Series`
  entity backs both.

## 3. #5 end-of-issue card

Extends the existing chapter-transition card (`TriggerChapterTransition`, `ChapterTransitionState`), and folds in
`MaybePromptReviewOnFinish` as Rate and Flag.

Shown on the last page or when paging off the end. Contents:
- "Finished · Issue #n" header.
- Up-next block: cover, title, source label and position from the resolver, and a Continue button.
- Actions: Mark read, Rate, Flag for review, Back to library.

`AppSettings.AutoNavigateComics` keeps its meaning. When on, a visible countdown ("Auto in 5s · any key cancels")
runs and any keypress cancels it. When off, the card waits. On the final issue of the list or series the up-next
block reads "That's the last issue".

Mockup: `.superpowers/brainstorm/191-1789993305/content/end-card.html`.

## 4. #27 context strip and #10 jump-back chip

Layout A (chosen): the strip lives in the top chrome, the chip floats above the bottom bar, and they never overlap.
Mockup: `.superpowers/brainstorm/191-1789993305/content/overlay-placement.html`.

**Strip.** "Absolute Universe · 3 of 12" with its own prev/next, which follow the strip's context, not series order.
Hides with the chrome, and flashes for about 4 seconds when an issue opens. Renders only when the resolver returns a
context. One chip at most.

**Chip.** "Back to page 34" plus its shortcut. Triggers on a jump of more than 5 pages that is not a normal step:
thumbnail click, go-to-page, bookmark jump, search hit. Stores one previous position (no history stack). Visible for
about 6 seconds or until the next page turn. The return command is registered in `KeyboardCommandRegistry`, so it is
remappable.

Both are transient overlays that never take focus or keys from page navigation. They stay in-reader rather than going
through the Activity Center, since a notification on another surface mid-read would be wrong.

## Testing

- Unit: `ReadingOrderResolver` (list, event, series, missing files, no context), the inheritance chain, jump-back
  state (threshold, expiry, one position), end-card state (auto-advance on/off, last issue).
- Migration test: the three columns exist and default to null.
- Headless UI tests assert state synchronously. Do not `await window.FadeOutAndCloseAsync()` (hangs headless).
- On-screen check by the user for the strip, chip and card. No FlaUI scripting without asking.

## Open items carried out of this design

- "+1 more" on the strip to open other memberships is a later item.
- A pre-open of the next issue (Cosmetics-adjacent #23) should call the same resolver.

## Implementation notes (2026-09-21, where the design met the code)

- **#4 columns:** `Series.PageLayoutMode` already existed and the reader already writes it, so only `PageFitModeOverride` and `AutoRotateOverride` were added (migration `AddSeriesReaderDefaults`, no-op `Down()`). `ReaderDefaultsResolver` (Data) is the shared `issue ?? series ?? global` chain.
- **#4 UI:** the manga detail screen embeds the same `DetailTabsViewModel`, so the show/clear control is one addition to the Details Info sub-tab, not a separate shared control. "Apply to series" is a button in the reader's fit flyout plus an "Auto-rotate to series" row in the actions drawer.
- **Resolver location:** `ReadingOrderResolver` lives in `Paperbunkr.App/Services/Reader` (not Data) because `OrderByNumber` is an App extension. Continuity is series-level and defines no issue order, so the strip falls back to `StoryEvent` membership (`EventMembership.Position`) only. There is no "most recently opened reading list" fallback: lists carry no last-opened timestamp.
- **#5 card:** there is no manual "flag for review" on issues (Needs Review means pending metadata proposals), so the card has Mark read, Rate, Keep reading and Back to library. Only the forward end of a paged issue uses the new `EndCard` state. The backward transition, the continuous-mode overscroll transition and the explicit Next/Previous Chapter buttons keep the existing card. The card now also appears when `AutoNavigateComics` is off (it waits, no countdown). Paging forward again while it is up means Continue.
- **#10 triggers:** the comic reader has no go-to-page or search yet, so only thumbnail clicks and bookmark jumps (including previous/next bookmark) set the chip, in paged mode only.
- **#27 strip:** there is no "opened from Event" anchor, only a reading-list anchor, so the Event context is a fallback by membership.
- **Not exercised:** nothing was launched or looked at on screen; the visual result is unverified.
