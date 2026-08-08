# Preferences Screen — Behavior Tab

*Date: 2026-08-07. Second tab on the shell established by
docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md §5. Scoped after directly
reading CE's `Settings.cs` (2807 lines) and `FormUtility.FillPanelWithOptions` (the reflection-driven
renderer the audit's "~10 reflection-driven categories" line refers to) — see §1 for why this
tab is much smaller than that line count implies.*

## 1. Triage: what CE's Behavior tab actually contains

`FormUtility.FillPanelWithOptions` renders exactly one kind of control from `Settings`: `bool`
properties that are both `[Browsable(true)]` (the default) and carry a `[Category]`+`[Description]`
pair, grouped into a checkbox per category. Reading every one of the ~38 checkboxes this produces
against Paperbunkr's actual current feature set, the overwhelming majority fall into three buckets
that don't belong in a settings screen right now:

- **Toggle WinForms-only chrome Paperbunkr doesn't have**: main menu bar (`ShowMainMenuNoComicOpen`),
  docking grips (`AlwaysDisplayBrowserDockingGrip`), tabbed book windows (`OpenInNewTab`,
  `CloseBrowserOnOpen` — CE's multi-tab/multi-window book-open model is a confirmed architectural
  non-goal per docs/ce-feature-inventory.md §H), a "Quick Open" dialog (`ShowQuickOpen`), a 3D
  cover-flip info dialog (`InformationCover3D`), list "stacks" (`CommonListStackLayout`).
- **Gate reader capabilities that don't exist in Paperbunkr's reader canvas at all yet**: zoom of
  any kind (`ResetZoomOnPageChange`, `ZoomInOutOnPageChange`), full-screen mode
  (`HideCursorFullScreen`, `AutoMinimalGui`), double-page spreads (`TrueRightToLeftReading`'s
  `FlipParts` mode), continuous-scroll paging (`PageChangeDelay`, `ScrollingDoesBrowse` — the
  `VerticalContinuous`/`HorizontalContinuous` `ReadingMode` values exist on `Series` but have no
  actual scroll-paging implementation behind them yet), a "Quick Review" dialog
  (`AutoShowQuickReview`).
- **Belong to already-decided-elsewhere areas**: export (`ExportedListsContainFilenames` — dropped,
  §H), web-comics/news (`UpdateWebComicsStartup`/`NewsStartup` — out of scope / deferred),
  scripting/network sharing (`Scripting`, `LookForShared` — these belong to the separate
  Scripts/Libraries tabs per docs/ce-feature-inventory.md §E's own table, not Behavior).

**What's left — genuinely gates something that exists in Paperbunkr today, or a feature small
enough to build alongside its own toggle:**

- `OpenLastPage` — the reader (`ReaderScreenViewModel.Load`) *always* resumes at
  `Issue.LastPageRead` today; this makes it a real on/off choice instead of hardcoded behavior.
- `AutoNavigateComics` ("reading beyond the start or end opens the next Book") — doesn't exist
  in any form yet (`NextPage`/`PreviousPage` just clamp at the issue's own boundaries). Small
  enough to build now rather than defer, per the user's explicit choice to build the underlying
  feature rather than ship a hollow toggle for it.

Everything else above stays out of this spec, to be revisited once its underlying feature
(zoom, continuous scroll, full-screen, Quick Open, etc.) actually gets built in its own right —
gating a feature that doesn't exist with a preference toggle would be scaffolding for nothing.

## 2. Data model

`AppSettings` (already a singleton row per the skin-system spec §1) gains two columns:

- `OpenLastPage` (`bool`, default `true`) — resume at `Issue.LastPageRead` on open, vs. always
  start at page 1.
- `AutoNavigateComics` (`bool`, default `true`) — reading past an issue's last page loads the
  next issue in the series at its first page; reading before its first page loads the previous
  issue at its last page. Both CE defaults, matched here (`OpenLastPage`/`AutoNavigateComics`
  are both `[DefaultValue(true)]` in CE's `Settings.cs`).

## 3. Auto-navigate-to-adjacent-issue

New behavior in `ReaderScreenViewModel`, gated by `AutoNavigateComics`:

- `NextPage()` at the last page: if enabled, look up the next issue in the same series (ordered
  by `IssueOrdering.OrderByNumber()` — the same numeric-aware issue-number ordering already used
  by `DetailTabsViewModel`/`EnsureIssueLoaded`), and `Load()` it at page 0. If there is no next
  issue (end of series) or the setting is off, no-op — same clamp behavior as today.
- `PreviousPage()` at the first page: mirror image — loads the previous issue at its *last* page
  (not page 0), so backward reading flows naturally rather than landing you at the start of the
  previous issue's story.
- Both resolve the sibling issue via a fresh `PaperbunkrDb.CreateContext()` query
  (`Issues.Where(i => i.SeriesId == seriesId)`, ordered client-side by `OrderByNumber()` since
  that ordering isn't translatable to SQL) rather than re-fetching the whole `Series.Issues`
  navigation — `ReaderScreenViewModel` doesn't otherwise hold the full issue list resident.

## 4. Preferences > Behavior tab

`PreferencesScreenViewModel` gains a second real tab (`"behavior"`, `IsBehaviorTab`) alongside
`"appearance"` — same `ActiveTab`-string + computed-flag pattern, no new abstraction. Two
checkboxes, both applying immediately (no separate Save step, matching the Appearance tab's
click-to-apply skin/font pattern):

- "Resume issues where you left off" → `OpenLastPage`
- "Reading past the last page opens the next issue" → `AutoNavigateComics`

Both persist straight to `AppSettings` via `PaperbunkrDbContext.GetOrCreateAppSettings()` — no new
service needed (`ReaderScreenViewModel` already reads `AppSettings` directly the same way).

## Testing

- `AppSettingsTests`: new columns default to `true`, round-trip via `GetOrCreateAppSettings`.
- `ReaderScreenViewModelTests` (new): `NextPage` past the last page loads the next issue at page 0
  when `AutoNavigateComics` is on, no-ops at the end of the series, and doesn't cross issues when
  the setting is off; `PreviousPage` before the first page loads the previous issue at its last
  page; `OpenLastPage = false` starts a freshly-loaded issue at page 0 regardless of
  `LastPageRead`.
- `PreferencesScreenViewModelTests`: `IsBehaviorTab` tab-switch flag; toggling each checkbox
  persists to `AppSettings`.
- Manual verification: same no-GUI-automation approach as prior specs — build + run real tests,
  then ask the user to flip both toggles in the running app and confirm the reader actually
  crosses issue boundaries.
