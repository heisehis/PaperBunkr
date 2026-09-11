# Reader Backlog Batch B — design

*Date: 2026-09-10. Status: draft for review.*

Two reader-canvas items from the reader backlog, one spec, two sections. Both reverse or extend a
deliberate deviation named in an earlier spec; both verified against `_reference/ComicRackCE`.

1. **Background texture** — a tiled image behind the page, a third option next to `Auto` and solid
   `Color`. Named as skipped in `docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-
   chrome-overlays-design.md` §10 ("`Texture` (image-tile background) skipped as a named deviation,
   needs bundled texture assets or a file-picker surface bigger than this pass").
2. **Per-page spread position** — CE's `ComicPagePosition { Default, Near, Far }` escape hatch for
   pairing drift, explicitly deferred "to its own follow-up spec" in `docs/superpowers/specs/
   2026-08-15-reader-double-page-spread-design.md` §1.

Not in this batch: CE's separate "paper texture" (a texture multiplied *over* each decoded page,
hardware-renderer-only — a render-pipeline change); the 5 CE background layout modes; a
user-file-picker background; ComicInfo `<Pages>` **write-back** of the spread flag.

---

## CE-parity notes

Verified against `_reference/ComicRackCE` (subagent sweep, 2026-09-10):

### Background

- CE's `ImageBackgroundMode` is `{ Auto=0, Color=1, Texture=2, Default=1 }`
  (`ComicRack.Engine/Display/ImageBackgroundMode.cs`). **Auto** samples a colour from the page;
  **Color** is a solid named colour; **Texture** is a tiled/placed image behind the page.
- **Two separate systems, easy to conflate.** *Background texture* (`DisplayWorkspace.BackgroundTexture`
  + `BackgroundImageLayout`) fills the whole canvas behind the page, painted in
  `ImageDisplayControl.RenderImageBackground` — active only when mode is `Texture`. *Paper texture*
  (`PaperTexture` + `PaperTextureStrength` + `PaperTextureLayout`) is `Multiply`-blended *over* the
  page bounds in `ComicDisplayControl.RenderImageEffect`, **hardware renderer only**, independent of
  `ImageBackgroundMode`. This spec is the first one only.
- CE's background texture is a **file path** (`Image.FromFile`), from either a bundled set
  (`Resources/Textures/Backgrounds/*.{jpg,gif,png}` — BrickWall, BrushedMetal, LightWood,
  ChalkBoard, Grass, PlankWood, …) or a user file picker (`OpenFileDialog`). `BackgroundImageLayout`
  is a WinForms `ImageLayout` (None/Tile/Center/Stretch/Zoom, default Tile). The texture **scales
  with page zoom** (a transform block in `RenderImageBackground`).
- Persisted on `DisplayWorkspace` (`[Serializable]`), effectively global (whatever workspace is
  current) — **not per-book, not stored in the comic**.
- **Page margin** is not a drawn border — it is a zoom reduction, so the background shows through
  the gap and the page just floats smaller inside it. When `DrawRealisticPages` is on (default
  true) CE also draws a blurred outside **drop-shadow** + a 1px black border + spine "page bow"
  shading around the page over the background (`ComicDisplayControl.DrawPageOrnaments`).
- Preferences UI: `ComicDisplaySettingsDialog` — a `cbBackgroundType` dropdown (the 3 modes), a
  colour picker for `Color`, and for `Texture` an **owner-drawn thumbnail dropdown** + a "..."
  browse button + a layout dropdown.

**Deliberate deviations, named:** bundled-only (no user file picker) v1; Tile-only (no layout
picker) v1; **static tiling, not zoom-scaled**; a 3-texture curated set rather than CE's ~14; a
texture **id** rather than a file path; the page shadow scoped to texture-mode only rather than
gated on a "realistic pages" toggle.

### Per-page spread position

