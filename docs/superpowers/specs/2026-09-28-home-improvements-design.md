# Home screen: improvements (pitch spec B)

Spec B of the Home screen pitch (Roadmap, "Home screen pitch", 2026-09-28). It covers I1–I5 and owns the section model
that spec A (`2026-09-28-home-cosmetics-design.md`, C1–C10 plus the landing-site effects) builds on. One plan builds both,
starting with this spec's section model.

The decisions came from the same 2026-09-28 grilling session as spec A. The Home dashboard is a deliberate CE deviation, so
no CE-parity lookup was owed.

## Facts this rests on (verified 2026-09-28)

- **`HomeScreenViewModel` (535 lines)** owns every module:
  - `BuildSnapshot` runs off the UI thread through `LoadFromDatabaseAsync`, and `ApplySnapshot` repopulates on the UI thread.
  - The queries live in `HomeFeedResolver` (Data).
  - Because You Read uses `RecommendationResolver.GetRecommendations`. The pitch's premise that the engine was "backend-only"
    was wrong: Home already renders it.
- **`HomeScreen.axaml` (443 lines)** is one hand-written `StackPanel`: masthead, spotlight, Continue Reading, Continue Reading —
  Books (gated on `HasBooksLibrary`), Recently Added, Collections, Because You Read and Try This Reading List. The automation
  IDs used by tests include `HomeContinueReadingHeader`, `HomeRecentlyAddedHeader`, `HomeSpotlightCard` and so on.
- **Spotlight** (`HomeFeedResolver.GetSpotlightPicks`): genre-frequency weighted draws without replacement from `IsUnread()`
  issues, up to 6. There's no per-series cap, so one series can take several slots.
- **Insights** (`InsightsResolver.Build(ctx, nowUtc)`) returns `Continue` (with a "dropped off Nwk ago" subtitle past 21 days),
  `AlmostDone` (≤3 left), `DiveIn` and `Gaps`. `AttentionSeries(SeriesId, SeriesName, Subtitle, ResumeIssueId)`.
- **Preferences › Appearance tabs** are `StackPanel Tag="appearance.*"` blocks in `AppearanceSection.axaml`, registered in
  `Models/PreferenceIndex.cs`.
- **Supporting pieces:**
  - Toasts support buttons: `IToastHost.Show(new ToastRequest(title, message, severity, Actions: [new ToastAction(label, command)]))`.
  - Context menus use per-surface `*ContextMenuBuilder` classes over the shared `MenuFlyout` mechanism.
  - Row removal from inside a routed event must be deferred with `Dispatcher.UIThread.Post` (CLAUDE.md).
- **AppSettings migrations** need a no-op `Down()` for added columns (the SQLite rebuild drops orphaned columns). Never edit a
  migration once the user has launched the app.

## Section model (Approach 1+, shared with spec A)

- **`Paperbunkr.App/Models/HomeSectionKey.cs`**: string constants `spotlight`, `needsAttention`, `continueReading`,
  `recentlyAdded`, `collections`, `becauseYouRead`, `readingList`, plus `Default` (that order) and display names.
- **`HomeLayout`** (pure, App): `Resolve(string? orderCsv, string? hiddenCsv)` returns `(IReadOnlyList<string> Visible,
  IReadOnlyList<string> Order)`.
  - Unknown keys are dropped and duplicates collapse.
  - Missing known keys are inserted at their default index, clamped, so a section added in a future release appears for
    existing users.
  - `Serialize(order, hidden)` writes it back. Null in AppSettings means the default.
- **`ViewModels/Home/HomeSectionViewModel`** (abstract): `Key`, `Title`, `Icon` (`FluentIcons.Common.Symbol`), `HeaderAutomationId`.
  There's one subclass per section: `SpotlightSectionViewModel`, `NeedsAttentionSectionViewModel`,
  `ContinueReadingSectionViewModel`, `RecentlyAddedSectionViewModel`, `CollectionsSectionViewModel`,
  `BecauseYouReadSectionViewModel` and `ReadingListSectionViewModel`. Each holds its display data, its empty-state text and
  its commands, which call back into navigation delegates the coordinator passes in.
