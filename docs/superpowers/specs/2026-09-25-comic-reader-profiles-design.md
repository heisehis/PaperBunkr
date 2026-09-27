# Comic reader — Profiles (slice F2) design

Date: 2026-09-25. Status: **built 2026-09-25** (uncommitted; on-screen check by the user pending, see "Implementation notes" at the end).
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, item #15 ("Reader profiles"). Second of three specs split from slice F:
F1 "Reach" (`2026-09-25-comic-reader-reach-design.md`), **F2 "Profiles"** (this one), F3 "Comfort" (`2026-09-25-comic-reader-comfort-design.md`).
**Build order is F1 → F2 → F3.** F2 assumes F1's tap-zone settings and reader palette exist (both are optional fields of a profile, and the palette gains
"Profile: …" entries); if F1 is not built when F2 starts, those two pieces are skipped and added when F1 lands. Slices A, B, G are built and uncommitted; this
builds on the same shared working tree.

## Facts this design rests on (verified 2026-09-25)

- **CE precedent.** `DisplayWorkspace` (`_reference/ComicRackCE/ComicRack/Config/DisplayWorkspace.cs`) has `WorkspaceType` flags `WindowLayout`, `ViewsSetup`,
  `ComicPageLayout`, `ComicPageDisplay`. The two comic-page flags carry the reader part: `LandscapeLayout`/`PortraitLayout` (`BookPageLayout`),
  `RightToLeftReading`, `PageTransitionEffect`, `DrawRealisticPages`, `PageImageBackgroundMode`, `BackgroundColor`, `BackgroundTexture`, `PageMargin`,
  `PageMarginPercentWidth`, paper texture. A CE workspace is a **global snapshot you switch between**: applying it overwrites the current display config. CE has
  no per-series link and no hotkey. Paperbunkr's Library and Books "Saved Workspaces" (`2026-09-03-library-saved-workspaces-design.md`) already generalised the
  idea per screen.
- **Existing machinery to reuse.** `Workspace` (table: `Screen`, `Name`, `SortOrder`, `IsBuiltIn`, `StateJson`) and `WorkspaceService`
  (`List`/`Create`/`UpdateState`/`Rename`/`Delete`/`Reorder`/`EnsureBuiltInsSeeded`, built-ins read-only and enforced in the service). `WorkspaceScreen` has
  `Library = 0`, `Books = 1`; the column is an int, so `Reader = 2` needs **no schema change**. State records live in `Models/WorkspaceState.cs`
  (`LibraryWorkspaceState`, `BooksWorkspaceState`, `WorkspaceStateJson.Serialize/Deserialize…`, tolerant on read). `PromptWorkspaceName` (MainViewModel) is the name
  prompt the Library and Books view models already receive as `promptForName`.
- **Which reader settings already have a global layer.** `AppSettings`: `DefaultPageFitMode`, `DefaultAutoRotate`, `DefaultPageLayoutMode`, `PageTransitionStyle`
  (`None/Slide/Crossfade`), `PageTransitionDurationMs`, `DefaultBrightness/Contrast/Saturation/Gamma`, `ImageBackgroundMode` (`Auto/Color/Texture`),
  `BackgroundColor`, `BackgroundTexture`, `PageMarginEnabled`, `PageMarginPercentWidth`, `ReaderAutoHideChrome`, `ReaderChromeHoverMode` (`PerCluster/Ambient`).
  Resolution today: fit mode and auto-rotate = `Issue override ?? Series override ?? global` (`ReaderDefaultsResolver`, slice A); layout mode =
  `Issue.PageLayoutModeOverride ?? Series.PageLayoutMode ?? global`; adjustments = global default plus the issue's delta; background/margin/transition/chrome are
  global-only.
- **Reading mode cannot be layered.** `Series.ReadingMode` is a concrete, non-null value (default `LeftToRight`), so a profile could never sit under it as a
  fallback. It is a series fact (content type seeds it, the toolbar toggle writes it) and stays out of profiles.
- **`AppSettings.ShowScrubberOverlay` has no consumer** in `Paperbunkr.App` (the column exists, nothing reads it), so it is not part of a profile.
- **The reader reads settings in two places:** `Load` (`appSettings = context.GetOrCreateAppSettings()`, `ReaderScreenViewModel.cs` ~1095-1175) and
  `RefreshDisplaySettings` (called from `Load` and from `Preferences.ReaderDisplaySettingsChanged`). Fit mode and auto-rotate are **not** persisted by a property
  handler; they persist through explicit commands (`SetFitMode` ~1471, "apply to series" ~1503). Brightness/contrast/saturation/gamma persist through
  `PersistAdjustmentOverride`, which has a `_suppressAdjustmentPersist` guard. So a profile can be applied without writing overrides, as long as the apply path
  sets values under that guard and never calls the persisting commands.
