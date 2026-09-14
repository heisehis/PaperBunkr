# Reader chrome redesign — comic/manga Reader

Date: 2026-09-14
Status: Approved for implementation (via visual companion brainstorming session)

## Scope

Comic/manga paged Reader only ([ReaderScreen.axaml](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml), [ReaderScreen.axaml.cs](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml.cs), [ReaderScreenViewModel.cs](../../../src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs)) — the Books EPUB/PDF reader (`BookReaderScreen`/`PdfPageReaderScreen`, sharing `ReaderChrome.axaml`) is a structurally different, already-twice-redesigned subsystem and is explicitly out of scope.

Unlike the Detail screens ([2026-09-13-detail-screens-redesign-design.md](2026-09-13-detail-screens-redesign-design.md)) and Metadata editors ([2026-09-14-metadata-editors-redesign-design.md](2026-09-14-metadata-editors-redesign-design.md)) redesigns, the Reader was **not** stuck on the old `groupBox`/`groupHeader` chrome-bar language — it already went through its own dedicated overhaul ([2026-08-25-reader-chrome-design.md](2026-08-25-reader-chrome-design.md), [2026-08-27-reader-chrome-icon-pass-design.md](2026-08-27-reader-chrome-icon-pass-design.md)): floating corner clusters on `Pb*` tokens, vector `fi:SymbolIcon` glyphs, no hardcoded hex. So this pass is not a chrome-bar→card migration; it's a full teardown driven by the user wanting a fresh look and fixing accumulated inconsistencies, decided via an extended visual-companion session (mockups pushed to a local browser tab, user reacted by click + typed feedback + real screenshots of the running app).

**User's own framing, verbatim:** "genuinely all of it" — a full rebuild, not an area-by-area patch. Three paradigm options were presented (refined floating clusters / docked top-bottom bars matching the Books reader / minimal single-strip-plus-panel); the user picked **refined floating clusters** — same interaction model, full visual rebuild. This preserves the deliberate 2026-08-25 decision to unify windowed/fullscreen into one chrome system (reverting to docked bars would have undone that).

## 1. Visual language — frosted glass, not opaque cards

Every corner cluster (`Border Classes="floatingPanel chromeCluster"`) and the drawer move from their current `PbSurface1/2/3Brush` opaque-ish backgrounds to a genuinely translucent, blurred treatment: `rgba`-style low-alpha background + backdrop blur, thin low-alpha border, no solid card fill. This is a deliberate departure from the Detail/Metadata redesigns' opaque `Border.card` — those sit on the app's own flat background; Reader chrome floats over arbitrary, unpredictable page art and needs to stay legible over any of it, which an opaque card can't guarantee as gracefully as a blur does.

Concretely: `Background` becomes a semi-transparent `Pb*` brush (existing `Pb*Brush` tokens at reduced opacity, not new hex), and Avalonia's blur-behind (`Border.Effect` with a blur, or platform-appropriate translucency such as the acrylic material already used elsewhere in the app) is applied to the four corner clusters, the drawer, and the thumbnail rail's tiles panel. `BoxShadow` stays for elevation separation from the page beneath.

Applies uniformly to: Navigate cluster, Actions cluster, View cluster, Page-turn cluster, the drawer, and the thumbnail rail's tile sidebar. No structural change to what each cluster contains beyond what's specified in the sections below — this section is the "everything gets rebuilt" visual foundation the rest of the spec sits on.

## 2. Reading-mode picker — flyout list → segmented pills, with renamed labels

Current: `Button.Flyout` opening a `Flyout Placement="Top"` containing 7 vertically-stacked `Button Classes="drawerRow"` items ([ReaderScreen.axaml:409-420](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L409-L420)). Replaced with a horizontal segmented-pill row — same pattern as the segmented Weight picker just shipped in Metadata editors (`Button Classes="segItem"`/`.active`, mirroring [DetailTabs.axaml:249-277](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L249-L277) for shape and [DetailTabs.axaml:74-87](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L74-L87) for the `segItem`/`.active` style block, mirrored locally into `ReaderScreen.axaml`'s own `UserControl.Styles` per this codebase's established "styles don't share across files" convention).

