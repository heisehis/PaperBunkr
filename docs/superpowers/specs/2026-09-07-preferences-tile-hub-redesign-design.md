# Preferences Tile-Hub Redesign — Design

**Sub-project 1 of "Whole UI Re-Architecture"** — a multi-sub-project visual/structural refresh
decided in chat 2026-09-07, decomposed into: (1) **Preferences** (this doc), (2) Navigation &
shell, (3) Library browsing structure, (4) Cross-screen consistency pass. Each gets its own full
design → plan → implementation cycle; this is the first.

Supersedes the shell/IA parts of
[2026-08-28-preferences-rework-design.md](2026-08-28-preferences-rework-design.md) (which shipped
the current left-sidebar + hard-switch-pane shell, 8→10 sections since, and the group-level search
index this doc keeps unchanged).

## Background

Today's `PreferencesScreen.axaml`: a fixed left sidebar (`Button.prefNavItem` × 10, one per
`PreferencesSection`) and a right `ContentControl` that hard-switches to exactly one of 10 section
`UserControl`s (`Views/Preferences/*Section.axaml`) — `General, Appearance, Library, Automation,
Reader, KeyboardShortcuts, Connections, Plugins, Advanced, About` (this order,
`PreferencesSection.cs`). Search (`PreferenceIndex.cs`, ~20 group-level entries) sets
`ActiveSection` then scrolls/pulses the matching group.

Settled via a brainstorming session with the visual companion (mockups in
`.superpowers/brainstorm/1903-1788782467/content/`): the motivation is a general visual refresh,
not a reaction to any specific bug or IA complaint with what's *in* each section — this is a pure
restructuring of navigation and per-setting layout.

## Goals

1. Replace the sidebar + hard-switch pane with a **single continuous scrolling page**, entered via
   a **tile-grid hub** that **collapses to a sticky icon strip** once scrolled past.
2. Restructure **every individual setting** (not just every group) onto a shared row layout: icon +
   title + optional description + control.
3. Add **3 new built-in skins** to the existing Skins picker, alongside Default (unchanged) and
   Windows 11.

## Non-goals

- Any change to what settings exist, what they do, or which section they live in — pure
  layout/navigation restructuring, section membership from `PreferencesScreen.axaml` today carries
  over unchanged.
- Search granularity — stays group-level (`PreferenceIndex.cs`'s existing ~20 entries), per the
  original rework's own explicit non-goal. Per-setting granularity remains a future add-on.
- The Windows 11 skin and the installable-skin `.crpck`/`theme.json` mechanism — untouched.
- Writing the description text itself — see §2 below; this ships with descriptions empty by design.
- `PluginScreen`'s own internals — unchanged, still a real screen (see §1).

## Architecture

### 1. Shell: tile hub → single scroll → sticky strip

`PreferencesScreen.axaml`'s current `Grid` (sidebar + `ContentControl`) is replaced by one
`ScrollViewer` whose content is, top to bottom: the tile hub, then all 10 sections' content
stacked in `PreferencesSection` enum order (Plugins excepted, see below) — no more per-section
`UserControl` swap; every section's markup is present in the tree simultaneously (each still its
own `Views/Preferences/*Section.axaml` file/control, just all mounted at once instead of one at a
time via `ContentControl`).

**Tile hub:** a grid of 9 tiles (icon + section label; Plugins is the 10th tile but behaves
differently, see below), one per section other than the entry itself.  Clicking a tile calls
`BringIntoView()` on that section's root (the exact mechanism `PreferenceIndex`'s search-jump
already uses) — no `ActiveSection` hard-switch anymore, `ActiveSection` is removed from the
ViewModel entirely (nothing left to switch to).

**Sticky strip:** once the hub has scrolled out of the viewport, a slim always-visible row of
icon-only tiles pins to the top of the screen (outside the `ScrollViewer`, overlaying it), replacing
the full hub. Implemented the same way `DetailHero`'s parallax (2026-09-07 chrome-motion-polish
work) tracks scroll position — a `ScrollViewer.ScrollChanged` handler comparing the hub's `Bounds`
against the viewport, toggling the strip's visibility and swapping the "active" highlight to
whichever section's bounds currently intersect the viewport top. Clicking a strip icon does the same
`BringIntoView()` as a hub tile.

