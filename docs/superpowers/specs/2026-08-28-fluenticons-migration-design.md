# App-wide FluentIcons Migration

**Date:** 2026-08-28
**Status:** Design approved, implementing.
**Branch 2** of the Home-screen work (branch 1 = `2026-08-28-home-screen-redesign-design.md`). The
user asked for the whole app's iconography moved to `FluentIcons.Avalonia` ("implement it to all"),
sequenced after the Home redesign.

## Background

Icons today are a mix of two hand-rolled systems:

- **Vector:** `Styles/Icons.axaml` holds 49 hand-computed `StreamGeometry` resources named `PbIcon*`,
  consumed as `<Path Classes="pbIcon" Data="{StaticResource PbIconX}"/>`. `Path.pbIcon` styles set
  `Stroke`/`Fill` from `DynamicResource` tokens, with context-specific overrides scattered across
  ~24 views (e.g. `Button.toolbarPill.open Path.pbIcon { Stroke: accent }`).
- **Raster:** 39 grayscale+alpha PNGs in `Assets/Icons/`, consumed via `<Border Classes="icon">` +
  `ImageBrush` + `OpacityMask` so they recolour with the theme.

`FluentIcons.Avalonia` 2.1.337 + `FluentIcons.Avalonia.Fluent` are already referenced (added in the
.NET 10 / FluentAvalonia migration, 2026-08-27) with zero call sites. `FluentIcons.Avalonia.SymbolIcon`
exposes a `Symbol` enum of 2831 icons, an `IconVariant` (Regular / Filled), sizes by `FontSize`, and
inherits `Foreground` from its parent like a `TextBlock`.

## Decisions

1. **Direct usage.** `<fi:SymbolIcon Symbol="Search"/>` at each call site (xmlns
   `fi="using:FluentIcons.Avalonia"`). No wrapper control, no static-constant indirection.
   `Assets/Icons/icon-mapping.md` is rewritten as the canonical `action → Symbol` table and kept as
   review discipline (the same status the `PbIcon*` naming convention had).
2. **Shared default style.** A new `Styles/Icons.axaml` (same path, replacing the geometry
   dictionary) with one rule: `<Style Selector="fi|SymbolIcon">` → `FontSize="16"`,
   `IconVariant="Regular"`, `Foreground="{DynamicResource PbTextBrush}"`. Call sites override
   `FontSize` only where needed (nav rail larger, inline-in-button smaller). `IconVariant="Regular"`
   keeps the thin-outline direction the rework's design-language doc set.
3. **Colour by inheritance.** Because `SymbolIcon` inherits `Foreground`, the per-context stroke
   selectors collapse into `Foreground` on the parent control. E.g.
   `Button.toolbarPill.open Path.pbIcon { Stroke: PbAccent }` becomes
   `Button.toolbarPill.open { Foreground: PbAccent }` and the icon follows.
4. **Delete the old machinery in this pass** (user's call, accepting the merge risk that existed
   while a concurrent session held ~10 of the 24 files — now resolved, that work is committed at
   `d0b44fc`):
   - `Styles/Icons.axaml`'s 49 `PbIcon*` geometries (file is reused for the new default style).
   - The 39 PNGs under `src/Paperbunkr.App/Assets/Icons/`.
   - Every `Path.pbIcon` / `Border.icon` / `.icon`-`OpacityMask` style block, wherever defined
     (`App.axaml`, per-view `UserControl.Styles`, `Styles/*.axaml`).
5. **`ReadingModeIconConverter`** returns a `FluentIcons.Common.Symbol`
   (`ArrowLeft` / `ArrowDown` / `ArrowRight`) instead of a `Geometry`. The whole
   `Application.Current` resource-lookup + cross-thread guard goes away; `SymbolFor(ReadingMode)`
   stays a pure, testable switch. Bound as
   `<fi:SymbolIcon Symbol="{Binding …, Converter={x:Static views:ReadingModeIconConverter.Instance}}"/>`.
