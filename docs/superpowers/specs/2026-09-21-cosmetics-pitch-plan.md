# Cosmetics pitch — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md*

Working-tree note: the tree has uncommitted edits from a concurrent session (Wanted screen redesign, incl.
`LibraryScreen.axaml`, `Badges.axaml`, `PosterTile.axaml`). Edit on top of them; never revert or stage them.

## Survey findings that shape the steps
- Library cover templates in `LibraryScreen.axaml`: `PanoramaGridItemTemplate` (issue), `SeriesPanoramaGridItemTemplate`,
  `PosterGridIssueTemplate`, `PosterGridSeriesTemplate`. Each has a cover `Border` > `Grid` with `libScrim`, badges, dog-ear.
  Hover already flows through `OnCoverPointerEntered/Exited` code-behind (no per-tile binding needed).
- Scroll-smoothness work (2026-09-19) made per-tile realization cost matter (`LazyPart`). The new overlay must be ONE
  custom-rendered control (no template, no bindings) that reads its own DataContext.
- Models: `IssueListRow` (`ReadPercentage`, `IsRead`, `ReadingDirectionLabel`), `SeriesCardSample` (`IssueCount`, `UnreadCount`,
  `ReadingDirectionLabel`). Bottom-left = rating, bottom-right = select/dog-ear/remote, top-right = unread badge, so the ring
  goes **bottom-center** (deviation from the mock, which had bottom-right).
- Glow: `ThemeService.ApplyGlowRing` builds `PbGlowRing` (Spread 4, alpha 0x99) in one place, called from theme apply and
  accent override. Tiers = scale spread/alpha there.
- Settings: `AppSettings` + `PaperbunkrDbContext` HasDefaultValue + migration + `CosmeticThumbnailSettings` static cache;
  Preferences `ReducedMotion` is the toggle pattern (`PreferencesScreenViewModel` + `AppearanceSection.axaml` + `ThemeService`).

## Slice B
1. **Settings plumbing** — `AppSettings` (BindingSpine, ProgressRing, GlowTier, ReadingListMosaic, SplashAmbientMotion),
   `PaperbunkrDbContext` defaults, migration `AddCosmeticsPitchSettings` (+ migration test), `CosmeticThumbnailSettings` cache + `Changed` event.
   Verify: `AddCosmeticsPitchSettingsMigrationTests`.
2. **ArcGeometry helper** — extract from `CategoryDonut.cs` into `Views/ArcGeometry.cs`; donut behavior unchanged. Verify: existing Stats tests.
3. **TileCosmeticsOverlay** (+ `ITileProgressSource` on both models) — spine + ring, `HoverRing` set from `LibraryScreen.axaml.cs`
   pointer handlers; insert into the 4 templates after `libScrim`. Verify: new unit tests for fraction/spine-side logic; on-screen.
4. **Glow tiers** — `ThemeService.ApplyGlowRing(tier)`, live re-apply; Preferences selector. Verify: extend `ThemeServiceTests`.
5. **Preferences UI** — Appearance toggles (spine, ring, glow selector) via `PreferencesScreenViewModel` + `AppearanceSection.axaml`.
   Verify: `PreferencesScreenViewModelTests` persistence.

## Slice A / C — see spec; each will be planned here when slice B is verified.
6. Library Health chips (#5). 7. CBL mosaic (#7). 8. Timeline connectors (#4). 9. Splash ambient motion (#6).

## Test strategy
xUnit per project conventions; run `dotnet test` on App.Tests + Data.Tests filtered to touched areas; migration test per new column;
then a full-build check. On-screen verification is the user's (no UI automation without permission).
