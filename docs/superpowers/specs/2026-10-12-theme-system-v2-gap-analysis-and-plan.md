# Theme System v2: gap analysis and phased implementation plan

**Date:** 2026-10-12
**Status:** For review. **Nothing is implemented from this document.** No Theme Studio and no plugin runtime are in scope.
**Input:** `paperbunkr-theme-system-v2-spec-for-claude.md` (the "spec"), checked against the repository as it stands today (master, uncommitted tree
included). Every claim in section 1 was verified in code unless marked *not verified*.
**Status labels** follow the spec: **DECIDED / EXCLUDED / PROPOSED / OPEN / EXISTING**. Where this plan adds its own recommendation it says so and
labels it **MY RECOMMENDATION** (not a user decision).

---

## 0. Summary

- The current theme system is a **color-and-metrics engine with good plumbing**: one `theme.json` per theme, applied by overwriting about 60 `Pb*`
  resources that about 2,800 `DynamicResource` references consume. It is a sound base for v2. The bridge the spec calls for (section 6.5) already
  exists in spirit: `Pb*` **is** the compatibility surface.
- Roughly **a third of the spec exists in some form** (variants-by-mode, user accent/font/density/glow/motion options, quality-ish fallbacks, reader
  `Match App`, diagnostics of contrast). **Most of the spec is new**: the resolver and family/variant model, semantic roles, scoped overrides,
  component styles, the icon registry, materials, the declarative layout engine, and declarative motion.
- **The declarative layout engine (spec phase 4) is the one item that is genuinely a different order of magnitude.** The screens are hand-written
  XAML (Library alone is 2,047 lines over a 5,177-line view-model) and none has a UI contract a theme could target. I recommend a **pilot on one low-risk
  screen** and an explicit go/no-go before porting any others (section 5, phase 4).
- **Four existing defects the plan must fix on the way** (all verified): failed `.crpck` re-install destroys the working copy; Match App in the
  book reader is hardcoded to stale values; reader enter/exit rewrites every theme resource; the PDF reader's brushes are hardcoded
  `StaticResource`s (the spec/briefing placed them in `ReaderScreen.axaml`; they are in `PdfPageReaderScreen.axaml`).
- **Eleven open decisions** need an answer before specific phases lock (section 4). Phases 0 and 1 need none of them.

---

## 1. Repository findings (baseline inventory, spec section 2 verified)

### 1.1 What the spec's briefing got right

| Spec claim | Verified |
|---|---|
| Ten built-in themes, keys as listed | Yes: `default`, `cool_technical`, `vibrant_pop`, `vintage_paperback`, `windows_11`, `daylight`, `overcast`, `matrix`, `maximum_contrast`, `colorblind_safe` (`ThemeService.BuiltInThemeKeys`) |
| `ThemeService` orchestrates, replaces `Pb*` resource objects, consumers are `DynamicResource` | Yes. 2,767 `DynamicResource Pb*Brush/Color` references in XAML, 100 distinct `Pb*` keys referenced from XAML, 76 more referenced by string from C#. The only `StaticResource Pb*Brush/Color` is in `App.axaml` (the startup definitions) |
| `RequestedThemeVariant` set from `mode`, UI-thread guarded | Yes (`ApplyThemeVariant`, with the `CheckAccess()` guard and its comment about test threads) |
| FluentAvalonia accent pushed on each apply | Yes (`ApplyAccentColor` -> `CustomAccentColor`) |
| Glow `BoxShadows` rebuilt in code | Yes (`PbGlowRing`, `PbGlowPulseRing`, `PbGlowRingInset`, three tiers) |
| Auto mode Off / FollowSystem / Scheduled, platform callback deferred a tick | Yes, plus a test that the subscription is added and removed (`SetThemeAutoMode_FollowSystem_SubscribesAndUnsubscribes_ColorValuesChanged`) |
| Scrollbar tokens parsed but unconsumed | Yes. `PbScrollbarWidth` / `PbScrollbarThumbOpacity` are written and read by nothing |
| `icons {}` / `GetIcon` unconsumed | Yes. The only reference to `GetIcon` outside its definition is a test asserting it returns null |
| `category` unused | **Was true; no longer.** The Appearance work built earlier today uses it for the Accessibility gallery group (`ThemeService.GroupOf`) |
| Reader True Black suspend reloads everything | Yes. `SetTrueBlackReaderSuspend` calls `LoadTheme` and `ApplyThemeResources` on every reader enter/exit; the call site's own comment says it "reloads the theme and rewrites every theme resource". Only fires on reader entry/exit, not every navigation |
| Hardcoded colors in the listed files | Yes, and more (list below) |
| `Match App` is a hardcoded approximation | Yes: `BookThemeBrushes` returns `#14161B` / `#ECE7DB` / `#CC14161B` for `MatchAppSkin`. Those do not even equal the `default` theme's own values (`#0A0B0D`) |
| Danger / soft / sky / season colors are not theme-aware | Yes. `PbDanger*`, `PbBadgeSoft`, `PbSuccessSoft`, `PbSky*`, `PbSeason*` are defined once in `AppTokens.axaml`/`App.axaml`; `ThemeService` never writes them |
| Custom renderers read resources at render time | Yes (Event Map, Insights charts). They have no invalidation channel of their own beyond the next render |

### 1.2 What differs from the spec's briefing

