# UI polish, sub-project 5: Leftovers — Design

Date: 2026-10-20. Audit §8 and §11, plus the user's screen: 1366×768 at 100 % scaling. Every recommendation (Q1–Q5) was accepted.

| Q | Decision |
|---|---|
| Q1 | The three `Width` transitions stay (nav rail 64↔200, contextual sidebar 0↔236, the reader's active page dot). The sidebars are meant to push content aside, which needs layout; the dot's `RenderTransform` belongs to the dock-style hover magnify. Each gets a comment saying why. |
| Q2 | Every `*Transition` names an easing (`PbMotionEase`). New lint rule `TransitionsHaveEasing` in `XamlConsistencyLintTests`. |
| Q3 | Matrix rain picks a typeface per glyph: Cascadia Code (or Consolas) when it has the glyph, otherwise `SKFontManager.Default.MatchCharacter` (Yu Gothic / MS Gothic on Windows). No bundled font. |
| Q4 | DPI: headless renders of the Library toolbar, a dialog, the sidebar and the reader chrome at 125 % and 150 %; fix what they show. |
| Q5 | 1366×768 is the design minimum. Below 900 px of toolbar width the Library search pill collapses to a search icon button; a click or Ctrl+F (`FocusSearch`) expands it, and it collapses again when it loses focus while empty. Render pass of Library (preview open), Detail, Insights, Smart and Preferences at 1366×768. |

Testing: the lint, Matrix rain typeface choice (a unit test on the per-glyph picker), a toolbar test for the collapse and expand, and the
render sweeps. Not verified on screen until the user checks.