- **The reader view model has no toast channel.** `MainViewModel` owns `ShowToast(ToastRequest)`; screen view models get it as a delegate or an event
  (`ReviewPromptRequested` is the precedent for an event wired in `MainViewModel`). Actionable toasts (`ToastRequest.Actions != null`) are persistent, others
  close after 5 s (`MainWindow.axaml.cs:534-548`).
- **Series defaults UI.** The comic and manga detail screens share one control that shows and clears the series' fit/auto-rotate defaults
  (`DetailTabsViewModel` `HasSeriesFitDefault`, `SeriesFitDefaultLabel`, `ClearSeriesFitDefault`, `ClearSeriesAutoRotateDefault`, `UpdateSeriesReaderDefaults`).

## Scope

In: a `Reader` workspace kind holding named reader profiles; the layered resolution (`AppSettings` → profile → series → issue); a per-series profile pointer and
a default profile; a session-only live switch (hotkey, drawer picker, palette entries); capture from the current reader state; Preferences management; three
built-in profiles.

Out (decided): reading mode/direction in a profile; a field-by-field profile editor; a "modified" drift indicator; per-orientation (landscape/portrait) layouts as in
CE; paper texture and "realistic pages" (not features of this reader); profiles for the EPUB/PDF readers; import/export of profiles.

## 1. Model: a profile is a layer, and switching is a session overlay

`ReaderProfileState` (App.Models, new; every field nullable, null = "this profile does not set it") holds:
`FitMode`, `AutoRotate`, `PageLayoutMode`, `PageTransitionStyle`, `PageTransitionDurationMs`, `Brightness`, `Contrast`, `Saturation`, `Gamma` (absolute values,
-100..100), `ImageBackgroundMode`, `BackgroundColor`, `BackgroundTexture`, `PageMarginEnabled`, `PageMarginPercentWidth`, `ReaderAutoHideChrome`,
`ReaderChromeHoverMode`, and, from F1, `PagedTapZoneLayout`, `PagedTapZoneInvert`, `ContinuousTapZoneLayout`, `ContinuousTapZoneInvert`. F3 adds `ShowSessionHud`,
`WarmShiftEnabled`, `WarmShiftStrength` (nullable, same file). JSON via `ReaderProfileStateJson.Serialize/Deserialize` next to `WorkspaceStateJson`: tolerant of
unknown and missing keys, enums stored by name. Not included by design: zoom and pan, key bindings, gamepad and mouse-button toggles, memory and pre-open settings.

**Precedence, lowest to highest:** `AppSettings` → **profile** → series overrides → issue overrides.

- Which profile: `Series.ReaderProfileId`, else `AppSettings.DefaultReaderProfileId`, else none ("Standard": plain `AppSettings`). A pointer to a deleted or missing
  row is skipped (no foreign key, the `LibrarySortVirtualTagId` precedent), falling to the next candidate.
- **Pure `ReaderProfileOverlay.Apply(AppSettings, ReaderProfileState?)`** returns a **detached copy** of the settings (`context.Entry(settings).CurrentValues.ToObject()`)
  with the profile's non-null fields laid over it. It never touches the tracked entity, so nothing from a profile can be written back by a later `SaveChanges`.
  `Load` and `RefreshDisplaySettings` resolve the profile once, take the overlaid copy as `appSettings`, and pass it to the code that already reads it
  (`ReaderDefaultsResolver`, brightness defaults, background, margin, transition, chrome, F1's tap zones). `ReaderDefaultsResolver` needs no change: it already
  takes a settings object as the bottom layer.
- Layout mode: `Issue.PageLayoutModeOverride ?? Series.PageLayoutMode ?? overlaid.DefaultPageLayoutMode` (already how `RefreshDisplaySettings` computes it once it
  reads the overlaid copy).
- Adjustments: `_brightnessGlobalDefault` etc. become the overlaid values, so the issue's stored delta stays a delta over whatever the profile provides.

**Session switch (the hotkey / picker / palette).** The reader view model holds `SessionProfileId` (int?, "Standard" = a sentinel meaning "explicitly none"). Choosing
a profile sets it and re-applies to the open reader:

- the session profile's non-null fields **win over series and issue overrides** (an explicit choice is the latest statement; otherwise switching to "Webtoon"
  would silently not change fit mode for a series that has its own fit default);
- application is live and **never persists**: values are set under `_suppressAdjustmentPersist` and through direct property sets, never through `SetFitMode`,
  "apply to series", or any writer, so no `Issue`/`Series` row changes;
- it survives `Load` of another issue (continue to the next issue, end card) and is cleared by `GoBack` (leaving the reader) and by choosing "Standard";
- a manual change after switching (fit mode, brightness, ...) behaves exactly as today (issue override write); there is no drift indicator.

