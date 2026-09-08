# Appearance Redesign — Design (Phase 7 of Preferences)

Preferences Tile-Hub Redesign's §4 deferred item 8 ("Appearance: Skins picker") — but at the user's
explicit request, a genuinely thorough pass rather than the lighter visual-only treatment given to
Connections (Phase 5) and Keyboard Shortcuts (Phase 6): the Skins picker gets real visual previews,
Install Skin folds into it, and Motion/Navigation/Developer get restructured, not just recolored.
Reached via `/grilling` + one round in the visual companion (skin-card treatment, grid vs. list).

## Background

Today's `AppearanceSection.axaml`: Font/Motion/Navigation/Developer/Install Skin already got the
`pref:SettingsRow` treatment in Phase 1 (the tile-hub redesign), each its own tiny group with a
`TextBlock.settingsGroupCaption` header (`PreferencesScreen.axaml:80` — bold, micro-size, faint,
letter-spaced; the real shared class this project uses for section captions, confirmed by direct
read). Only the Skins list itself was deferred, and still sits in the original `Border.groupBox`
card as bare text rows: skin name, an "Active" label when selected, nothing else — no color preview
at all, despite `SkinService.GetAvailableSkins()` already parsing every skin's full `theme.json`
(all of `bg`/`chrome`/`border`/`text`/`accent`/etc.) just to read its `Name` and throwing the rest
away. `SkinSummary` today only exposes `Key`/`Name`/`IsActive`.

**CE-parity check** (`_reference/ComicRackCE`, per the standing rule): confirmed CE has **no**
user-facing theme/skin picker at all — `Theme`/`UseDarkMode` are launch-time-only
`[CommandLineSwitch]`s (`-theme`/`-dark`), never exposed in `PreferencesDialog.cs`. CE also has
**no** application font-family setting anywhere. Its closest "reduce motion" analog,
`DisplayChangeAnimation`, is a narrow page-turn-animation checkbox bundled into the Reader tab's
hardware-acceleration group, not a general UI-motion toggle; a second flag, `AnimatePanels`, exists
in CE's settings model but was never wired to a Preferences checkbox at all. **Net finding:**
nothing in Appearance is filling a CE-parity gap — skins, font selection, and reduce-motion are all
genuine from-scratch Paperbunkr additions. This phase has no CE shape to reconcile against; every
decision below is a clean-slate call.

## Goals

1. **Skins become mini UI-mockup preview cards**, not text rows: each card renders a small fake
   screen — a title-bar sliver (skin's `chrome` color) with an accent-colored dot, a thin sidebar
   block (`surface3`), and a few content lines (`textMuted`) — all in that skin's own real colors,
   on a `bg`-colored card background. Confirmed via the visual companion over color-dot swatches and
   a larger detailed-preview alternative: the mini-mockup card was the clear pick (shows *how a skin
   feels*, not just its palette, at a size that still fits several per row).
2. **Grid layout**, not a vertical list: cards wrap 2-3 per row (confirmed over keeping the current
   list shape) — reads as a gallery of choices, matching how most apps present themes/wallpapers,
   and scales better as more skins get added later.
3. **Active skin indicator:** a `PbAccentBrush` border ring around the selected card plus a small
   checkmark badge in its corner — deliberately the *app's own current accent* (i.e., whatever skin
   is presently applying its resources), not each card's own internal accent color, so the
   selection affordance itself always reads consistently regardless of which skin's colors a given
   card is displaying.
4. **Install Skin folds into the Skins group** — no more separate "INSTALL SKIN" card below. An
   "Install…" ghost button sits in the Skins group's own header row (next to the "Skins" caption),
   with "Open Skins Folder" as a second small action beside it; the install-error banner moves along
   with them.
5. **Fix the hardcoded `#D96C6C`** in the install-error banner (same bug pattern already fixed in
   Connections/Keyboard Shortcuts this session) → `PbDangerBrush`.
6. **Structural consolidation of Motion/Navigation/Developer:** these are three separate groups
   today, each holding exactly one row — three caption headers for three rows is more chrome than
   content. Merge them into a single "INTERFACE" group with three `SettingsRow`s stacked inside one
   caption; Developer's row keeps its own `IsVisible="{Binding IsDebugBuild}"` exactly as today, so
   the group simply shows two rows instead of three in a Release build rather than needing its own
   conditional group wrapper.
7. **Font gets a live preview:** a small sample-text line below the Font family row, inside the
   same FONT group, rendered in whatever font is currently selected — so picking a font shows its
   actual look immediately instead of requiring a trip to a screen that uses body text.

