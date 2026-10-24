# Paperbunkr Cosmetics Package — Implementation Plan

*Implements: `docs/superpowers/specs/2026-10-18-cosmetics-package-design.md`*

## Step 1: Shared visual state and theme resources
**Files:** `src/Paperbunkr.App/Models/IssueCardSample.cs`, `src/Paperbunkr.App/Models/ReaderThumbnailSample.cs`, `src/Paperbunkr.App/Controls/BrandMark.cs`, `src/Paperbunkr.App/Styles/AppTokens.axaml`, focused tests under `src/Paperbunkr.App.Tests/`.
**What:** Inspect and extend existing models/resources for explicit edition labels, cover availability, bookmark count/accessibility, and selection/collection visual states. Keep the cover fallback truthful and avoid new domain fields. Preserve existing control APIs where possible.
**Depends on:** none.
**Verify:** Targeted resolver/control tests; inspect theme token resolution under Vintage Pop and maximum contrast.

## Step 2: Library card package
**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml`, `LibraryScreen.axaml.cs`, `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` and relevant partials, `src/Paperbunkr.App/Views/TileCosmeticsOverlay.cs`, related Library model/tests.
**What:** Add the cover mat, stronger app-motion-governed hover spotlight with stable pointer coverage, collection accent extensions behind the existing `SeriesAccentColor` preference, explicit edition ribbons, series divider cards in grouped poster mode, and a stronger selected wash. Preserve the binding spine/progress overlay, virtualization performance, selection counts, focus ring, and selection checkbox behavior.
**Depends on:** Step 1.
**Verify:** Targeted `LibraryScreenViewTests`, `LibraryScreenViewModelTests`, `LibraryListLayoutTests`, and `TileCosmeticsOverlayTests`; include state/count and pointer-boundary cases where existing harnesses support them.

## Step 3: Detail issue rail and reading-list progress path
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml`, `DetailTabs.axaml.cs`, `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs`, `src/Paperbunkr.App/Models/PosterRailItem.cs` or a focused issue-rail model, `src/Paperbunkr.App/Views/ReadingListsScreen.axaml`, `ReadingListPageView.axaml`, `src/Paperbunkr.App/ViewModels/ReadingListPageViewModel.cs` and `.Path.cs`, relevant tests.
**What:** Add issue covers and transient “Reading now” state to the existing detail issue rail, and add ordered progress segments/counts to reading-list cards using existing rows/read state. Preserve current order, actions, filters, and missing-item semantics.
**Depends on:** Step 1.
**Verify:** `DetailTabsViewModelTests` and reading-list page/card tests; assert order, active-reader marker lifecycle, exact read/total counts, and unavailable items remaining incomplete.

## Step 4: Comic Reader frame setting and bookmark consistency
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs`, a `ReaderPageFrameStyle` enum, a migration plus Designer/snapshot, `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` and `.Profiles.cs`, `src/Paperbunkr.App/Views/PageCanvas.cs`, `ReaderScreen.axaml`, `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml`, `PreferencesScreenViewModel*.cs`, targeted App/Data tests.
**What:** Add the global persisted None/Paper edge/Soft shadow setting (default None), bind it through open-reader refresh, decouple it from the texture-mode shadow flag, apply individual-page framing in paged and continuous modes, and unify the existing bookmark mark on dots and rail with accessible count/details.
**Depends on:** Step 1.
**Verify:** Focused Reader and preferences tests plus an AppSettings migration round-trip test; inspect high-contrast, theme switching, both page layouts, and no-gutter behavior. Ensure the active migrations snapshot is coherent before generating a migration.

## Step 5: Reflowable book chapter openings
**Files:** `src/Paperbunkr.App/Views/BookReaderScreen.axaml`, `BookReaderScreen.axaml.cs`, `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs`, existing chapter/content HTML or CSS builder found in the Reader implementation, `BookReaderScreenViewModelTests` and focused rendering tests.
**What:** Add the restrained chapter ornament and opening cap only when parsed reflowable chapter headings exist. Keep reader typography, selection/search semantics, screen-reader reading order, and PDF rendering unchanged.
**Depends on:** Step 1.
**Verify:** Tests for chapter-present/absent content and regression coverage for book reading settings. Compile XAML with its code-behind present if a new view is necessary.

## Step 6: Provenance marks, cover placeholders, and live theme folio
**Files:** `src/Paperbunkr.App/Controls/BrandMark.cs`, `src/Paperbunkr.App/Services/MarkResolver.cs`, `src/Paperbunkr.App/Views/AsyncCoverImage.cs`, relevant Library/detail/connection views and view models, `src/Paperbunkr.App/Views/Preferences/AppearanceSection.axaml`, `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.Appearance.cs`, `ThemeSummary.cs`, focused tests.
**What:** Audit existing placements and fill only meaningful gaps for issue-level recorded sources and actionable series tracker links. Add accurate missing/unavailable placeholder presentation without treating null paths/cache entries as proof. Replace/enlarge the selected-theme inspector sample with tabbed real Library/Reader/Bookshelf components that refresh live from the chosen skin.
**Depends on:** Steps 1 and 2 for shared resources.
**Verify:** `BrandMarkRenderSmokeTests`, `AsyncCoverImageTests`, `ThemeServiceTests`, `PreferencesScreenViewModelTests`; validate provenance null/merge cases and live Vintage Pop/maximum-contrast samples.

## Step 7: Integration review and focused verification
**Files:** Remaining touched UI/test files; no unrelated cleanup.
**What:** Run the Avalonia review checklist across default, compact, and wide layouts; check keyboard focus, contrast, reduced motion, virtualization, collection-color preference, and no-data/placeholder states. Resolve issues found and update the design spec’s implementation notes if behavior must differ from the approved design.
**Depends on:** Steps 2–6.
**Verify:** Targeted test filters for touched classes, one serialized `dotnet build` proving the Avalonia XAML weave, and manual app review. Do not start concurrent builds/tests or run the long full suite during routine work.
