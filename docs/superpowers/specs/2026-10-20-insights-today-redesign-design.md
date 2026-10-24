# Insights · Today redesign - design

Date 2026-10-20. Asked for by the user right after the Reading Lists visual-identity pass ("the last thing I wanna revamp is the Today section of
Insights, the rest are fine"). Five takes in the visual companion (`.superpowers/brainstorm/1280-1792495252/content/today-five-takes.html`), then a
**B (bento) + D (cinematic)** mix (`today-mix-b-d.html`), signed off ("perfect"). Trends, History and Recap are untouched.

Follows `paperbunkr-design-language`: tokens only, the type scale, 4 px grid, shared families, `PbMotionEase`, 1366x768 minimum.

## Problem

Everything on Today was a 272 px card (`attnCard`) packed to the left of a 1250 px canvas, with 28x40 thumbnails, so the tab read as small and
unfinished next to the rest of the app and wasted most of its width.

## Layout (top to bottom)

1. **Header row** - the "TODAY" label and "Add goal" (unchanged).
2. **Spotlight + goal** - a cinematic banner (`Border.todaySpotlight`): a soft blurred copy of the cover behind a wash, the cover large (84x122), a
   36 px display title, the subtitle ("0 of 9 read") and one primary button. To its right the **goal tile** (ring, `8 / 30`, title, status), fixed
   about 280 px, only when there is a hero goal. The spotlight is the series you are *continuing*; **only when there is nothing to continue** it
   falls back to the top recommendation ("BECAUSE YOU FINISHED X", button "View series"). With neither it shows the existing "Nothing to highlight
   yet" prompt, and with no goal either the whole row collapses (`HeroRowEmpty`, as before).
3. **Other goals / Past goals** - unchanged (their cards keep the 272 px size).
4. **Shelf tiles** - `ResponsiveColumnsPanel` (at least 340 px per column, at most 3, equal widths; a hidden tile frees its slot). Each tile is a card
   with a caps heading and count, a one-line description, and a **cover shelf**: 76x110 covers with the title and a short detail underneath, as many
   as fit across one row (the rest clip). Tiles: **Almost done**, **Dive in**, **Because you finished X** (every recommendation, so the old
   "More because you finished" list folds in) and **Drop-off watch** (when it has rows). With nothing in Almost done and Dive in, the existing
   "Nothing else needs attention" card shows in their place.
5. **Collection health** - one full-width card: the heading and count, the explanatory line, and the near-complete runs as a responsive grid of
   compact rows (24x34 cover, title, missing numbers; 1-4 columns).

Theme: a missed goal is the ring's own colour, nothing louder. No hex in the view; the spotlight wash and the tiles use surface and accent tokens, so
Daylight, dark and Matrix all follow.

## Data (no new queries)

`InsightsScreenViewModel` gains `HasSpotlight`, `SpotlightIsContinue`, `SpotlightHeading`, `SpotlightTitle`, `SpotlightSubtitle`,
`SpotlightCoverKey`, `SpotlightActionLabel` and an `OpenSpotlight` command (resume the continue row's issue, or open the recommended series), raised
with the other Today notifications. `AttentionRow` gains `ShortSubtitle` (the part before the first " · ", so "own 100 issues · #1-100, never
opened" shows as "own 100 issues" under a cover). `SecondaryRecommendations` stays (tests) but the view uses `Recommendations`.

## Unchanged

Goals editing, the goal ring control, Trends/History/Recap, keyboard behaviour (every tile's interactive element is a Button, as before; the tab
strip handling is untouched).

## Testing

View-model tests for the spotlight source rules and `ShortSubtitle`; the panel's column maths; `XamlConsistencyLintTests` and
`ThemeHardcodedColorLintTests`; `InsightsScreenFocusTests` (focus rings not clipped); a render of Today in default and Daylight at 1296x720.