## Non-goals

- No changes to `SkinService`'s actual skin-loading/application logic, the `.crpck`/`theme.json`
  install mechanism, or which 5 skins exist — presentation and information architecture only.
- No live "preview before applying" hover state (hovering a card doesn't temporarily apply it) —
  clicking still applies immediately, same as today; a hover-preview is a bigger interaction change
  this phase doesn't need.
- Font/Motion/Navigation/Developer's *settings themselves* are unchanged (same toggles, same
  commands) — only their grouping (Goal 6) and Font's added preview (Goal 7) change.
- No new skins added, no changes to the 5 existing `theme.json` files.

## Architecture

### 1. `SkinSummary` gains preview colors

`Models/SkinSummary.cs` adds four `IBrush` properties needed to render the mini-mockup:
`ChromeBrush` (title bar), `AccentBrush` (title-bar dot), `SurfaceBrush` (sidebar sliver, from
`surface3`), `TextMutedBrush` (content lines), plus `BackgroundBrush` (`bg`, the card's own
background). Computed once in `SkinService.GetAvailableSkins()` from the already-parsed `theme`
object (`new SolidColorBrush(Color.Parse(theme.Colors.Chrome))` etc. — the same `Color.Parse` this
file already uses elsewhere, e.g. line 178) rather than passing hex strings to the View and parsing
there with a converter; `SkinSummary` stays a plain data-carrying class, no behavior added.

### 2. Skins card: grid of mini-mockup cards

Replace the `Border.groupBox` + `ItemsControl` (currently a vertical `Button.sideItemButton` list)
with:
- A `WrapPanel` (or `ItemsControl` with a `WrapPanel` `ItemsPanel`) so cards flow 2-3 per row and
  reflow at narrower widths, per Goal 2.
- Each card: a `Button` (keeps click → `SelectSkinCommand`, `CommandParameter="{Binding}"`, exactly
  as today) whose content is the mini-mockup — a small `Grid`/`StackPanel` with a title-bar `Border`
  (`Background="{Binding ChromeBrush}"`, a small circle `Background="{Binding AccentBrush}"`), a
  sidebar `Border` (`Background="{Binding SurfaceBrush}"`), and 2-3 content-line `Border`s
  (`Background="{Binding TextMutedBrush}"`) — all sized in fixed small pixels (this is a decorative
  miniature, not a real layout), on a card background bound to `BackgroundBrush`.
- Active state: `Classes.skinActive="{Binding IsActive}"` on the card's outer `Border`, a local style
  setting `BorderBrush="{DynamicResource PbAccentBrush}"` + `BorderThickness="2"` (the *app's*
  accent token, per Goal 3 — not any per-card brush), plus a small checkmark badge
  (`fi:SymbolIcon Symbol="Checkmark"`, `PbSuccessBrush`, same corner-badge shape used for "Connected"
  elsewhere this session) shown only when `IsActive`.

### 3. Install Skin folds into the Skins group header

The Skins group's own header row becomes a `Grid`/`DockPanel`: "Skins" caption on the left,
`Button Classes="headerAction ghost" Content="Install…" Command="{Binding BrowseForSkinCommand}"`
and a smaller icon-only `Button Command="{Binding OpenSkinsFolderCommand}"`
(`fi:SymbolIcon Symbol="FolderOpen"`, `AutomationProperties.Name="Open Skins Folder"`) on the right.
The install-error `StackPanel` (icon + `TextBlock`) moves to sit directly below this header, above
the card grid, with its hardcoded `#D96C6C` replaced by `{DynamicResource PbDangerBrush}` on both
the icon and text (Goal 5). The separate "INSTALL SKIN" `StackPanel`/caption and its 2
`SettingsRow`s are removed entirely — no ViewModel change, `BrowseForSkinCommand`/
`OpenSkinsFolderCommand`/`InstallSkinError`/`HasInstallSkinError` are all reused as-is, only their
View location changes.

### 4. Motion/Navigation/Developer consolidation

The three `StackPanel Tag="appearance.motion"` / `appearance.navigation` / `appearance.developer`
blocks (each today: one `TextBlock.settingsGroupCaption` + one `SettingsRow`) collapse into one
`StackPanel Tag="appearance.interface"` with a single "INTERFACE" caption and all three
`SettingsRow`s stacked inside, in the same order as today (Motion, Navigation, Developer). The
Developer row keeps `IsVisible="{Binding IsDebugBuild}"` on the row itself (not the group) — a
Release build's INTERFACE group just renders two rows instead of three, no empty caption-with-
nothing-under-it case to handle.

