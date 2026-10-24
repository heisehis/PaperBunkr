# Reader Issue Covers and Independent Series Artwork — Implementation Plan

*Implements: [2026-10-24-reader-issue-and-series-cover-design.md](2026-10-24-reader-issue-and-series-cover-design.md)*

## Step 1: Persist explicit cover choices

**Files:** `src/Paperbunkr.Data/Entities/Issue.cs`, `src/Paperbunkr.Data/Entities/Series.cs`,
`src/Paperbunkr.Data/PaperbunkrDbContext.cs`, new EF migration and model snapshot.

**What:** Add nullable `PreferredCoverPageNumber` and `CustomCoverPath`, map their optional fields,
and migrate existing rows with null defaults. Existing cover tags and `CoverIssueId` keep current
fallback behavior.

**Depends on:** none.

**Verify:** Inspect the generated migration and model snapshot to confirm nullable fields and no
unintended data rewrite.

## Step 2: Make reader cover selection generate the issue thumbnail

**Files:** `src/Paperbunkr.App/Services/CoverThumbnailService.cs`,
`src/Paperbunkr.App/Services/CoverImageCache.cs`, `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs`,
`src/Paperbunkr.App/ViewModels/ReaderPageContextMenuBuilder.cs`.

**What:** Resolve an issue's thumbnail page using explicit preference, first `Cover` page, then page
0. Add a service operation that decodes a selected archive page into the existing 400px thumbnail
format. Route the thumbnail's Page Type → Cover action and a current-page context-menu action through
one operation that persists the tag/preference, replaces a manual issue override only after a
successful decode/write, and invalidates image/aspect-ratio caches. Clear a preference when its page
is retagged away from Cover.

**Depends on:** Step 1.

**Verify:** Check decode/write failure paths leave the prior preference, tags, and manual cover
intact; check cache invalidation uses the existing issue identity and grid pipeline.

## Step 3: Add independent series cover storage and resolution

**Files:** new `src/Paperbunkr.App/Services/Covers/CustomSeriesCoverPaths.cs` (or equivalent),
series cover service/resolver, `src/Paperbunkr.App/Services/CoverImageCache.cs`,
`src/Paperbunkr.App/Services/GridCoverCache.cs`, `src/Paperbunkr.App/Views/AsyncCoverImage.cs`,
`src/Paperbunkr.App/Models/SeriesCardSample.cs`, `src/Paperbunkr.App/Models/LibraryTile.cs`.

**What:** Copy custom series art into a series-specific app-data directory and persist its relative
key in `Series.CustomCoverPath`. Introduce a typed/namespaced cover identity so series IDs cannot
collide with issue IDs in shared/grid caches. Resolve series artwork before the existing
`CoverIssueId`/first-issue fallback, and invalidate the series cache on set/reset.

**Depends on:** Step 1.

**Verify:** Trace custom/fallback/missing-file resolution through both full-size and grid cover
loading; confirm custom series entries cannot resolve as issues with matching IDs.

## Step 4: Route series-level pickers and refresh UI

**Files:** `src/Paperbunkr.App/ViewModels/CoverPickerViewModel.cs`,
`src/Paperbunkr.App/Views/CoverPickerView.axaml`,
`src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs`,
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs`,
`src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs`, plus the series-cover consumers identified
by the Step 3 resolver search.

**What:** Give the cover picker a series target mode that preserves its current file, issue, reading
list, and available external-provider sources while writing only series artwork. Route the two Detail
Change Cover actions and external-metadata Apply Cover to this mode; add reset-to-fallback. Keep
issue-targeted pickers unchanged. Refresh series tiles, Detail heroes, and issue/series samples when
their effective cover changes.

**Depends on:** Steps 1 and 3.

**Verify:** Confirm series set/reset never changes `Issue` or `IssuePage` state, issue cover pickers
still target issues, and refreshed screens use the same custom-series-first fallback rule.

## Verification constraints

This implementation pass will not add or run tests unless the user asks for test verification. The
code review will still inspect migration output, cache identity/precedence, failure paths, and
refresh behavior. Manual on-screen verification remains necessary for reader and Detail menus.
