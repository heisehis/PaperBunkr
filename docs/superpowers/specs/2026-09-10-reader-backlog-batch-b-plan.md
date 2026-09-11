# Reader Backlog Batch B — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md*

Branch `claude/reader-backlog-batch-b` (off master `d8aa2b1`). Two features; steps grouped, ordered
by dependency. Commit after each step (the shared worktree has clobbered in-progress work before —
Batch A's `ReaderScreen.axaml.cs`).

## Surveyed shapes this relies on

- **`ReaderScreenViewModel`**
  - `ComputeCanvasBackgroundBrush(ImageBackgroundMode mode, string? colorName)` → `IBrush`
    (private static); `RefreshDisplaySettings()` calls it with `appSettings.ImageBackgroundMode` +
    `.BackgroundColor` and assigns `CanvasBackgroundBrush` (`[ObservableProperty] IBrush`, default
    `ImmutableSolidColorBrush #0B0C0F`). `RefreshDisplaySettings` is wired to
    `PreferencesScreenViewModel.ReaderDisplaySettingsChanged` and also runs in `Load`.
  - `_pageOverrides` = `Dictionary<int, IssuePage>`, loaded in `Load` from
    `context.IssuePages.Where(p => p.IssueId == issue.Id)`; consumed for rotation at
    `_currentPageIndex` and per-thumbnail in the `Thumbnails` build loop.
  - `SetPageOverride(ReaderThumbnailSample? thumbnail, PageType? newType, int? newRotation)` —
    resolves `Thumbnails.IndexOf(thumbnail)` → page number, upserts/deletes an `IssuePage` row
    (deletes when back to `Story` + `0`), rebuilds `Thumbnails[pageNumber]`. Wrapped by
    `SetPageTypeStory/Cover/Advertisement/Deleted` + `SetPageRotation0/90/180/270` `[RelayCommand]`s.
  - `NextPage`: `int step = CurrentPageSecondary is not null ? 2 : 1; GoToPage(_currentPageIndex + step);`
  - `PreviousPage`: `int step = ArePagesPaired(_currentPageIndex - 2) ? 2 : 1; GoToPage(_currentPageIndex - step);`
  - `ArePagesPaired(int)` / `DoublePagePairingActive` / `TryDecodePairedPage(int, PixelSize)` — the
    pairing test, **unchanged** by this work. `EffectivePageLayoutMode`, `IsContinuousMode`
    (`[ObservableProperty]`), `_currentPageIndex`, `_loadedIssueId` all present.
- **`ReaderThumbnailSample`** (`Models/`) — `sealed class`, `init`-only: `IsSelected`, `CoverBrush`
  (required `IBrush`), `CoverImage` (`Bitmap?`), `IsBookmarked`, `PageType`, `IsRotated`; computed
  `HasPageTypeBadge`, `PageTypeBadgeText`.
- **`ReaderPageContextMenuBuilder`** — `Build(object? target)` returns `IReadOnlyList<ContextMenuEntry>?`;
  `ReaderThumbnailSample` → `[ SubMenu("Page Type", …), SubMenu("Rotate", …) ]`. `ContextMenuEntry.SubMenu`
  and `.Item(header, ICommand, param)` exist. `ReaderPageContextMenuBuilderTests` (in
  `[Collection(nameof(AvaloniaTestCollection))]`) already asserts the 2 submenus + counts.
- **`PreferencesScreenViewModel`** — `[ObservableProperty] ImageBackgroundMode _imageBackgroundMode`
  (`[NotifyPropertyChangedFor(nameof(ImageBackgroundModeText))]`), `ImageBackgroundModeText`
  get/set text passthrough, `BackgroundModeNames = Enum.GetNames<ImageBackgroundMode>()` (so a new
  enum value shows up automatically), `[ObservableProperty] string _backgroundColor`,
  `OnImageBackgroundModeChanged` / `OnBackgroundColorChanged` both persist + raise
  `ReaderDisplaySettingsChanged`. `PersistBehaviorSetting(Action<AppSettings>)`; hydration in the
  `_suppressBehaviorApply` block (`ImageBackgroundMode = settings.ImageBackgroundMode;` etc.).
- **`ReaderSection.axaml`** `reader.background` group — "Canvas background" `SuggestBox`
  (`IsStrict="True" Text="{Binding ImageBackgroundModeText}" Suggestions="{Binding BackgroundModeNames}"`);
  "Background color" row `IsVisible="{Binding ImageBackgroundMode, Converter={x:Static conv:ObjectConverters.Equal},
  ConverterParameter={x:Static entities:ImageBackgroundMode.Color}}"` with a preset `SuggestBox` +
  free `TextBox`. (`46e18fe` moved these to `SuggestBox`.)
- **Data** — `AppSettings` enum columns use `.HasConversion<string>().HasMaxLength(32).HasDefaultValue(X).HasSentinel(Y)`;
  `IssuePage` config is `HasKey(Id)` + `PageType.HasConversion<string>().HasMaxLength(32)` +
  unique `(IssueId, PageNumber)` index. `IssuePage` = `{ Id, IssueId, Issue?, PageNumber, PageType=Story, RotationDegrees }`.
  Migrations are `dotnet ef`-scaffolded then hand-edited; no-op `Down()` pattern (see
  `20260909221449_AddReaderMemoryLimitMb.cs`).
- **Engine** already has `ComicInfo` + `ComicPageInfo` + `ComicPagePosition { Default, Near, Far }`
  (`src/Paperbunkr.Engine/ComicPagePosition.cs`). **`IssuePage` rows are created only in
  `ReaderScreenViewModel`** — no scan/migration path creates them today.
- **`PageCanvas`** renders the page on a `CompositionCustomVisualHandler`
  (`ReaderPageVisualHandler`), not the control's `Render`. `RenderPaged` computes
  `plan = ComputeDrawPlan(...)` → `plan.DestRect` (the on-screen page rect); `RenderSpread` the same.
- Asset bitmap load: `new Bitmap(AssetLoader.Open(new Uri("avares://Paperbunkr.App/Assets/…")))`
  (`SkinService.cs:419`).

---

## Step 1 — Data: enum + columns + migration
**Files:** `src/Paperbunkr.Data/Entities/ImageBackgroundMode.cs` (edit),
`src/Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`src/Paperbunkr.Data/Entities/PageSpreadPosition.cs` (new),
`src/Paperbunkr.Data/Entities/IssuePage.cs` (edit),
`src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit),
`src/Paperbunkr.Data/Migrations/*_AddReaderBackgroundTextureAndSpreadPosition.cs` (new, via `dotnet ef`).
**What:**
- `ImageBackgroundMode` → `{ Auto, Color, Texture }` (append `Texture`; keep the doc comment,
  update the "minus `Texture`" line).