- CE's flag: `enum ComicPagePosition : short { Default, Near, Far }`, an `[XmlAttribute]
  PagePosition` on the `ComicPageInfo` struct — serialized as `<Page … PagePosition="Near" />`
  inside `<Pages>` in ComicInfo (embedded XML or CE's DB). **Not** a `DoublePage="true"` attribute
  — CE has no serialized double-page page attribute (`ComicPageInfo.IsDoublePage` is *computed*
  from `ImageWidth > ImageHeight`).
- Set per-page(s) from `PagesView`'s right-click **"Page Position"** submenu → radio items
  "Default" / "Near" / "Far". No "join with previous / next" phrasing exists for this feature.
- **CE's display pairing (`ComicDisplayControl.GetImageInfo`) never reads `PagePosition`.** It
  pairs `CurrentPage`+`NextPage` purely on: two-page mode, neither page a single type
  (FrontCover/BackCover), neither landscape. `PagePosition` is consumed only by the **navigation
  stepping** in `ComicDisplay.DisplayNextPage` / `DisplayPreviousPage`, which controls *which* page
  becomes `CurrentPage` — i.e. the spread phase:
  - **Forward:** normally advance 2 (a spread). If the page at current+1 is `Near`, advance 1
    instead — forcing that page to lead the next spread and shifting every subsequent pairing by one.
  - **Backward:** normally go back 2. Go back 1 if a single-type / aspect-ratio-double page is
    involved, **or** the page at −1 is `Near` while the page at −2 is not `Far`.
  - `Far` = "may stay as the second/right page of its spread" — it suppresses the `Near`-driven
    single-step in the backward case.
- Default (`PagePosition == Default`): CE still auto-detects wide pages by aspect ratio (a
  landscape page shows solo / as a full spread); it does not treat everything as solo.

**Deliberate deviations, named:** readable menu labels ("Automatic / Near side (leading) / Far
side (trailing)") instead of CE's terse "Default / Near / Far"; DB-only persistence (ComicInfo
`<Pages>` write-back deferred, matching the existing per-page type/rotation deferral); a
thumbnail-rail indicator CE doesn't have (CE has a text "Position" column in `PagesView` instead).

---

## Item 1 — Background texture

### 1.1 Data

- **Enum:** add `Texture` to `Paperbunkr.Data.Entities.ImageBackgroundMode` →
  `{ Auto, Color, Texture }`. Stored as its string name
  (`PaperbunkrDbContext` already: `.HasConversion<string>().HasMaxLength(32).HasDefaultValue(Color)
  .HasSentinel(Auto)`), so adding a member is **not** a schema change for the enum column itself.
- **New column:** `AppSettings.BackgroundTexture` — `string?`, nullable, no default. Holds a
  **texture id** (`"neutral-dark"` / `"carbon"` / `"linen"`), never a file path. `null` / empty /
  unknown id → the first texture in the catalog (`neutral-dark`).
- Global-only — no per-`Issue` override, consistent with the rest of the background/margin
  settings ("a personal viewing preference, not a per-book concern").

### 1.2 Texture assets

- `src/Paperbunkr.App/Assets/Textures/{neutral-dark,carbon,linen}.png` — seamless, ~256×256,
  authored so the tile seam is invisible at 1× on a real monitor.
- **Produced by a committed generator** (`tools/gen-reader-textures/` — a small standalone
  SkiaSharp or Python/PIL script) run once; both the script and its output PNGs are committed.
  Reproducible and tweakable, but ships as static ~30–80 KB assets — no MSBuild step, no runtime
  generation. (Rejected: sourcing stock textures — licensing / can't; build-time generation —
  needless MSBuild surface; runtime generation — needless complexity.)
- `neutral-dark` — near-black, a barely-there fine grain (reads as "not flat black" under the dark
  UI). `carbon` — dark, a directional brushed weave. `linen` — soft light woven grey.

### 1.3 Catalog

`Services/Reader/ReaderBackgroundTextures.cs` (new) — the single source of truth:

```csharp
public sealed record ReaderBackgroundTexture(string Id, string DisplayName, Uri AssetUri);

public static class ReaderBackgroundTextures
{
    public static readonly IReadOnlyList<ReaderBackgroundTexture> All = [
        new("neutral-dark", "Neutral dark", new("avares://Paperbunkr.App/Assets/Textures/neutral-dark.png")),
        new("carbon",       "Carbon",       new("avares://Paperbunkr.App/Assets/Textures/carbon.png")),
        new("linen",        "Linen",        new("avares://Paperbunkr.App/Assets/Textures/linen.png")),
    ];

