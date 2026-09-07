# Preferences Tile-Hub Redesign — Design (Phase 1 of Preferences)

**Sub-project 1 of "Whole UI Re-Architecture"** — a multi-sub-project visual/structural refresh
decided in chat 2026-09-07, decomposed into: (1) **Preferences** (this doc + 9 more phases, see
below), (2) Navigation & shell, (3) Library browsing structure, (4) Cross-screen consistency pass.
Each gets its own full design → plan → implementation cycle.

**Preferences itself turned out too large for one design** (confirmed in the same chat session,
after a codebase survey found roughly half its content is repeaters/dashboards/modals that a shared
row primitive can't represent — see "Phasing" below). This doc covers only the shell restructuring +
the genuinely-simple settings + new skins; §4 lists the 9 further phases, each deferred to its own
future design cycle.

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

## Phasing (added after a fact-finding survey during plan-writing)

A survey of all 9 non-Plugins section files found the original Goal 2 ("every individual setting")
doesn't hold: roughly half of Preferences' actual content isn't a simple setting at all — it's
repeaters, dashboards, a 9-provider modal-form system, and a master/detail editor. Forcing those
into an icon+title+description+control row would look wrong or silently drop functionality. The
user confirmed (chat, same session): every one of these areas gets a **genuine individual
redesign**, not a reskin — but each is a different enough shape (list vs. dashboard vs. modal system
vs. editor) that it needs its own brainstorm, not a shared row primitive. Sequencing decided:
**this phase ships what's below; each area listed in §4 "Deferred" gets its own future design → plan
cycle, one at a time.**

## Goals (this phase)

1. Replace the sidebar + hard-switch pane with a **single continuous scrolling page**, entered via
   a **tile-grid hub** that **collapses to a sticky icon strip** once scrolled past.
2. Restructure every **genuinely simple** setting (a single bound control with a static label — see
   the per-section breakdown in §2) onto a shared row layout: icon + title + optional description +
   control. This covers all of General; most of Reader; Appearance's Font/Motion/Navigation/
   Developer groups; Advanced's Rendering and Comic File Metadata groups; About's Updates group's
   one toggle. Everything else in §4 keeps its current layout for this phase.
3. Add **3 new built-in skins** to the existing Skins picker, alongside Default (unchanged).
   **Finding:** Windows 11 (`Assets/Skins/windows_11/theme.json`) exists on disk but isn't actually
   wired into `SkinService.GetAvailableSkins()` today — only `default` is special-cased as embedded;
   everything else in that method only scans `SkinPaths.ExtractedDirectory` (user-*installed* skins).
   Since this phase already has to build a proper "list of embedded built-in skins" mechanism to add
   the 3 new ones, Windows 11 is fixed to be a real 5th option in that same list as a natural
   byproduct — not new scope, just completing the mechanism this phase already touches.

## Non-goals (this phase)

- Any change to what settings exist or what they do — pure layout/navigation restructuring for the
  in-scope items; section membership from `PreferencesScreen.axaml` today carries over unchanged.
- Search granularity — stays group-level (`PreferenceIndex.cs`'s existing ~30 entries), per the
  original rework's own explicit non-goal. Per-setting granularity remains a future add-on.
- The installable-skin `.crpck`/`theme.json` mechanism itself (`TryInstallSkin`) — untouched, only
  the built-in-skins list changes.
- Writing the description text itself — see §2 below; this ships with descriptions empty by design.
- `PluginScreen`'s own internals — unchanged, still a real screen (see §1).
- Everything in §4 "Deferred to future sub-projects" — explicitly not reskinned or restructured this
  phase; each keeps its exact current layout until its own design cycle happens.

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

**In scope this phase** (confirmed via a full per-section survey during plan-writing — file/group
counts in that survey's own notes, not repeated here): all of **General**'s 4 groups; **Reader**'s
5 groups except the live-updating Slider labels need `SettingsRow.Title` to support a bound string,
not just a literal; **Appearance**'s Font/Motion/Navigation/Developer groups (not Skins or Install
Skin — see §4); **Advanced**'s Rendering and Comic File Metadata groups (not File Association or
Backup Manager — see §4); **About**'s Updates group's one toggle (not Changelog/Legal — see §4).
Everything else keeps its exact current `Border.groupBox` layout for this phase.

New shared control, e.g. `Views/Preferences/SettingsRow.axaml` (a small `UserControl` or
`ControlTemplate`, whichever fits the existing `pref:` namespace convention in
`PreferencesScreen.axaml`), replacing the in-scope settings' current ad-hoc label-then-control
markup:

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
`spacingUnit`, `radius`, `radiusSm`, `radiusLg`). `SkinService.GetAvailableSkins()`/`TryLoadSkin()`
gain a small embedded-built-in-skins list (`default`, `windows_11`, plus these 3 new keys, each
loaded via its own `avares://Paperbunkr.App/Assets/Skins/<key>/` root the same way
`DefaultSkinAssetRoot` already does for `default`) instead of `default` being the only special case
— this is what also makes Windows 11 a real, selectable option for the first time (see the Goals §3
finding above). All 5 appear in the existing Skins picker in the Appearance section's "Active Skin"
row/repeater. Starting values for the 3 new skins (refined during implementation/visual QA, not
pixel-final):

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

## 4. Deferred to future sub-projects

Each of these keeps its *exact current layout* this phase (shell restructuring in §1 still applies
around it — it just sits in the scrolling page unchanged, the way it sits in the hard-switch pane
today) and gets its own future brainstorm → design → plan cycle, confirmed in chat as needing a
genuine redesign, not a reskin:

1. **Library folder management** — Comic Library Folders + Book Folders repeaters.
2. **Library Health dashboard** — stat tiles + missing-files/recently-removed repeaters.
3. **Virtual Tags editor** — master/detail tag picker + edit form.
4. **Connections** — 2 provider-picker lists + 9 per-provider modal connection forms.
5. **Keyboard Shortcuts** — 3 key-binding repeaters (label + key-picker ComboBox rows).
6. **Automation task list** — scheduled-tasks repeater with per-row mode-dependent controls.
7. **Advanced: Backup Manager + File Association** — the backup sub-UI and the
   checkbox-per-extension repeater.
8. **Appearance: Skins picker** — currently a repeater of skin-choice buttons (the picker itself,
   not the 5 skins it lists — those are in scope, see §3).
9. **About section as a whole** — Updates' non-toggle parts (version display, Check for Updates
   button), Changelog, and Legal.

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
- **External caller found during the codebase survey:** `MainViewModel.GoLibraryFoldersPreferences()`
  (`MainViewModel.cs:821-827`) sets `Preferences.ActiveSection = PreferencesSection.Library` to open
  Preferences straight to Library's folders (Library's own empty-state "Scan folders" CTA). With
  `ActiveSection` gone, this becomes a new `PreferencesScreenViewModel.RequestScrollToAnchor(string)`
  public method wrapping `ScrollToAnchorRequested?.Invoke(...)` (events can't be raised from outside
  their declaring class), called with `"library.comicFolders"` instead of setting the now-gone
  property.

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
