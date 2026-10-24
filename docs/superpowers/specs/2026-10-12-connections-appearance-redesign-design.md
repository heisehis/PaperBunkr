# Connections and Appearance redesign (Preferences)

**Date:** 2026-10-12
**Status:** Approved in chat 2026-10-12 (user picked Connections direction A, Appearance direction B, accepted every recommended answer in
rounds 1 and 2, and asked for the spec to be written and implemented in the same step).
**Builds on:** [2026-09-07-connections-redesign-design.md](2026-09-07-connections-redesign-design.md) (per-kind row model, status-bleed fix),
[2026-09-07-appearance-redesign-design.md](2026-09-07-appearance-redesign-design.md) (mini-mockup theme cards),
[2026-09-16-theme-system-design.md](2026-09-16-theme-system-design.md) (what a theme is), and
[2026-09-26-preferences-reader-organize-tabs-design.md](2026-09-26-preferences-reader-organize-tabs-design.md) (the shared sub-tab strip).
Worked out with the visual companion (mockups in `.superpowers/brainstorm/`).

CE parity check: ComicRack CE has no theme picker, no accent choice and no connection-status screen (its online scraping lives in scripts), so
nothing here fills a CE gap. Every decision is a clean-slate Paperbunkr call.

## Goals

1. **Connections becomes a service gallery**: captioned tile groups (Metadata, Trackers, Downloads), each tile showing live status, with the
   selected service's settings in an **inline pane** under the gallery. The per-provider modal is removed.
2. **One Metron tile** replaces three scattered places (source login, "Metron account" sync card, the tracking toggles that mention it).
   Its pane has Account, Sync and Activity tabs.
3. **Status you can trust**: Connected, Syncing, Expired, Unreachable, Not set up, plus a header summary and an attention strip.
4. **Test connection** and an inline-confirmed **Disconnect** in every pane header.
5. **Appearance is split into three sub-tabs**: Theme, Covers & Detail, Home & Interface.
6. **The Theme tab is a gallery + inspector**: grouped theme cards (Dark, Light, Accessibility) and an inspector for the active theme with a large
   preview, palette swatches, computed contrast badges and the per-theme options.
7. **Live samples** for the two settings hardest to judge from a label: glow tier and row density.

## Non-goals

- No new providers, no change to how credentials are stored, no change to any adapter's push/pull logic.
- No background polling of any service. Checks run when the Connections page is first shown (local download clients only) and when
  **Test connection** is pressed.
- No app-wide live theme preview on hover (switching resources while a popup is mid-open caused the 2026-09-10 freeze). Hover and keyboard
  focus highlight a card only.
- No theme editor, authoring UI or `.crpck` export (the separate theme-system track).
- Tracker "Expired" cannot be produced yet (see Test connection scope). The state exists in the model and the UI.

## Connections

### Layout

Header: title, a summary line (`4 of 12 connected`), and the attention strip (hidden when nothing needs attention). Below, three groups with
the shared `settingsGroupCaption` style (the boxed card headers are dropped):

| Group | Tiles | Anchor tag |
|---|---|---|
| METADATA | ComicVine, Metron, Grand Comics DB | `connections.metadataSources` |
| TRACKERS | AniList, MyAnimeList, Shikimori, Bangumi, MangaBaka, MangaUpdates, Kitsu, MangaDex, Behavior | `connections.trackers` |
| DOWNLOADS | Prowlarr, qBittorrent | `connections.downloadAutomation` |

Tiles are a wrap panel (about 150 px wide, so 4 across at the usual content width and 2 below about 720 px). A tile is a real `Button`: brand
mark and name on the first line, status dot and text on the second. A connected tile keeps the accent-soft tint the 09-07 spec introduced; the
selected tile gets an accent border. The Behavior tile shows "N options" instead of a status. The GCD tile shows its data state.

The pane sits under all three groups, full width, and scrolls into view when a tile is chosen from a narrow window.

### Pane

Header row: brand mark, name, status dot + text, **Test connection**, **Disconnect** (visible only when there is something to disconnect).
Disconnect is two-step inline: the first press turns the label into "Confirm?" for 3 seconds (same `DispatcherTimer` shape as
`MissingFileRowViewModel.Remove`), the second press runs the existing command. Prowlarr and qBittorrent have no disconnect command and show none.

Body, by `ConnectionDialogKind`: the existing per-kind forms (OAuth, Token, Credential, CredentialWithClient, Prowlarr, QBittorrent) move
unchanged into the pane. Two kinds are added: `Gcd` (the existing Grand Comics Database block) and `TrackerBehavior` (the seven existing toggles).
Every field keeps its `AutomationId`.

Metron's pane has tabs (`SettingsTabs`): **Account** (the Metron credential form), **Sync** (the existing Metron account sync rows, bound to
`MetronSync`) and **Activity** (last check, last sync text). The "Metron account" card disappears as a separate group.

