# Brand & Metadata Iconography — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-28-brand-metadata-iconography-design.md*
*Asset checklist: docs/superpowers/specs/2026-08-28-brand-logo-sourcing-checklist.md*

## Context found while surveying

- **Assets already sourced.** `src/Paperbunkr.App/Assets/Marks/Services/` has 10 SVGs, `.../Publishers/`
  has 18, plus `SOURCES.md`. Some are single-path monochrome (Simple Icons: `anilist`, `myanimelist`,
  `kitsu`, `mangaupdates`, `square-enix` — no `fill`, want tinting); others are genuine multi-colour
  brand marks (`mangadex` #ff6740+#272b30, `marvel` red rect + white text, `comicvine` full colour).
  So a real SVG renderer is unavoidable — a monochrome glyph won't do for all of them.
- **SVG rendering:** `Avalonia.Svg.Skia` is 11.3.0 (Avalonia 11 — no). `Svg.Skia` **core 5.2.3**
  pins `SkiaSharp 4.148.0` / `HarfBuzzSharp 14.2.0` — conflicts with Avalonia 12.1.1's
  `SkiaSharp 3.119.4` / `HarfBuzzSharp 8.3.1.3`. **`Svg.Skia` 5.1.0 pins `SkiaSharp 3.119.2` /
  `HarfBuzzSharp 8.3.1.3` — exact match.** Use **`Svg.Skia` 5.1.0**, rasterise SVG → `Bitmap` at
  display size and cache — the SkiaSharp→`WriteableBitmap` pattern `Services/BackdropBlurRenderer.cs`
  already uses.
- **`AvaloniaResource Include="Assets\**"`** already in the csproj — everything under `Assets/Marks/`
  is a loadable `avares://` resource with no csproj change. Alias `.tsv` files live there too, read
  via `AssetLoader.Open`.
- **`SplitText`** (`Controls/SplitText.cs` + implicit `ControlTheme` in `Styles/Typography.axaml`,
  registered by App.axaml's `<StyleInclude>` list) is the exact precedent for `BrandMark`.
- **Enums/fields:** `Issue.Format` / `Issue.AgeRating` / `Issue.LanguageISO` are `string?`;
  `Issue.ColorMode` is the `ColorMode` enum; `ContentType` / `ReadingMode` enums exist. `Series.Publisher`
  is `string?`.
- **`DetailBandViewModel`** — inline meta row is `StatusText` / `PublisherText` / `YearText`
  (`[ObservableProperty]` + `Has*` computed). Populated by the host VM, not `LoadIssue`/`LoadSeries`:
  `DetailScreenViewModel.cs:185`, `MangaDetailScreenViewModel.cs:319`, `BookDetailScreenViewModel.cs:304`.
- **Render surfaces (exact):**
  - Publisher text: `DetailBand.axaml:109`, `DetailTabs.axaml:363` (Details tab), `IssueListScreen.axaml:22`
    (col 5 of the `IssueRowTemplate` grid), `LibraryScreen.axaml` 235 (grid-card overlay badge, 9px),
    621 (2nd grid template), 794 (list col), 951 + 1036 (caption rows).
  - Language: `LibraryScreen.axaml` 243 + 629 — **already** a toggle (`ShowLanguageBadge` /
    `UseLanguageIcon`, currently text or a `fi:SymbolIcon Symbol="Globe"`).
  - Services: `Preferences/ConnectionsSection.axaml` (per-service `<TextBlock Text="AniList">` headers —
    AniList / MyAnimeList / Shikimori / Bangumi / MangaBaka in Trackers; ComicVine / Metron in Sources),
    `NewReadingListOverlay.axaml:87` (arc-source `ComboBox` item template — `ArcSourceOption.DisplayName`).
  - Format / AgeRating: `IssuePropertiesScreen.axaml` 320 (`AutoCompleteBox`) / 334 (`TextBox`) only.

---

## Step 1: `Svg.Skia` package + `SvgMarkRenderer` service
**Files:** `src/Paperbunkr.App/Paperbunkr.App.csproj` (edit — add `Svg.Skia` 5.2.3),
`src/Paperbunkr.App/Services/SvgMarkRenderer.cs` (new),
`src/Paperbunkr.App.Tests/SvgMarkRendererTests.cs` (new)
**What:** `static Bitmap? Render(string avaresPath, PixelSize target, IBrush? tint = null)` — loads the
SVG via `Svg.Skia`'s `SKSvg`, renders its `SKPicture` scaled to `target` onto an `SKSurface`, optional
`tint` re-colours a monochrome (no-fill) SVG via an `SKColorFilter`, copies pixels into a
`WriteableBitmap` (same `Marshal.Copy` path as `BackdropBlurRenderer`). An internal
`ConcurrentDictionary<(path,w,h,tint), Bitmap>` cache — marks are tiny and repeat heavily.
**Depends on:** none
**Verify:** `SvgMarkRendererTests` under `[Collection(nameof(AvaloniaTestCollection))]` — a bundled
monochrome SVG (`anilist.svg`) and a multi-colour one (`mangadex.svg`) each render to the requested
`PixelSize`; a missing path returns null; tint changes output for the monochrome one.

## Step 2: `MarkSpec` / `MarkKind`
**Files:** `src/Paperbunkr.App/Models/MarkSpec.cs` (new)
**What:**
```csharp
enum MarkKind { None, Text, LetterMark, Glyph, SvgAsset, Flag }
record MarkSpec(MarkKind Kind, string? AssetPath = null, Symbol? Glyph = null,
                string? Text = null, string? Background = null, string? Foreground = null)
{ static readonly MarkSpec None; static MarkSpec PlainText(string?); }
```
`Kind == Text`/`None` = "no mark, keep the plain TextBlock". `AssetPath` is a full `avares://` string.
`Background`/`Foreground` are hex for `LetterMark` chips.
**Depends on:** none
**Verify:** compiles; covered transitively by Step 4 tests.

## Step 3: Alias data files
**Files:** `src/Paperbunkr.App/Assets/Marks/publisher-aliases.tsv` (new),
`.../format-aliases.tsv` (new), `.../age-rating-aliases.tsv` (new),
`.../language-regions.tsv` (new), a throwaway generator script in the session scratchpad
**What:** TSV, `canonical<TAB>alias|alias|…`, one row per canonical value, `#` comment lines allowed.
- `format-aliases.tsv` / `age-rating-aliases.tsv` — **generated verbatim** from
  `_reference/ComicRackCE/ComicRack/Output/Resources/Icons/Formats/*.png` and `AgeRatings/*.png`
  filenames (split on `#`) + `Formats/map.ini` (`1_2.png=1/2`, `Black & White…=B/W`). Each canonical
  row also carries, in a 3rd column, the chosen `Symbol` glyph + short label + optional bg/fg
  (e.g. `Trade Paperback<TAB>TPB<TAB>Book|TPB`, `Mature 17+<TAB><TAB>|17+|#854F0B`). The glyph/colour
  column is authored by hand in this step — a finite reviewed table (~40 formats, ~14 ratings).
- `publisher-aliases.tsv` — hand-built. Rows for the 18 publishers with SVG assets (canonical →
  asset filename stem is implicit) + ~15 more letter-mark-only rows (`Marvel<TAB>marvel comics|marvel|MARVEL`).
  Colour-tier publishers get a bg hex in a 3rd column for the letter-mark fallback.
- `language-regions.tsv` — `ja<TAB>jp`, `en<TAB>us`, `ko<TAB>kr`, `zh<TAB>cn`, `zh-hant<TAB>tw`,
  `zh-hans<TAB>cn`, `es<TAB>es`, `fr<TAB>fr`, `de<TAB>de`, `it<TAB>it`, `pt<TAB>br`, `pt-pt<TAB>pt`,
  `ru<TAB>ru`, `nl<TAB>nl`, `pl<TAB>pl`, `id<TAB>id`, `th<TAB>th`, `vi<TAB>vn`, `tr<TAB>tr`, `ar<TAB>sa`,
  `ja-ro<TAB>jp`.
**Depends on:** none
**Verify:** Step 4 tests parse every row; a unit test asserts the CE Format/AgeRating filenames are
all represented (guards drift if `_reference` updates).

## Step 4: `MarkResolver`
**Files:** `src/Paperbunkr.App/Services/MarkResolver.cs` (new),
`src/Paperbunkr.App.Tests/MarkResolverTests.cs` (new)
**What:** singleton, loads the four TSVs once (via `AssetLoader.Open`), builds case-insensitive
lookup dicts, probes `Assets/Marks/{Services,Publishers,Flags}/` for asset presence at construction.
Methods:
```
MarkSpec ResolveService(string id)      // "AniList", "ComicVine", "ComicBookReadingOrders", …
MarkSpec ResolvePublisher(string? s)
MarkSpec ResolveFormat(string? s)
MarkSpec ResolveAgeRating(string? s)
MarkSpec ResolveLanguage(string? iso)
IReadOnlyList<MarkSpec> ResolveSpecial(Issue issue)   // manga / B&W / RTL / complete
```
- Service: SVG asset if `Services/{key}.svg` exists → `SvgAsset`; else `LetterMark` from a small
  built-in `id→initials` table (`MangaUpdates→MU`, `ComicBookReadingOrders→CBRO`, …).
- Publisher: normalise (trim, lowercase, strip trailing "comics"/"publishing"/"entertainment"),
  look up canonical → `SvgAsset` if `Publishers/{stem}.svg`, else `LetterMark` (with bg from the tsv
  colour column), else `PlainText(s)`.
- Format/AgeRating: normalise → canonical row → `Glyph` (Symbol + label) or `LetterMark`; unknown →
  `PlainText`.
- Language: iso (lowercased, `_`→`-`) → region via tsv → `Flag` (`Flags/{region}.svg`); unknown →
  `PlainText(iso.ToUpperInvariant())`.
- Special: derived — `ContentType` manga-family → manga glyph; `ColorMode.BlackAndWhite` → B&W glyph;
  `ReadingMode` RTL → RTL glyph. Returns `[]` when none apply.
**Depends on:** Steps 2, 3
**Verify:** `MarkResolverTests` — each map resolves known inputs incl. case/whitespace/suffix
variants; unknowns → `Kind == Text`; `ResolveService` falls back to the right letter-mark for a
no-asset service; `language-regions` is total over its declared keys; `ResolveSpecial` fires on each
derived condition and returns `[]` otherwise.

## Step 5: `BrandMark` control
**Files:** `src/Paperbunkr.App/Controls/BrandMark.cs` (new),
`src/Paperbunkr.App/Styles/Marks.axaml` (new),
`src/Paperbunkr.App/App.axaml` (edit — add `<StyleInclude Source="…/Styles/Marks.axaml"/>` to the list),
`src/Paperbunkr.App.Tests/BrandMarkRenderSmokeTests.cs` (new)
**What:** code-only `TemplatedControl` (SplitText pattern — no new `x:Class` axaml). Properties:
`Family` (enum Service/Publisher/Format/AgeRating/Language/Special), `Value` (string),
`ShowText` (bool, default true), `Size` (double, default 16). On `Value`/`Family` change it calls the
static `MarkResolver`, then the template (implicit `ControlTheme` in `Marks.axaml`) renders by
`MarkKind`:
- `SvgAsset`/`Flag` → an `Image` whose `Source` is `SvgMarkRenderer.Render(AssetPath, Size)`
  (bound through a tiny value converter or set in code-behind on change).
- `Glyph` → `<fi:SymbolIcon>` + optional label `TextBlock`.
- `LetterMark` → a `Border` chip (`Background` from spec or `PbSurface3`) + `TextBlock`.
- `Text`/`None` → a bare `TextBlock Text="{Value}"` — so a call site swapping a plain
  `<TextBlock Text="{Binding X}"/>` for `<ctl:BrandMark .../>` never regresses layout.
`ShowText=false` (tiny overlays) hides the name, mark only. `AutomationProperties.Name` = the resolved
display text.
**Depends on:** Steps 1, 4
**Verify:** `BrandMarkRenderSmokeTests` — a `BrandMark` of each `Family` with a representative `Value`
measures to non-zero size (guards the whole asset+resolver+render pipeline, like
`FluentIconRenderSmokeTests`).

## Step 6: Flag assets
**Files:** `src/Paperbunkr.App/Assets/Marks/Flags/*.svg` (new — ~21: jp us gb kr cn tw es fr de it br
pt ru nl pl id th vn tr sa), `src/Paperbunkr.App/Assets/Marks/SOURCES.md` (edit — add Flags section)
**What:** copy the needed country SVGs from the `flag-icons` project (MIT) — 4x3 `viewBox`, flat.
**Depends on:** none (parallel with 1–5)
**Verify:** Step 5 smoke test renders `Family=Language Value="ja"`; visual check in Step 11.

## Step 7: Wire service marks
**Files:** `src/Paperbunkr.App/Views/Preferences/ConnectionsSection.axaml` (edit),
`src/Paperbunkr.App/Views/NewReadingListOverlay.axaml` (edit)
**What:** ConnectionsSection — replace each `<TextBlock Text="AniList" …/>` service header with
`<ctl:BrandMark Family="Service" Value="AniList" Size="18" ShowText="True"/>` (7 spots: AniList,
MyAnimeList, Shikimori, Bangumi, MangaBaka, ComicVine, Metron). NewReadingListOverlay — the
`ArcSourceOption` `ComboBox.ItemTemplate` becomes a `BrandMark` (`Family="Service"`,
`Value="{Binding Key}"`, `ShowText="True"`).
**Depends on:** Step 5
**Verify:** build; on-screen (Step 11).

## Step 8: Wire publisher marks
**Files:** `src/Paperbunkr.App/Views/DetailBand.axaml` (edit),
`src/Paperbunkr.App/Views/DetailTabs.axaml` (edit),
`src/Paperbunkr.App/Views/IssueListScreen.axaml` (edit),
`src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — 235, 621, 794, 951, 1036)
**What:** each publisher `<TextBlock Text="{Binding Publisher}"/>` (or `PublisherText`) becomes
`<ctl:BrandMark Family="Publisher" Value="{Binding Publisher}" ShowText="…"/>`.
`ShowText="False"` on the 9px grid-card overlays (235, 621) — mark only; `True` elsewhere. Preserve
each site's `FontSize`/`Foreground`/`TextTrimming` by setting `Size` and letting the `Text`-fallback
inherit. The DetailBand `·` separator + `IsVisible="{Binding HasPublisher}"` stay.
**Depends on:** Step 5
**Verify:** build; `DetailBandViewModelTests` unchanged; on-screen (Step 11).

## Step 9: Wire language flags
**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — 243, 629)
**What:** replace the text-or-Globe `Panel` inside the language overlay badge with
`<ctl:BrandMark Family="Language" Value="{Binding LanguageIso}" ShowText="False" Size="12"/>`.
The existing `ShowLanguageBadge` gate stays; the `UseLanguageIcon` toggle + `fi:SymbolIcon Globe`
are removed (the flag *is* the icon now).
**Depends on:** Steps 5, 6
**Verify:** build; check `LibraryScreenViewModel.UseLanguageIcon` has no other consumers (grep) —
remove it and its setting if orphaned; on-screen (Step 11).

## Step 10: Format / AgeRating / Special on the Detail band + editor preview
**Files:** `src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/DetailBand.axaml` (edit),
`src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml` (edit),
`src/Paperbunkr.App.Tests/DetailBandViewModelTests.cs` (edit)
**What:**
- `DetailBandViewModel` gains `[ObservableProperty] string FormatText / AgeRatingText` + `Has*`
  computed + `ObservableCollection<MarkSpec> SpecialMarks` (or a small display record). No resolver
  call in the VM — it stores the raw strings + the focused `Issue`; the view's `BrandMark`s resolve.
  Actually store raw: `FormatText`, `AgeRatingText`, and a `SpecialMarks` list the VM fills by calling
  `MarkResolver.ResolveSpecial(issue)` (VM→resolver is fine, it's pure).
- Host VMs: in `DetailScreenViewModel` (~line 185, series/issue-focus) and `MangaDetailScreenViewModel`
  (~319) set `Band.FormatText` / `Band.AgeRatingText` / `Band.SpecialMarks` from the focused issue
  (issue-focus mode) or the cover/representative issue (series mode). `BookDetailScreenViewModel`
  leaves them empty.
- `DetailBand.axaml` inline meta row: after `YearText`, append `<ctl:BrandMark Family="Format"
  Value="{Binding FormatText}" IsVisible="{Binding HasFormat}"/>`, same for AgeRating, and an
  `ItemsControl` over `SpecialMarks`. Each preceded by the same `·` separator pattern.
- `IssuePropertiesScreen.axaml`: next to the Format `AutoCompleteBox` and AgeRating `TextBox`, a
  read-only `<ctl:BrandMark>` bound to the field's live value — a "this resolves to →" preview.
**Depends on:** Steps 4, 5
**Verify:** `DetailBandViewModelTests` — new cases: `FormatText`/`AgeRatingText`/`Has*` set from a
seeded issue; `SpecialMarks` populated for a manga / B&W issue, empty otherwise; book mode leaves all
empty. Build; on-screen (Step 11).

## Step 11: Build, suite, on-screen sweep
**What:** clean XAML-weave rebuild (delete `obj/.../Paperbunkr.App.dll` first, per CLAUDE.md), full
`dotnet test` for `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests`, crash-free `Paperbunkr.App.exe`
launch. On-screen: Preferences → Connections (service marks), New Reading List → arc picker, a
series Detail (band publisher + format/age/special), Library grid + list + Comic-List (publisher
marks, language flags on the card overlay), Issue Properties editor (format/age preview).
**Depends on:** all
**Verify:** 0 build errors / no new warnings; suites green (bar the two known-flaky CBZ-write-back
tests); user confirms each surface.

---

## Test strategy summary

| Piece | Test |
|---|---|
| `SvgMarkRenderer` | `SvgMarkRendererTests` (`AvaloniaTestCollection`) — mono + colour SVG → target `PixelSize`, missing → null, tint effect |
| Alias TSVs | parsed + CE-filename-coverage assertion in `MarkResolverTests` |
| `MarkResolver` | `MarkResolverTests` — all five families, case/whitespace/suffix variants, unknown → `Text`, `ResolveSpecial` conditions |
| `BrandMark` | `BrandMarkRenderSmokeTests` — each `Family` measures non-zero |
| `DetailBandViewModel` | existing suite + new Format/AgeRating/Special cases |
| Every view surface | on-screen sweep, Step 11 (no automation for these — standing caveat) |

## Implementation notes (2026-08-29)

All 11 steps done. Build clean; `Paperbunkr.App.Tests` 1190/1190, `Paperbunkr.Data.Tests` 539/539,
+40 new mark tests (`MarkResolverTests` 33, `SvgMarkRendererTests` 5, `BrandMarkRenderSmokeTests` 12).
App launches to "Startup complete" (one `[FreezeWatchdog]` on an earlier attempt = the env's known
GPU-compositing flakiness, not this code — cleared on relaunch).

- **`Svg.Skia` 5.1.0** works on Avalonia 12.1 / .NET 10. One real bug found + fixed: the `SKPicture`
  is owned by the `SKSvg`, so `RenderUncached` must do all its drawing *inside* the `using var svg`
  block — reading `.CullRect` after the `SKSvg` disposed was a hard native crash
  (`sk_picture_get_cull_rect`).
- **`SvgMarkRenderer.Render(path, int maxSize, tint)`** returns an aspect-fitted bitmap (longest
  side = `maxSize`), so portrait ESRB boxes aren't letterboxed. `BrandMark`'s `<Image Height=…>`
  scales it.
- **Age ratings + formats grew an SVG-asset tier** while implementing (user added
  `Assets/Marks/AgeRatings/*.svg` ESRB glyphs and `Assets/Marks/Formats/{crossover,nsfw}.svg`, and
  reshaped `age-rating-aliases.tsv` to `canonical / aliases / asset / chiptext / bg`). `MarkResolver`
  now: age rating → ESRB SVG if the alias row names an existing `AgeRatings/` asset, else colour
  letter chip; format → `Formats/<slug>.svg` (explicit 6th column or `<canonical-slug>.svg`) if it
  exists, else glyph+label. `AliasTable` gained a `Col6` and an optional key-normaliser (age ratings
  collapse ` -+_`).
- **Language card badge** kept the existing `UseLanguageIcon` toggle (it has a Library-toolbar
  checkbox + persisted setting + tests — not orphaned); relabelled it "Show language flag". Off =
  ISO text, on = flag.
- **Special marks** on the Detail band are short text chips (`SpecialMarks` = `ObservableCollection<string>`
  of `MarkResolver.ResolveSpecial(issue).Text`), not full `BrandMark`s — the resolver returns specs,
  the band renders their labels.
- Format/AgeRating/Special fed to `DetailBandViewModel` from the focused issue (`DetailScreenViewModel`
  issue-focus + series paths) or the cover/representative issue (`MangaDetailScreenViewModel`);
  `BookDetailScreenViewModel` leaves them empty.

## Risks / notes

- **`Svg.Skia` 5.2.3 on net10 / this SkiaSharp version** — if it pulls an incompatible `SkiaSharp`,
  pin to the app's existing `SkiaSharp` version or drop to build-time rasterisation (a `dotnet` tool
  run in a `BeforeBuild` target producing PNGs). Resolver/`BrandMark` API unchanged either way — this
  only swaps `SvgMarkRenderer`'s internals + `MarkKind.SvgAsset`→`Raster`.
- The 4 potrace auto-traced publisher SVGs (`boom`, `dynamite`, `oni`, `seven-seas`) are single black
  paths — render fine tinted; flagged in `SOURCES.md` to swap later.
- `valiant.svg` is CC BY-SA 4.0 (needs attribution if marks ever get a credits surface) — noted in
  `SOURCES.md`, not blocking.
- No DB migration, no entity changes, no new settings. `LibraryScreenViewModel.UseLanguageIcon` +
  its persisted setting may become dead (Step 9) — remove if so.
