# Reader Chrome — Icon Pass

**Status:** Implemented 2026-08-27. Build clean (0 errors; forced `CoreCompile` + verified the
Avalonia XAML weave ran by launching the exe crash-free), full `Paperbunkr.App.Tests` suite green
(878/878, including 9 new `ReadingModeIconConverterTests`). On-screen verification of icon
legibility at 12–14px in both skins and the reading-direction icon flip is still pending — the
standing GUI-automation caveat from every prior reader spec.
**Extension of Sub-project 6 of 7** in the full UI rework (see
[Design Language Foundation](2026-08-24-design-language-foundation-design.md) for the full phase
breakdown). The [Reader Chrome](2026-08-25-reader-chrome-design.md) phase deliberately kept
iconography minimal — it converted only `Skip_Back.png`/`Skip_Forward.png` (the two page-turn
buttons) to vector and left every other control on a text glyph or emoji. This follow-up completes
the reader chrome's icon language so it matches the rest of the rework.

## Background

After the Reader Chrome phase landed, `ReaderScreen.axaml`'s four floating clusters
(Navigate/View/Page-turn/Actions), the right drawer, and the thumbnail rail still use a mix of:

- **Emoji** — `🔖` (bookmark toggle), `⋮` (drawer/overflow toggle), `⛶` (fullscreen toggle). These
  render full-colour and platform-dependent, clashing with the app's stroked-outline icon set.
- **Typographic / math glyphs** — `«` `»` (prev/next chapter), `−` `+` (zoom out/in), `✕` (drawer
  close, delete bookmark), `⟳` `⟲` (rotate CW/CCW), `✎` (rename bookmark), `✓` (commit rename),
  `◀ Prev` / `Next ▶` (prev/next bookmark), `↻` (thumbnail rotation indicator).
- **Text-only picker pills** — reading-mode (`Left to Right`…), fit-mode (`Best Fit`…), zoom-preset
  (`150%`), transition-style (`Crossfade`) — a value label that opens a `Flyout`.
- **Text-only toggle rows** in the drawer — `Auto-rotate landscape pages`, `Double-page spread`,
  `Auto-scroll` (each a `Button.drawerRow` with a `.active` accent state).

Only `PbIconSkipBack`/`PbIconSkipForward` (page-turn) and the pre-existing `PbIconBookmark` (nav
rail) are vector today.

The [Design Language Foundation](2026-08-24-design-language-foundation-design.md) Iconography
section sets the rule: an icon's raster PNG converts to vector *when its consuming screen is
touched in its own phase*, not all at once. The reader screen is being reworked now, so converting
its icons — and adding vector icons where only a glyph existed — is in scope for this phase.

## Scope

**In scope:** every button in `ReaderScreen.axaml`'s four clusters, the drawer, and the thumbnail
rail gets a `Path Classes="pbIcon"` vector icon in place of its text glyph / emoji. Picker pills
keep their live value text with a **leading** icon added. One new `IValueConverter` for a dynamic
reading-direction icon. New `PbIcon*` geometries in `Styles/Icons.axaml`. `icon-mapping.md`
updated.

**Out of scope — explicitly unchanged:**

- Any command, tooltip, `Classes.active` binding, flyout content, or layout structure. Buttons keep
  their positions, sizes, and behaviour. This is a content swap inside existing controls plus the
  supporting style selectors and one converter — nothing else.
- The value text *inside* picker pills (`{Binding ReadingModeLabel}` etc.) stays.
- Page-type letter badge on thumbnails (`C`/`A`/…) — it's a semantic letter, not an icon.
- Bookmark ribbon `Polygon` on thumbnails — already a vector shape.
- Drawer section headers, sliders, and the `Reset to defaults` text button (a rare terminal action,
  clearly labelled, not a toggle — a leading icon would add noise, not clarity).
- No new reader *functionality*. Fit-mode gets a **static** icon, not a per-mode dynamic one (see
  §3) — the only dynamic icon is reading-direction, which is a pure view-layer converter over an
  already-`[ObservableProperty]` value.

## 1. New geometries — `Styles/Icons.axaml`

Built the same way as every existing icon in this file: hand-computed stroked outlines in the
Lucide/Feather idiom on a 24×24 viewBox, `Fill="Transparent"`, consumed through the shared
`Path.pbIcon` style (`Stroke` = recolour hook, `StrokeThickness` 1.5, round caps/joins). **Not**
pixel-traced from any PNG.

