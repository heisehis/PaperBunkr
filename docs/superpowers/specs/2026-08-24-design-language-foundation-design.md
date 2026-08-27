# Design Language Foundation

**Status:** Design phase, not yet planned/implemented.
**Sub-project 1 of 7** in the full UI rework (see [Full UI rework — phase breakdown](#full-ui-rework--phase-breakdown) below). Each phase gets its own spec → plan → implementation cycle.

## Background

Paperbunkr's current UI runs on Avalonia's stock `FluentTheme` with a thin custom layer on top: a flat `Pb*` color-token set in [App.axaml](../../../src/Paperbunkr.App/App.axaml) (mirrored by the installable skin system's `theme.json`/[SkinTheme.cs](../../../src/Paperbunkr.App/Models/SkinTheme.cs)), no bundled fonts (relies on the OS default unless a user picks one in Preferences), and no shared icon set (icon geometry is duplicated inline as `PathIcon`/`StreamGeometry` across 16+ view files). Motion is limited to a few hand-built cases (the reader's page-turn animation); most of the app has no transitions at all.

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
- Replace ad-hoc inline icon geometry with one shared outline-icon resource dictionary (only for icons the app already uses — no new iconography invented).
- Define motion tokens (duration/easing constants) — established here, wired into actual screen transitions starting in Phase 2.
- Build four reusable `ControlTheme`s: PosterTile, Chip/Pill, Button variants, FloatingPanel chrome — proven on an internal showcase view.
- Migrate the existing overlay set (listed below) to the new FloatingPanel chrome, since that's a low-risk restyle of an existing wrapper, not new screen work.

**Out of scope (deferred to later phases):**
- Any change to Home, Library, Detail, Reader, or nav-rail layout/behavior.
- Alternate color schemes beyond "Evolved Amber" (the skin system already supports multiple skins; more schemes can be authored later using this same expanded schema).
- Migrating all 16+ inline-icon call sites — that happens incrementally as each screen is touched in its own phase.
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
| `heroGradientStart` / `heroGradientEnd` | New — stops for hero-art overlay gradients (consumed starting Phase 3, values picked now so they're not improvised per-screen) |
| `border`, `text`, `textMuted`, `textFaint`, `accent`, `accentText`, `accentSoft`, `badge`, `badgeText`, `success` | Carry forward, re-tuned for contrast against the darker `surface0` |

Changes are **additive** to `SkinColors`/`theme.json` — no field renames — so any existing or future custom skin missing the new fields falls back to schema defaults rather than failing to load.

## Typography

Bebas Neue and Source Serif 4 ship as embedded assets under `Assets/Fonts/`, registered via Avalonia's `FontManager` (`avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue`) so rendering is identical regardless of what's installed on the host machine. This becomes the new **default** — separate from and layered underneath the existing Preferences → Appearance font-override feature (`PbFontFamily`/`SelectedFontFamily` in [SkinService.cs](../../../src/Paperbunkr.App/Services/SkinService.cs)), which is unchanged and still lets a user override everything.

A small type-scale is defined as named styles, not every size in the app:

- `PbTextHero` — Bebas Neue, large — series/issue titles in hero moments (Phase 3+)
- `PbTextHeading` — Bebas Neue, smaller — section headers
- `PbTextBody` — Source Serif 4, regular — default body copy
- `PbTextCaption` — Source Serif 4, smaller, muted color — metadata lines

The existing global `TextBlock`/`Button`/`TextBox` style setters in `App.axaml` get a default `FontFamily` from this scale; screens opt into the named styles as they're touched in later phases.

## Iconography

A new `Icons.axaml` resource dictionary holds one `StreamGeometry` per icon, named `PbIcon*` (e.g. `PbIconBook`, `PbIconStar`, `PbIconSettings`), in the thin-outline style. The icon set is inventoried from what the 16+ existing view files already use — no new icons invented in this phase. This phase defines the dictionary and the style; it does not migrate the 16+ call sites (that happens incrementally as each screen is touched in Phases 2–7, so no screen regresses mid-migration).

The `icons` dictionary already present in `SkinTheme`/`theme.json` (currently unused) is unrelated to this — it stays reserved for skin-level icon *overrides*, not the internal shared-geometry set.

## Elevation, spacing, radius & motion tokens

- **Radius:** current `PbRadius` (7) becomes a small scale — `PbRadiusSm`/`PbRadiusMd`/`PbRadiusLg` — since poster tiles, chips, and floating panels want different corner treatment.
- **Spacing:** current `PbSpacingUnit` (4) is unchanged; the existing multiplication pattern already scales fine.
- **Shadows:** two distinct tokens — `PbGlowFocus` (the amber `BoxShadow` for hover/keyboard-focus on cards and floating panels) and a subtler `PbElevationShadow` (genuine depth for surface3, not interaction feedback).
- **Motion:** `PbMotionFast` (~150ms) and `PbMotionEase` (a cubic-out curve), matching "snappy & responsive." Defined here as tokens only; Phase 2 is where they get wired into actual screen/panel transitions app-wide. This phase uses them narrowly, for the FloatingPanel open/close transition only.

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

## Compatibility with the skin system

All token changes are additive to the existing `theme.json` schema, so the installable-skin feature ([docs/onboarding.md](../../onboarding.md) §13) keeps working unmodified. "Evolved Amber" ships as the new `default` skin's values; the `windows_11` skin is untouched by this phase. Future alternate color schemes (mentioned by the user as a later interest) would be authored as new skins using this same expanded schema — no architecture change needed for that later.

## Testing / verification

- Unit-level: none of this phase's changes are behavioral, so no new ViewModel/logic tests are expected. Existing tests that assert on current token values (if any) get updated for the additive schema change.
- Visual verification: the internal showcase view is how PosterTile/Chip/Button get confirmed on-screen before any real screen consumes them.
- The five migrated overlays get manually verified on-screen (open/close each, check hover/focus glow, check they still function identically) since they're real user-facing surfaces changing today, not just a showcase.

## Open questions / deferred

- Exact hex values for the elevation scale, glow opacity, and hero-gradient stops are implementation-time decisions within the direction agreed here, not pre-specified line-by-line.
- Full icon inventory (which exact `PbIcon*` names are needed) is done during implementation by grepping the 16+ existing files, not enumerated here.
