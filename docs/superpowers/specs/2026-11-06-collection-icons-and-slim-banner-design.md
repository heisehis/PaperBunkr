# Collection icons and slim banner — Design

Follow-up to [cosmetics pitch 3](2026-11-06-cosmetics-pitch-3.md) Slices 2 and 3, from the user's first on-screen look (2026-11-06): "we need super hero icons for the glyphs and
maybe you can include publisher logos", and the collection banner "is stuck in one position and in my resolution, it's kind of a pain". Grilled 2026-11-06 in two rounds; every
recommended answer accepted. Status: **built 2026-11-06, not committed, not seen on screen** (see Implementation notes).

## Summary

A collection's icon can now be one of four kinds: today's **symbols**, 24 bundled **hero** icons (game-icons.net, CC BY 3.0), a **publisher logo** from the existing mark set, or an
image from the user's own **"My icons"** folder (where the user puts any character emblems they collect themselves; the app ships none). One control draws all four everywhere an icon
shows. The Library's collection **banner turns into a slim one-line bar** once you scroll, in every view mode, and a chevron can pin it slim.

## Goals / non-goals

Goals: hero icons, publisher logos and user images as collection icons; a banner that stops costing a quarter of a 1366x768 window.

Non-goals (decided): **bundling DC/Marvel character emblems** (the user's reference sheets are registered trademarks from unlicensed sources, and the app, installer, landing site
and posts are public - the "My icons" folder gives the user's own install any emblem they want instead); icons8 icons bundled (their free use needs an About link and gives limited
formats; files the user downloads go in "My icons" and are never shipped, so no icons8 credit is needed); icon search in the picker; per-icon tint options; the banner scrolling
away entirely (it would have to be rebuilt inside seven view hosts and would take Edit with it).

## CE parity

ComicRack CE has no collection icons or banner; nothing to verify against `_reference/ComicRackCE`.

## Existing anchors (verified 2026-11-06)

- `Collection.IconGlyph` (string, no length limit) stores a FluentIcons `Symbol` name from `Models/CollectionGlyphs.All` (24 entries); `CollectionGlyphs.TryParse` returns null for an
  unknown name and every surface then shows the accent dot. Saved by `CollectionService.SetIdentity`.
- The icon is drawn by `fi:SymbolIcon Symbol="{Binding GlyphSymbol}"` in four places: `MainWindow.axaml` (sidebar row), `CollectionBanner.axaml`, `CollectionEmptyState.axaml`,
  `CollectionPropertiesOverlay.axaml` (dialog preview row). The picker is a `WrapPanel` of `Button.iconButton.glyphChoice` (`GlyphChoices`, `SetGlyphCommand`, `ClearGlyphCommand`).
- Publisher logos: `MarkResolver.Instance.ResolvePublisher(name)` -> `MarkSpec`, drawn by `Controls/BrandMark` (bundled marks under `Assets/Marks/Publishers` plus the user folder
  `PublisherIconFolder` = `%APPDATA%\Paperbunkr\publisher-icons`). SVG rendering: `Svg.Skia` via `Services/SvgMarkRenderer`.
- Banner: `Views/CollectionBanner` sits in `LibraryScreen` `RootGrid` row 1 (`Auto`) above row 2, which holds seven view hosts (Poster, Panorama, Bookshelf, Tiles and
  Collection `SmoothScrollViewer`s; the List panel and the Details table with their own scrollers). The code-behind adds `.short` (name row only) under a 700 px window height, which a
  maximised 1366x768 window never reaches. Description cap: `MarkdownView.capped` MaxHeight 60 (three lines).
- No generic UI-state store exists; per-user switches are `AppSettings` columns.

## Decisions

