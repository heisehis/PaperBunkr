# Design Language Foundation

**Status:** Implemented 2026-08-24. See docs/superpowers/specs/2026-08-24-design-language-foundation-plan.md for the implementation plan and what was actually verified.
**Sub-project 1 of 7** in the full UI rework (see [Full UI rework — phase breakdown](#full-ui-rework--phase-breakdown) below). Each phase gets its own spec → plan → implementation cycle.

## Background

Paperbunkr's current UI runs on Avalonia's stock `FluentTheme` with a thin custom layer on top: a flat `Pb*` color-token set in [App.axaml](../../../src/Paperbunkr.App/App.axaml) (mirrored by the installable skin system's `theme.json`/[SkinTheme.cs](../../../src/Paperbunkr.App/Models/SkinTheme.cs)) and no bundled fonts (relies on the OS default unless a user picks one in Preferences). Motion is limited to a few hand-built cases (the reader's page-turn animation); most of the app has no transitions at all.

There is already a shared icon set — 39 raster icons in [Assets/Icons/](../../../src/Paperbunkr.App/Assets/Icons) (96×96, grayscale+alpha), consumed via `ImageBrush`/`Border.OpacityMask` across 17 view files, a technique that already makes every icon brush-recolorable. This phase converts that set to vector rather than building one from scratch — see [Iconography](#iconography) below.

The user wants a full visual redesign of the app, taking cues from comic-reader apps at its roots blended with streaming-media browsing (Plex/Netflix/Apple TV: poster-forward grids, hero art, carousels). That's too large for one spec — it spans navigation, Home, Library, Detail, Reader chrome, and editing surfaces, each with different concerns. This document covers only the first, foundational phase: the shared token system and component primitives everything else will be built from. It does not redesign any actual screen (Home, Library, Detail, etc.) — that's phases 3–7.

## Full UI rework — phase breakdown

1. **Design language foundation** *(this document)* — tokens, fonts, icons, component primitives.
2. **Navigation shell & motion system** — app chrome, nav rail, screen-to-screen transitions, the actual "fluid/reactive" plumbing.
3. **Home screen** — hero art, continue-reading rail, recommendation carousels.
4. **Library browsing** — poster-forward grid/tiles, sort/group/filter chrome.
5. **Detail screen** — series/issue detail, tabs, related-media surfaces.
6. **Reader chrome** — controls/overlay redesign (the page-render engine itself is unaffected).
7. **Preferences & remaining editing overlays** — anything not already covered by this phase's FloatingPanel migration.

## Scope

**In scope:**
- Extend the `Pb*` token system (`App.axaml`, `SkinTheme.cs`, both `theme.json` skins, `SkinService`) with elevation tiers, semantic states, and a glow/focus token.
- Bundle Bebas Neue + Source Serif 4 as embedded font assets and register them as the new default typography.
- Convert the existing 39 raster icons ([Assets/Icons/](../../../src/Paperbunkr.App/Assets/Icons)) to vector `StreamGeometry` in a shared `Icons.axaml` dictionary, re-traced (not just re-exported) to the thin-outline style — plus a one-icon-per-action canonical mapping (see [Iconography](#iconography)).
- Define motion tokens (duration/easing constants) — established here, wired into actual screen transitions starting in Phase 2 — and a **reduced-motion preference** the tokens respect from day one.
- Build four reusable `ControlTheme`s: PosterTile, Chip/Pill, Button variants, FloatingPanel chrome — proven on an internal showcase view.
- Migrate the existing overlay set (listed below) to the new FloatingPanel chrome, since that's a low-risk restyle of an existing wrapper, not new screen work.
- Restyle the two real-`Window` surfaces (`CrashReportWindow`, `PluginQuestionDialog`) with the new color/radius/font tokens — they stay real OS windows, they just stop looking like the old visual identity.
- Hit WCAG AA contrast (4.5:1 body text, 3:1 large/display text) for every surface/text token pairing introduced by the elevation scale.

**Out of scope (deferred to later phases):**
- Any change to Home, Library, Detail, Reader, or nav-rail layout/behavior.
- Alternate color schemes beyond "Evolved Amber" (the skin system already supports multiple skins; more schemes can be authored later using this same expanded schema).
- Migrating all 17 icon-consuming call sites to the new vector icons — that happens incrementally as each screen is touched in its own phase (except the FloatingPanel/real-window set already being touched now).
- Real separate OS windows for config/editing menus — explicitly considered and rejected in favor of keeping the existing in-window overlay pattern, just restyled.

## Visual direction (decided)

- **Palette — "Evolved Amber":** keep the current near-black + amber identity, refine it into a full elevation scale pushed toward true-black contrast. Not a new color scheme; a deeper version of the existing one.
- **Typography:** Bebas Neue (display/headline) + Source Serif 4 (body/UI text everywhere, including dense surfaces like forms and lists) — the "Comic Ink" direction, leaning into the app's comic-book roots rather than a neutral sans-only system.
- **Card interaction:** glow ring — an amber glow (`BoxShadow`) on hover *and* keyboard focus, no scale/shadow-lift movement. Calmer than a "streaming depth" shadow-lift treatment, and doubles as the focus-visible indicator.
- **Motion feel:** snappy & responsive — short durations (~120–200ms), minimal easing overshoot. Confirms actions happened without making the UI feel like it's waiting on itself.
- **Icons:** thin outline style (Lucide/Feather-like, consistent ~1.5px stroke weight), neutral enough not to compete with the Bebas Neue + Source Serif pairing.

## Color system

Today's schema is single-tier: one `bg`, one `chrome`, no real depth. The expansion:

| New/changed field | Purpose |
|---|---|
| `surface0` | App background (replaces `bg`), pushed toward true black (~`#0A0A0C`) |
| `surface1` | Panels/toolbars (today's `chrome` level) |
| `surface2` | Cards-on-surface1, e.g. poster tiles |
| `surface3` | Popovers/modals/floating panels |
| `glow` | New — amber, higher opacity than `accentSoft`; drives the poster-tile and floating-panel hover/focus ring |
| `heroGradientStart` / `heroGradientEnd` | New — a **dark vignette**: transparent at the art fading to `surface0` at the bottom/edges (the standard streaming-poster technique), not a tinted fade — keeps title text legible over any cover art without recoloring the art itself. Consumed starting Phase 3; the direction is fixed here, exact stop positions are an implementation-time detail. |
| `border`, `text`, `textMuted`, `textFaint`, `accent`, `accentText`, `accentSoft`, `badge`, `badgeText`, `success` | Carry forward, re-tuned for contrast against the darker `surface0` |

Changes are **additive** to `SkinColors`/`theme.json` — no field renames — so any existing or future custom skin missing the new fields falls back to schema defaults rather than failing to load.

Every surface/text token pairing (e.g. `text` on `surface0`, `textMuted` on `surface2`, `accentText` on `surface3`) must meet **WCAG AA**: 4.5:1 for body-sized text, 3:1 for large/display text (`PbTextHero`/`PbTextHeading`). This is checked during implementation when the actual hex values are picked, not pre-computed here — but it's a hard constraint, not a nice-to-have, given the accessibility work already invested in the app's UI Automation coverage.

## Typography

Bebas Neue and Source Serif 4 ship as embedded assets under `Assets/Fonts/`, registered via Avalonia's `FontManager` (`avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue`) so rendering is identical regardless of what's installed on the host machine. This becomes the new **default** — separate from and layered underneath the existing Preferences → Appearance font-override feature (`PbFontFamily`/`SelectedFontFamily` in [SkinService.cs](../../../src/Paperbunkr.App/Services/SkinService.cs)), which is unchanged and still lets a user override everything.

A small type-scale is defined as named styles, not every size in the app:

- `PbTextHero` — Bebas Neue, large — series/issue titles in hero moments (Phase 3+)
- `PbTextHeading` — Bebas Neue, smaller — section headers
- `PbTextBody` — Source Serif 4, regular — default body copy
- `PbTextCaption` — Source Serif 4, smaller, muted color — metadata lines

The existing global `TextBlock`/`Button`/`TextBox` style setters in `App.axaml` get a default `FontFamily` from this scale; screens opt into the named styles as they're touched in later phases.

**Addendum, 2026-08-30 (avalonia-pro-max/review-checklist audit of the Phase 5 detail screens):**
the audit found four distinct font sizes — 9.5/10.5/11.5/12.5 — repeated as unnamed literals across
`DetailBand`/`DetailChrome` (chip/pill text, uppercase section labels + the tab counter, inline
meta text, and hero/tab-strip buttons+links) with no home in the four-step scale above, plus two
places (13, 11) that already matched `PbTextBody`/`PbTextCaption`'s size but restated the literal
instead of referencing it. Rather than snapping the odd values onto the nearest existing step
(which would visibly resize already-shipped, user-verified chrome) or leaving them as magic
numbers, they're now named `x:Double` resources in `App.axaml`, matching the prior literals
exactly (zero visual diff) — `PbFontSizeMicro` (9.5), `PbFontSizeOverline` (10.5),
`PbFontSizeMeta` (11.5), `PbFontSizeControl` (12.5), plus `PbFontSizeBody`/`PbFontSizeCaption`
(13/11, now the single source of truth `PbTextBody`/`PbTextCaption` also reference) — for a
surface that needs Body/Caption's size with a different `Foreground` than those bundled classes
provide, e.g. `DetailBand`'s muted synopsis. These are font-size-only (no bundled `FontFamily`/
color) since, unlike Hero/Heading/Body/Caption, they're chrome-density tiers rather than semantic
text roles — a chip and a section label at the same size still mean different things and keep
their own `Foreground`/`FontWeight` locally.

## Iconography

The existing 39 icons in [Assets/Icons/](../../../src/Paperbunkr.App/Assets/Icons) get re-traced as vector `StreamGeometry` resources in a new `Icons.axaml` dictionary, named `PbIcon*` (e.g. `PbIconBook`, `PbIconStar`, `PbIconSettings`) — vector over raster so icons scale cleanly if a later phase needs a larger size (e.g. an empty-state glyph) without a second asset. No new icon *concepts* are invented in this phase, only the format changes.

**One icon per action, enforced.** Before the geometry conversion, this phase audits all 17 current consuming files for every place an icon appears and what action/concept it represents. Any place the same action currently uses different icons in different screens, or the same icon is reused for two different meanings, gets resolved to a single canonical choice — documented as an explicit action → `PbIcon*` mapping (not just a flat list of icon names) alongside the dictionary, so future additions follow the same rule instead of drifting back into inconsistency. Where the audit finds a genuine conflict requiring a judgment call (not just "reuse the existing one everywhere"), that gets flagged for review rather than decided silently.

Rollout of the new vector icons into the 17 consuming files still follows the same incremental principle as everything else non-FloatingPanel/non-real-window in this phase: it happens as each screen is touched in Phases 2–7, so no screen regresses mid-migration. The exception is the five FloatingPanel-migrated overlays and the two restyled real windows (already being touched this phase for their chrome) — those pick up their new icons now rather than later, since the call site is already open.

The `icons` dictionary already present in `SkinTheme`/`theme.json` (currently unused) is unrelated to this — it stays reserved for skin-level icon *overrides*, not the internal shared-geometry set.

## Elevation, spacing, radius & motion tokens

- **Radius:** current `PbRadius` (7) becomes a small scale — `PbRadiusSm`/`PbRadiusMd`/`PbRadiusLg` — since poster tiles, chips, and floating panels want different corner treatment.
- **Spacing:** current `PbSpacingUnit` (4) is unchanged; the existing multiplication pattern already scales fine.
- **Shadows:** two distinct tokens — `PbGlowFocus` (the amber `BoxShadow` for hover/keyboard-focus on cards and floating panels) and a subtler `PbElevationShadow` (genuine depth for surface3, not interaction feedback).
- **Motion:** `PbMotionFast` (~150ms) and `PbMotionEase` (a cubic-out curve), matching "snappy & responsive." Defined here as tokens only; Phase 2 is where they get wired into actual screen/panel transitions app-wide. This phase uses them narrowly, for the FloatingPanel open/close transition only.
- **Reduced motion:** a new Preferences → Appearance toggle (alongside the existing font override) that the motion tokens respect from day one — when enabled, `PbMotionFast` resolves to effectively 0ms rather than every consumer having to check a flag individually. Scoped into this phase (not Phase 2) specifically so nothing added later has to retrofit it.

## Component primitives

Four `ControlTheme`s, built here and validated on a small internal showcase view (not a real screen):

- **PosterTile** — surface2 background, glow-ring hover/focus, badge slot, progress-bar slot. Later phases (3–4) place instances of this rather than reinventing card markup per screen.
- **Chip/Pill** — extends the pattern already established in [DetailPills.axaml](../../../src/Paperbunkr.App/Views/DetailPills.axaml).
- **Button variants** — primary/secondary/ghost, replacing default `FluentTheme` button styling with the new palette/radius/motion tokens.
- **FloatingPanel chrome** — surface3 background, `PbElevationShadow`, radius token, open/close transition using `PbMotionFast`/`PbMotionEase`. Unlike the other three primitives, this one **is** rolled out in this phase (see below) since it's a low-risk restyle of an existing wrapper, not new screen work.

### FloatingPanel migration (this phase)

The following existing in-window overlays get their outer chrome swapped to the new FloatingPanel style — internal form logic and content are untouched, this is purely the container styling:

- [ReadingListPropertiesOverlay.axaml](../../../src/Paperbunkr.App/Views/ReadingListPropertiesOverlay.axaml)
- [IssuePropertiesScreen.axaml](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml)
- [BulkIssuePropertiesScreen.axaml](../../../src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml)
- [QuickRateOverlay.axaml](../../../src/Paperbunkr.App/Views/QuickRateOverlay.axaml)
- [MigrationOverlay.axaml](../../../src/Paperbunkr.App/Views/MigrationOverlay.axaml)

Real separate OS windows (`SystemDecorations="None"`, owned by `MainWindow`, independently draggable/off-bounds) were considered and explicitly rejected — the in-window overlay pattern stays, it's just restyled.

### Real-window restyle (this phase)

Two existing surfaces are genuine `Window`s, not `UserControl` overlays, and stay that way — they're **not** converted to FloatingPanel/in-window overlays:

- [CrashReportWindow.axaml](../../../src/Paperbunkr.App/Views/CrashReportWindow.axaml) — deliberately independent of the main app's overlay system since it has to work even if the main window/renderer is in a broken state. Only its color/radius/font tokens are updated, not its architecture.
- [PluginQuestionDialog.axaml.cs](../../../src/Paperbunkr.App/Views/PluginQuestionDialog.axaml.cs) — plugin-facing; same treatment, tokens only.

## Compatibility with the skin system

All token changes are additive to the existing `theme.json` schema, so the installable-skin feature ([docs/onboarding.md](../../onboarding.md) §13) keeps working unmodified. "Evolved Amber" ships as the new `default` skin's values; the `windows_11` skin is untouched by this phase. Future alternate color schemes (mentioned by the user as a later interest) would be authored as new skins using this same expanded schema — no architecture change needed for that later.

## Testing / verification

- Unit-level: the reduced-motion preference is genuinely behavioral (a new setting with persistence and a resolved-duration effect) and gets test coverage the way other Preferences settings do. [WindowsElevenSkinTests.cs](../../../src/Paperbunkr.App.Tests/WindowsElevenSkinTests.cs) and [SkinServiceTests.cs](../../../src/Paperbunkr.App.Tests/SkinServiceTests.cs) already assert on current skin color values and get updated for the additive schema change.
- Contrast verification: every surface/text token pairing gets checked against the WCAG AA targets above when hex values are picked, not just eyeballed.
- Icon-mapping audit: the action → `PbIcon*` mapping is reviewed against all 17 current consuming files to confirm no action still resolves to two different icons, and no icon still means two different things.
- Visual verification: the internal showcase view is how PosterTile/Chip/Button get confirmed on-screen before any real screen consumes them.
- The five migrated overlays plus the two restyled real windows get manually verified on-screen (open/close each, check hover/focus glow, check the reduced-motion toggle actually shortens/removes transitions, check they still function identically otherwise) since they're real user-facing surfaces changing today, not just a showcase.

## Open questions / deferred

- Exact hex values for the elevation scale, glow opacity, and hero-gradient stop positions are implementation-time decisions within the direction agreed here (dark vignette, WCAG AA minimum), not pre-specified line-by-line.
- The action → `PbIcon*` mapping itself (which of the 39 icons map to which canonical action, and how any found conflicts get resolved) is produced during implementation by auditing the 17 consuming files, not enumerated here. If the audit finds a conflict that isn't a clear "pick the existing majority usage" call, that comes back for a decision rather than being resolved silently.
- Whether the reduced-motion preference also suppresses the reader's existing page-turn animation is an implementation-time call, not decided here — that animation predates this phase and isn't otherwise in scope.
