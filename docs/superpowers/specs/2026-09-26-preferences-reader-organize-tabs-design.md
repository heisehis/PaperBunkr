# Preferences: Reader and Organize & Scrape sub-tabs

**Date:** 2026-09-26
**Status:** Implemented and checked on screen by the user (2026-09-26). Approved in grilling (one round, every recommendation accepted, tab strip chosen over a sub-nav rail and collapsible groups).
**Builds on:** `2026-09-07-preferences-tile-hub-redesign-design.md` (kept: `SettingsRow`, captioned groups; the single-scroll shell was rejected and stays rejected), `2026-09-26-library-health-subtabs-design.md` (the precedent for tabs inside a Preferences section)

## Problem

**Reader** is one scroll of 11 groups and about 45 rows, in the order the features were added (RTL, Display, Zoom & Navigation, Tap Zones, Panels, Comfort, Profiles, Image Adjustment, Info Panel, Auto-crop, Background). Related settings are far apart (auto-hide toolbar sits with fit mode; image quality, adjustment, crop and background are four separate groups spread over the page) and every new reader feature has been appended at the bottom.

**Organize & Scrape** is two features under one name. Seven stacked blocks: ComicVine key, Source, Matching, Writing, Filters, a Save button in the middle of the page, and Organizer Profiles below the Save button so they look like part of scraping. Only one search entry exists for the whole section. Copy says "ComicVine" although Metron is a selectable source.

The user dislikes a single long scroll for settings screens (2026-09-07), so the fix is discrete views, not folded groups.

## Decisions

1. **Pattern: a tab strip under the section title.** One tab's groups visible at a time. Title and strip stay pinned; each tab has its own `ScrollViewer`, so there is no nested-scroll trap. Groups inside a tab keep the small caps captions.
2. **Shared, reusable pieces** (Library Health is left as built; it was never checked on screen and can move over later):
   - `Models/SettingsTabs` (+ `SettingsTabItem`): the items, the selected one, `Select(key)`. Panels show with `IsVisible` bound to `Tabs.SelectedKey` equal to their key.
   - `Views/Preferences/SettingsTabStrip` (`UserControl`, same shape as `SettingsRow`): a restyled Avalonia `TabStrip` bound to a `SettingsTabs`. Real tab roles for Narrator and arrow-key movement between tabs come from `TabStrip`.
3. **Reader tabs** (every existing setting placed, none dropped):

   | Tab | Group (anchor) | Rows |
   |---|---|---|
   | Pages | Reading direction (`reader.rtl`) | reverse page-turn for RTL |
   | | Layout (`reader.layout`) | default fit, auto-rotate, double-page spread |
   | | Turning (`reader.turning`) | transition, transition speed, reset zoom, skip Deleted, skip Advertisement, pre-open next issue |
   | Controls | Tap zones (`reader.tapZones`) | paged and continuous layout + invert, mouse toggle, previews |
   | | Mouse & controller (`reader.pointerInput`) | side buttons, game controller, wheel speed |
   | | Panels & zoom (`reader.panelsZoom`) | guided view on open, smart double-click zoom |
   | Image | Quality (`reader.quality`) | high quality page display |
   | | Image adjustment (`reader.imageAdjust`) | brightness, contrast, saturation, gamma, auto levels, sharpen |
   | | Auto-crop (`reader.autoCrop`) | trim scan borders |
   | | Background & margin (`reader.background`) | canvas mode, colour/texture, margin |
   | Comfort | Toolbar (`reader.toolbar`) | auto-hide when idle, reveal style |
   | | Session (`reader.session`) | stats chip, eye-rest reminder + interval |
   | | Night (`reader.night`) | warm tint on/off, start, end, strength |
   | | Info panel (`reader.infoPanel`) | show summary straight away |
   | Profiles | Profiles (`reader.profiles`) | default profile, profile list |

   Removed anchors: `reader.display`, `reader.zoomNav`, `reader.comfort` (their rows moved to the groups above).
4. **Organize & Scrape tabs:**

   | Tab | Group (anchor) | Rows |
   |---|---|---|
   | Scrape | Source (`organizeScrape.source`) | Scrape from, API-key / login status + Open Connections (merges the old "ComicVine" key group into Source) |
   | | Matching (`organizeScrape.matching`) | auto-choose, confirm issue, results considered, series art, thumbnails, delay |
   | | Writing (`organizeScrape.writing`) | overwrite, never blank, imprints, fields to fill in |
   | Filters & names | Ignore rules (`organizeScrape.filters`) | before/after year, never-ignore threshold, ignored publishers, ignored search words |
   | | Name mapping (`organizeScrape.names`) | imprint to publisher, publisher aliases |
   | Organize | Organizer profiles (`organizeScrape.profiles`) | the existing `ProfileManagerView` |

   Removed anchor: `organizeScrape.key`.
