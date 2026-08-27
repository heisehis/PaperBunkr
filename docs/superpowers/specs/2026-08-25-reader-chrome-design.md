# Reader Chrome

**Status:** Design phase, not yet planned/implemented.
**Sub-project 6 of 7** in the full UI rework (see [Design Language Foundation](2026-08-24-design-language-foundation-design.md) for the full phase breakdown). Phases 1–2 are implemented and verified. Phase 3 (Home screen) is implemented but still mid-iteration with the user, set aside for now. Phases 4 (Library browsing) and 5 (Detail screen) have **not** been done — this phase was deliberately taken out of order at the user's request.

## Background

The Reader screen's chrome (`src/Paperbunkr.App/Views/ReaderScreen.axaml`, `ReaderScreen.axaml.cs`,
`ViewModels/ReaderScreenViewModel.cs`) has never been touched by this UI rework — confirmed by grep,
zero references to `PosterTile`/`FloatingPanel`/`pbChip`/`PbTextHero`/`PbTextHeading`/`Icons.axaml`
vector resources/Phase 2 motion tokens anywhere in the Reader files. It's styled with a bespoke,
hand-rolled local palette declared in `ReaderScreen.axaml`'s own `UserControl.Resources`
(`ReaderBgBrush`, `ReaderChromeBrush`, `ReaderRailBrush`, `ReaderBorderBrush`, `ReaderTextBrush`,
`ReaderTextMutedBrush`, `ReaderTrackBrush`, `ReaderOverlayBrush` — all hardcoded hex, not
`Dynamic`/`StaticResource` app tokens), local `Style` selectors (`Button.toolPill`,
`Button.toolIcon`, `Border.thumb`, `Border.scrubberThumb`), and raster PNG icons via
`Border.OpacityMask` for the bottom-bar prev/next buttons (`Skip_Back.png`/`Skip_Forward.png`) — the
pre-Phase-1 icon technique.

Every control this phase touches is **already-shipped functionality** from a dozen prior specs
(reading modes, fit/zoom, rotation, double-page spread, auto-scroll, page transitions, remappable
shortcuts, bookmarks, chapter-transition feedback, live brightness/contrast/saturation/gamma
adjustment). **This phase is a restyle and rearrangement of an existing, working control surface —
not new reader functionality**, and no ViewModel command/behavior changes are in scope beyond what's
needed to wire the new chrome shape (see Scope).