1. **Reader brushes are in the wrong file.** The `Reader*Brush` family (`ReaderChromeBrush`, `ReaderBorderBrush`, `ReaderTextMutedBrush`) is defined in
   **`PdfPageReaderScreen.axaml`** with hex literals and used through `StaticResource`, so it can never follow a theme. The comic reader
   (`ReaderScreen.axaml`) and `Styles/ReaderChrome.axaml` use translucent white/black literals (`#22FFFFFF`, `#CC000000`, ...) rather than a family.
   So there are **three** reader styling paths (PDF brushes, comic literals/styles, book `BookThemeBrushes`), not one separate family.
2. **`IThemePlugin` already exists as a public plugin contract.** It exposes only `CurrentThemeKey`. Whatever v2 does with families, that string must
   keep resolving (see decision D4).
3. **There is already a host-rendered declarative UI precedent**, but only for forms: plugin `<Settings>` schemas rendered by
   `PluginSettingsSchemaViewModel` / `PluginSettingRowViewModel`, plus native plugin UI hooks (`INativeSeriesDetailUi`, `INativePluginSettingsUi`).
   Nothing declarative exists for layouts. This matters for spec phase 7 (reuse, do not add a runtime).
4. **There is already a "component layer", but it is class-based, not role-based.** `Styles/` holds 16 files (about 2,300 lines): `Primitives`, `Menu`,
   `FormControls`, `ReaderChrome`, ... defining classes such as `Button.headerAction`, `groupBox`, `sideItemButton`, `connTile`, `statusDot`. Spec layer C
   (component roles like `button.primary`) would sit on or replace this; there is no registry of named roles today.
5. **`ThemeService` is about 1,200 lines and owns eight concerns**: theme load/apply, installer, auto mode, true black, accent override math, glow, density,
   motion/reduced-motion, font precedence, era colors, contrast math. The spec's decomposition (section 4) is correct but is a **refactor of a live,
   well-tested class**, not an addition.
6. **Two things were added today that v2 must build on, not discard**: the grouped gallery + inspector in Appearance (`ThemeGroups`, `ActiveThemeSummary`,
   contrast badges) and `ThemeSummary` inspector fields. Families and variants will change what the gallery lists.

### 1.3 Hardcoded color inventory (for the allowlist, spec 6.5)

Hex literals in XAML outside `AppTokens.axaml`, by file (count): `PluginScreen` 15, `Primitives` 15, `DetailHero` 10 (intentional light-on-dark), `SplashWindow` 8,
`MainWindow` 7, `LibraryScreen` 6, `DetailTabs` 5, `SmartScreen` 4, `ReaderScreen` 4, `BookReaderScreen` 4, `FirstLook` 4, then 20 files with 1-3 each. Hex or
`Color.FromRgb/Argb` in C# (outside theme/model code): `SpineTile` 15, `BookThemeBrushes` 14, `InsightsChartTheme` 11, `SeriesCardSample` 8, `TileCosmeticsOverlay` 6,
`ReadingStatusPresentation` 6, `GoalRing` 5, `ShelfBoards` 4, `HighlightColorConverter` 4, `SpotlightAccentSampler` 4, plus about 15 more. **Not yet classified as intentional vs
defect**; classifying them is a phase 0 deliverable. No automated check for new hex literals exists today.

### 1.4 Packages and installation (spec section 5)

- `TryInstallSkin` copies the `.crpck` over any existing one with the same key, **deletes the existing extracted folder, extracts, then validates**; on
  failure it deletes both. **A bad re-install therefore destroys a working installed theme** (violates spec 13.3). It is not transactional.
- Extraction uses `ZipFile.ExtractToDirectory`, which rejects path-traversal entries; there is **no limit on entry count, uncompressed size or asset
  type**, and no asset validation at all (only `theme.json` is ever read; the rest of the extracted tree is ignored).
- Packages today are `theme.json` + optional icon files. `schemaVersion` does not exist, so v1 is "no `schemaVersion`".

### 1.5 Persistence