    public static ReaderBackgroundTexture Resolve(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All[0];

    // Decoded once, cached. Bitmap is never mutated -> safe to share across threads/brushes.
    public static Bitmap LoadBitmap(string id);   // AssetLoader.Open(...) + cache in a static dict
}
```

### 1.4 Rendering

`ReaderScreenViewModel.ComputeCanvasBackgroundBrush` gains a `Texture` branch:

```csharp
private static IBrush ComputeCanvasBackgroundBrush(ImageBackgroundMode mode, string? colorName, string? textureId)
{
    switch (mode)
    {
        case ImageBackgroundMode.Texture:
            var bmp = ReaderBackgroundTextures.LoadBitmap(textureId);
            var px = bmp.PixelSize;
            return new ImmutableImageBrush(
                bmp,
                sourceRect: new RelativeRect(0, 0, px.Width, px.Height, RelativeUnit.Absolute),
                destinationRect: new RelativeRect(0, 0, px.Width, px.Height, RelativeUnit.Absolute),
                stretch: Stretch.None,
                tileMode: TileMode.Tile);
        case ImageBackgroundMode.Color when !string.IsNullOrWhiteSpace(colorName)
            && Color.TryParse(colorName, out var c):
            return new ImmutableSolidColorBrush(c);
        default:
            return DefaultCanvasBackgroundBrush;
    }
}
```

- **`ImmutableImageBrush`** (Avalonia 12) — no `AvaloniaObject` base, no dispatcher-thread
  affinity, genuinely immutable. Same reasoning the code already applies for
  `ImmutableSolidColorBrush` (see the `DefaultCanvasBackgroundBrush` doc comment about the
  cross-thread `.Color` crash under the parallel test runner). The exact constructor argument
  list (source / sourceRect / destinationRect / stretch / tileMode ordering, and whether
  `RelativeRect` absolute-unit rects or a plain `TileMode` + `Stretch.None` is enough) is pinned
  in the implementation plan against the installed Avalonia 12.1.1 — the snippet above is
  illustrative.
- `Grid.Background="{Binding CanvasBackgroundBrush}"` in `ReaderScreen.axaml` — **unchanged**
  (`IBrush`).
- **Static tiling** — `TileMode.Tile` with an absolute source/dest rect at the bitmap's native
  pixel size means the tile does not scale with page zoom (CE's does; this is the named deviation).
- Works in every reading mode automatically — the same `Grid` wraps `PageCanvas` in paged,
  continuous and webtoon.
- `RefreshDisplaySettings` (already wired to `PreferencesScreenViewModel.ReaderDisplaySettingsChanged`)
  passes the new `appSettings.BackgroundTexture` through — so changing the texture from Preferences
  live-updates an open book, same as the colour row already does.

### 1.5 Page shadow

A soft drop-shadow around the page, **only when the effective background mode is `Texture`**
(solid-`Color` and `Auto` are visually unchanged from today).

- New `ReaderScreenViewModel.ShowPageShadow` (`bool`, computed in `RefreshDisplaySettings` from
  `mode == ImageBackgroundMode.Texture`).
- Implemented in **`ReaderScreen.axaml`**, not `PageCanvas`: wrap the page `Image` (paged path
  only) in a `Border` with a `BoxShadow` (`"0 8 24 0 #8C000000"` — soft, offset down, no spread)
  and `IsVisible`/opacity driven by `ShowPageShadow`. No `PageCanvas` render-op changes.
- Paged modes only — continuous / webtoon pages abut and a per-page shadow there would be noise;
  `ShowPageShadow` is additionally gated on `!IsContinuousMode`.
- Static — reduced-motion irrelevant.
- **Open point:** `PageCanvas` currently draws the page via a custom draw operation; the page
  `Image` in `ReaderScreen.axaml` may not be a separate element the shadow `Border` can wrap. If
  the page is drawn entirely inside `PageCanvas`, the shadow moves into `PageCanvas` as a
  `BoxShadow` on the destination rect it already computes — still no new render pass, just an
  extra `context.DrawRectangle` with a shadow brush behind the page rect. The plan settles which.

### 1.6 Preferences

`reader.background` group in `Views/Preferences/ReaderSection.axaml`:

- The existing "Canvas background" `SuggestBox` gains **"Texture"** — add it to
  `PreferencesScreenViewModel.BackgroundModeNames` (and the `ImageBackgroundModeText` ⇄ enum
  mapping the SuggestBox migration introduced).
- New conditional `pref:SettingsRow` "Background texture" — `IsVisible` when
  `ImageBackgroundMode == Texture` (same `ObjectConverters.Equal` pattern the "Background color"
  row already uses). `SettingsContent` = a horizontal `StackPanel Spacing="8"` of **3 clickable
  swatch `Border`s** (~52×38, `CornerRadius="6"`, `Background` = the texture's `ImmutableImageBrush`,
  `Classes.active` bound to the per-swatch selected bool, `Cursor="Hand"`,
  `AutomationProperties.Name` = the display name). A `swatchSelected` style (accent outline) in
  `Primitives.axaml` or inline.
- VM: `[ObservableProperty] string? _backgroundTexture;` + `IsTexture{NeutralDark,Carbon,Linen}`
  computed bools + `[RelayCommand] SetBackgroundTexture(string id)` → sets the field, persists via
  `PersistBehaviorSetting(s => s.BackgroundTexture = id)`, raises `ReaderDisplaySettingsChanged`.
  Hydrated in the `_suppressBehaviorApply` load block.

---

## Item 2 — Per-page spread position

### 2.1 Data

- New enum `Paperbunkr.Data.Entities.PageSpreadPosition { Default, Near, Far }` (mirrors CE's
  `ComicPagePosition`).
- New column `IssuePage.SpreadPosition` — `PageSpreadPosition`, stored as string
  (`.HasConversion<string>().HasMaxLength(16)`), default `Default`. `IssuePage` is already the
  sparse per-page override row (holds `PageType` + `RotationDegrees`); this is a third field. A
  page with no `IssuePage` row is implicitly `Default`.
- `ReaderScreenViewModel._pageOverrides` (`Dictionary<int, IssuePage>`) already loads these rows
  in `Load` — no new query.

### 2.2 Mechanism — step-size only

Mirrors CE exactly: the flag changes the **navigation step (1 vs 2)**, not the pairing test.
`TryDecodePairedPage` / `SpreadLayoutMath.IsPairEligible` / `DoublePagePairingActive` are all
**unchanged**.

```csharp
private PageSpreadPosition SpreadPositionAt(int index) =>
    _pageOverrides.TryGetValue(index, out var row) ? row.SpreadPosition : PageSpreadPosition.Default;
```

- **`NextPage`** (current: `int step = CurrentPageSecondary is not null ? 2 : 1;`)
  → after that line: `if (step == 2 && SpreadPositionAt(_currentPageIndex + 1) == PageSpreadPosition.Near) step = 1;`
  (CE `DisplayNextPage`: `SeekNewPage(1)` is current+1; `Near` there → advance 1.)
- **`PreviousPage`** (current: `int step = ArePagesPaired(_currentPageIndex - 2) ? 2 : 1;`)
  → force `step = 1` when
  `SpreadPositionAt(_currentPageIndex - 1) == Near && SpreadPositionAt(_currentPageIndex - 2) != Far`.
  (CE `DisplayPreviousPage`: the `-1`/`-2` seek + the `page2.PagePosition == Near && page3.PagePosition != Far` clause.)
- Page 0 stays structurally solo regardless (the existing `pageIndex == 0` guard in
  `TryDecodePairedPage`).
- Only meaningful in `PageLayoutMode.Double` + a paged LTR/RTL mode — which is exactly when
  `CurrentPageSecondary`/`ArePagesPaired` can be non-null anyway, so the guard is naturally
  inert otherwise.

### 2.3 Editing UI

`ViewModels/ReaderPageContextMenuBuilder.cs` — a third submenu after "Page Type" / "Rotate":

```csharp
// only when NOT continuous — pairing (and therefore this) is meaningless in continuous/webtoon
_vm.IsContinuousMode ? null : ContextMenuEntry.SubMenu("Spread position", new[]
{
    ContextMenuEntry.Item("Automatic",            _vm.SetSpreadPositionDefaultCommand, thumbnail),
    ContextMenuEntry.Item("Near side (leading)",  _vm.SetSpreadPositionNearCommand,    thumbnail),
    ContextMenuEntry.Item("Far side (trailing)",  _vm.SetSpreadPositionFarCommand,     thumbnail),
})
```

`ReaderScreenViewModel.SetPageOverride` gains an optional `PageSpreadPosition?` param
(same sparse-upsert path as `PageType?`/`int? rotation` today); three `[RelayCommand]`
wrappers (`SetSpreadPositionDefault/Near/Far`) mirror the existing `SetPageType*` /
`SetPageRotation*` wrappers. After a change, `SetPageOverride` re-runs `RefreshCurrentPage`
(so a re-phased pairing takes effect immediately) — same as it already does for rotation.

### 2.4 Import

`CeLibraryMigrator` and the embedded-`ComicInfo.xml` scan path (`LibraryFolderScanner` /
`CeLibraryMigrator.MapStoryFields` neighbourhood) read `<Page … PagePosition="Near" />` from the
`<Pages>` list into `IssuePage.SpreadPosition` when creating/refreshing per-page rows —
**read-only**. Write-back stays deferred (the write-back service "v1 does not write per-page
type/rotation" — same boundary). A page with `PagePosition="Default"` or absent → no row / a row
with `Default`.

### 2.5 Thumbnail-rail indicator

`ReaderThumbnailSample` gains `SpreadHint` (`PageSpreadPosition`, `Default` = no indicator). In
`ReaderScreen.axaml`'s thumbnail `DataTemplate`, a small `fi:SymbolIcon` (`PbIconSizeXs`, bottom,
near the existing rotation icon / page-type badge): `ChevronLeft` for `Near`, `ChevronRight` for
`Far`, hidden for `Default`. `Load` and `SetPageOverride`'s thumbnail-refresh path populate it
alongside `IsRotated` / `PageType`.

---

## Migration

One migration `AddReaderBackgroundTextureAndSpreadPosition`:

- `AppSettings.BackgroundTexture` — `TEXT NULL`.
- `IssuePage.SpreadPosition` — `TEXT NOT NULL DEFAULT 'Default'`.
- **`Down()` is a no-op** with an explanatory comment, per the standing rule for new
  `AppSettings` / `IssuePage` columns (`docs/alpha-todo.md` migration-rollback-chain note —
  a `DropColumn` on SQLite forces a full-table rebuild that has broken later `Down()` steps
  before).
- Up/down replay test per the established pattern, asserting the no-op `Down()`.

---

## Testing

**Pure / VM (headless):**

- `ReaderScreenViewModelTests`:
  - `NextPage`: with `SpreadPositionAt(current+1) == Near` and a would-be 2-step, steps 1.
  - `PreviousPage`: `-1 == Near && -2 != Far` → steps 1; `-2 == Far` → still steps 2.
  - Neither flag set → step sizes exactly as today (regression guard — the existing
    `NextPage_StepsByTwo_WhenCurrentlyPaired` etc. must stay green).
  - `SetPageOverride` with a `PageSpreadPosition` persists an `IssuePage` row; `SpreadPositionAt`
    reads it; setting back to `Default` on a row that also has a type/rotation keeps the row,
    only clears the spread field.
  - `ComputeCanvasBackgroundBrush(Texture, _, "carbon")` → `ImmutableImageBrush` with
    `TileMode.Tile`; unknown id → `neutral-dark`'s brush; `Texture` mode + missing asset →
    falls back to `DefaultCanvasBackgroundBrush` without throwing.
  - `RefreshDisplaySettings` sets `ShowPageShadow` true only for `Texture` + `!IsContinuousMode`.
- `PreferencesScreenViewModelTests`:
  - `SetBackgroundTextureCommand("linen")` persists `"linen"`, flips `IsTextureLinen`.
  - A stored `BackgroundTexture` hydrates on load; mode "Texture" hydrates via the SuggestBox text.
- `ReaderPageContextMenuBuilderTests` (new if absent): "Spread position" submenu present in paged
  mode, **absent** when `IsContinuousMode`.
- `ReaderBackgroundTextures`: `Resolve(null)`/`Resolve("bogus")` → `neutral-dark`;
  `LoadBitmap` caches (same instance on the second call).
- Migration replay test (up → down no-op → up).
- CE-XML import: a `<Pages>` fixture with `PagePosition="Near"` on page 3 → after scan,
  `IssuePage.SpreadPosition == Near` for that page.

**On-screen (user — no computer-use):**

- Each texture tiles cleanly (no visible seam), persists across restart, live-applies when
  changed from Preferences with a book open; the swatch row only shows for `Texture` mode.
- The page drop-shadow appears only with a texture background, not with solid colour.
- On a real book with an odd landscape run, marking a page "Near side" actually shifts the
  spread pairing from that page onward; the thumbnail glyph shows on flagged pages; the submenu
  is gone in continuous mode.

---

## Files

| File | Change |
|---|---|
| `src/Paperbunkr.Data/Entities/ImageBackgroundMode.cs` | add `Texture` |
| `src/Paperbunkr.Data/Entities/AppSettings.cs` | `BackgroundTexture` (`string?`) |
| `src/Paperbunkr.Data/Entities/PageSpreadPosition.cs` | **new** enum |
| `src/Paperbunkr.Data/Entities/IssuePage.cs` | `SpreadPosition` |
| `src/Paperbunkr.Data/PaperbunkrDbContext.cs` | `IssuePage.SpreadPosition` `HasConversion<string>` |
| `src/Paperbunkr.Data/Migrations/*_AddReaderBackgroundTextureAndSpreadPosition.cs` | **new**, no-op `Down()` |
| `src/Paperbunkr.App/Services/Reader/ReaderBackgroundTextures.cs` | **new** catalog + cache |
| `src/Paperbunkr.App/Assets/Textures/{neutral-dark,carbon,linen}.png` | **new** assets |
| `tools/gen-reader-textures/` | **new** one-shot generator (script + README) |
| `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` | brush `Texture` branch; `ShowPageShadow`; `SpreadPositionAt`; `NextPage`/`PreviousPage` step guards; `SetPageOverride` + 3 spread commands; thumbnail `SpreadHint` |
| `src/Paperbunkr.App/ViewModels/ReaderPageContextMenuBuilder.cs` | "Spread position" submenu (hidden in continuous) |
| `src/Paperbunkr.App/Models/ReaderThumbnailSample.cs` | `SpreadHint` |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml` | page-shadow `Border`/box-shadow; thumbnail glyph |
| `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` | `BackgroundTexture`, swatch bools, `SetBackgroundTextureCommand`, `BackgroundModeNames` gains "Texture", hydration |
| `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` | swatch row (conditional on `Texture`) |
| `src/Paperbunkr.App/Styles/Primitives.axaml` | `swatchSelected` style (if not inline) |
| `src/Paperbunkr.App/Services/CeLibraryMigrator.cs` / scan path | read `<Page PagePosition>` → `IssuePage.SpreadPosition` |
| `src/Paperbunkr.App.Tests/*` | per the Testing section |
| `docs/ce-feature-inventory.md` | rows 136 + 138 + §239 gap list — texture (v1 scope) + spread position shipped |
| `docs/Paperbunkr-Roadmap.md`, `docs/alpha-todo.md` | session notes |

## Risks / open points

- **Page-shadow placement** (§1.5) — whether the page `Image` is a wrappable element in
  `ReaderScreen.axaml` or drawn entirely inside `PageCanvas`. The plan resolves it; both routes
  are small and neither adds a render pass.
- **`ImmutableImageBrush` tile alignment** — a `TileMode.Tile` brush on a `Grid.Background` tiles
  from the control's origin; verify the phase looks right (no half-tile at the top-left) and that
  `Stretch.None` + absolute rects give 1:1 pixels. Cheap to check on screen.
- **`SuggestBox` "Texture" option** — the SuggestBox migration (`46e18fe`) made these text-bound;
  confirm the `ImageBackgroundModeText` ⇄ enum round-trip handles a third value cleanly and that
  `IsStrict="True"` accepts it.
- **Texture-asset authoring** — "seamless at 1×" is a real bar; the generator script may need a
  couple of iterations. Not blocking design.
- **Branch/worktree** — `claude/reader-backlog-batch-a` must be rebased onto current master
  (post-PR-#74: the duplicate dropdown-fix `a3c0455`, the `46e18fe` SuggestBox conversion which
  overlaps Batch A's `ReaderSection.axaml` "Performance" group) before Batch B branches off it.
  All git operations held until the shared worktree (currently `claude/hotfix-r2r-launch-crash`,
  uncommitted) is free.
- **On-screen verification** is the user's — automated tests cover the VM/data logic only.