**Plugins stays a screen change, not a scroll target.** Its tile (in both the hub and the sticky
strip) keeps calling today's `GoPluginsCommand`-equivalent, replacing the whole visible content with
`PluginScreen` the way `ActiveSection` hard-switching does today — it's the one section that isn't
part of the scrolling page at all. A visible "← Back to Preferences" affordance returns to the
scroll view.

**Search** (`PreferenceIndex`, unchanged data/matching logic) drives the same `BringIntoView()` +
pulse-highlight, just targeting a position within the one continuous page instead of switching
panes first.

### 2. Settings row primitive

New shared control, e.g. `Views/Preferences/SettingsRow.axaml` (a small `UserControl` or
`ControlTemplate`, whichever fits the existing `pref:` namespace convention in
`PreferencesScreen.axaml`), replacing every individual setting's current ad-hoc
label-then-control markup across all 10 sections:

```
[icon]  Title                              [control]
        Description (optional, one line)
```

Properties: `Icon` (a `PbIcon*` glyph key), `Title` (string), `Description` (`string?`), `Content`
(the control — ComboBox/CheckBox/Slider/Button/etc., unchanged from today per-setting). When
`Description` is null/empty, that `TextBlock` is `IsVisible="{Binding ..., Converter=
{x:Static StringConverters.IsNotNullOrEmpty}}"` — the row collapses to just the title+control line,
no dead space, no visible placeholder. **Every row ships with `Description` unset** — the user
writes this copy separately, on their own schedule, with no code changes needed to add it later
(just filling in the XAML attribute). A companion checklist (`docs/preferences-descriptions-todo.md`
or similar, generated as part of this work) enumerates every row needing copy, grouped by section,
so it's one place to work through rather than ten files to hunt across.

**Icons:** picked per-setting from the existing `PbIcon*` vector library
(`Styles/Icons.axaml`/`Assets/Icons/icon-mapping.md`); new geometries added where nothing fitting
exists, following the standing convention (update `icon-mapping.md`).

**Grouping:** the current `Border.groupBox`/`Border.groupHeader` card treatment is dropped in favor
of a light caption-style label (small, uppercase, `TextBlock.pbTextCaption` or equivalent — no
border/background) above each cluster of related rows, e.g. "SKINS" above the Active
Skin/Install Skin rows — keeps long sections (Advanced, Reader) scannable without the heavier card
chrome the old design used.

### 3. New skins

Three new `Assets/Skins/<key>/theme.json` files (schema identical to `default`/`windows_11` — see
those two for the full field set: `colors.{bg,chrome,border,text,textMuted,textFaint,accent,
accentText,accentSoft,badge,badgeText,success,surface0-3,glow,heroGradientStart,heroGradientEnd}`,
`spacingUnit`, `radius`, `radiusSm`, `radiusLg`), registered as built-in the same way `windows_11`
is (`SkinService.GetAvailableSkins()`), appearing in the existing Skins picker in the Appearance
section's "Active Skin" row. Default is untouched. Starting values (refined during implementation/
visual QA, not pixel-final):

**Cool & Technical** — blue-gray surfaces, crisp blue accent:
```json
{
  "name": "Cool & Technical",
  "colors": {
    "bg": "#0B0E14", "chrome": "#12151C", "border": "#232A36",
    "text": "#E4E7EC", "textMuted": "#9BA3B0", "textFaint": "#626B7A",
    "accent": "#5EA7F0", "accentText": "#7FBBF5", "accentSoft": "#295EA7F0",
    "badge": "#4FC3D9", "badgeText": "#071824", "success": "#4FA87F",
    "surface0": "#060810", "surface1": "#0B0E14", "surface2": "#12151C", "surface3": "#1A1F29",
    "glow": "#665EA7F0", "heroGradientStart": "#00000000", "heroGradientEnd": "#FF06080D"
  },
  "spacingUnit": 4, "radius": 7, "radiusSm": 5, "radiusLg": 14
}
```