5. **Save.** Stays explicit: one Save writes the whole `ScrapeSettings` row and the numeric fields validate on save. It moves from mid-page to a footer pinned under the panel, with the status message beside it, visible on Scrape and Filters & names and hidden on Organize (profiles manage themselves). Edits survive tab switches because both tabs share one view model.
6. **Copy.** The scrape descriptions say "the source" / "the matched issue" instead of "ComicVine", because the ignore rules, imprint and alias maps run in the shared `ScrapeOrchestrator` for whichever source is selected. Kept literal: the ComicVine key row, and "ComicVine only" where behaviour really is provider-specific (search-term rewriting).
7. **Search and deep links.** `PreferenceIndexEntry` gains an optional `SubTab` key. One entry per group (Reader 15, Organize & Scrape 6). Opening a hit, or any `RequestScrollToAnchor(anchor)` deep link, first selects the entry's tab, then scrolls and pulses (the existing two-hop post lets the newly visible panel lay out). `Tag` anchors stay on the groups, so `PreferenceIndexTests` still holds.
8. **State.** The selected tab is remembered while the app runs only. No `AppSettings` column, no migration. Preferences reopens on the first tab after a restart.

## How future additions slot in

- New setting: a `SettingsRow` inside the matching captioned group.
- New group: a captioned block in a tab plus one index entry.
- New feature area: one `SettingsTabItem` in the section's tab list, one panel, its index entries. No navigation, search or persistence code.

## Out of scope

- Library Health and every other section keep their current structure.
- No setting is added, removed or changes behaviour; no binding, command or persistence path changes.
- The generic `SettingsTabs` is not persisted (decision 8).

## Testing

- `SettingsTabsTests`: default selection, `Select` known/unknown key, `SelectedKey` change notification.
- `PreferencesScreenViewModelTests`: a search hit for a Reader group in a non-first tab selects that tab; an Organize & Scrape hit selects its tab; a Library Health hit is unaffected; the Organize save footer is visible on Scrape and Filters and hidden on Organize.
- `PreferenceIndexTests`: existing anchor/tag guard, plus every `SubTab` names a real tab of its section and every tab has at least one index entry.
- Headless render of each tab (both views), read as PNG.
- Build with the Avalonia weave check (CLAUDE.md), then run `avalonia-pro-max/review-checklist`. On-screen check by the user: tab switching, keyboard arrows, a search hit into a hidden tab, both skins, Save footer.

## Implementation notes (2026-09-26)

Built as specified. Details worth knowing:

- **Files.** `Models/SettingsTabs.cs` (state), `Views/Preferences/SettingsTabStrip.axaml(.cs)` (strip), `PreferencesScreenViewModel.SectionTabs.cs` (`ReaderTabs`, `ReaderTabKeys`, `RevealSubTab`), `OrganizeScrapeSettingsViewModel.Tabs` / `TabKeys` / `IsSaveFooterVisible`, `PreferenceIndexEntry.SubTab` + `PreferenceIndex.Find`. `ReaderSection.axaml` and `OrganizeScrapeSection.axaml` are rewritten into per-tab panels.
- **`TabStrip` is in `Avalonia.Controls.Primitives`.** The stock selected-item underline is `Border#PART_SelectedPipe`; hiding it needs the selector `TabStripItem:selected /template/ Border#PART_SelectedPipe` (the theme's own rule is `:selected`, so a plain `TabStripItem /template/ ...` loses on specificity). Checked in a throwaway headless render: the strip matches the `segTab` look.
- **Selection sync is by hand, not a TwoWay binding.** `TabStrip` re-selects its first item when its selection is cleared. The strip control fences model-to-strip pushes from strip-to-model writes and ignores null selections.
- **A hidden panel is never templated**, so its groups are not in the visual tree until the tab has been shown once. Search hits and deep links therefore select the tab first and let the shell's existing two-hop post lay it out before scrolling (the same order Library Health uses). Tests visit every tab once before asserting.
- **`RequestScrollToAnchor` also reveals the tab**, so external deep links (Main view model) land correctly, not only search hits.
- **Removed anchors:** `reader.display`, `reader.zoomNav`, `reader.comfort`, `organizeScrape.key`. New: `reader.layout`, `reader.turning`, `reader.pointerInput`, `reader.quality`, `reader.toolbar`, `reader.session`, `reader.night`, `organizeScrape.names`. Reader index entries went from 10 to 15 (Reading direction's title changed from "Right to Left"); Organize & Scrape from 1 to 6.
- **Scrollbar gutter (found on screen).** With the margin on the outer grid, each tab's scrollbar was drawn over the right edge of the rows (dropdown arrows and toggles sat under it). The right margin now lives inside each scrolled panel (`Margin="0,16,28,..."`, and `0,0,28,0` on the Save footer) and the grid has none, so the scrollbar sits at the section's edge with a gutter before the rows. Any new panel in these sections must follow the same shape.
- **Verified on screen by the user:** the tab strips and the scrollbar fix. Not checked: keyboard arrows between tabs, a search hit into a hidden tab, both skins, a long Save status message, narrow widths. The headless tests prove structure and visibility (every Tag'd group sits in the panel of the tab the index names; the footer hides on Organize), not looks.