- `AppSettings.BackgroundTexture` — `public string? BackgroundTexture { get; set; }` (nullable).
  DbContext: `builder.Property(a => a.BackgroundTexture).HasMaxLength(64);` (nullable, no default).
- `PageSpreadPosition { Default, Near, Far }` — new enum, doc comment citing CE's `ComicPagePosition`.
- `IssuePage.SpreadPosition` — `public PageSpreadPosition? SpreadPosition { get; set; }` (nullable;
  `null` ≡ `Default`, avoids a DB default / sentinel on a possibly-populated table). DbContext:
  `builder.Property(p => p.SpreadPosition).HasConversion<string>().HasMaxLength(16);`.
- Scaffold: `dotnet ef migrations add AddReaderBackgroundTextureAndSpreadPosition -p src/Paperbunkr.Data`
  — **do not** `database update` (the dev DB is shared across worktrees). Hand-edit `Down()` to a
  no-op with the standard comment (columns left as orphans).
**Verify:** `dotnet build src/Paperbunkr.Data`; the generated `Up()` adds two nullable `TEXT`
columns; snapshot updated. Migration replay test in Step 9.

## Step 2 — Texture assets + generator
**Files:** `tools/gen-reader-textures/` (new — script + README),
`src/Paperbunkr.App/Assets/Textures/{neutral-dark,carbon,linen}.png` (new),
`src/Paperbunkr.App/Paperbunkr.App.csproj` (verify `Assets/**` is already `AvaloniaResource` — it
is for `Assets/Skins` etc., confirm the glob covers `Assets/Textures`).
**What:** a small standalone generator (Python + Pillow, or a `dotnet run` SkiaSharp console) that
writes three ~256×256 seamless PNGs: `neutral-dark` (near-black fine grain), `carbon` (dark
directional brushed weave), `linen` (soft light woven grey). Commit script **and** output. Tune
tiling: offset-wrap the noise so opposite edges match. Keep each PNG < ~80 KB.
**Depends on:** none.
**Verify:** open each PNG; tile it 3×3 in an image viewer / a scratch HTML page — no visible seam.
(On-screen in-app check is Step 10.)