Every other pane ends with an **Activity** line: `Last checked: never | 12 min ago | <detail>`.

### Status model

`ConnectionStatus`: `NotSetUp`, `Connected`, `Syncing`, `Expired`, `Unreachable`, `Info`.

`ConnectionStatusResolver.Resolve(isConnected, lastCheck, isSyncing)` is a pure function:

1. not connected → `NotSetUp`
2. last check failed → `Expired` if the failure was an auth failure, else `Unreachable`
3. connected and syncing → `Syncing`
4. else `Connected`

`Info` is for tiles that are not accounts (Behavior, and GCD's data state). The row exposes `Status`, `StatusText` and a status brush key.

### Persistence of check results

`ConnectionStatusStore` writes `connection-status.json` next to the database (same location rule as `keymap.json`, so a database-path override
moves it). Shape: `{ "version": 1, "checks": { "<providerId>": { "outcome": "Ok|Failed", "failure": "Auth|Unreachable|null", "checkedUtc": "...", "detail": "..." } } }`.
Writes go through a temp file and replace, and a file that cannot be read is treated as empty (no crash, no data loss of anything but cached
check results). No EF migration.

### Test connection scope (a deliberate narrowing, called out)

The approved plan said every provider gets a Test button backed by a harmless read-only call. Checking the code found that none of the eight
tracker adapters, nor ComicVine or Metron, expose a read-only "who am I" call: `ITrackerAdapter` only has push and entry-read, both of which need a
linked series. Writing eight new HTTP probes that cannot be verified against the real services from here would risk reporting false passes.
So:

- **Prowlarr and qBittorrent**: the button runs the existing `TestProwlarr` / `TestQBittorrent` commands and records the result
  (`Ok`, or `Failed/Unreachable`).
- **Every other provider**: the button is present and reports honestly, with no fake pass: "Credentials are saved. This service has no read-only
  check, so it is verified the first time it is used." Nothing is recorded.
- The `IConnectionProbe` seam (provider id → `ConnectionProbeResult`) is where a real tracker probe plugs in later, which is what will make
  `Expired` reachable. The resolver, store and UI already handle it.

### Attention strip and summary

The strip lists providers whose status is `Expired` or `Unreachable`: "N need attention", the first provider's name and detail, and a button
that selects that tile. The summary counts providers only (ComicVine, Metron, 8 trackers, Prowlarr, qBittorrent = 12), not GCD or Behavior.

### View-model changes

- `ConnectionProviderRow`: add `Group`, `IsSelected`, `IsConfirmingDisconnect`, `LastCheck`, `IsSyncing`, `InfoText`, computed `Status` and
  `StatusText`. Add `ConnectionDialogKind.Gcd` and `.TrackerBehavior`, `ConnectionGroup`, and a `CreateMetadataExtras()`-style factory for the
  two non-account tiles.
- `PreferencesScreenViewModel` (a new partial file, `...Connections.cs`, so the 4,484-line main file does not grow): `MetadataTiles`,
  `TrackerTiles`, `DownloadTiles`, `ConnectionSummaryText`, `AttentionRow`, `HasAttention`, `MetronTabs`, `TestConnectionCommand`,
  `RequestDisconnectCommand`, `RefreshConnectionStatuses()`, and `CheckLocalServicesOnceAsync()`.
- `OpenConnectionDialog` / `CloseConnectionDialog` / `IsConnectionDialogOpen` / `SelectedConnectionProvider` / `ConnectionDialogStatus` keep their
  names (existing tests and the 09-07 status-bleed fix depend on them); they now drive the inline pane. `IsConnectionDialogOpen` is true while a
  tile is selected.
- Search: connection anchors that live inside a pane (`gcdData`, `metronSync`, `trackingBehavior`, and Prowlarr/qBittorrent) select their tile
  (and the Metron Sync tab) before the shell scrolls, through the same `RevealSubTab` hook the other sections use.

## Appearance

### Sub-tabs

`AppearanceTabs` (`SettingsTabs`, in memory only, starts on Theme). Each tab is its own panel with its own `ScrollViewer`, as in Reader.

| Tab | Key | Groups |
|---|---|---|
| Theme | `theme` | themes gallery + inspector, Auto switch |
| Covers & Detail | `covers` | cover cosmetics, detail screens |
| Home & Interface | `interface` | Home, Interface |

`PreferenceIndexEntry.SubTab` is set for every Appearance entry, and `RevealSubTab` handles `PreferencesSection.Appearance`. All existing
group tags are kept.

### Theme tab

**Gallery (left).** `ThemeGroups`: three groups with captions, built from the loaded `ThemeSummary` list. `ThemeSummary.Group` rule:
`category == "accessibility"` → Accessibility, else `mode == Light` → Light, else Dark. `maximum_contrast/theme.json` gains
`"category": "accessibility"` (additive; `colorblind_safe` already has it). Card size is smaller than today (about 104 px) because the inspector
now carries the detail. Clicking a card applies it immediately (unchanged). The header keeps Install… and Open themes folder.

**Inspector (right, about 300 px).** Always shows the **active** theme (`ActiveThemeSummary`, refreshed on every apply):

- name and mode ("Default · Dark"),
- a large mini-mockup built from the same brushes as the gallery cards plus `Surface1`, text and chart colors,
- palette swatches: background, chrome, text, accent, badge, success,
- computed contrast badges: **Text** (text on bg) and **Accent** (accent on bg), shown as `14.6:1`. Below 4.5:1 a badge turns amber. The numbers come from `ThemeService.ContrastRatio`, the same WCAG function the accent override uses. `windows_11`'s accent will honestly show amber (4.08:1),
- the per-theme options: **True black** (dark themes only), **Matrix rain** (Matrix only), **Accent color override**, **Font** with its preview line.

Below about 720 px the inspector stacks under the gallery.

**Auto switch** is a separate group under the pair (mode, dark hour, light hour, auto-enable true black hour), since it is global behavior and
the inspector is per-theme. Tag `appearance.autoSwitch`.

### Covers & Detail and Home & Interface

Rows are unchanged. Two live samples are added:

- **Accent glow**: four small tiles (Off, Subtle, Normal, Vivid) next to the tier selector, each drawn with that tier's ring from
  `ThemeService.BuildGlowRing` using the active theme's glow color, refreshed on theme apply. The active tier is outlined.
- **Density**: three mini row stacks (Compact, Comfortable, Spacious) using `DensityPresets.For(preset)` row padding, active one outlined.

## Search index

New or changed entries (every anchor must still resolve to a `Tag` in its section's `.axaml`, enforced by `PreferenceIndexTests`):

- Appearance: `appearance.skin` (theme gallery), new `appearance.themeOptions` (true black, accent override, matrix rain), new `appearance.autoSwitch`, `appearance.font`,
  `appearance.cosmetics`, `appearance.detail`, `appearance.home`, `appearance.interface`, each with its `SubTab`.
- Connections: the six existing anchors are kept; `connections.trackers` and `connections.metadataSources` and `connections.downloadAutomation` tag the tile
  groups, and `gcdData`, `metronSync`, `trackingBehavior` tag the pane blocks.

## Keyboard and accessibility

Tiles and gallery cards are focusable `Button`s (no per-screen key handlers; arrow keys use the app's directional focus search). Pane inputs
keep their `AutomationId`s. Status text sits in an `AutomationProperties.LiveSetting="Polite"` region so a changed status is announced.
Contrast badges carry `AutomationProperties.Name` such as "Text contrast 14.6 to 1, passes". The tab strips are the existing `TabStrip`.
Dropdowns stay `SuggestBox`. No hardcoded hex: every color is a `Pb*` token or a brush from the theme being previewed.

## Testing

- `ConnectionStatusResolverTests`: each branch of the resolver.
- `ConnectionStatusStoreTests`: round trip, unreadable file → empty, atomic replace, version field.
- `PreferencesScreenViewModelTests` (extended): tile groups hold the right rows; selecting a tile sets `SelectedConnectionProvider` and
  `IsConnectionDialogOpen`; disconnect needs two presses and the confirm resets after the timer; Test connection on a non-probe provider records nothing and says so; attention strip appears for a failed Prowlarr check; summary counts providers only; Metron tabs.
- Appearance: `ThemeGroups` grouping rule for built-ins and an installed theme; inspector follows the active theme after `SelectTheme`; contrast badges (windows_11 amber, default pass); `RevealSubTab` for Appearance; glow and density samples refresh.
- `PreferenceIndexTests` stays green with the new anchors; an added assertion that each Appearance entry has a `SubTab`.
- A headless render of both sections (dark theme and a light theme) to catch binding and layout errors before the real window.
- **Not verifiable here, to be checked by the user on screen:** the real window at several widths, a light theme and Matrix, keyboard walk of the
  tiles, Narrator, and a real Prowlarr / qBittorrent test.

## Risks

- The 09-07 spec confirmed the boxed card headers on Connections as a deliberate exception; this redesign reverses that on the user's answer (Q5).
- `PreferencesScreen` search reveal for hidden panes depends on selecting the tile before the shell calls `BringIntoView`; covered by a test of
  the reveal hook, but the visual scroll needs an on-screen check.
- Moving large XAML blocks (GCD, Metron sync, tracker toggles) into the pane must keep every binding and `AutomationId`; the headless render and
  the existing view-model tests are the guard.