| Topic | Decision |
|---|---|
| Stored value | `Collection.IconGlyph` keeps its column. Plain name = symbol (every existing value stays valid); `hero:<slug>`; `publisher:<publisher name>`; `file:<file name>`. No migration for this. |
| Parsing | New `Models/CollectionIcon` (record: Kind Symbol/Hero/Publisher/File, Key) with `Parse(string?)` and `ToStored()`. Unknown symbol or hero slug, a publisher with no mark, or a missing/unreadable file -> null -> the accent dot, as today. |
| Drawing | New `Controls/CollectionGlyphView` (size in px, accent brush): Symbol -> `SymbolIcon`; Hero -> a `Path` with the bundled geometry, filled with the accent; Publisher -> `BrandMark` (own colours, `TextFallback="False"`); File -> `Image` of the cached bitmap (own colours). Replaces the `SymbolIcon` at all four call sites. |
| Hero set | 24 icons from game-icons.net as path data in `Models/HeroGlyphs` (slug, label, author, path): Masked Hero (lorc/domino-mask), Cape (delapouite/cape), Raised Fist (lorc/fist), Lightning Bolt (lorc/power-lightning), Hero Shield (lorc/bolt-shield), Power Ring (delapouite/power-ring), Hammer (delapouite/flat-hammer), Claws (lorc/claw-slashes), Web (lorc/spider-web), Atomic (skoll/atom), Cosmic (delapouite/galaxy), Robot (lorc/vintage-robot), Cyborg (delapouite/cyborg-face), Alien (lorc/alien-stare), Skull (sbed/death-skull), Ninja (lorc/ninja-mask), Sorcery (lorc/magic-swirl), Villain (delapouite/devil-mask), POW! (lorc/spiky-explosion), Smash (lorc/punch-blast), Speech Bubble (seregacthtuf/thought-bubble), Utility Belt (lucasms/belt), Rocket (lorc/rocket-flight), Royalty (lorc/crown). Excluded as emblem lookalikes: american-shield, bat-mask, spider-mask, masked-spider, iron-mask, lightning-mask, guy-fawkes-mask; `flat-hammer` instead of `thor-hammer`. |
| Credit | About > Legal gets a "game-icons.net" entry: the CC BY 3.0 licence link and the authors used (Lorc, Delapouite, Skoll, sbed, Lucas, SeregaCthtuf), noting the icons are recoloured. |
| Publishers section | Publishers that appear in the library and resolve to a mark, alphabetical, with the publisher most common among this collection's members first (marked "Suggested"). |
| My icons | `%APPDATA%\Paperbunkr\collection-icons` (`Services/CollectionIconFolder`, like `PublisherIconFolder`). `.svg` and `.png`; label = file name without extension; files over 2 MB or that fail to load are skipped and counted ("2 files skipped"). Bitmaps are rendered once per file and size and cached; the folder is read when the picker opens and on Refresh. Buttons: Open icons folder, Refresh. Empty folder: one line saying where to put files. |
| Picker | The ICON field in the Edit Collection dialog becomes four labelled sections, in order: Symbols, Heroes, Publishers, My icons (each a `WrapPanel` of `iconButton glyphChoice`). "No icon (use the dot)" stays. |
| Banner slim | Scrolling any view host past 24 px puts the banner in `.slim` (one row: icon, name, read %, Edit, Pin, chevron; blur layer kept at the row's height); back at offset 0 it returns to full. Instant class switch, no height animation. Driven by `ScrollViewer.ScrollChanged` bubbling to `RootGrid` row 2, so all seven hosts work. |
| Banner chevron | An `iconButton sm` with the shared `chevronToggle` pins the banner slim; saved as new `AppSettings.CollectionBannerSlim` (migration `AddCollectionBannerSlim`, no-op Down). |
| Banner details | The 700 px `.short` rule is removed. Full banner description cap is two lines. Selecting a collection scrolls to the top and shows the full banner unless pinned slim. |

## Behaviour details

- **Fallbacks:** a `file:` icon whose file was deleted, a `publisher:` whose logo was removed, and a `hero:` slug no longer in the set all show the accent dot; the dialog shows no
  selection for them and "No icon" is enabled so the value can be cleared.
- **Colour:** symbols and heroes take the collection's accent (as symbols do now); publisher logos and user images keep their own colours.
- **Sizes:** the view draws at the call site's icon token (sidebar `PbIconSizeXs`, dialog `PbIconSizeSm`, banner `PbIconSizeLg`, empty state `PbIconSizeXl`); images are fitted
  `Uniform` into that square.
- **Scroll hysteresis:** slim at offset > 24, full only at 0, so the bar does not flicker near the top. Switching view modes re-reads the visible host's offset.
- **Accessibility:** each picker button's automation name is "<label> icon" ("Cape icon", "DC Comics logo", "Batman image"); the chevron is "Keep the banner slim".

## Approaches considered

1. **One value type + one view control (chosen).** All four kinds parse in one place and draw in one control, so the four call sites stay one line each.
2. Extra `Image`/`Path` elements beside each `SymbolIcon` with visibility switches - four copies of the same logic to keep in step.
3. Everything rendered to bitmaps - simplest drawing, but symbols and heroes would stop following the accent and theme.

Banner: a scroll-driven slim bar (chosen) vs. moving the banner inside each of the seven hosts so it scrolls away (rejected: seven copies, loses Edit) vs. a manual toggle only
(kept as the chevron, not as the only control).

## Avalonia / design-language constraints

- Hero icons are collection identity art like `BrandMark`, not UI chrome; they are `Path` geometry, not text glyphs, so `NoTextGlyphIcons` is unaffected. UI icons stay FluentIcons.
- Tokens only: accent from the collection's brush, radii `PbRadius*`, icon sizes `PbIconSize*`; the slim row on the 4 px grid with the 24 px gutter.
- No Height transition on the banner (motion rules); the class switch is instant. Description cap stays a `MaxHeight` style.
- Picker buttons keep the `iconButton` family (no size overrides); a `BrandMark` or `Image` inside fits the 28 px square.
- Focus rings of the picker's first-column buttons need 6 px clearance (`Screen_FocusRings_AreNotClipped`).

## Verification

- Unit: `CollectionIcon.Parse`/`ToStored` for all kinds, legacy names and junk; `HeroGlyphs` (24 unique slugs, each path parses as geometry); `CollectionIconFolder` scan (svg/png,
  2 MB cap, bad file counted, label from file name); publisher section order (most common first, only publishers with a mark).
- View model: picking each kind saves the prefixed value; a missing file shows no selection; banner slim switch from a scroll offset, hysteresis, chevron persisted across a new view model.
- Data: `AddCollectionBannerSlimMigrationTests`.
- Render: sidebar row and banner (full and slim) with each icon kind on default/daylight/matrix at 1366 px wide; dialog picker sections.
- Lint trio, focus-ring clipping, theme goldens, `PreferenceIndexTests` unchanged.

## Risks

- **Publisher name drift:** a `publisher:` value keyed by name breaks if the publisher is renamed; it then falls back to the dot (acceptable, re-pick).
- **Large SVGs:** a heavy user SVG could stall the UI on first render; rendering happens off the UI thread and is cached, with the 2 MB cap.
- **ScrollChanged from nested scrollers:** the Details table or a horizontal shelf could raise ScrollChanged; only the vertical offset of the visible view host counts.

## Implementation notes (2026-11-06)

Built per the plan. Additions and corrections found while building:
- **Credits checked against the game-icons repository:** Iconify's `fist` is Skoll's drawing and `flat-hammer` is Lorc's, so those two credits differ from the survey; the notice lists Lorc,
  Delapouite, Skoll, sbed, Lucas and SeregaCthtuf.
- **Picker tiles** draw the icon in the button's own text colour (no accent), so symbols and heroes read the same in all three themes; the accent is used on the sidebar, banner and empty card.
- **`HasIconValue`** keeps "No icon (use the dot)" enabled for a stored value that no longer draws (a deleted file), so it can always be cleared.
- **"My icons" scan** runs on a worker thread when the dialog opens and on Refresh; the newest scan wins if two overlap.
- **Banner height bug found by the render:** the blurred-cover layer's height is bound to the content's height, and the content stretched to the row, so the banner could never shrink. The
  content is now top-aligned (`BannerContent VerticalAlignment="Top"`). The slim bar measures 61 px (full banner with a description: 145 px).
- **Selectors:** the banner's own styles use `views|CollectionBanner.slim`; a plain `UserControl.slim` selector does not match the subclass (the old `.short` rule had the same latent problem).
- Only the list area's vertical scroller counts (`RootGrid` row 2, not the preview column; a horizontal shelf's offset is ignored).