## Step 3 — `ReaderBackgroundTextures` catalog
**Files:** `src/Paperbunkr.App/Services/Reader/ReaderBackgroundTextures.cs` (new),
`src/Paperbunkr.App.Tests/ReaderBackgroundTexturesTests.cs` (new).
**What:** `record ReaderBackgroundTexture(string Id, string DisplayName, Uri AssetUri)`; static
`All` (the 3, `neutral-dark` first); `Resolve(string?)` → match-or-`All[0]`; `LoadBitmap(string id)`
→ `new Bitmap(AssetLoader.Open(...))` cached in a `static ConcurrentDictionary<string, Bitmap>`
(bitmap never mutated → shareable). `Ids` helper for the Preferences swatch loop.
**Depends on:** Step 2 (assets must exist for `LoadBitmap` tests; use `[Collection(nameof(AvaloniaTestCollection))]`).
**Verify:** `ReaderBackgroundTexturesTests` — `Resolve(null)`/`Resolve("bogus")` → `neutral-dark`;
`LoadBitmap("carbon")` returns non-null and the same instance twice; `All` has 3 unique ids.

## Step 4 — Brush + page-shadow flag in `ReaderScreenViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit).
**What:**
- `ComputeCanvasBackgroundBrush` gains a 3rd param `string? textureId` and a `Texture` branch
  returning an `ImmutableImageBrush` tiled at native pixel size (`TileMode.Tile`, `Stretch.None`;
  pin the exact ctor arg list against Avalonia 12.1.1 during impl — `RelativeRect` absolute units
  vs plain tile). Missing/failed asset → `DefaultCanvasBackgroundBrush` (wrap `LoadBitmap` in
  try/catch). `Color` branch unchanged.
- `RefreshDisplaySettings` passes `appSettings.BackgroundTexture`.
- `[ObservableProperty] bool _showPageShadow;` — set in `RefreshDisplaySettings` to
  `appSettings.ImageBackgroundMode == ImageBackgroundMode.Texture && !IsContinuousMode`.
**Depends on:** Steps 1, 3.
**Verify:** `ReaderScreenViewModelTests` — `ComputeCanvasBackgroundBrush(Texture, _, "carbon")` is
an `ImmutableImageBrush` with `TileMode.Tile`; unknown id still returns an `ImmutableImageBrush`
(resolves to neutral-dark); `Color` unchanged; a `RefreshDisplaySettings` with the settings row
set to `Texture` sets `ShowPageShadow` (and clears it in continuous mode). Reuse the existing
`PaperbunkrDbContext.DatabasePathOverride` fixture pattern.

## Step 5 — Preferences: Texture option + swatch row
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` (edit),
`src/Paperbunkr.App/Styles/Primitives.axaml` (edit — `swatchSelected` style),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit).
**What:**
- VM: `[ObservableProperty] string? _backgroundTexture;` with `[NotifyPropertyChangedFor]` on
  `IsTextureNeutralDark`/`IsTextureCarbon`/`IsTextureLinen` (computed: `== id`, with
  `IsTextureNeutralDark` also true when `null`/unknown, matching `Resolve`). `SetBackgroundTexture(string id)`
  `[RelayCommand]` → set field + `PersistBehaviorSetting(s => s.BackgroundTexture = id)` +
  `ReaderDisplaySettingsChanged?.Invoke()`. Hydrate `BackgroundTexture = settings.BackgroundTexture;`
  in the load block. `BackgroundModeNames` already picks up `Texture` from `Enum.GetNames`.
- View: after the "Background color" `SettingsRow`, a new `pref:SettingsRow` "Background texture"
  `IsVisible="{Binding ImageBackgroundMode, Converter=…ObjectConverters.Equal, ConverterParameter={x:Static entities:ImageBackgroundMode.Texture}}"`.
  `SettingsContent` = `StackPanel Orientation="Horizontal" Spacing="8"` of 3 `Border`s
  (`Width="56" Height="38" CornerRadius="6" Cursor="Hand"`, `Background` = the texture brush — a
  tiny `x:Static` helper or a converter that calls `ReaderBackgroundTextures.LoadBitmap` →
  `ImmutableImageBrush`; simplest: a `BackgroundTextureSwatchConverter` taking the id), each with a
  `Button`/`InputElement.PointerPressed` → `SetBackgroundTextureCommand` + `CommandParameter`, and
  `Classes.active` bound to the `IsTexture*` bool. `AutomationProperties.Name` per swatch.
- `Primitives.axaml`: `Border.textureSwatch` (border/radius) + `.textureSwatch.active` (accent
  outline).
**Depends on:** Steps 1, 3, 4.
**Verify:** `PreferencesScreenViewModelTests` — `SetBackgroundTextureCommand.Execute("linen")`
persists `"linen"`, flips `IsTextureLinen`; a stored value hydrates; `BackgroundModeNames`
contains `"Texture"`. `dotnet build` (XAML). On-screen: Step 10.

## Step 6 — Per-page spread position: VM + commands
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Models/ReaderThumbnailSample.cs` (edit),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit).
**What:**
- `SpreadPositionAt(int index)` → `_pageOverrides.TryGetValue(index, out var r) ? (r.SpreadPosition ?? PageSpreadPosition.Default) : PageSpreadPosition.Default`.
- `NextPage`: after `int step = …`, `if (step == 2 && SpreadPositionAt(_currentPageIndex + 1) == PageSpreadPosition.Near) step = 1;`
- `PreviousPage`: `if (step == 2 && SpreadPositionAt(_currentPageIndex - 1) == PageSpreadPosition.Near && SpreadPositionAt(_currentPageIndex - 2) != PageSpreadPosition.Far) step = 1;`
- `SetPageOverride` — add `PageSpreadPosition? newSpread` param; `effectiveSpread = newSpread ?? row?.SpreadPosition ?? PageSpreadPosition.Default`; the "delete row" guard becomes
  `effectiveType == Story && effectiveRotation == 0 && effectiveSpread == Default`; set
  `row.SpreadPosition = effectiveSpread == Default ? null : effectiveSpread;` on the upsert branch;
  after a change re-run `RefreshCurrentPage()` (re-phase takes effect immediately).
  Update the 3 existing thumbnail-rebuild sites (`SetPageOverride`, `SetThumbnailBookmarked`,
  the two others at ~1834/2063) to carry `SpreadHint` through.
