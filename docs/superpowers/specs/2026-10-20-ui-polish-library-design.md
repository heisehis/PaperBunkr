# UI polish, sub-project 4b: Library — Design

Date: 2026-10-20. This builds on the foundation, shared-controls, icons and dialogs specs. Audit §11c, shot 12 and the user's toolbar
screenshots apply. Every recommendation in this round (Q1–Q8) was accepted.

| Q | Decision | Where |
|---|---|---|
| Q1 | **Headings:** the screen title "Library" uses `pbTextHeading`. Content section headings ("Continue reading", group headers) use `pbTextSection` in sentence case. Sidebar group labels ("COLLECTIONS", "SERIES", …) use `pbTextOverline`. | MainWindow sidebar (title, `sideHeading`); LibraryContinueStrip; LibraryScreen group headers |
| Q2 | **Hover card on a series tile:** series name, publisher, "N issues · M unread", status label. An issue tile is unchanged. | LibraryScreen hover popup and its code-behind: `ShowHoverTooltip` gets a series flag from the hovered `SeriesCardSample` |
| Q3 | **Hover card look:** below the tile as now, restyled to the floating-panel tokens (no hex shadow), with `MaxHeight` 320. | LibraryScreen |
| Q4 | **Tile corner badges:** one shared style, `Border.tileBadge` — a dark scrim (`PbHeroScrimColor`-like `#B0000000` is already allowed on cover art; a token `PbCoverBadgeBackground` is added), height 20, radius `PbRadiusChip`, padding `6,0`, caption-size text. Used by the rating, read tick, count and publisher mark. The stack-peek edge stays. | PosterTile / LibraryScreen tile template; Styles/Controls.axaml |
| Q5 | **Preview panel actions:** the local `previewAction` look is dropped. Read is `primary`, mark read/unread are `secondary`, add-to-collection and more-actions are `iconButton`. | LibraryPreviewPanel |
| Q6 | **Continue Reading strip:** right-edge fade (`OpacityMask`, last 5 %), as in the chip row. | LibraryContinueStrip |
| Q7 | **Narrow windows:** below about 1000 px the Workspace button shows only its icon; the search pill has `MinWidth` 200. Implemented with a width observer in code-behind that toggles a `compact` class. | LibraryToolbar |
| Q8 | **Spacing:** margins and paddings in the Library files snap to the 4-px scale. The content area uses the 24-px gutter. | LibraryScreen, LibraryToolbar, LibraryPreviewPanel, LibraryContinueStrip |

## Testing

- `XamlConsistencyLintTests` and `SharedControlsStyleTests` still pass.
- `LibraryLensTests`, `LibraryScreenViewTests`, `LibraryOpenKeysTests` and `ListLayoutViewTests` pass.
- New test: on a series tile the hover card shows the series name and count, not an issue title.
- The toolbar render is checked at 1070 and 860 px.