- **`HomeScreenViewModel`** keeps the masthead state: greeting, sky, seasonal, search, refresh, cover wall, accent. It exposes
  `ObservableCollection<HomeSectionViewModel> Sections`.
  - `BuildSnapshot` reads the layout first and only runs queries for visible sections.
  - `ApplySnapshot` builds the section list in order.
  - The existing public members that tests and `MainViewModel` use stay as forwarding members where cheap. Tests that
    reached into removed members are updated.
- **Views.** `HomeScreen.axaml` keeps the masthead, then an `ItemsControl ItemsSource="{Binding Sections}"`.
  - Each section's `DataTemplate` lives in its own resource-dictionary file, `Views/Home/<Name>Section.axaml`, with no
    `x:Class`, merged via `ResourceInclude` in `HomeScreen`'s resources and matched by `DataType`.
  - Shared styles (heading, empty state, links, spotlight dots) move to `Views/Home/HomeStyles.axaml` (a `Styles` file).
  - The automation IDs stay on the same elements.
- **All sections hidden.** Home shows one line, "All Home sections are hidden — choose what to show in Preferences ›
  Appearance › Home.", with a link that navigates there. `MainViewModel` passes a `goPreferencesAnchor` callback.

## I1 — reorder and hide sections

- **Settings** (one migration, `AddHomeCustomization`, with a no-op `Down()` for the columns):
  - `AppSettings.HomeSectionOrder` (`string?`)
  - `AppSettings.HomeHiddenSections` (`string?`)
  - `AppSettings.HomeSeasonalFlourish` (`bool`, default false; spec A C10)
