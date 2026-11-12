# Collection icons and slim banner — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md*

**Status (2026-11-06):** Steps 1-6 built; not committed; the on-screen pass is the user's. See the spec's Implementation notes.

The tree carries many uncommitted edits from other work: touch only the files named here, run only targeted tests, never two `dotnet build`/`test` at once, never kill a running
Paperbunkr (build with `-p:UseAppHost=false` if it locks `bin`). Load `paperbunkr-design-language`; read `avalonia-pro-max/review-checklist` before calling it done. Render tests load
`FormControls.axaml`/`Typography.axaml` (the harness's `AppStyles()` does not). One migration (Step 5); never regenerate it once the user has launched the app.

## Step 1: Icon value and hero set
**Files:** `Models/CollectionIcon.cs` (new), `Models/HeroGlyphs.cs` (new), `Models/CollectionGlyphs.cs` (edit: symbols stay, `TryParse` used by `CollectionIcon`),
`THIRD-PARTY-NOTICES.md` (edit: game-icons.net CC BY 3.0 entry with authors), tests `CollectionIconTests`, `HeroGlyphsTests` (new).
**What:** `CollectionIcon` record (`Kind` Symbol/Hero/Publisher/File, `Key`) with `Parse(string?)` (plain name = symbol, `hero:`, `publisher:`, `file:` prefixes; junk -> null) and
`ToStored()`. `HeroGlyphs.All` = the spec's 24 (slug, label, author, SVG path `d`), path data taken from the Iconify `@iconify-json/game-icons` JSON on jsDelivr (512 viewBox); each
`Geometry` parsed once and cached. Verify every slug exists before embedding; `flat-hammer` replaces `thor-hammer`.
**Depends on:** none.
**Verify:** `CollectionIconTests` (round trip per kind, legacy names, junk), `HeroGlyphsTests` (24 unique slugs, every path parses, labels unique).

## Step 2: "My icons" folder and image cache
**Files:** `Services/CollectionIconFolder.cs` (new, mirrors `PublisherIconFolder`), `Services/CollectionIconImages.cs` (new: scan + cache), `Services/SvgMarkRenderer.cs` (edit: a
file-path overload of `Render`), test `CollectionIconImagesTests` (new).
**What:** `Scan()` lists `.svg`/`.png` in `%APPDATA%\Paperbunkr\collection-icons` with label = file name without extension, skipping files over 2 MB or that fail to decode and
returning the skipped count. `Get(fileName, px)` returns a cached `Bitmap` (SVG rendered at the requested height, PNG decoded to width), rendered off the UI thread on first request
with the control updating when ready; missing file -> null.
**Depends on:** none (parallel with Step 1).
**Verify:** `CollectionIconImagesTests` with a temp folder (svg + png found, 2 MB cap, corrupt file counted, missing file null, label from name).

## Step 3: One icon control at all four call sites
**Files:** `Controls/CollectionGlyphView.cs` (new: `Icon`, `IconSize`, `AccentBrush` properties; draws `SymbolIcon` / filled `Path` / `BrandMark` with `TextFallback=False` /
`Image`), `Models/CollectionSummary.cs`, `Models/CollectionBannerState.cs`, `ViewModels/CollectionPropertiesScreenViewModel.cs` (edit: expose `Icon` = `CollectionIcon.Parse(IconGlyph)`
resolved to null when it cannot draw; `HasGlyph`/`ShowDot` follow it), `Views/MainWindow.axaml`, `Views/CollectionBanner.axaml`, `Views/CollectionEmptyState.axaml`,
`Views/CollectionPropertiesOverlay.axaml` (edit: replace the `SymbolIcon` with the view), render test `CollectionGlyphViewRenderTests` (new).
**What:** a `publisher:` resolves through `MarkResolver.Instance.ResolvePublisher`; no mark -> treated as no icon (dot). A `file:` with no file -> dot. Sizes per call site from
`PbIconSize*`. `Uniform` fit for images.
**Depends on:** Steps 1, 2.
**Verify:** render of the sidebar row and banner with each of the four kinds on default/daylight/matrix; `CollectionSidebarRenderTests` and `CollectionBannerTests` still pass.

## Step 4: Picker with four sections
**Files:** `ViewModels/CollectionPropertiesScreenViewModel.cs` (edit: `SymbolChoices`, `HeroChoices`, `PublisherChoices` (library publishers with a mark, alphabetical, the members'
most common first flagged Suggested), `MyIconChoices` + `MyIconsSkippedText`, `OpenIconsFolderCommand`, `RefreshIconsCommand`; `SetGlyphCommand` takes the stored value),
`Models/GlyphChoice` (edit: carries a `CollectionIcon` + automation name "<label> icon/logo/image"), `Views/CollectionPropertiesOverlay.axaml` (edit: four labelled sections, each a
`WrapPanel` of `iconButton glyphChoice` with the view inside; empty My icons line + buttons), tests in `CollectionPropertiesScreenViewModelTests` (or the dialog's existing test class).
**What:** picking any kind stores the prefixed value; the current value marks its choice selected wherever it lives; a value that no longer resolves selects nothing and leaves
"No icon" enabled. Publisher list from `Series.Publisher` distinct over the library; the suggestion counts the collection's resolved member series.
**Depends on:** Step 3.
**Verify:** VM tests (each kind saves its prefix, suggested publisher first, only publishers with a mark, missing file selects nothing, My icons skipped text); dialog render; focus-ring
clipping test for the dialog.

## Step 5: Slim banner
**Files:** `Paperbunkr.Data/Entities/AppSettings.cs` (edit: `CollectionBannerSlim`), migration `AddCollectionBannerSlim` (new, no-op Down) + `AddCollectionBannerSlimMigrationTests`,
`ViewModels/LibraryScreenViewModel.cs` (edit: `IsBannerSlimPinned` persisted, `IsBannerScrolled`, `IsBannerSlim` = either; selecting a collection resets `IsBannerScrolled`),
`Views/LibraryScreen.axaml.cs` (edit: in `OnAnyScrollChanged`, the vertical offset of the visible view host sets `IsBannerScrolled` with hysteresis >24 / ==0),
`Views/CollectionBanner.axaml` + `.axaml.cs` (edit: `.slim` layout - one row of icon, name, read %, Edit, Pin, chevron; chevron `iconButton sm` + `chevronToggle`; remove the 700 px
`.short` rule; description cap two lines), test additions in `CollectionBannerTests`.
**What:** instant class switch, no height animation; nested horizontal scrollers ignored (only the visible host's vertical offset counts).
**Depends on:** Step 3 (the banner hosts the new icon view).
**Verify:** `CollectionBannerTests` (scroll offset -> slim, hysteresis, chevron persisted in a new VM, selecting a collection returns to full); banner render full + slim at 1366 px;
migration test.

## Step 6: Review, docs and handover
**Files:** `docs/paperbunkr-todo.md` (entry), the spec's Implementation notes, this plan's status, memory `project_paperbunkr_cosmetics_pitch_3.md`.
**What:** review checklist; targeted run of every new/edited test class, the lint trio (known ComicVine `FamilyButtonsCarryNoSizeOverrides` failure), theme goldens, focus-ring
clipping, `PreferenceIndexTests`; build `src/Paperbunkr.App` into `bin`.
**Verify:** all of the above; on screen by the user (icons of each kind in the sidebar and banner, a real SVG/PNG in My icons, the slim banner while scrolling each view mode at 1366x768).

## Risks to watch while building
- `BrandMark` inside a 28 px `iconButton`: check it fits without a size override (lint `FamilyButtonsCarryNoSizeOverrides`).
- `ScrollChanged` also fires from the Details table's inner scroller and horizontal shelves: use the visible host's vertical offset, not `e.Source` blindly.
- Rendering user SVGs: never on the UI thread; a failure is a skipped file, not an exception.