`ReaderProfileResolution` (pure) implements the per-field precedence (`session ?? issue ?? series ?? profile ?? global`, with adjustments as absolute-vs-delta) and is
unit-tested per field, so the view model only wires values.

## 2. Storage

- `WorkspaceScreen.Reader = 2`. `WorkspaceService.EnsureBuiltInsSeeded` gains the Reader built-ins (same keyed-on-`(Screen, Name)` idempotence). No change to the
  service otherwise; `List(WorkspaceScreen.Reader)`, `Create`, `UpdateState`, `Rename`, `Delete`, `Reorder` all apply as is.
- New nullable columns: `Series.ReaderProfileId` (int?) and `AppSettings.DefaultReaderProfileId` (int?), both plain ids. One migration `AddReaderProfiles`,
  **no-op `Down()`** (the migration rollback-chain bug memo), `HasDefaultValue` not needed for nullable ints. `Up()` is read before it is kept.
- **Built-ins** (read-only, duplicate to edit; state is fixed at seeding, so F3's warm-shift fields are not retro-added to them):
  - **Manga night**: `ImageBackgroundMode.Color`, `BackgroundColor` near-black (`#0E0E12`), `Brightness` -8, transition `None`, `PageMarginEnabled` false.
  - **Webtoon**: `FitMode.FitWidth`, `PageLayoutMode.Single`, `PageMarginEnabled` false, transition `None`, `ReaderAutoHideChrome` true.
  - **Tablet**: `FitMode.Fit`, `PageLayoutMode.Single`, `ReaderAutoHideChrome` false, `ReaderChromeHoverMode.Ambient`, F1 tap zones `Kindlish` (paged) and
    `Disabled` (continuous) if F1 is present.

## 3. Reader UI

- **Drawer "Profile" section** at the top of the display area: the active profile's name ("Standard" when none) with a flyout list built on the shared
  `ContextMenuEntry` mechanism: *Standard* (clears the session profile), each profile (check mark on the active one), then *Save current as new profile…*,
  *Update “X” with current* (user profiles only), *Use for this series*, *Use as my default*, *Clear series profile* (visible only when the series has one).
  The name prompt is `PromptWorkspaceName`, handed to the reader view model like the other screens.
- **`Reader.NextProfile`** (remappable, `ConflictContext.Always`, default `P`; the registry duplicate test guards it) cycles Standard → profile 1 → … → back. Every
  switch shows a short toast, "Profile: Manga night", through a new `ReaderScreenViewModel.ToastRequested` event that `MainViewModel` wires to `ShowToast` (F3 reuses
  it).
- **Palette (F1):** `ReaderPaletteCatalog` gains one "Profile: <name>" entry per profile plus "Profile: Standard" and "Save current as profile…". Without F1, the
  hotkey and drawer picker are the only entry points.
- **Capture** (`CaptureProfileState`) snapshots what is on screen: the live `FitMode`, `AutoRotate`, `EffectivePageLayoutMode`, `Brightness/Contrast/Saturation/Gamma`
  and transition values, plus the resolved (overlaid) background, margin, chrome and tap-zone settings. All fields are written, so a saved profile is a complete
  snapshot (predictable), unlike the built-ins which set only what they need.
- **Series pointer:** *Use for this series* writes `Series.ReaderProfileId` and shows a toast. The shared series-defaults control on the comic and manga detail
  screens gains a profile row (`HasSeriesProfile`, `SeriesProfileLabel`, `ClearSeriesProfileCommand`, using `UpdateSeriesReaderDefaults`) so it can be seen and
  cleared where fit and auto-rotate defaults already are. *Use as my default* writes `AppSettings.DefaultReaderProfileId`.

## 4. Preferences

Preferences → Reader gains a **PROFILES** group (`Tag="reader.profiles"` plus its `PreferenceIndex` entry): a "Default profile" picker (`SuggestBox IsStrict`, entries
Standard + every profile), then the profile list (name, built-in badge) with rename, delete and drag/up-down reorder for user rows, and a hint that profiles are
captured from the reader's drawer. A profile deletion also clears `DefaultReaderProfileId` when it pointed at it (series pointers are left; they fall through on
read). Deleting and renaming go through the routed-event deferral rule (the row buttons live inside the list they mutate). Changes raise the existing
`ReaderDisplaySettingsChanged` so an open reader re-resolves.

## Build order