6. If `SymbolIcon`'s control theme isn't auto-registered by the package, add the required
   `<StyleInclude>` to `App.axaml` (checked at build time).

## Scope

All ~24 `.axaml` files that reference `pbIcon` / `PbIcon` / `Classes="icon"` / `OpacityMask`:
Book/Manga/DetailScreen, DetailTabs, BooksScreen, LibraryScreen, LibraryToolbar, MainWindow,
HomeScreen, ReaderScreen, ReadingScreen, SmartScreen, EventsScreen, PluginScreen, MigrationOverlay,
QuickRateOverlay, IssuePropertiesScreen, BulkIssuePropertiesScreen, BulkSeriesPropertiesScreen,
PreferencesScreen + `Preferences/{Advanced,Appearance,KeyboardShortcuts,Library}Section.axaml`.
Plus `ReadingModeIconConverter.cs` + its test, `App.axaml`, `Assets/Icons/icon-mapping.md`.

Out of scope: any behaviour change, any new icon *concepts*, the `SplitText` / Home work from
branch 1 (only `HomeScreen.axaml`'s single Refresh icon is touched here, incidentally).

## Testing

- `dotnet build` clean, no new warnings.
- Full `dotnet test` green. Only `ReadingModeIconConverterTests` changes (asserts a `Symbol` now).
- Grep audit: zero remaining `pbIcon`, `PbIcon`, `Assets/Icons/.*\.png`, `OpacityMask`,
  `Classes="icon"` matches under `src/Paperbunkr.App/`.
- Crash-free direct-exe launch; user does an on-screen sweep (rail, toolbars, dialogs, reader
  chrome, empty states).

## Open questions

None — the `action → Symbol` table landed in `icon-mapping.md`; no call site was genuinely ambiguous.

## Implementation notes

- **No StyleInclude needed.** `FluentIcons.Avalonia` v2 self-renders from the bundled
  `FluentIcons.Resources.Avalonia` font — `SymbolIcon` produces a glyph with only the package
  reference. `FluentIconRenderSmokeTests` guards this (a zero-size glyph = every icon gone blank).
- **Colour is inherited, not set.** The shared `fi|SymbolIcon` style sets only `FontSize` +
  `IconVariant` — `SymbolIcon` inherits `Foreground` from its parent `Button`/`TextBlock`
  (confirmed by test). This collapsed most of the per-context selectors: `Button.rail.active` /
  `Button.clusterPill.active` etc. already set the button's `Foreground`, so the icon recolours on
  activation for free. Only genuinely divergent spots keep an explicit `Foreground`.
- **Star toggle** uses `IconVariant="Filled"` for the selected state (outline → solid), not just a
  colour change — reads better than the old stroke-only flip.
- **`SmartScreen` "Add condition"** was a two-`Rectangle` hand-drawn plus (the old
  `OpacityMask`+`ImageBrush` silently failed in that one slot); now a real `<fi:SymbolIcon Symbol="Add"/>`.
- **Rail nav** kept its existing (pre-migration) glyph choices verbatim — Library and Books both
  map to `Book`, Redo is `ArrowUndo` mirrored via `RenderTransform="scaleX(-1)"`. Not this
  migration's job to re-choose them.
- **Deleted:** `Styles/Icons.axaml`'s 49 `PbIcon*` geometries (file reused for the new default
  style), all 39 `Assets/Icons/*.png`, `App.axaml`'s `Border.icon` style, every `Path.pbIcon` /
  `Button.* Path.pbIcon` selector across 22 views.
- **Tests:** `ReadingModeIconConverterTests` rewritten (`SymbolFor` returns a `Symbol`);
  `FluentIconRenderSmokeTests` added. Full `App.Tests` green apart from two pre-existing flaky
  CBZ-write-back tests (pass 8/8 in isolation, unrelated to icons).