- 3 `[RelayCommand]` wrappers: `SetSpreadPositionDefault/Near/Far(ReaderThumbnailSample?)` →
  `SetPageOverride(thumb, null, null, PageSpreadPosition.X)`. (The existing `SetPageType*` pass
  `newRotation: null`; these pass `newType: null, newRotation: null`.)
- `ReaderThumbnailSample` — add `PageSpreadPosition SpreadHint { get; init; }` + computed
  `bool HasSpreadHint => SpreadHint != Default` + a `SpreadHintGlyph` string (chevron name).
- `Load`'s `Thumbnails` build loop: `SpreadHint = pageOverride?.SpreadPosition ?? PageSpreadPosition.Default`.
**Depends on:** Step 1.
**Verify:** `ReaderScreenViewModelTests` (uses the CBZ fixture with its landscape page):
- `NextPage` steps 1 when the current trailing page (`idx+1`) is `Near` and it would otherwise step 2.
- `PreviousPage` steps 1 when `idx-1` is `Near` and `idx-2` isn't `Far`; steps 2 when `idx-2` is `Far`.
- No overrides → step sizes unchanged (the existing `NextPage_StepsByTwo…` / spread tests stay green).
- `SetPageOverride` with `Near` creates an `IssuePage` row; setting a page that has a rotation
  back to spread `Default` keeps the row (rotation still set); `SpreadPositionAt` round-trips.
- `SetSpreadPositionNearCommand` on `Thumbnails[2]` sets `Thumbnails[2].SpreadHint == Near`.

## Step 7 — "Spread position" context submenu
**Files:** `src/Paperbunkr.App/ViewModels/ReaderPageContextMenuBuilder.cs` (edit),
`src/Paperbunkr.App.Tests/ReaderPageContextMenuBuilderTests.cs` (edit).
**What:** third entry after "Rotate", **only when `!_vm.IsContinuousMode`**:
`ContextMenuEntry.SubMenu("Spread position", [ Item("Automatic", _vm.SetSpreadPositionDefaultCommand, thumbnail),
Item("Near side (leading)", _vm.SetSpreadPositionNearCommand, thumbnail), Item("Far side (trailing)", _vm.SetSpreadPositionFarCommand, thumbnail) ])`.
Build the entry list conditionally (the builder currently returns a fixed array — switch to a
`List<ContextMenuEntry>` and `.Add` the third submenu when paged).
**Depends on:** Step 6.
**Verify:** `ReaderPageContextMenuBuilderTests` — paged VM → 3 submenus, third is "Spread position"
with 3 items; a VM in continuous mode → 2 submenus (no "Spread position"). Update the existing
`Assert.Equal(2, entries.Count)` test to reflect paged = 3.