`AppSettings` (singleton row) already carries: `ActiveThemeKey`, `LastLightThemeKey`, `LastDarkThemeKey`, `ThemeAutoMode`, `ThemeScheduledDarkHour/LightHour`,
`TrueBlackDark`, `TrueBlackAutoHour`, `AccentOverrideHex`, `SelectedFontFamily`, `MatrixRainEnabled`, `GlowTier`, `DensityPreset`, `ReducedMotion`, and the book
reader's `BookReaderTheme` (enum `BookTheme`: Light, Dark, Sepia, MatchAppSkin, OledBlack, HighContrast). Theme-specific migrations so far: the key rename, the
theme-system extensions, `AddMatrixRainEnabled`, plus later cosmetic columns. Project gotchas that apply to any new column or enum value: explicit
`HasDefaultValue` for non-default bools (a C# initializer alone backfills `false`), the stale `Designer.cs` nullability trap, rollback-chain `Down()` rules, and **append-only enums**
(do not renumber `BookTheme`). There are two JSON-beside-the-database precedents: `keymap.json` and (as of today) `connection-status.json`.

### 1.6 Existing tests that act as the regression net

`ThemeServiceTests` (39 tests: every built-in loads and applies, mode-to-variant, glow tiers, true black on/off/suspend, accent override both directions,
auto mode incl. the subscription test, font precedence, backdrop event, install round-trip and malformed rejection), `ThemeServiceHomeResourcesTests`, `InsightsChartThemeTests`,
`WindowsElevenSkinTests`, `FluentAvaloniaWorkaroundsTests` (popup-freeze guard), `PreferenceIndexTests`. All theme tests join `AvaloniaTestCollection`. **There is no
test that snapshots the full resource set a theme produces**, which is the single most useful missing safety net (see phase 0).

### 1.7 Constraints from project rules that shape the design

- **Input**: every shortcut goes through `IInputService`; screens attach with `ScreenInput.Attach` in their constructors. A layout engine that rebuilds a screen's
  visual tree must keep those registrations (and `focusRoot`) intact. A theme cannot add `KeyDown`/gesture handlers.
- **Focus ring** is an adorner clipped by scroll viewports; custom layouts must not add a second hand-rolled ring.
- **Detaching a control from inside the event it is raising crashes** (the `Dispatcher.UIThread.Post` rule). A live layout swap triggered from a click is exactly that shape.
- **Virtualization**: covers use `VirtualizingVariableWrapPanel`; a generic "Grid/Stack" layout cannot be allowed to host a 2,000-item library.
- **Animation**: keyframes on `RenderTransform` crash; continuous animation must be composition/render-thread; reduced motion zeroes the `PbMotion*` durations (26 files consume them).
- **Fonts**: a multi-type XAML style selector that sets `FontFamily` does not compile; per-type blocks are required (App.axaml already does this).
- **Compiled bindings** (`x:DataType` on every view): any runtime-built UI must be built in code or through a closed control vocabulary; **Avalonia runtime XAML loading must not be the layout format** (it would admit markup extensions, bindings and event handlers into a `.crpck`).

---

## 2. Spec-to-code gap matrix

Legend: **E** exists and is fine, **M** exists and needs modification, **N** new.

| # | Spec item (section 3.1) | State | Evidence and work |
|---|---|---|---|
| 1 | Palette -> semantic -> component layers | **N** (partly M) | Today: one flat token set. Layer C has class-based styles but no roles. Work: resolver + semantic vocabulary + bridge to `Pb*` |
| 2 | Derived defaults for missing states | **M** | Era colors (`EraColors`), accent text derivation (`AdjustForContrast`), glow ring alpha, `PbHeroScrim` already derive. Work: generalize to a deterministic derivation engine with provenance |
| 3 | Families with Light/Dark/OLED/HC variants | **N** | `mode` is per theme; auto mode pairs two keys. No family object. True Black is a global toggle, not a variant |
| 4 | Global / family / variant / component user overrides | **M** (global only) | Global exists (`AccentOverrideHex`, `SelectedFontFamily`, density, glow). Nothing scoped. Needs an override store (decision D2) |
| 5 | Declarative component backgrounds/states/shapes/etc. | **N** | See 1.2 item 4 |
| 6 | Typography roles; semantic icon overrides | **M** / **N** | Font precedence exists (explicit > theme default > Source Serif 4); `PbDisplayFontFamily` (Bebas Neue) is a fixed resource used by 6 files. No role system. Icons: field + cache exist, zero consumers, icon system is FluentIcons `SymbolIcon` + `Path.pbIcon` styles, so a semantic registry means a **new indirection layer over ~1,000 icon uses (not counted; *not verified*)** |
| 7 | Materials (gradients, textures, images, blur, platform) | **M** / **N** | Window backdrop (Mica/Acrylic) exists via `WindowBackdropRequested`. Gradients: hero start/end only. Textures: reader background textures exist (separate system). Per-element blur: **not verified as supportable in Avalonia 12**, needs a spike |
| 8 | Near-unrestricted rearrangement of screens | **N** | Highest risk. See 1.2 and phase 4 |
| 9 | Composable declarative trees, typed bindings, commands | **N** | Only the plugin-settings schema precedent |
| 10 | Declarative keyframes/triggers/sequences | **N** | Only host-owned motion tokens and the Matrix rain composition handler. Supported property set needs a spike |
| 11 | Live switching with state preservation, reduced motion | **M** | Live switching exists and keeps screen state naturally (resources only). Becomes hard only once layouts can change. Rollback on failed apply does **not** exist today (a throwing `ApplyTheme` leaves a half-applied resource set) |
| 12 | High / Balanced / Performance quality tiers | **N** (partly M) | Today: reduced motion, glow tier, Matrix battery throttle, smooth scrolling. No unified tier |
| 13 | `.crpck` import/export | **M** / **N** | Import exists (see 1.4, needs hardening). Export: none (deferred in the 09-16 spec; **OPEN** now) |
| 14 | Theme-aware reader chrome, content overrides | **M** | Three separate reader styling paths (1.2 item 1) |

Success criteria (spec 1.3): all ten themes survive (testable now by golden snapshots), legacy `.crpck` loads (needs v1 adapter test), families (N), a
rearranged screen (N), scoped overrides (N), validate-before-commit and rollback (N), reduced-motion for theme-defined motion (N), `Match App` from live tokens (M).

---

## 3. Architecture decisions I recommend for the plan (MY RECOMMENDATION)

These refine spec section 4; none is a user decision.

1. **Keep `ThemeService` as the public facade and shrink it by extraction**, in this order, each step behavior-preserving: `ContrastMath` (pure),
   `ThemeEraPalette`, `GlowRingBuilder` (already static), `ThemePackageInstaller` (replaces `TryInstallSkin`), `ThemeAutoSwitcher` (auto mode + schedule), then the new
   resolver/applier. The new `ThemeResolver` lives in a **non-Avalonia assembly folder** (pure C#, `Color` as its own tiny struct) so its tests need no `AvaloniaTestCollection`.
   The existing `Paperbunkr.Data` precedent (pure logic with no UI dependency, tests run without Avalonia) applies.
2. **The compatibility bridge is a table, not a rewrite.** A `LegacyPbMap` maps resolved semantic roles to the existing 100 `Pb*` keys; screens migrate role by role later.
   `AppTokens.axaml` stays the startup fallback and gets a generated-vs-hand-written consistency test.
3. **Layout format**: JSON describing a tree of **closed-vocabulary host components** built in code by a `ThemeLayoutRenderer`. **Not** runtime XAML, not
   expressions beyond a finite grammar. Large content components (the cover grid, the reader canvas, the Event Map) are **host-provided "slot" components** that a layout
   places and sizes but does not re-implement, which also keeps virtualization, input registration and the focus ring intact.
4. **User overrides**: one versioned JSON document beside the database (`theme-overrides.json`), following `keymap.json`'s load/atomic-write/set-aside-on-corruption
   conventions, rather than EF columns. It avoids migrations, rollback-chain hazards and the shared dev database being migrated by a worktree (a known issue in this
   project). Decision D2.
5. **Stable keys**: `ActiveThemeKey` keeps meaning "the resolved variant key" so `IThemePlugin.CurrentThemeKey`, `LastLight/DarkThemeKey` and all stored values stay valid; a new
   nullable `ActiveVariant` is added only when needed. Decision D4.
6. **Transactional apply** (spec 10.1) is built **before** anything that can fail interestingly (phase 1b), as it is a prerequisite for safe layout and package work.

---

## 4. Decisions and open questions

Labelled by status. **DECIDED** items are not reopened. **OPEN** items each list a recommendation, the phase it blocks, and what is lost if guessed wrong.

### 4.1 Decided (spec section 3.1) and excluded (3.2): taken as given
Three-layer model, derived defaults, families with variants, four override scopes, component-level declarative styling, typography roles, semantic icons, materials,
declarative layout/motion, live switching, quality tiers, `.crpck` workflow, reader chrome. Excluded: standalone Theme Studio, visual canvas, timeline editor, asset-management
workstation, synchronized canvas/source editor.

### 4.2 Proposed in the spec: this plan adopts them unless you object
Legacy v1 adapter at load time (no archive rewrite); single-variant families for old themes; no silent contrast correction of authored colors (`windows_11` unchanged); package
safety checks; transactional apply; `{ "ref": "..." }` references; `Pb*` bridge.

### 4.3 Open: need your answer (each with my recommendation)

| ID | Question | My recommendation | Blocks |
|---|---|---|---|
| D1 | Are the 10 built-ins single-variant families, or do I author families by pairing (e.g. Default + Daylight)? | Single-variant for all ten in phases 1-2 (zero visual change). Authoring real families (Paperbunkr Dark/Light/OLED) is a **separate, later design pass** once the engine exists | Phase 2 gallery shape |
| D2 | Where do user overrides live: JSON file beside the DB, or `AppSettings` / a new table? | JSON file (see 3.4) | Phase 2 |
| D3 | Precedence order in spec 6.3 (eight levels, with explicit accessibility last) | Accept as proposed | Phase 2 |
| D4 | What does `ActiveThemeKey` mean once variants exist; is `IThemePlugin.CurrentThemeKey` the family or the variant key? | Variant key (what it effectively is now); add `ActiveFamilyKey`/`ActiveVariant` only if a plugin needs them | Phase 1 persistence |
| D5 | Auto light/dark under families: prefer a variant of the active family, then the remembered pair | Accept as proposed; keep `LastLight/DarkThemeKey` as the fallback | Phase 2 |
| D6 | OLED vs True Black: authored OLED variant wins; True Black stays as a legacy transform when no OLED variant exists | Accept; keep the reader auto-suspend exactly as is until phase 6 | Phase 2 |
| D7 | Reader boundary: chrome follows theme roles, book content keeps its own Light/Dark/Sepia/OLED/HC choice, `Match App` reads live tokens, comic page art is never recolored | Accept (the spec notes you did not answer this) | Phase 6 |
| D8 | Package limits (max entries, max uncompressed size, per-asset size and decode caps, allowed asset types) | Entries <= 512, uncompressed <= 64 MB, single asset <= 16 MB, images <= 8192 px per side, fonts TTF/OTF only; **numbers are placeholders to tune**, not researched | Phase 1b |
| D9 | `.crpck` export in v2: what does "export" include (a theme, or theme + your overrides)? | Export the installed theme's folder as-is, and "Export my customizations" as a separate small JSON overlay. Neither needs an editor | Phase 6 |
| D10 | Layout pilot screen and go/no-go | Pilot on **Home** (137-line view, an existing section model and template selector, user-orderable sections). Go/no-go gate before any second screen | Phase 4 |
| D11 | Release sequencing: ship phases individually behind no flag, or behind an "Experimental themes" switch until phase 4 | No switch for phases 1-3 (they are compatible by construction); a switch for phase 4+ | Phase 4 |

---

## 5. Phased implementation plan

Each phase ends in a working app and a defined **exit gate**. Sizes are rough (S = days, M = about a week or two, L = several weeks) and assume one person with
testing; they are my estimates, not measured.

### Phase 0: Baseline and safety net (S) (no behavior change, no user decisions needed)
**Build**
- **Golden resource snapshot test**: for each of the 10 themes, apply it and serialize every `Pb*` color/brush/BoxShadows/CornerRadius/double/font resource to a checked-in JSON golden.
  This is the equivalence oracle for every later phase.
- **Hex-literal lint test**: scans `*.axaml` and the listed C# files against a checked-in allowlist (intentional literals classified from 1.3); fails on new ones.
- **Reader/Match App baseline test**: capture the current `BookThemeBrushes` outputs so the later change is deliberate.
- Classify the 1.3 inventory (intentional vs defect) into a short doc; confirm the **light-theme** state of Library, Reading Lists, Continuity, Event Map, Insights (these are listed in the spec and in the project's "not verified on screen" notes).
- Measure the real theme-switch and reader-enter cost (the spec asks not to claim gains without a baseline).
**Risks**: golden files are brittle if a theme legitimately changes; mitigate by generating them with a one-command updater and reviewing diffs.
**Exit gate**: all of the above green; no production code changed apart from test helpers.

### Phase 1: Extraction, hardened packages, pure resolver, transactional apply (M-L)
**1a. Behavior-preserving extraction** of `ContrastMath`, era palette, glow builder, installer, auto switcher from `ThemeService`. The golden test and the 39 existing tests prove nothing moved.
**1b. Package hardening** (fixes two verified defects): a staged install (extract to a temp folder, validate, then atomically swap, keeping the previous version on any failure);
limits from D8; asset validation; structured diagnostics (spec 13.2 record). New tests: re-install of a bad package leaves the working theme in place, zip bomb, traversal, duplicate keys, missing `theme.json`.
**1c. Pure model and resolver**: immutable `ThemeDefinition v2` + `ResolvedTheme`, `schemaVersion` detection, the v1 adapter (single-variant family), `{ref}` resolution with cycle/type diagnostics,
deterministic derivation with provenance. **No Avalonia types.** The resolver resolves each built-in to a `ResolvedTheme` whose projection through `LegacyPbMap` **equals the golden snapshot**.
**1d. Transactional apply**: snapshot, apply on the dispatcher, restore on failure, then fall back to the protected built-in. Replaces the current "throw and leave half applied".
**Dependencies**: phase 0 (oracle). D4 and D8 answered. **Migration**: none (no DB change; v1 packages load through the adapter).
**Risks**: (1) the extraction touching a heavily used class (mitigation: golden + existing tests, one extraction per commit-sized step); (2) the pure `Color` type duplicating Avalonia's (accept; the bridge converts at the edge).
**Exit gate**: every built-in and every pre-existing `.crpck` resolves and applies identically (golden equality), failed installs and failed applies are recoverable.

### Phase 2: Semantic roles, derivation, scoped overrides, Appearance (M-L)
- Semantic vocabulary (spec 6.1 list) with fallbacks; `LegacyPbMap` complete; **theme-aware** `PbDanger*`, soft colors, status.*, chart/era roles (preserving the intentional special cases: Home sky, seasons, the dark Detail hero).
- `ThemeOverrideStore` (`theme-overrides.json`, versioned, atomic, set-aside on corruption); precedence per D3; per-property reset; keep `AccentOverrideHex` / `SelectedFontFamily` behavior (migrate them *into* the store lazily, never remove the columns).
- Contrast diagnostics extended to semantic roles and states, **report-only** (spec 13.1). Keep the `windows_11` warning, no color change.
- Appearance UI: family/variant picker (extends today's gallery), scoped override editing for a finite set of roles (accent, surface, text, radius), Reset to theme / Reset to global. **Builds on the gallery and inspector shipped today.**
- Auto switching and OLED behavior per D5/D6.
**Dependencies**: phase 1; D1, D2, D3, D5, D6 answered. **Migration**: none required if D2 = JSON. If D2 = EF: one migration (override table or JSON column), with explicit defaults and a rollback test.
**Risks**: precedence bugs (tests: a table-driven matrix over all eight levels); user overrides interacting with True Black/accent override ordering that exists today (keep order: theme -> variant -> overrides -> True Black -> accent), proven by the existing true-black/accent tests, kept green.
**Exit gate**: overrides persist across restart and survive theme switches; legacy visuals unchanged (golden); override files that are corrupt or from a newer version are ignored safely.

### Phase 3: Typography, icons, materials, component skins (L)
- **Typography roles** (display/heading/body/caption/monospace) with the existing font override preserved; `PbDisplayFontFamily` becomes a role. Per-type style blocks (no multi-type selector).
- **Semantic icon registry** as a thin indirection over the current icon system, introduced **incrementally by area** (reader chrome, nav rail first); the existing `GetIcon`/`icons` field is reused or retired, not left dangling.
- **Materials**: gradients and packaged textures first; window backdrop stays as is; **per-element blur needs a spike** (Avalonia 12 support is *not verified*; I would check the Avalonia docs index before promising it) and has a mandatory solid fallback.
- **Component skins**: a closed registry of roles (`button.primary`, `sidebar.item`, `cover.tile`, `scrollbar.thumb`, ...) mapped onto the existing class-based styles, so existing XAML does not change. Scrollbar tokens finally get a consumer **or are explicitly retired**, with a visual check (the earlier pass deferred this because it could not be looked at).
**Dependencies**: phase 2. **Migration**: none. **Risks**: scrollbar `ControlTheme` override on a FluentAvalonia lookless control (breakage risk app-wide; needs on-screen verification); icon indirection touching many files (mitigate with the registry defaulting to today's glyph, so unmapped icons are unchanged); font licensing for bundled theme fonts.
**Exit gate**: one representative control per role renders all states from a theme, switching works live, fallbacks are tested.

### Phase 4: Declarative layout composer, **pilot screen, then gate** (L, high risk)
- `ThemeComponentRegistry` (closed vocabulary), `ThemeLayoutRenderer` (JSON -> control tree in code), typed/versioned bindings and registered commands, responsive constraints and breakpoints, host fallback layout, state capture/restore.
- **Pilot: Home** (D10). Two sample packages arrange it differently without re-implementing logic (the spec's exit gate).
- Layout validator: required roles, duplicate IDs, cycles, depth/child limits, breakpoint overlaps.
- Compatibility with project rules: input registrations stay with the host screen; large components (cover grid, canvas) are host slots; swaps are deferred a dispatcher tick; the focus ring adorner is untouched.
- **Go/no-go gate**: before a second screen is attempted, review cost, bug count and whether the closed vocabulary is expressive enough. **Library (2,047 lines, 5,177-line VM) is deliberately not the pilot.**
**Dependencies**: phases 1-3; D10, D11. **Migration**: layout patches (spec 8.6) in the override store, stable-ID based; conflict UX is **OPEN** and stays minimal (keep the last working layout, flag the conflict).
**Risks (the highest in the plan)**: scope growth into a general UI framework; layout swap crashing on event routing; keyboard/focus regressions; performance of large lists; screen-reader order changes; the binding grammar drifting into scripting. Mitigations: closed vocabulary, finite grammar, per-screen contract documents, pilot gate, `RealWindowKeyboardTests` extended for any ported screen.
**Exit gate**: spec 4 exit gate, plus keyboard, focus and screen-reader checks on the pilot, plus fallback tested by deliberately breaking a layout.

### Phase 5: Declarative motion and quality tiers (M)
- Spike first to fix the **supported property set** (opacity, translation via supported mechanisms, color transitions); no `RenderTransform` keyframes; continuous effects only through the composition handler.
- Triggers (theme change, hover, focus, selection, navigation, load, reader chrome visibility), presets, easing; reduced motion stops decorative loops and shortens others (reuses the `PbMotion*` zeroing).
- Quality tiers High/Balanced/Performance as one setting that gates materials and effects; Matrix rain's battery policy folds in rather than being duplicated.
**Dependencies**: phase 3 (materials) and 4 (if triggers target layout components). **Risks**: freezes (the existing popup-freeze lesson: no resource mutation inside popup/platform callbacks); animation cost on low-end GPUs (measure against the phase 0 baseline).
**Exit gate**: no UI stall in tests that open popups during an animated switch; reduced motion verified.

### Phase 6: Reader integration and authoring path (M)
- One reader styling model: PDF brushes and comic chrome literals move onto reader roles (`reader.chrome.*`, `reader.background.*`); `Match App` reads live resolved tokens (D7); `BookTheme` enum untouched (append-only), only its `MatchAppSkin` behavior changes.
- True Black reader suspension gets a **targeted** resource update instead of a full theme reload (only if phase 0 measurements justify it).
- No-Studio authoring: JSON schema files, a sample theme set, precise error messages with JSON paths, an in-app "Import theme" result list, the export forms from D9, and an **optional** validator only if cheap (shared code with the loader).
**Dependencies**: phases 2-4. **Risks**: changing how book content looks for existing users (mitigate: a migration test that every stored `BookReaderTheme` renders as before; default `Match App` for the default theme should produce visually the same dark reading surface or the change is called out).
**Exit gate**: spec 6 exit gate.

### Phase 7: Extension attachment points only (S)
- A **component-provider contract** and fallback rendering, proven with a simulated provider. Reuse the existing plugin host and the `PluginSettingsSchema` precedent; add no sandbox, SDK, protocol or process host. Document `.pbpck` requirements for the separate project.
**Exit gate**: nothing executable was added to the theme subsystem.

---

## 6. Migration requirements (consolidated)

| Area | Requirement | Phase |
|---|---|---|
| Theme packages | Missing `schemaVersion` = v1, adapted in memory, archive never rewritten | 1 |
| Existing keys | All 10 built-in keys and every installed key stay valid; `ActiveThemeKey` semantics unchanged (D4) | 1 |
| DB | None for phases 0-1. Phase 2: none if overrides are a JSON file (D2); otherwise one explicit-default migration with a rollback test and a check of the Designer snapshot | 2 |
| Settings | `AccentOverrideHex`, `SelectedFontFamily`, density, glow, reduced motion stay authoritative until their replacements are proven; migrate-forward, never delete columns in this project | 2 |
| Enums | `BookTheme` append-only; new reader behavior added through roles, not enum values | 6 |
| Startup | `AppTokens.axaml` stays the fallback; a test asserts it matches the `default` golden | 0-2 |
| Plugin API | `IThemePlugin.CurrentThemeKey` unchanged; any new member waits for a real consumer | 1 |
| Files | `theme-overrides.json`: versioned, atomic writes, corrupt or newer file set aside (the `keymap.json` pattern) | 2 |
| Shared dev DB | Worktrees share the dev database; any schema change in a worktree migrates it. Prefer no schema change | all |

## 7. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Layout engine grows into a UI framework | High | Very high | Closed vocabulary, finite binding grammar, Home pilot, go/no-go gate, host slots for big components |
| Regression in the 10 themes during extraction | Medium | High | Phase 0 golden snapshots, one extraction per step |
| Popup/platform-callback freeze reintroduced | Low-Medium | High | Keep apply deferred; keep `FluentAvaloniaWorkaroundsTests` and the subscription test; add a test that opens a popup during a switch |
| Hot swap of layouts crashes on event routing | Medium | High | Post swaps one dispatcher tick; never detach inside the raising event |
| Keyboard/focus/input regressions | Medium | High | Host owns `ScreenInput` registrations; extend `RealWindowKeyboardTests` |
| Large-library performance in custom layouts | Medium | High | Host slots (existing virtualizing panels), measured against phase 0 |
| Scrollbar/FluentAvalonia control-theme breakage | Medium | Medium | Isolate; visual verification; allowed to be retired |
| Per-element blur unavailable or slow | Medium | Low-Medium | Spike first; mandatory solid fallback |
| Override precedence bugs | Medium | Medium | Table-driven matrix test over all 8 levels |
| Reader look changes for existing users | Medium | Medium | Migration test per `BookTheme`; changes called out |
| Package abuse (bombs, huge assets) | Low | Medium | Phase 1b limits and staged install |
| Untested on-screen behavior accumulates | High | Medium | Each phase carries a short manual checklist; this project already has many "not verified on screen" items |

## 8. Test strategy by phase

- **0**: golden snapshots (10 themes), hex-lint, Match App baseline, light-theme visual checklist, timings.
- **1**: pure resolver unit tests (no Avalonia): inheritance, refs, cycles, type errors, derivation determinism, provenance; legacy equality with golden; installer: staged swap, failure keeps previous, limits, traversal, bomb; transactional apply: forced failure restores; the existing 39 theme tests.
- **2**: override precedence matrix, persistence and corruption, interplay with true black / accent override / auto mode, contrast report-only (no mutation), Appearance view-model tests, `PreferenceIndexTests` anchors.
- **3**: font role fallback, missing font, icon fallback, material fallback, one control per role per state (headless render in two themes), scrollbar visual checklist.
- **4**: validator suite (duplicate IDs, cycles, depth), renderer builds the pilot from two packages, breakpoint behavior, rollback on a broken layout, state capture/restore, keyboard walk, focus ring, screen-reader order, large-list performance.
- **5**: animation property validation, reduced-motion, quality-tier fallbacks, popup-during-animation test, no RenderTransform keyframes.
- **6**: `Match App` reads live tokens, every `BookTheme` unchanged-or-called-out, reader enter/exit state, comic art not recolored.
- **7**: absent provider renders fallback, nothing executes.
- **Every phase**: the Appearance and Connections view-model suites (28 new tests from today), `FluentAvaloniaWorkaroundsTests`, and a manual checklist for custom renderers (Event Map, Insights, Matrix).

## 9. What I recommend doing first (when you approve a phase)

1. **Phase 0 in full** (small, safe, and everything later depends on its golden snapshots and lint).
2. **Phase 1b** (package hardening) can go in parallel or first, because it fixes a real data-loss bug independent of v2.
3. Then your decisions D1 to D6 and D8, and phase 1c to 2.

## 10. Explicitly not planned

A Theme Studio or any visual editor; a plugin runtime, SDK, sandbox or `.pbpck` handling; auto-installing or executing anything from a `.crpck`; remote asset loading; silent contrast
correction of authored themes; changing reader navigation, zoom, progress or decoding; renaming existing `Pb*` XAML in bulk.

## 11. As built (2026-10-13)

Decisions D1 to D11 were accepted as recommended and phases 1 to 7 were implemented in one pass. Nothing is committed. What follows records
what exists, where it differs from the plan above, and what has not been checked.

### 11.1 Where things are

| Area | Code |
| --- | --- |
| Pure model, vocabulary, resolver, v1 adapter | `src/Paperbunkr.App/Theming/` (`ThemeDocument`, `ThemeVocabulary`, `ThemeResolver`, `LegacyThemeAdapter`, `ThemeColorMath`, `ThemeValue`, `UserThemeOverrides`, `ThemeComponentRegistry`, `ThemeIconRegistry`) |
| Catalog, installer, override store, transactional applier, materials, skins, state styles | `src/Paperbunkr.App/Theming/Runtime/` |
| Layout model, validator, renderer, source, extension registry | `src/Paperbunkr.App/Theming/Layout/`, `Controls/ThemeLayoutHost.cs` |
| Motion definitions and runtime | `src/Paperbunkr.App/Theming/Motion/` |
| Service surface | `Services/ThemeService.cs` plus `.Engine`, `.Overrides`, `.Skins`, `.Layouts`, `.Export` partials |
| Home pilot | `Views/HomeScreen.axaml(.cs)`, `Views/Home/HomeMasthead`, `HomeSections`, `HomeLayoutParts.cs` (the contract) |
| Appearance | `ViewModels/ThemeCustomizationViewModel.cs`, `PreferencesScreenViewModel.Appearance.cs`, `Views/Preferences/AppearanceSection.axaml` |
| Authoring | `docs/themes/README.md`, `docs/themes/theme.schema.json`, `docs/themes/samples/harbor`, `docs/themes/samples/gazette` |

### 11.2 Deviations from the plan

- **Phase 0 was folded into phase 1.** The golden resource snapshots (14 files, all 10 built-in themes plus True Black and accent-override
  cases) were captured from the old code path before it was removed, and are the equivalence proof. The hardcoded-colour inventory became
  a lint test with a recorded baseline rather than a classified allowlist: it stops new literals, it does not judge the existing ones.
  **No timing baseline was taken**, so no performance claim is made for theme switching or reader entry.
- **No feature switch for phase 4 onward (D11).** The plan said "behind a switch". What shipped is narrower: only theme *layouts* are
  behind a user setting (Appearance > Customize > Theme layouts, off by default). Motion, materials, skins and typography apply whenever a
  theme declares them, because every built-in theme declares none and so is unaffected.
- **State skins are styles created once in code, not resource-keyed XAML setters.** Two framework behaviours found by test forced this: a
  style setter bound to a missing `DynamicResource` blanks the value instead of falling through, and removing a `Style` at runtime does
  not un-apply values already pushed into a control's template parts. `ThemeStateStyleHost` keeps persistent styles bound to mutable
  holders that fall back to the framework's own brushes.
- **Packaged fonts are validated but not loaded.** `typography` names fonts installed on the machine; a missing family falls back and is
  reported. Loading `.ttf`/`.otf` out of a package is not built.
- **No per-element blur.** An `acrylic` material draws as its tint. The window-level Mica/Acrylic backdrop is unchanged.
- **Icons: nav rail only.** The semantic registry and `ThemedIcon` exist and the ten navigation-rail icons use them. Reader chrome and
  the rest of the app still use `SymbolIcon` directly.
- **Component roles: five.** `button.primary`, `button.secondary`, `button.ghost`, `chip`, `panel.floating`. Inputs, list rows, tabs and
  cards are not skinnable roles yet.
- **Motion triggers: appear only.** A layout node plays a motion when it is built. Hover, focus, selection, navigation and reader-chrome
  triggers are parsed as names but nothing fires them. There is no theme-change crossfade.
- **Reader (phase 6):** `Match app` now reads the live `reader.background.page` / `reader.background.text` roles, and the PDF reader's
  three hardcoded brushes became `PbReader*` dynamic resources. **This changes how `Match app` looks for existing users**: on the default
  theme the page goes from the old fixed `#14161B` to the theme's own surface. The comic reader's translucent white/black washes
  (`ReaderChrome.axaml`, `ReaderScreen.axaml`) were left as literals on purpose: they sit over page art and over a bar already tinted
  by the book's own theme, so an app-theme role would be wrong there. The True Black reader suspend was not changed; the applier already
  writes only the resources that differ, and without a measurement there was no case for more.
- **Export (D9):** as planned, plus **Import customizations**, since an export nobody can read back is not useful.
- **Phase 7:** `IThemeComponentProvider` + `ThemeExtensionRegistry` and the `Extension` layout node with a fallback child. Proven with a
  fake provider in tests (absent, registered, declining, throwing). Nothing registers a real provider, and the plugin host was not touched.
  `.pbpck` requirements are written up in `docs/themes/README.md`.
- **Layout pilot go/no-go (D10): not decided.** Home works through two different sample layouts in headless tests, with fallback on
  every failure path. Whether to extend contracts to other screens should wait until Home has been used on a real screen.

### 11.3 Bugs found and fixed while testing

- A golden test failed only in large runs: the glow tier is process-wide state and leaked from another test.
- The installer refused a package for any parse finding; it now refuses only for invalid JSON, an unsupported schema, an unsafe package,
  a limit, or a reference cycle.
- `Home.Search` built its Enter key as a code-created `KeyBinding` with a data binding, which throws ("Cannot find a DataContext"), so
  every layout using the bare search fell back to the built-in layout. It is a plain key handler now.
- A user layout patch set from code (`["width"] = 280`) was rejected as "not a number", because a CLR-backed JSON int does not read as a
  double. Patches are re-read from their text before validation.
- The Harbor sample's light variant reused the dark variants' success green at 2.54:1; the contrast report caught it.

### 11.4 Verification

- Tests added: `ThemeGoldenResourceTests`, `Theming/ThemeResolverTests`, `ThemePackageInstallerTests`, `ThemeApplyTransactionTests`,
  `ThemeHardcodedColorLintTests`, `ThemeOverrideTests`, `ThemeSkinTests`, `ThemeLayoutValidatorTests`, `ThemeLayoutHostTests`,
  `ThemeAuthoringTests`; `ReaderChromeTintTests` extended. All headless.
- **Not seen on a real screen:** every new visual. Variants in the gallery, the Customize rows, state skins, materials, typography
  roles, the themed nav icons, scrollbar metrics, Home through a theme layout, motion, the reworked `Match app`, and the PDF error box.
- **Not tested at all:** keyboard and focus walk of Home inside a theme layout in the real `MainWindow`, screen-reader order, large-list
  scrolling performance inside a layout, a theme switch while a reader is open, and installing a package through the real file picker.