`ReadingMode` ([ReadingMode.cs:28-37](../../../src/Paperbunkr.Data/Entities/ReadingMode.cs#L28-L37)) has 7 values. `ReaderScreenViewModel.UpdateReadingModeState` ([ReaderScreenViewModel.cs:1119-1137](../../../src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs#L1119-L1137)) builds `ReadingModeLabel` from a `switch` — that switch's string literals are renamed (the trailing `" ▾"` chevron suffix is dropped from all of them; a pill row has no dropdown-chevron affordance to label):

| Enum value | Old label | New label |
|---|---|---|
| `LeftToRight` | "Left to Right ▾" | "Left to Right" |
| `RightToLeft` | "Right to Left ▾" | "Right to Left" |
| `TopToBottom` | "Vertical ▾" | "Top to Bottom" |
| `VerticalContinuous` | "Vertical (Continuous) ▾" | "Longstrip (gapped)" |
| `HorizontalContinuous` | "Horizontal (Continuous) ▾" | "Horizontal Long Strip" |
| `HorizontalContinuousRightToLeft` | "Horizontal RTL (Continuous) ▾" | "Horizontal Long Strip (RTL)" |
| `Webtoon` | "Webtoon ▾" | "Long Strip" |

`ReadingModeLabel`'s own doc/usage stays as the human-readable string for anywhere it's still consumed as text (e.g. a collapsed-state summary); the pill row itself binds each segment's active state directly off `EffectiveReadingMode` via `ObjectConverters.Equal` (mirroring `TierClass`-style checks already used in `DetailTabs.axaml`), not off the label string, so the rename is display-only and touches no comparison logic.

## 3. Fit-mode picker — same pill treatment, plus a new `FitModeLabel`

Same flyout→segmented-pill conversion as §2, applied to both places the fit-mode control currently appears — the primary View cluster ([ReaderScreen.axaml:429-441](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L429-L441)) and its narrow-window fold-in duplicate inside the drawer ([ReaderScreen.axaml:537-557](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L537-L557), shown once `IsViewClusterCollapsed` is true below ~720px window width). Both must get the pill treatment, not just the primary one — they're the same control duplicated for the width breakpoint, not two different controls.

**New defect found while grounding this section, not previously visible:** unlike `ReadingModeLabel`, `FitMode` has no friendly-label property today — both flyout-adjacent `TextBlock`s bind the raw enum directly (`Text="{Binding FitMode}"`), so the display value is whatever `ImageFitMode.ToString()` produces: "Original", **"Fit"**, "FitWidth", "FitHeight", "BestFit" ([ImageFitMode.cs:12-19](../../../src/Paperbunkr.Data/Entities/ImageFitMode.cs#L12-L19)) — not the nicer "Fit All"/"Best Fit" captions the flyout's own buttons already use. This spec adds a `FitModeLabel` computed property to `ReaderScreenViewModel`, parallel to `ReadingModeLabel`, both places' `TextBlock` rebound to it:

| Enum value | Flyout button said | New `FitModeLabel` |
|---|---|---|
| `Original` | "Original" | "Original" |
| `Fit` | "Fit All" | **"Fit Page"** (renamed per this session) |
| `FitWidth` | "Fit Width" | "Fit Width" |
| `FitHeight` | "Fit Height" | "Fit Height" |
| `BestFit` | "Best Fit" | "Best Fit" |

## 4. Zoom — one unified control, paged and continuous alike

Current: paged mode shows stepper buttons + a percent-readout pill opening a preset `Flyout` ([ReaderScreen.axaml:444-462](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L444-L462)); continuous mode shows the *same* stepper+pill **plus** an inline `Slider` bound to `ZoomLevel` ([ReaderScreen.axaml:467-468](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L467-L468)), so the continuous-mode row is wider and busier for no functional reason — both modes zoom the same `ZoomLevel` property ([ReaderScreenViewModel.cs:368](../../../src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs#L368), clamped `0.5–4.0` in continuous vs `1.0–MaxZoom` in paged per lines 375-376).

Replaced with a single compact pill (`🔍 {ZoomLevel:P0}`) identical in both modes. Clicking opens one popover containing: a `Slider` (same `Minimum`/`Maximum` clamp logic the ViewModel already applies per-mode, unchanged), the five preset buttons (`SetZoom100/125/150/200/400Command`, unchanged), and the existing `ZoomInCommand`/`ZoomOutCommand` as +/− buttons framing the slider. This removes the mode-conditional branching from the View cluster's XAML shape (`IsVisible="{Binding IsContinuousMode}"` on the slider goes away entirely) in favor of one control tree used unconditionally — the *popover's contents* don't change between modes, only the numbers the existing clamp logic already produces. Applies to both the primary View cluster and its drawer fold-in duplicate, same as §3.

No `ZoomLevel`/clamp/step logic changes — this is a control-composition change only, all existing commands reused as-is.

## 5. Reader Tools drawer — rebuilt arrangement (structure and content unchanged)

Same four sections (PAGE, ADJUST, TRANSITION, BOOKMARKS,[ReaderScreen.axaml:536-...](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L536)) — this is an arrangement/visual rework, not a change to what's in the drawer or what it writes to.

- **PAGE** becomes a 2×2 icon-toggle grid: Rotate CW, Rotate CCW, Auto-rotate landscape, Double-page spread — each an icon+label toggle cell instead of today's mixed icon-button-pair + two separate `Button Classes="drawerRow"` toggle rows. Auto-scroll (with its speed sub-control) doesn't fit a plain toggle cell — it stays its own compact row directly below the grid, unchanged in behavior.
- **ADJUST** (Brightness/Contrast/Saturation/Gamma sliders + Reset) collapses behind a `▸ Adjust` expander, collapsed by default — used less often than PAGE/BOOKMARKS per the session's own reasoning, not worth permanent vertical space.
- **TRANSITION** becomes a single compact inline row ("Transition: Crossfade ▾") instead of its own full section heading — it's one control, doesn't need section weight.
- **BOOKMARKS** is pinned as an always-visible footer (outside the `ScrollViewer` that the other three sections scroll within) rather than scrolling away with everything else — it's the section read most often mid-session.

The drawer's existing fixed-header-outside-`ScrollViewer` structure ([ReaderScreen.axaml:524-535](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L524-L535), itself a prior on-screen-verified bug fix — close button used to sit under the scrollbar track) is preserved; BOOKMARKS joins the header as a second fixed (non-scrolling) region, PAGE/ADJUST/TRANSITION are what scrolls.

## 6. Thumbnail rail — demoted to opt-in, frosted restyle

**Finding from this session, confirmed against the file:** the rail's own doc comment ([ReaderScreen.axaml:23-25](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L23-L25)) already states the page-turn cluster's dot-strip scrubber shows "the exact same data the rail shows" via hover-tooltip ([ReaderScreen.axaml:484-488](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L484-L488)) — both use the identical `SelectThumbnailCommand` for click-to-jump. The dot-strip's tooltip is cover-image-only ([ReaderScreen.axaml:500-505](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L500-L505)); the rail's tiles additionally carry a bookmark ribbon, page-type badge, rotation indicator, and spread-position carets that the tooltip doesn't replicate — so the two aren't fully redundant, but they overlap enough that keeping both always-on is real duplication.

Resolution (user-selected over full retirement): **keep the rail, drop its always-there left-edge hover-trigger** ([`RailEdgeTrigger`, ReaderScreen.axaml:179-180](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L179-L180)). It becomes opt-in, opened only via a new icon button placed in the **Page-turn cluster** (contextually paired with the dot-strip it complements, not the Actions cluster's session-level actions like bookmark/fullscreen). `OnRailHoverEntered`/`OnRailHoverExited` code-behind handlers ([ReaderScreen.axaml.cs](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml.cs)) and the 14px edge-trigger `Border` are removed; the rail's open/closed state becomes an explicit toggle (new `IsRailOpen`-shaped state, code-behind or ViewModel — implementation-time choice, matching whichever of `ShowChrome`/`IsDrawerOpen`'s existing patterns fits, since the rail currently has no VM-side visibility state at all per the original chrome-audit's own finding that it's "code-behind hover state only, not in the VM").

Visually, the rail's tile sidebar and its tiles get the same frosted-glass treatment as §1 (translucent blurred background replacing today's opaque `PbSurface2Brush`), and the active/current-page tile's selection border becomes the accent-color ring already used elsewhere (`Border.thumb.selected`, [ReaderScreen.axaml:39-41](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L39-L41) — brush swap only, class/logic unchanged).

## Explicitly out of scope

- The Books EPUB/PDF reader (`ReaderChrome.axaml`, `BookReaderScreen`, `PdfPageReaderScreen`) — separate subsystem, already redesigned twice this cycle.
- `PageCanvas` itself (page rendering/pan/zoom math, gestures, transitions, adjustments) — chrome-only pass, per the original 2026-08-25 spec's own scope boundary, unchanged here.
- Perf overlay (dev-only, `Ctrl+Shift+P`), error panel, chapter-transition overlay — functional/debug surfaces, not flagged by the user, no visual complaint raised.
- Any `ZoomLevel`/fit/rotation/reading-mode *logic* — every rename and control-composition change in §§2-4 is presentation-layer only; no clamp, no command, no persistence behavior changes.
- Bookmark data model, transition-style options, adjust-slider ranges — drawer §5 rearranges, doesn't add/remove capability.
- The magnifier/loupe overlay — explicitly declined previously (`docs/Paperbunkr-Roadmap.md:223`), not reopened here.

## Testing

- No ViewModel logic changes for §§1, 2 (label-only), 5, 6 (visual/arrangement only) — existing `ReaderScreenViewModelTests`-equivalent coverage should pass unchanged.
- §3 (`FitModeLabel` addition) and §6 (new rail-open state) are the only genuinely new bindable surface — add focused unit tests: `FitModeLabel` returns the right string for each `ImageFitMode` value (5 cases, including the `Fit`→"Fit Page" rename); rail-open state toggles correctly and defaults closed.
- On-screen verification required for every visual change here (frosted-glass legibility over varied page art in both light/dark skin, pill-row wrapping at narrow window widths, zoom popover in both paged and continuous mode, drawer's collapsed-Adjust expander, rail's new open/close affordance replacing the old hover-trigger) — no automated test substitutes for this. Same shared-working-tree caveat as prior sessions: check concurrent-session activity before launching the app.