**`PreferenceIndex.cs` updates** (confirmed by direct read of `Models/PreferenceIndex.cs`: exactly
5 real entries exist for Appearance today — Skin, Install Skin, Font, Motion, Developer; there is no
existing search entry for Navigation at all, a pre-existing gap). **Correction found during plan-
writing:** `PreferenceIndexTests.AnchorKeysAreUnique` enforces every `AnchorKey` is globally unique
across the whole catalog — three separate entries can't all point at `"appearance.interface"`. The
actual fix is better than the original draft anyway: since Motion/Navigation/Developer become one
UI group, they become **one** consolidated search entry, not three:
- The `"Motion"` and `"Developer"` entries are removed; a single new `"Interface"` entry
  (`GroupTitle`/`Title` = `"Interface"`, `Tag: "appearance.interface"`) replaces both, with combined
  keywords (`"motion", "reduce motion", "animation", "transitions", "nav rail", "hover", "expand",
  "developer", "design showcase", "debug"`) — also closing the pre-existing gap of Navigation having
  no search entry at all, since its terms fold into this same merged one.
- **Further correction, found while running the tests:** `AnchorKeysAreUnique` also forbids
  `"Install Skin"` keeping its own entry pointed at `"appearance.skin"`, since `"Skins"` already
  uses that exact key. Same fix as Motion/Developer: `"Install Skin"`'s entry is deleted and its
  search terms (`"install skin", "crpck", "browse skin", "skins folder"`) fold into the `"Skins"`
  entry's own keyword list — one entry per `Tag`, always, is the real rule this whole section
  should have started from.

### 5. Font preview

Inside the existing `StackPanel Tag="appearance.font"` group, below the Font family `SettingsRow`:
a `TextBlock Text="The quick brown fox jumps over the lazy dog" FontFamily="{Binding
SelectedFontFamily}" FontSize="14" Foreground="{DynamicResource PbTextMutedBrush}" Margin="0,8,0,0"`
— live-updates automatically since it's a direct binding to the same `SelectedFontFamily` the
ComboBox already writes to, no new ViewModel property needed. `FontFamily` accepts a plain family-
name string directly in Avalonia, so no conversion is needed between the ComboBox's string value and
the TextBlock's `FontFamily` property.

## ViewModel changes

None beyond what Architecture §1 already covers (colors computed in `SkinService`, carried on
`SkinSummary`). Every command/property `AppearanceSection.axaml` binds to today
(`SelectSkinCommand`, `BrowseForSkinCommand`, `OpenSkinsFolderCommand`, `InstallSkinError`,
`HasInstallSkinError`, `SelectedFontFamily`, `FontFamilies`, `ReducedMotion`,
`NavRailHoverExpandEnabled`, `IsDebugBuild`, `OpenDesignShowcaseCommand`) is reused unchanged.

## Testing

- `SkinServiceTests`: new assertions that `GetAvailableSkins()`'s returned `SkinSummary` rows carry
  non-null brush properties matching each skin's actual `theme.json` colors (spot-check 2-3 skins,
  e.g. Default's `ChromeBrush` resolves to `#131519`).
- `PreferenceIndexTests`: the new merged "Interface" entry resolves to `appearance.interface` and
  the existing `AnchorKeysAreUnique`/`EveryEntryAnchorResolvesToATagInItsSection` tests still pass
  with 3 Appearance entries instead of 5 (Skin — now also covering Install Skin's search terms,
  Font, Interface).
- Manual on-screen pass (standing no-unattended-GUI caveat): all 5 skins render distinguishable
  mini-mockups; clicking a card applies that skin and moves the accent-ring+checkmark to it; Install…
  and Open Skins Folder both still work from their new header location; a bad file still shows the
  (now `PbDangerBrush`-colored) error; the INTERFACE group shows 2 rows in a Release-style check
  (toggle `IsDebugBuild` off manually or check a Release build) and 3 in Debug; typing in the Font
  ComboBox's search updates the live preview text's font immediately.

## Deliverable

No new `SettingsRow`s for the Skins grid itself (stays a dynamic list/grid, same reasoning as every
other Library/Connections/Keyboard-Shortcuts-shaped phase) — but Font/Motion/Navigation/Developer's
existing `SettingsRow`s are being *regrouped*, not newly created, so nothing new to add to
`docs/preferences-descriptions-todo.md` either.