## Step 8 — Thumbnail-rail glyph
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit).
**What:** in the thumbnail `DataTemplate` (`Grid` overlay, near the rotation `fi:SymbolIcon` /
page-type badge), a small `fi:SymbolIcon` `IsVisible="{Binding HasSpreadHint}"`, `Symbol` bound via
a converter or two toggled icons: `ChevronLeft` for `Near`, `ChevronRight` for `Far`,
`FontSize="{StaticResource PbIconSizeXs}"`, positioned bottom (offset from the rotation icon so
they don't overlap). `AutomationProperties.Name` = "Leading page" / "Trailing page".
**Depends on:** Step 6.
**Verify:** `dotnet build` (XAML weave — obj-dll delete + rebuild). On-screen: Step 10.

## Step 9 — Page drop-shadow (conditional scope)
**Files:** `src/Paperbunkr.App/Views/PageCanvas.cs` (edit — `ShowPageShadow` styled prop + push into
visual data), `src/Paperbunkr.App/Views/ReaderPageVisualHandler.cs` (edit — draw behind
`plan.DestRect` in `RenderPaged` + `RenderSpread`), `src/Paperbunkr.App/Views/ReaderScreen.axaml`
(bind `ShowPageShadow`).
**What:** add a `bool ShowPageShadow` `StyledProperty` on `PageCanvas`, bound
`{Binding ShowPageShadow}`, threaded into `ReaderPageVisualData` (like `HighQuality`). In
`RenderPaged`/`RenderSpread`, before `DrawBitmap`, when the flag is set: draw a soft dark blurred
rectangle behind `plan.DestRect` — a leased Skia path (`SKPaint` + `SKMaskFilter.MakeBlur` /
`SKImageFilter.CreateDropShadowOnly`), matching the blur idiom already in `CoverWallRenderer`.
**Scope guard — do this step only if the render-handler addition stays ≲ 30 lines and doesn't
perturb the transition path.** If it needs deeper surgery (lease/no-lease branching, transition
compositing), **stop, ship Steps 1–8 without the shadow, and record it as a follow-up** (the page
still abuts the texture, which is acceptable — it was one of the mock options). `ShowPageShadow`
from Step 4 stays regardless (harmless if unread).
**Depends on:** Step 4.
**Verify:** on-screen only (render-thread visual). No headless test.

## Step 10 — ComicInfo `<Pages>` import (conditional scope)
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit) or
`src/Paperbunkr.App/Services/CeLibraryMigrator.cs` (edit),
`src/Paperbunkr.App.Tests/*` (new import test).
**What:** where the scanner already reads embedded `ComicInfo.xml` story fields via `IInfoStorage`,
also read `comicInfo.Pages` (the Engine model exposes `ComicPageInfo.PagePosition`); for each page
with `PagePosition != Default`, upsert an `IssuePage` row with `SpreadPosition` set. **Read-only** —
no write-back.
**Scope guard — do this only if `ComicInfo.Pages` is already deserialized on the path the scanner
uses and the upsert is a small addition.** `IssuePage` rows are created nowhere but the reader
today; if this needs a new per-page write path, **defer it** (a note in the spec + roadmap —
per-page type/rotation aren't imported either). The in-reader menu is the primary surface.
**Depends on:** Step 1.
**Verify:** if built — a scan test with a `<Pages><Page Image="2" PagePosition="Near"/></Pages>`
ComicInfo fixture → `IssuePage.SpreadPosition == Near` for page 2.

## Step 11 — Migration replay test + docs
**Files:** `src/Paperbunkr.Data.Tests/*_AddReaderBackgroundTextureAndSpreadPositionMigrationTests.cs`
(new, mirror `AddReaderMemoryLimitMbMigrationTests`), `docs/ce-feature-inventory.md`,
`docs/Paperbunkr-Roadmap.md`, `docs/alpha-todo.md`.
**What:** up → down(no-op) → up replay test asserting the no-op `Down()` leaves the columns.
`ce-feature-inventory.md` rows 136 + 138 + the §239 gap list: background texture (v1 scope, named
deviations) + per-page spread position shipped; paper-texture-over-page still a gap.
Roadmap + alpha-todo session notes (P0–P7 unchanged; Beta-backlog).
**Depends on:** Steps 1–10.

## Step 12 — Full verification
`rm src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.{dll,pdb}` then
`dotnet build src/Paperbunkr.App` (XAML weave gotcha — new View bindings + swatch converter).
`dotnet build Paperbunkr.sln`. Targeted suites:
`dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~ReaderScreenViewModelTests|FullyQualifiedName~PreferencesScreenViewModelTests|FullyQualifiedName~ReaderPageContextMenuBuilderTests|FullyQualifiedName~ReaderBackgroundTextures"`
+ `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~AddReaderBackgroundTexture"`.
Launch the exe once (weave ran). **On-screen (user):** each texture tiles seamlessly + persists +
live-applies; page shadow only with texture (if Step 9 shipped); spread override actually shifts
pairing on a real book; thumbnail chevron shows; submenu gone in continuous mode.
