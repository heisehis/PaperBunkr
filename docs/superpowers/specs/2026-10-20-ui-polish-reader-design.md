# UI polish, sub-project 4c: Reader — Design

Date: 2026-10-20. Audit §11d, shots 9 and 13. Every recommendation (Q1–Q6) was accepted.

| Q | Decision | Cause it fixes |
|---|---|---|
| Q1 | The Reader Tools drawer is 280 wide, starts below the top row (top margin 72), and lists one tool per row (icon + full label). | Width 230 and full height: the drawer covered the top-right actions, and the two-column grid truncated labels. |
| Q2 | Drawer list actions are full-width left-aligned `drawerRow`s. Small action groups (clip Copy / Save… / Delete) are `secondary sm` in a row. | Centred and left-aligned buttons were mixed. |
| Q3 | The breadcrumb puts the series name in a star column that trims, with the issue title Auto (max 220), so it never overflows into the navigator. "←" becomes an `ArrowLeft` icon. | A horizontal StackPanel gave the text unlimited width, so it never trimmed. |
| Q4 | The reading-stats chip hides while the drawer or the Info panel is open. | It sat under the drawer in the bottom-right corner. |
| Q5 | `clusterPill` and `clusterIcon` are 28 tall (icons 28×28), and every chrome cluster has the same padding and radius. | The two bottom bars had different heights. |
| Q6 | Info panel: "Show summary" is a left-aligned `linkText` button. Previous / Next and Open details / Edit properties are `secondary sm`. | Mixed styles. |

Testing: the reader view tests that exist (by class) and the lint. Not verified on screen until the user checks.