**Vibrant & Pop** — purple-tinted near-black, punchy red-orange accent:
```json
{
  "name": "Vibrant & Pop",
  "colors": {
    "bg": "#0D0B12", "chrome": "#17131F", "border": "#2E2540",
    "text": "#F5F0E8", "textMuted": "#B0A5AE", "textFaint": "#766B75",
    "accent": "#E85D4E", "accentText": "#F0806F", "accentSoft": "#29E85D4E",
    "badge": "#F0C14C", "badgeText": "#241505", "success": "#5FA889",
    "surface0": "#000000", "surface1": "#0D0B12", "surface2": "#17131F", "surface3": "#201A2C",
    "glow": "#66E85D4E", "heroGradientStart": "#00000000", "heroGradientEnd": "#FF0D0B12"
  },
  "spacingUnit": 4, "radius": 7, "radiusSm": 5, "radiusLg": 14
}
```

**Vintage Paperback** — sepia/brown-black, brick-red + amber two-tone accent:
```json
{
  "name": "Vintage Paperback",
  "colors": {
    "bg": "#12100D", "chrome": "#1C1712", "border": "#3A2F22",
    "text": "#EDE0C8", "textMuted": "#B0A084", "textFaint": "#786D58",
    "accent": "#8C3B2E", "accentText": "#C98A52", "accentSoft": "#298C3B2E",
    "badge": "#B8763D", "badgeText": "#241505", "success": "#6B8F5C",
    "surface0": "#000000", "surface1": "#12100D", "surface2": "#1C1712", "surface3": "#241D16",
    "glow": "#66B8763D", "heroGradientStart": "#00000000", "heroGradientEnd": "#FF12100D"
  },
  "spacingUnit": 4, "radius": 7, "radiusSm": 5, "radiusLg": 14
}
```

## ViewModel changes (summary)

- `ActiveSection` (and every `Is<Section>Section`/`Go<Section>Command`, except Plugins') removed —
  nothing left to switch between; sections are all simultaneously present.
- Plugins keeps its own `IsPluginsSection`-equivalent/`GoPluginsCommand` pair, now the *only*
  section-switch left, toggling between "the scroll view" and "the Plugins screen" as a two-state
  flag rather than a 10-way enum.
- New: hub/sticky-strip "which section is currently in view" tracking (a bindable
  `CurrentSectionInView` or similar, driven by the `ScrollViewer.ScrollChanged` handler described in
  §1) — replaces `ActiveSection` for the purpose of highlighting.
- `PreferenceIndex`/search: unchanged data and matching; only the "jump to result" mechanism's
  target changes (position in one page vs. a pane switch).
- No changes to any `Persist*` helper or any setting's own command/binding — every row's `Content`
  is the exact same control wired the exact same way, just re-hosted inside `SettingsRow`.

## Testing

- `PreferenceIndexTests` — unchanged assertions still hold (anchor keys still resolve to a `Tag` on
  a row/group within the now-single page).
- New: a scroll-tracking test mirroring `DetailHero`'s own parallax test shape — mount the shell in
  a `ScrollViewer`-hosting harness, scroll past the hub, assert the sticky strip becomes visible and
  highlights the correct section for a given scroll offset.
- `PreferencesScreenViewModelTests` — remove `ActiveSection`-cycling assertions that no longer apply
  (10-way switch is gone); add: Plugins toggle still works as a two-state flag; search jump still
  resolves an `AnchorKey`.
- New `SkinServiceTests` cases: each of the 3 new skins loads without error via
  `GetAvailableSkins()`/`LoadSkin()`, and its `theme.json` round-trips through the same validation
  `TryInstallSkin` already applies to installed skins.
- Manual on-screen pass (standing no-unattended-GUI caveat): scroll through the full page, confirm
  the hub collapses/expands correctly at the scroll boundary and the sticky strip's active highlight
  tracks correctly; click every hub/strip tile and confirm it lands on the right section; open
  Plugins and confirm returning to Preferences restores scroll position; switch through all 5 skins
  (Default, Windows 11, + 3 new) and spot-check contrast/legibility in each; confirm a row with no
  description collapses cleanly with no dead space.

## Deliverable: description checklist

A generated list (one line per `SettingsRow` across all 10 sections, grouped by section, in the
format `[ ] Section → Group → Title`) handed to the user as part of this work's output, tracking
which settings still need `Description` text filled in. Not a design decision — just named here so
it isn't lost between design and implementation.