- **The Preferences › Appearance › Home tab** (`Tag="appearance.home"`, a new `PreferenceIndex` entry with keywords "home,
  sections, reorder, hide, greeting, seasonal, recommendations, not interested"):
  - **Sections.** A list of `HomeSectionRow` (`Key`, `Name`, `IsVisible`), with a drag handle and a checkbox per row, plus
    "Reset to default".
    - The drag uses an in-process `DataFormat`, like the reading-lists sidebar. ↑/↓ buttons on each row are the keyboard
      path and the fallback.
    - Every change saves immediately (no Save button), matching Saved List Layouts.
  - **Seasonal flourish.** A toggle row for `HomeSeasonalFlourish`.
  - **Hidden recommendations.** See I5.
- **Live update.** `PreferencesScreenViewModel` raises `HomeLayoutChanged`. `MainViewModel` forwards it to Home, which
  reloads on its next visit (it already reloads on every visit). Nothing needs to update while Home is off screen.

## I2 — one Continue Reading row

- **`HomeFeedResolver.GetContinueReadingMixed(context, limit = 10)`** returns `IReadOnlyList<ResumeCandidate>`. A
  `ResumeCandidate` is either a comic (`Series`, `ResumeIssue`, `LastTouchUtc = ResumeIssue.OpenedTime`) or a book (`Book`,
  `LastTouchUtc = LastOpenedTime`).
  - It reuses the existing `GetContinueReading` and `GetContinueReadingBooks` filters unchanged: dropped series excluded, only
    in-progress issues, only unfinished books with a real position.
  - It merges the two lists by `LastTouchUtc` descending and takes `limit`.
- **`HomeResumeCard`** (App) wraps either a comic or a book: cover source, title, badge (`#N` for comics), meta (author for
  books), progress fraction, `IsBook`, and an open command target.
- Books show a small `Book` icon in the cover's top-right corner. The row's open command dispatches to the reader for an issue
  or a book.
- **Removed:** `HasBooksLibrary`, `ContinueReadingBooks`, the separate row and its automation IDs (`HomeContinueReadingBooks*`).

## I3 — Needs Attention

- **`AttentionSeries`** gains `bool IsStalled = false`, a defaulted positional parameter so existing callers compile.
  `ComputeContinue` sets it when the last touch is older than `StalledDays`.
- **`HomeFeedResolver.GetTopAttention(context, nowUtc)`** returns `HomeAttention?`. It calls `InsightsResolver.Build` and picks
  the first of:
  1. `AlmostDone[0]`: headline "{n} issues left in {Series}" (from its subtitle), reason "You're close to finishing this run",
     action "Resume #{number}" when it has a resume issue.
  2. The first `Continue` item with `IsStalled`: headline "Pick {Series} back up", reason = its subtitle ("… dropped off Nwk
     ago"), action "Resume #{number}".
  3. `Gaps[0]`: headline "{k} missing from {Series}", reason "Missing #{list of up to 3}…", action "View series".
  
  If none of those exist, it returns null.
- `HomeAttention(SeriesId, SeriesName, Headline, Reason, ResumeIssueId?, ActionLabel)`. The card shows the series cover,
  resolved from the series' cover key in the App layer.
- **Card** (`round3-visuals.html`, option B): a banner with the cover (34×48), headline, reason, the action on the right, and a small
  "All in Insights" link (navigates to Insights). About 64px tall.
- The section is not rendered when the attention result is null. When the user has hidden it, it isn't computed at all.

## I4 — spotlight relevance

- **`GetSpotlightPicks(context, random, count = 6, nowUtc = default, newArrivalSlots = 2, newArrivalDays = 14)`**:
  1. **New arrivals.** From unread issues with `AddedTime >= now - 14d`, take the most recent per series, order by `AddedTime`
     descending, and take up to `newArrivalSlots`.
  2. **Weighted fill.** The existing genre-weighted draw over the remaining unread candidates, with the per-series cap: after
     each pick, remove every other candidate from that series. The uniform fallback also respects the cap.
  3. Unused new-arrival slots are filled by the weighted draw.
- The final order is interleaved (weighted, new, weighted, new, …) so new arrivals don't always lead. Randomness stays
  injectable, so tests stay deterministic.
- Wanted and unowned releases stay out, because the spotlight opens the reader.

## I5 — "Not interested"

- **Entity `DismissedRecommendation`** (Data): `Id`, `SeriesId` (unique index), `DismissedUtc`. There's no foreign key (the
  `ReadingEvent` precedent), so a deleted series leaves a harmless orphan row. `DbSet` `DismissedRecommendations`. It's in the
  `AddHomeCustomization` migration, and `Down()` drops the table.
- **`DismissedRecommendations`** (Data, static): `Dismiss(ctx, seriesId, now)`, which is idempotent; `Restore(ctx, seriesId)`;
  `GetIds(ctx)`; and `List(ctx)` (joined to series names, orphans skipped).
- **Triggers:**
  - A hover ✕ (`Dismiss` icon, top-right of the cover) on recommendation cards only.
  - `HomeRecommendationContextMenuBuilder` (right-click) with "Open series" and "Not interested".
- **Flow.** `BecauseYouReadSectionViewModel.NotInterested(card)` does the following:
  1. Write the row.
  2. `Dispatcher.UIThread.Post(() => remove the card from every row; drop rows that became empty)`.
  3. `toastHost.Show(new ToastRequest("Hidden from recommendations", seriesName, Info, [new ToastAction("Undo", undo)]))`.
     Undo restores the row and reloads Home.
- **Filtering.** `BuildSnapshot` loads the dismissed ids once and filters `RecommendationResolver` results before mapping
  cards. The resolver is unchanged, so Detail and other callers are unaffected. A row whose recommendations are all dismissed
  is skipped, as today.
- **Preferences › Appearance › Home › Hidden recommendations.** A list of series name and date, each with "Unhide" (removal
  deferred with `Dispatcher.UIThread.Post`), plus the empty state "Nothing hidden".

## Error handling

- A bad layout string resolves to the default and never throws.
- The attention lookup is wrapped. A failure logs through `DiagnosticsService` and hides the card rather than failing the whole
  Home load.
- A dismissal write failure shows an error toast and leaves the card in place.
- `LoadFromDatabaseAsync` keeps its existing log-and-fall-back-to-synchronous path.

## Testing

- **`HomeLayout`** (pure): default for null; unknown keys dropped; duplicates collapsed; a missing key inserted at its default
  index; hidden keys removed from `Visible` but kept in `Order`; round-trip `Serialize`.
- **Resolvers** (real SQLite, Data.Tests):
  - `GetContinueReadingMixed`: interleaves by timestamp, keeps the dropped/finished exclusions, respects the limit.
  - `GetTopAttention`: priority order, the stalled flag, the gaps wording, null when empty.
  - `GetSpotlightPicks`: one per series, two new-arrival slots, fallback when there are no new arrivals, deterministic with
    a seeded `Random`.
  - `InsightsResolver`: `IsStalled` set only past 21 days.
- **`DismissedRecommendations`**: dismiss is idempotent; restore; list skips orphans.
- **ViewModels** (App.Tests):
  - `HomeScreenViewModelTests` updated: default section order; hidden sections absent and not queried (verified by a
    counting seam); the all-hidden empty line; a dismissed series filtered from every row; Undo restores it.
  - Preferences Home-tab tests: reorder, hide, reset and unhide all persist to AppSettings.
- **Migration**: `AddHomeCustomization` applies and rolls back, following the existing migration-test pattern (no up-down-up).

## Implementation notes

Built 2026-09-28 in the shared working tree (uncommitted).

- **Thin section ViewModels.** `ViewModels/Home/HomeSections.cs` holds one class per section, with `Key`/`Title`/`Icon`/
  `HeaderAutomationId` and a `Home` back-reference. The data stays on `HomeScreenViewModel`: one background snapshot fills every
  section at once, and the existing public members (`ContinueReading`, `RecentlyAdded`, the commands) kept their names, so
  `MainViewModel` and the tests barely changed. This departs from "each holds its display data".
- **Template lookup.** Avalonia doesn't match `DataType`-implicit templates out of a merged resource dictionary. Each section file
  therefore holds a keyed `HomeSection.<key>` template, and `Views/Home/HomeSectionTemplateSelector.cs` looks it up by the
  section's key. `HomeScreen.axaml.cs` installs the selector.
- **Needs Attention**
  - It sits in the section list only while it has something to show.
  - `AttentionSeries.IsStalled` was added as specified.
  - The resume label reads `Issue.Number` ("Resume #2").
- **`DismissedRecommendation` has a cascading foreign key to `Series`**, like the other dismissal tables (`ReadingListOverlapDismissal`),
  instead of the planned no-FK orphan approach. A dismissal disappears with its series, so the Preferences list never shows ghosts.
- **Migration.** `20260928211622_AddHomeCustomization`: three `AppSettings` columns plus the table. `Down()` drops the table and
  leaves the columns, following the `AddReadingListViewMode` precedent.
- **Preferences tab**
  - `ViewModels/Home/HomePreferencesViewModel.cs` is exposed as `PreferencesScreenViewModel.HomePrefs`, so that file gained one
    property and one `Load()` call.
  - The UI is the `appearance.home` block in `AppearanceSection.axaml`.
  - Drag-reorder uses the attached behavior `Views/Home/HomeSectionRowDrag.cs` (in-process `DataFormat`, like the reading-list page).
  - ↑/↓ buttons are the keyboard path.
  - Moves and Unhide are deferred with `Dispatcher.UIThread.Post`, because they're triggered from inside the row being moved or
    removed.
- **Navigation.** `MainViewModel.GoPreferencesAnchor` opens Preferences on the anchor's section and scrolls to it. Home receives
  that, `GoInsights`, and a `DispatcherToastHost` for the Undo toast.
- **Spotlight test updates.** Existing spotlight tests that seeded several unread issues in one series were changed to one series
  each, because of the new per-series cap.