| Key | Action | Raster precedent (stays on disk, still used by unconverted screens) |
|---|---|---|
| `PbIconMoreVertical` | Overflow / open drawer | — |
| `PbIconFullscreen` | Toggle fullscreen | — |
| `PbIconChevronsLeft` | Previous chapter | — |
| `PbIconChevronsRight` | Next chapter | — |
| `PbIconChevronLeft` | Previous bookmark | — |
| `PbIconChevronRight` | Next bookmark | — |
| `PbIconMinus` | Zoom out | — |
| `PbIconPlus` | Zoom in | — |
| `PbIconSearch` | Zoom level (magnifier, leads the zoom-preset pill) | `Search_Magnifying_Glass` |
| `PbIconClose` | Close drawer / remove bookmark row | — |
| `PbIconRotateCw` | Rotate clockwise; auto-rotate row; thumbnail rotation indicator | — |
| `PbIconRotateCcw` | Rotate counter-clockwise | — |
| `PbIconEdit` | Rename bookmark | `Edit_Pencil` |
| `PbIconCheck` | Commit bookmark rename (plain check) | `Check` |
| `PbIconTrash` | Delete bookmark | `Trash_Empty` |
| `PbIconBookOpen` | Double-page spread row | `Book_Open` |
| `PbIconPlay` | Auto-scroll row | `Play` |
| `PbIconFit` | Fit mode (static, leads the fit-mode pill) | — |
| `PbIconArrowRight` | Reading direction: left-to-right | — |
| `PbIconArrowLeft` | Reading direction: right-to-left | — |
| `PbIconArrowDown` | Reading direction: vertical / webtoon | — |

**Distinct from existing keys, deliberately:** `PbIconClose` is a plain `✕`; the existing
`PbIconCloseCircle` is the circled dismiss glyph used by the FloatingPanel overlays — different
action, different weight, both kept. `PbIconCheck` is a plain check; the existing `PbIconCircleCheck`
is the circled "success/confirmed" state glyph — kept separate for the same reason.

**Reuses, no new geometry:** `PbIconBookmark` (bookmark toggle — the nav rail's Reading Lists icon
already means "bookmark" everywhere it appears), `PbIconSkipBack` / `PbIconSkipForward` (page-turn,
already in this file).

Each new geometry gets a source comment naming its action and raster precedent, matching the
existing entries.

## 2. Icon sizing and the picker pills

Cluster icon-buttons (`Button.clusterIcon`) and drawer rows keep the shared `Path.pbIcon` default
of 14×14 (the page-turn buttons already use `Width="13" Height="13"` — that stays). The thumbnail
rotation indicator renders at ~12px, matching the glyph it replaces.

Picker pills (`Button.clusterPill`) currently take a plain string `Content`. They become a
horizontal `StackPanel` — leading `Path Classes="pbIcon"` (12px, `Stroke` = `PbTextMutedBrush`) +
the existing value `TextBlock` — with `Spacing="6"`. The `.active` pill selector already recolours
`Foreground`; a matching `Button.clusterPill.active Path.pbIcon { Stroke: PbAccentTextBrush }` keeps
the leading icon in step.

Affected pills and their leading icon:

