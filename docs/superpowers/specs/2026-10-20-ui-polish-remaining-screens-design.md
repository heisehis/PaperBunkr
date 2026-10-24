# UI polish, sub-project 4d: Remaining screens — Design

Date: 2026-10-20. Covers Detail, Manga detail, Home, Continuity, Preferences, Insights, Books, Smart, Wanted, Plugins and the property
editors. Every recommendation (Q1–Q6) was accepted.

| Q | Decision | Cause it fixes |
|---|---|---|
| Q1 | Every screen title uses `pbTextHeading`, with no local FontSize / FontWeight / Foreground (Insights, Smart, Plugins, Issue properties, Bulk issue properties, Bulk series properties). | Each screen sized and coloured its own title. |
| Q2 | The Manga detail band does not show a special mark that repeats the content-type picker's value (`DetailBandViewModel.SetSpecialMarks(issue, skip)`). The picker is `PbControlHeight` tall with `10,0` padding. | "Manga" showed twice, side by side, at two heights. |
| Q3 | `SeriesStatusChip` shows the icon only, not a dot as well. | Two indicators for one status. |
| Q4 | The Home spotlight sliver shade uses a token, `PbCoverShadeColor` (same value, dark in every theme because it sits on cover art). | A hardcoded hex in HomeStyles. |
| Q5 | Synopsis display (`MarkdownLite.ParseDescription`) drops lines made only of dashes, underscores, equals signs or en/em dashes. | Scraped separators showed as lines of punctuation. |
| Q6 | Odd margins and paddings in the remaining views snap to the nearest multiple of 4. Even values, 1, and negative overhang offsets stay. The toast text indent, the MainWindow sidebar offsets and the splash screen are left alone because they line up with something else. | Off-scale spacing (3, 5, 7, 9, 11, 15) everywhere. |

Testing: the lint, the hardcoded-colour baseline, MarkdownLite (one new test), and the Detail, Manga, Home, Insights, Smart, Wanted, focus-ring
and shared-controls tests by class. Not verified on screen until the user checks.