The current shape is a strict 3-row `Grid` (top toolbar / body / bottom scrubber) with a fixed 96px
thumbnail rail, and it runs **two different chrome systems** depending on window state: always-docked
in windowed mode, versus a separate pair of auto-hiding floating overlays that only exist in
fullscreen. The top toolbar alone crams ~12 distinct control groups into one row. Both of these were
identified as the real design risk for this phase (not any individual control's look) and became the
subject of an extended brainstorm — see [Approved direction](#approved-direction) below.

## Full current control inventory

Preserved here so nothing gets silently dropped during implementation:

- **Navigate:** back/breadcrumb button, current page number ("Page 14 / 32")
- **View:** reading-mode picker (LTR / RTL / Vertical-continuous / Horizontal-continuous /
  Horizontal-RTL-continuous / Webtoon), fit-mode picker (**hidden** in any continuous mode), zoom
  −/percentage-flyout/+ (a slider additionally shown **only** in continuous mode)
- **Page:** rotate CW, rotate CCW, auto-rotate toggle, and **exactly one of** double-page toggle
  (paged modes) **or** auto-scroll toggle+speed-slider (continuous modes) — these two are mutually
  exclusive by mode today and must stay that way
- **Adjust:** brightness/contrast/saturation/gamma sliders + reset (currently one flyout)
- **Transition:** page-transition-style picker (None / Slide / Crossfade)
- **Bookmarks:** toggle-bookmark-on-current-page (quick action) + a scrollable list with inline
  rename/delete + prev-bookmark/next-bookmark navigation
- **Fullscreen:** toggle
- **Page-turn:** prev-chapter, prev-page, reading-progress bar, next-page, next-chapter (today's
  bottom bar)
- **Thumbnail rail:** fixed-width vertical strip of page thumbnails (bookmark-ribbon marker,
  page-type badge, rotation indicator, right-click context menu for page-type/rotate, click-to-jump)
- **On-canvas overlays (not toolbar controls, unaffected by this redesign's structural change):**
  error card, chapter-transition "Loading…" card, chapter-transition from/to info card

## Scope

**In scope:**
- Replace the top-toolbar-plus-bottom-bar layout with the corner-cluster + drawer system described
  below, applied identically in windowed and fullscreen (retiring the current two-systems split).
- Re-skin every control with `Pb*` design tokens (colors, radii, `PbTextCaption`/`PbTextHeading`
  typography where applicable) in place of the local hardcoded-hex resources and styles.
- Convert the bottom-bar prev/next raster icons (and any other reader icon this phase newly surfaces
  in a cluster) to vector `Icons.axaml` resources, following the same audit-then-convert,
  one-icon-per-action discipline every prior icon-conversion phase used — grep every other consumer
  of the same PNG first, record the mapping in `icon-mapping.md`.
- Restyle the thumbnail rail's tiles/borders/badges with `Pb*` tokens (kept as a persistent,
  always-visible vertical strip — see [Thumbnail rail](#thumbnail-rail-stays-persistent) below for
  why it's excluded from the auto-hide behavior).
- Restyle the on-canvas overlay cards (error, chapter-transition) with `Pb*` tokens and
  `PbMotionFast`/`PbMotionEase` in place of their local hardcoded `DoubleTransition`.
- Wire the new idle-fade/reappear behavior (windowed **and** fullscreen) using the same
  cursor-idle-notification mechanism `ReaderScreen.axaml.cs` already has for fullscreen today.
- **Finish the keyboard-shortcut work already started, not deferred as untouched.** Verified directly
  (not guessed) that the remapping *mechanism* is solid — `KeyboardCommandRegistry` has grown to 26
  commands, every `PageCanvas` gesture property is genuinely bound in XAML, `KeyBindingService`
  persists real `KeyGesture`s with safe fallback, Preferences already has its 3-section remap UI. But
  three concrete, live gaps sit squarely in reader-chrome territory:
  - **Shortcut hints are hardcoded strings, not bound to the actual current gesture** — `ReaderScreen.axaml`
    has exactly 4 `ToolTip.Tip` hints (`"Rotate 90° (R)"`, `"Rotate -90° (Shift+R)"`, `"Auto-scroll (S)"`,
    `"Fullscreen (F)"`), and none of them update when the user remaps that action in Preferences — the
    tooltip silently lies after a remap. Fix: bind hint text to `KeyBindingService`'s live gesture for
    that command, not a literal string.
  - **Hint coverage is inconsistent** — those are the *only* 4 controls with any hint at all; zoom
    in/out, the fit-mode picker, page-turn, and bookmark-nav show none. Every cluster/drawer control
    that has a real keyboard shortcut gets a live-bound hint as part of this restyle, not just the 4
    that happened to have one already.
  - **Keyboard-layout import/export doesn't exist** — confirmed via grep, zero `ImportKeyBinding`/
    `ExportKeyBinding` hits anywhere in `src/`, and `KeyBindingService` only exposes `GetKey`/`SetKey`/
    `GetAllBindings`. `docs/ce-feature-inventory.md`'s "not independently re-verified" phrasing
    undersold this — it was never built. Add it to the Preferences Keyboard Shortcuts section (not
    Reader chrome itself, since that's where the rest of the remap UI already lives) — a small,
    bounded addition, not a new subsystem.

**Out of scope (deferred):**
- Any change to the page-render/decode engine (`PageCanvas.axaml`/`.cs`) — explicitly excluded per
  the design doc's own Phase 6 scope line.
- Any new reader *functionality* beyond the three keyboard-shortcut gaps above — every other control
  this phase touches already exists as-is; if implementation surfaces a further gap, it gets flagged,
  not silently added.
- A capture-any-key remap picker (replacing the curated dropdown) — already explicitly scoped out by
  the original remappable-shortcuts spec ("23 curated entries is still manageable as a dropdown"),
  and nothing here reopens that call.
- Phases 4/5 (Library, Detail) — not touched by this phase, still pending in the overall rollout.

## Approved direction

Explored six distinct layouts before landing here (session-only mockups, not committed to the repo):
a flat restyle-in-place, a grouped-bar-with-overflow-menu, a floating capsule pair, a vertical
side-dock, independent corner clusters, and an expandable side drawer. The approved design is a
**hybrid of three of those**: corner clusters for the resting layout, floating-capsule visual/behavior
treatment for how those clusters look and fade, and the expandable drawer for anything too deep for a
small cluster.

### Corner clusters (resting layout)

Four small, independently positioned floating groups, purpose-grouped rather than arranged in one
continuous bar — there is no single toolbar shape any more:

| Cluster | Position | Contents |
|---|---|---|
| **Navigate** | top-left | Back button, page-number label |
| **View** | bottom-left | Reading-mode picker, fit-mode picker (hidden in continuous modes, same rule as today), zoom controls |
| **Page (turn)** | bottom-center | Prev-chapter, prev-page, progress bar, next-page, next-chapter |
| **Actions** | top-right | Bookmark-current-page quick toggle, overflow ("⋮", opens the drawer), fullscreen toggle |

Each cluster is a small `Border` — `PbRadiusMd`-ish rounded, `Pb*` surface color at reduced opacity,
1px `PbBorderBrush`, drop shadow — sized to its own content, not a fixed shared width.

**Minimum width, resolved:** estimating each cluster's content width (Navigate ~110px, View ~230px at
its widest — mode + fit + zoom all shown — Page-turn ~220px, Actions ~110px, all plus 14px corner
margins), View's right edge and Page-turn's left edge converge — and start crowding — once the window
narrows below roughly **720px**. Below that threshold, the View cluster collapses to just the
reading-mode picker (its highest-priority control) and folds fit-mode + zoom into the drawer, reusing
the exact same drawer mechanism already built for the Actions overflow rather than inventing a second
responsive system. This reclaims the space View needed without a new fallback UI pattern.

### Capsule behavior (visual treatment + idle-fade)

Clusters render as translucent floating capsules (blurred/translucent backing, matching the visual
weight of Phase 1's `FloatingPanel` chrome) rather than opaque docked bars. **Idle-fade applies in
both windowed and fullscreen** — this is the one deliberate behavior change beyond pure restyle: today
only fullscreen auto-hides; this phase makes windowed mode do the same.

**Timing and opacity, resolved — reusing exact existing values rather than inventing new ones:** the
current fullscreen overlay mechanism (`ReaderScreenViewModel.OverlayAutoHideDelay = TimeSpan.
FromSeconds(3)`, driving the binary `ShowFullscreenOverlays` property) is proven and already accepted
behavior — this phase extends it to all four clusters and to windowed mode, unchanged: **3 seconds of
cursor inactivity, full show ↔ fully hidden** (not a partial-opacity dim — that would be new,
untested behavior where a proven binary one already exists). Any pointer movement restores full
visibility immediately; hovering or interacting with a cluster keeps it visible for the duration.
This retires the current "docked bar in windowed / floating HUD only in fullscreen" split in favor of
one consistent system, reusing the same mechanism rather than building a second one.

Keyboard shortcuts are unaffected by fade state — a faded cluster's actions remain triggerable via
their shortcut without needing the cluster visible first.

**Bookmark quick-toggle feedback, resolved:** yes — toggling the current page's bookmark from the
Actions cluster gets a brief accent-glow pulse (`PbGlowColor`/`PbMotionFast`), the same feedback
language Phase 1 already established for interactive state changes elsewhere (e.g. `PosterTile`'s
hover glow) — not a new visual idiom, just this control's first use of an existing one.

### Drawer (everything else)

Clicking the Actions cluster's "⋮" slides in a right-anchored panel (~230px wide, matching the width
established during the mockup) with labeled sections — a real inspector panel, not a stack of nested
flyouts:

- **Page:** rotate CW, rotate CCW, auto-rotate toggle, and the double-page-toggle-or-auto-scroll
  slot (same mutual-exclusivity-by-mode rule as today)
- **Adjust:** brightness/contrast/saturation/gamma sliders inline (not a separate flyout-within-the-
  drawer) + reset
- **Transition:** the page-transition-style picker (None/Slide/Crossfade)
- **Bookmarks:** the full scrollable list with inline rename/delete, prev/next-bookmark navigation

The other three clusters (Navigate/View/Page-turn) stay floating over the now-narrower canvas while
the drawer is open — the drawer doesn't block page-turning or fit/zoom changes. The drawer itself
does **not** idle-fade — once opened, it stays until explicitly closed (click "⋮" again, or a
close affordance on the drawer itself), since it represents deliberate user intent to see it, unlike
the ambient clusters.

### Thumbnail rail stays persistent

The 96px vertical thumbnail rail is **not** folded into the cluster/drawer system and does **not**
idle-fade. Rationale: it's a spatial navigation aid used continuously while reading (closer to a
scrollbar than a settings surface), not a secondary tool — hiding it on idle would work against its
own purpose. It gets the same token-based restyle (tile borders, selected-state, bookmark ribbon,
page-type badge all switch to `Pb*` colors/radii) but keeps its current always-visible, always-docked
behavior and position (left edge). This is a deliberate scope decision, not an oversight — flagged
explicitly so it doesn't get silently swept into "everything becomes a floating capsule."

**CE precedent, resolved and corrected:** the design doc's first draft pointed at
`_reference/ComicRackCE/ComicRack/Controls/PagesView.cs`/`Views/ComicPagesView.cs` — checked directly,
and that's the wrong file. Those implement CE's "Pages" workspace, a full-window page-*management* tab
(sortable grid, drag-drop reorder, merge/delete) you navigate away to, not an in-reader navigation
strip — no real precedent for a docked rail here. The actual precedent is
`_reference/ComicRackCE/ComicRack.Engine.Display.Forms/NavigationOverlay.cs`, already correctly cited
in the earlier `2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md` spec for
Paperbunkr's own **fullscreen** scrubber — a fullscreen-only floating filmstrip (not a persistent
sidebar) where the current page renders at full opacity and flanking pages fade to 30% opacity, over a
semi-transparent "glass" panel background, with 4 corner nav buttons and a center scrub slider. CE has
no docked-sidebar convention at all — Paperbunkr's persistent rail is an original addition, not a CE
port, which is expected and fine. The one thing worth carrying over: CE's glass-panel translucency and
focus/periphery opacity fade is a real, concrete precedent for this phase's own floating-capsule
aesthetic (see [Capsule behavior](#capsule-behavior-visual-treatment--idle-fade) above) — not for the
rail, which stays a plain restyle.

### On-canvas overlays stay as-is, restyled only

The error card and the two chapter-transition cards (loading, from/to info) are functional feedback,
not navigation/settings chrome. They're carried over structurally unchanged — same trigger conditions,
same fade-in/out behavior — just re-skinned with `Pb*` tokens and `PbMotionFast`/`PbMotionEase` in
place of the current locally-declared, hardcoded-duration `DoubleTransition`.

## Testing

Two real, additive surfaces need new coverage, beyond the pure-restyle parts of this phase:
- `ReaderScreenViewModel`: new bindable properties for drawer-open/closed and cluster-fade state
  (existing commands keep their existing signatures), plus live-bound shortcut-hint text sourced from
  `KeyBindingService` per command instead of literal strings.
- `PreferencesScreenViewModel`'s Keyboard Shortcuts section: new import/export commands, needing tests
  for round-tripping a layout (export then import restores the same bindings) and for a corrupt/invalid
  imported file falling back safely (matching `KeyBindingService`'s existing bad-gesture fallback
  precedent, not a new failure mode).

Existing `ReaderScreenViewModelTests`-equivalent coverage (whatever the actual test file is named —
confirmed during planning, not guessed here) should keep passing unmodified otherwise. Verification
matches every prior phase: `dotnet build` clean with the XAML weave confirmed to have actually run
(per this repo's own `CLAUDE.md` gotcha — a bare "0 Errors" isn't sufficient proof), full test suite
green, and a crash-free direct-exe launch. On-screen interactive verification (does the fade timing
feel right, does the drawer's open/close animation feel right, does cluster positioning hold up at
different window sizes, do the now-live shortcut hints actually update after a remap) is real work
that benefits from the user's own direct testing, same as Phase 3 — flagged explicitly, not assumed
away.

## Open questions / deferred

None remaining — the four items originally listed here (idle-fade timing/opacity, bookmark-toggle
feedback, CE thumbnail-rail precedent, minimum-width behavior) are now resolved inline in their
relevant sections above.