| Pill | Leading icon |
|---|---|
| Reading-mode | dynamic — `ReadingModeIconConverter` (§3) |
| Fit-mode (both the View-cluster instance and the drawer's collapsed-VIEW instance) | static `PbIconFit` |
| Zoom-preset (`150%`) | static `PbIconSearch` |

The transition-style control is **not** in this list: it's a `Button.drawerRow`, not a
`clusterPill`, and sits directly under a `TRANSITION` drawer section header that already names it.
It stays text-only — pulling it in would need a 21st geometry for one control the header explains.
The leading-icon rule covers exactly reading-mode, fit-mode, and zoom-preset.

## 3. Dynamic reading-direction icon

New file `src/Paperbunkr.App/Views/ReadingModeIconConverter.cs` — `IValueConverter` with a static
`Instance` field, mirroring `CoverImageConverter` in the same directory (static `Instance`,
`ConvertBack` throws `NotSupportedException`).

`Convert` maps a `ReadingMode` value to a `StreamGeometry` resolved from
`Application.Current!.Resources`:

| `ReadingMode` | Geometry key |
|---|---|
| `LeftToRight`, `HorizontalContinuous` | `PbIconArrowRight` |
| `RightToLeft`, `HorizontalContinuousRightToLeft` | `PbIconArrowLeft` |
| `VerticalContinuous`, `Webtoon` | `PbIconArrowDown` |
| anything else / not a `ReadingMode` | `PbIconArrowRight` (fallback) |

Usage in the reading-mode pill:

```xml
<Path Classes="pbIcon"
      Data="{Binding EffectiveReadingMode,
                     Converter={x:Static views:ReadingModeIconConverter.Instance}}" />
```

`EffectiveReadingMode` is `[ObservableProperty]` (`ReaderScreenViewModel` line 182), so the icon
updates reactively when the mode changes via the flyout or a keyboard shortcut. No ViewModel
change.

## 4. Toggle / active-state treatment

Follows the nav rail's established pattern (`MainWindow.axaml` — `Button.rail.active Path.pbIcon
{ Stroke: PbAccentTextBrush }`): the stroke recolours on `.active`, the geometry never swaps.

New style selectors in `ReaderScreen.axaml`'s `<UserControl.Styles>`:

```
Button.clusterIcon Path.pbIcon         { Stroke: PbTextMutedBrush }   /* base, matches the button's own Foreground */
Button.clusterIcon.active Path.pbIcon  { Stroke: PbAccentTextBrush }
Button.clusterPill.active Path.pbIcon  { Stroke: PbAccentTextBrush }
Button.drawerRow Path.pbIcon           { Stroke: PbTextMutedBrush }
Button.drawerRow.active Path.pbIcon    { Stroke: PbAccentTextBrush }
```

Buttons that already carry a `Classes.active` binding keep it and now show the accent stroke:

| Button | Existing `.active` binding |
|---|---|
| Bookmark toggle (Actions cluster) | `IsCurrentPageBookmarked` |
| Drawer toggle (Actions cluster) | `IsDrawerOpen` |
| Auto-rotate row (drawer PAGE) | `AutoRotate` |
| Double-page row (drawer PAGE) | `IsDoublePageMode` |
| Auto-scroll row (drawer PAGE) | `IsAutoScrolling` |

One addition: the fullscreen button gains `Classes.active="{Binding IsFullscreen}"` — the state
already exists on the ViewModel (`ReaderScreenViewModel` line 1227) and is currently not reflected
in the chrome. No new property.

`Button.drawerRow` currently takes a plain string `Content`. Each affected row becomes a horizontal
`StackPanel` (`Spacing="8"`): leading `Path Classes="pbIcon"` + the existing `TextBlock`/label,
`HorizontalContentAlignment="Left"` unchanged. Bookmark-list rows (rename/delete/commit) are
`clusterIcon` buttons, not `drawerRow`s — they just swap `Content` for a `Path`.

## 5. Full button → icon map

**Navigate cluster (top-left):** back button stays a text breadcrumb (`← {series}` / title) — it's
a labelled link, not an icon target; unchanged.

**Actions cluster (top-right):**

| Button | Was | Icon |
|---|---|---|
| Bookmark toggle | `🔖` | `PbIconBookmark` (reuse), `.active` = `IsCurrentPageBookmarked` |
| Overflow / drawer | `⋮` | `PbIconMoreVertical`, `.active` = `IsDrawerOpen` |
| Fullscreen | `⛶` | `PbIconFullscreen`, new `.active` = `IsFullscreen` |

**Page-turn cluster (bottom-centre):**

| Button | Was | Icon |
|---|---|---|
| Previous chapter | `«` | `PbIconChevronsLeft` |
| Previous page | `PbIconSkipBack` | unchanged |
| Next page | `PbIconSkipForward` | unchanged |
| Next chapter | `»` | `PbIconChevronsRight` |

Per-page dot strip between them is unchanged.

**View cluster (bottom-left) + its collapsed copy in the drawer's VIEW section:**

| Button | Was | Icon |
|---|---|---|
| Reading-mode pill | text only | leading dynamic icon (§3) + value text |
| Fit-mode pill | text only | leading `PbIconFit` + value text |
| Zoom out | `−` | `PbIconMinus` |
| Zoom-preset pill | text only | leading `PbIconSearch` + value text |
| Zoom in | `+` | `PbIconPlus` |

**Drawer:**

| Button | Was | Icon |
|---|---|---|
| Close (header) | `✕` | `PbIconClose` |
| Rotate CW (PAGE) | `⟳` | `PbIconRotateCw` |
| Rotate CCW (PAGE) | `⟲` | `PbIconRotateCcw` |
| Auto-rotate row | text | leading `PbIconRotateCw` + label, `.active` = `AutoRotate` |
| Double-page row | text | leading `PbIconBookOpen` + label, `.active` = `IsDoublePageMode` |
| Auto-scroll row | text | leading `PbIconPlay` + label, `.active` = `IsAutoScrolling` |
| Transition-style pill | text | unchanged (see §2 correction) |
| Bookmark row — rename | `✎` | `PbIconEdit` |
| Bookmark row — delete | `✕` | `PbIconTrash` |
| Bookmark row — commit rename | `✓` | `PbIconCheck` |
| Prev bookmark | `◀ Prev` | leading `PbIconChevronLeft` + `Prev` |
| Next bookmark | `Next ▶` | `Next` + trailing `PbIconChevronRight` |
| Reset to defaults (ADJUST) | text | unchanged |

**Thumbnail rail:** the `↻` rotation indicator `TextBlock` becomes a `Path Classes="pbIcon"
Data="{StaticResource PbIconRotateCw}"` at ~12px, `Stroke` = `White` (it overlays cover art, same
as the glyph did). Bookmark ribbon and page-type badge unchanged.

## 6. `icon-mapping.md`

The "Converted to vector — Phase 6 (Reader chrome)" table grows from 2 rows to the full set above,
one action per icon. Six entries move out of the "Still raster" inventory list —
`Search_Magnifying_Glass`, `Edit_Pencil`, `Check`, `Trash_Empty`, `Book_Open`, `Play` — each noted
as "still raster elsewhere until that screen's own phase." The three reading-direction glyphs
(`PbIconArrowRight`/`Left`/`Down`) are recorded as one action, "reader reading-direction indicator,"
with a note that the mapping is driven by `ReadingModeIconConverter`. `PbIconMoreVertical`,
`PbIconFullscreen`, the four chevrons, `PbIconMinus`/`Plus`, `PbIconClose`, `PbIconRotateCw`/`Ccw`,
and `PbIconFit` are recorded as new concepts with no raster precedent (same as `PbIconPin` in
Phase 2).

## 7. Testing

- **New** `src/Paperbunkr.App.Tests/ReadingModeIconConverterTests.cs` — for every `ReadingMode`
  enum value, `Convert` returns a non-null `Geometry`; the three expected groups map to the three
  expected geometry instances (compare by reference against the resolved app resource); a non-
  `ReadingMode` input returns the `PbIconArrowRight` fallback; `ConvertBack` throws
  `NotSupportedException`. Matches the repo's existing converter-test convention.
- **Build** with the `CLAUDE.md` AVLN2000 guard — this change touches `.axaml` **and** adds a new
  `.cs`, so delete `obj/Debug/net8.0/Paperbunkr.App.dll` + `.pdb` before the build if the compile
  skips `CoreCompile`, and verify the XAML weave actually ran (launch the exe, not just "0 Errors").
- **Full** `dotnet test` green across the solution.
- **Manual on-screen check** (flagged, not assumed — same standing GUI-automation caveat as every
  prior reader spec):
  - every cluster / drawer / rail button shows its icon, legible at 12–14px, in both the `default`
    (dark amber) and `windows_11` (light) skins;
  - bookmark, drawer, fullscreen, auto-rotate, double-page, auto-scroll show the accent stroke when
    active and revert when not;
  - the reading-mode pill's leading icon flips between →, ←, ↓ as the mode changes via its flyout
    and via a keyboard shortcut;
  - no emoji remain anywhere in the reader chrome (grep `ReaderScreen.axaml` for the specific
    codepoints as a backstop).

## Risks / notes

- **1-D geometry collapses under `Stretch="Uniform"`** (found on-screen after first implementation):
  `PbIconMinus` (`M 5,12 L 19,12`) and `PbIconMoreVertical` (three dots on one x) each had a
  zero-area bounding box, which `Path.pbIcon`'s `Stretch="Uniform"` can't scale - both rendered as
  a single dot. Fixed by drawing minus as two hairline-spaced strokes and more-vertical as three
  small rings, so every geometry has real 2-D extent. Any future 1-D-looking icon (a plain divider,
  a single bar) needs the same treatment.

- **Converter reaching into `Application.Current.Resources`** is a documented Avalonia pattern, but
  if a resolve returns null (resource not yet loaded during very early startup) the pill would show
  no icon rather than crash — `Convert` returns null safely and the `Path` renders empty. Acceptable;
  the reader screen is never the first thing shown.
- **`PbIconRotateCw` used in three places** (rotate-CW button, auto-rotate row, thumbnail
  indicator) is a deliberate one-icon-per-*action* call — all three are "clockwise rotation." The
  `icon-mapping.md` entry notes the three consumers so a future audit doesn't read it as drift.
- No `Converters/` directory exists; `ReadingModeIconConverter.cs` goes in `Views/` next to
  `CoverImageConverter.cs`, matching where the project already keeps its one converter.