1. `ReaderProfileState` + JSON + `ReaderProfileOverlay` + `ReaderProfileResolution` (pure, tested per field), `WorkspaceScreen.Reader`, built-ins, service tests.
2. Migration `AddReaderProfiles` + entity columns + `HasDefaultValue`-free config; migration test (nullable, round trip).
3. Reader integration: resolve at `Load`/`RefreshDisplaySettings`, `SessionProfileId`, session apply/clear, `ToastRequested`, `NextProfile` command and key.
4. Drawer section, capture/update/use-for-series/use-as-default, name prompt wiring in `MainViewModel`.
5. Detail-screen series profile row; Preferences PROFILES group.
6. Palette entries (if F1 present), docs, wiki (`Reading.md`, `Preferences.md`, `Keyboard-Shortcuts.md`), memory, review checklist over the new XAML.

## Testing

- **Pure:** `ReaderProfileOverlay` (null fields keep the base, each field overrides, the input entity is never mutated); `ReaderProfileResolution` (every layer order,
  session over issue/series, adjustments absolute vs delta, missing pointer falls through); JSON tolerance (unknown/missing keys, bad enum).
- **Service:** `EnsureBuiltInsSeeded` seeds the Reader built-ins once and leaves user rows alone; built-ins cannot be renamed, updated or deleted; deleting a profile
  clears the default pointer.
- **View model:** opening an issue whose series points at a profile applies it; a session switch changes fit/adjustments/background live and writes **no** `Issue` or
  `Series` row (assert against the database); it persists across `Load` and clears on `GoBack`; capture round-trips; `NextProfile` cycles; the toast fires; a manual
  change after a switch still writes the issue override as today.
- **Data:** migration adds two nullable columns; unknown `WorkspaceScreen` values round-trip.
- **Not testable here:** the look of the three built-ins and the drawer flyout; the user's on-screen check is switching profiles on a real manga, a webtoon and a
  normal issue.

## Open risks

- **Session-wins-over-overrides is deliberate but surprising** if someone expects a series fit default to hold; the toast names the profile so the change is
  visible, and choosing "Standard" restores series/issue values.
- **`AppSettings` copying** via `CurrentValues.ToObject()` copies every column (a wide entity); it happens once per `Load` and per `RefreshDisplaySettings`, both rare.
- **Shared working tree:** `ReaderScreenViewModel.cs`, `ReaderScreen.axaml`, `KeyboardCommandRegistry.cs`, `DetailTabsViewModel.cs`, `AppSettings.cs`, the model
  snapshot and `MainViewModel.cs` are already modified by earlier slices and F1; edits stay narrow and `git diff` is read before and after.

## Implementation notes (2026-09-25)

Built in the order given under "Build order". Where the code differs from the design:

- **The overlay copies by reflection** (`ReaderProfileOverlay.Clone` copies every settable `AppSettings` property into a new instance), not by `context.Entry(...).CurrentValues.ToObject()`: it
  needs no context and gives the same detached copy. With no profile the settings themselves are returned (callers only read them).
- **Selection is pure** (`ReaderProfileSelector.Select` over the candidate rows; `Resolve` loads only the ids in play). "Standard" is the session id 0 (`StandardSessionId`): it ignores the series pointer
  and the default. A session profile's fields win over series and issue overrides for fit, auto-rotate, layout and the four adjustments (absolute values, replacing the issue's delta).
- **Reader wiring:** `Load` and `RefreshDisplaySettings` both resolve the profile; `ReaderScreenViewModel.Profiles.cs` holds the session switch (`ApplySessionProfile`, `ReapplyProfile`, `NextProfile`),
  capture (`CaptureProfileState`), the drawer list (`Profiles`, `ReaderProfileRow`) and the pointers. Applying values never goes through `SetFitMode` or any persisting command, and the adjustments set
  under `_suppressAdjustmentPersist`; a test asserts no `Issue`/`Series` row changes. The session profile is cleared in `GoBack`.
- **Storage:** as designed (`WorkspaceScreen.Reader = 2`, `Series.ReaderProfileId`, `AppSettings.DefaultReaderProfileId`, migration `AddReaderProfiles` with a no-op `Down()`).
  `WorkspaceService.Delete` also clears the default pointer; the three built-ins are seeded by `EnsureBuiltInsSeeded`. `AppSettings.ShowScrubberOverlay` has no consumer, so it is not in a profile.
- **Two view models refresh each other's lists** through `Reader.ProfilesChanged` and `Preferences.ReaderProfilesChanged`, wired in `MainViewModel` after `Preferences` is constructed.
- **Detail screens:** the series-defaults card gained a third column (`SeriesProfileLabel`, `HasSeriesProfile`, `ClearSeriesProfileCommand`); a pointer to a deleted profile shows "Deleted profile" and can be cleared.
- **Palette:** "Profile: Standard", one entry per profile, "Save current look as a profile…" and the two pointer actions.
- **`RemoteRowIsolationTests`** allowlists `ReaderScreenViewModel.Profiles.cs` (it opens remote-inclusive contexts like the reader itself); the palette partial uses a default context.
- **Not verified:** how the three built-ins look and the drawer flyout; both are on-screen checks.
