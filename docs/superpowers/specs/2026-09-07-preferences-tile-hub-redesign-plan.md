# Preferences Tile-Hub Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-07-preferences-tile-hub-redesign-design.md*

**Steps 1-11 were all implemented, then Step 2 (shell) was reverted the same session** - tried on
screen, found too annoying to navigate ("infinite scroll"), per direct user feedback. Reverted back
to the sidebar + hard-switch pane (`ActiveSection` restored, tile hub/sticky-strip/search-popup
removed, every section's own `<ScrollViewer>` restored), keeping one improvement from the attempt:
sidebar items now show an icon. Steps 1 (`SettingsRow`), 3-7 (section conversions), 9 (skins), and
10 (description checklist) all stand unchanged - only Step 2's shell and Step 8's `ScrollViewer`
removal were undone.

## Notes from the codebase survey (context for every step below)

- All 9 non-Plugins section files follow `GeneralSection.axaml`'s identical shape: root
  `<ScrollViewer><StackPanel Margin="28,20,28,40" Spacing="20">` containing a `TextBlock.pbTextHeading`
  then N `Border Classes="groupBox" Tag="<anchorKey>"` groups, each a `StackPanel` with a
  `Border Classes="groupHeader"` title followed by `Grid ColumnDefinitions="*,Auto"` label+control
  rows (or `Slider`/`ComboBox` variants) and often a `TextBlock.PbTextFaintBrush` description line
  right below.
- **`SettingsRow.Title` and `.Description` must both accept a bound value, not just a literal** —
  several in-scope rows have live-updating text today (`Text="{Binding PageTransitionDurationMs,
  StringFormat='Page transition speed: {0}ms'}"` as a Slider's title; `WriteAllMetadataStatus`/
  `UpdateCheckResultText` as a status line under an action button).
- **Reuse existing description text, don't leave everything empty.** Motion, Navigation, Rendering,
  Comic File Metadata, and most of Reader's groups already have a `TextBlock.PbTextFaintBrush` line
  directly under the row explaining it — carry that exact text into `SettingsRow.Description`
  instead of leaving it unset. Only rows with genuinely no existing description text go on the
  checklist in Step 9.
- **Compound content:** Reader's "Background color" row is a `ComboBox` (presets) + `TextBox`
  (freeform hex) side by side for one logical setting — `SettingsRow.Content` holds a small
  `StackPanel` with both, not a single control. Not a special case to design around, just noted so
  the row isn't mistaken for two settings.
- **Search results have nowhere to render once the sidebar is gone.** The design doc doesn't specify
  this. Decision made here: the results list becomes a `Popup` anchored below the header's search
  `TextBox` (`IsOpen="{Binding IsSearching}"`), same shape as `StatusBar.axaml`'s peek popover
  (2026-09-07 chrome-motion-polish work) — closes on selecting a result or clearing the query. This
  is a new UI element the design left implicit, not a scope change (search behavior itself is
  unchanged, per the design's own non-goals).

## Step 1: `SettingsRow` primitive

**Files:** `src/Paperbunkr.App/Views/Preferences/SettingsRow.axaml` (new),
`src/Paperbunkr.App/Views/Preferences/SettingsRow.axaml.cs` (new)
**What:** New `UserControl` (per CLAUDE.md's "adding a new Avalonia View" note — code-behind ships
in the same step). Styled properties: `Icon` (`FluentIcons.Common.Symbol`), `Title` (`object`, so
either a literal string or a `{Binding}` works when consumers set it), `Description` (`object?`,
same reasoning), `Content` inherited from `ContentControl`/whatever base fits (or an explicit
`SettingsContent` property if `Content` collides with wanting a custom template — implementer's
call). Layout: `Grid` — icon (fixed-width column) + a `StackPanel` (Title `TextBlock`, then
Description `TextBlock` with `IsVisible` bound via a converter checking for null/empty, so an unset
Description collapses to nothing, no dead space) — then the content column docked right. Add the
light caption-style group-label style (e.g. `TextBlock.settingsGroupCaption` — small, uppercase, no
border/background) to `PreferencesScreen.axaml`'s own `UserControl.Styles` block (matching the old
design doc's own precedent: define locally first, promote to `Primitives.axaml` only if a second
consumer outside Preferences appears).
**Depends on:** none
**Verify:** `dotnet build`. No dedicated unit test for a pure-XAML control with no logic beyond
visibility binding — covered indirectly once a real section (Step 3) uses it.

## Step 2: Shell restructuring

**Files:** `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit),
`src/Paperbunkr.App/Views/PreferencesScreen.axaml.cs` (edit),
`src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:**
- `PreferencesScreen.axaml`: replace the current `Grid ColumnDefinitions="220,*"` (sidebar +
  `ContentHost` Panel) with one `ScrollViewer` containing a `StackPanel`: the tile hub (a `UniformGrid`
  or `WrapPanel` of 9 tiles — icon + label per `PreferencesSection` other than Plugins, using
  `PreferencesSectionMeta.Order`/`Label`), then the 9 non-Plugins sections' `<pref:XSection/>`
  controls stacked in that same order with their `IsVisible` bindings removed (all always visible
  now). Plugins' `<pref:PluginsSection>` moves outside the `ScrollViewer` entirely, `IsVisible` bound
  to a new two-state flag (see below), with a "← Back to Preferences" button.
- Sticky strip: a `Border` positioned above/overlaying the `ScrollViewer` (e.g. in a `Grid` with the
  `ScrollViewer` in the same cell, `ZIndex` above it), `IsVisible` toggled by a
  `ScrollViewer.ScrollChanged` handler in the code-behind comparing the hub's `Bounds.Bottom` against
  the viewport — same technique `DetailHero.axaml.cs`'s parallax handler already uses this session
  (`this.FindAncestorOfType<ScrollViewer>()`, subscribe/unsubscribe on attach/detach). Its own
  "active section" highlight updates from the same handler, comparing each section's `Bounds` against
  the viewport top.
- Both hub tiles and strip icons: `Command` calling a new `RequestScrollToAnchor(string anchorKey)`
  method on `PreferencesScreenViewModel` (see `ViewModelBase`/existing pattern) which raises the
  existing `ScrollToAnchorRequested` event — reusing `PreferencesScreen.axaml.cs`'s existing
  `OnScrollToAnchorRequested`/`ScrollToAnchor`/`Pulse` methods unchanged, except the "two hops to let
  IsVisible flip" comment/delay in `OnScrollToAnchorRequested` is no longer needed (nothing needs to
  become visible first) — simplify to a single `Dispatcher.UIThread.Post`.
- Search: header's `prefSearchBox` stays; the results list moves into a `Popup`
  (`IsOpen="{Binding IsSearching}"`, `PlacementTarget=` the search `TextBox`) — see survey notes
  above. `OpenSearchResultCommand` drops its `ActiveSection = result.Section` line, keeping only
  `ScrollToAnchorRequested?.Invoke(result.AnchorKey)`.
- `PreferencesScreenViewModel.cs`: remove `ActiveSection`, every `Is<Section>Section` (except
  `IsPluginsSection`), every `Go<Section>Command` (except `GoPluginsCommand`), and
  `OnActiveSectionChanged`. Add `RequestScrollToAnchor(string)` (public, wraps the existing event
  invoke). `IsPluginsSection`/`GoPluginsCommand` become a plain two-state toggle (a bool, not an enum
  comparison). Add a bindable property for "which section is currently in view" (e.g.
  `CurrentSectionInView`, a `PreferencesSection?`) that the scroll-tracking handler sets, for the
  sticky strip's active-highlight binding.
- **External callers found during implementation** (a broader search than the design doc's one
  example turned up two more): `MainViewModel.cs:821-827` (`GoLibraryFoldersPreferences`) replaces
  `Preferences.ActiveSection = Models.PreferencesSection.Library;` with
  `Preferences.RequestScrollToAnchor("library.comicFolders");`. `MainViewModel.cs:1801` (update-ready
  toast's "What's New" action) replaces `Preferences.GoAboutCommand.Execute(null);` with
  `Preferences.RequestScrollToAnchor("about.changelog");` (the actual intent is showing the
  changelog, not just landing on About). `MainViewModel.cs:2260` (Activity Center notification-link
  handler, `ActivityLinkKind.Preferences` / `Payload == "Automation"`) replaces
  `Preferences.GoAutomationCommand.Execute(null);` with
  `Preferences.RequestScrollToAnchor("automation.tasks");`. `MainViewModel.cs:2264`
  (`Payload == "LibraryHealth"`, `Preferences.GoLibraryHealthCommand.Execute(null);`) needs **no
  change** — `GoLibraryHealthCommand` stays as a real command (Library Health has always lived
  inside the Library section, not as its own top-level section), only its own body loses the
  `ActiveSection = PreferencesSection.Library;` line, keeping `ScrollToAnchorRequested?.Invoke(
  "library.health")`.
**Depends on:** none (independent of Step 1 — this is navigation/scroll structure, not row content)
**Verify:** `dotnet build`. Update `PreferencesScreenViewModelTests.cs`: remove the ~9
`Go<Section>_SetsActiveSectionFlag` tests for every section except Plugins (test method names found
during the design survey: `GoAppearance_SetsActiveSectionFlag`, `GoGeneral_SetsActiveSectionFlag`,
`GoKeyboardShortcuts_SetsActiveSectionFlag`, `GoConnections_SetsActiveSectionFlag`,
`GoGeneral_FromAppearance_SetsActiveSectionFlag`, `GoReader_SetsActiveSectionFlag`,
`GoAbout_SetsActiveSectionFlag`, `GoLibrary_SetsActiveSectionFlag`,
`GoAdvanced_SetsActiveSectionFlag`); update `GoPlugins_SetsActiveSectionFlag` for the new two-state
flag; add a test for `RequestScrollToAnchor` raising `ScrollToAnchorRequested` with the given key;
add a test for `MainViewModel.GoLibraryFoldersPreferencesCommand`-equivalent now calling
`RequestScrollToAnchor` instead of setting a property. New headless test (mirroring
`DetailHero`'s own parallax test shape from this session) for the scroll-tracking handler: mount the
shell in a `ScrollViewer` harness, scroll past the hub, assert the sticky strip becomes visible and
`CurrentSectionInView` matches whichever section's bounds intersect the viewport top.

## Step 3: Convert GeneralSection

**Files:** `src/Paperbunkr.App/Views/Preferences/GeneralSection.axaml` (edit)
**What:** Remove the root `<ScrollViewer>` (keep the inner `StackPanel` as root). Convert all 4
groups' rows to `SettingsRow`: Startup (1 toggle — no existing description text, add to Step 9's
checklist), Reading (3 toggles, none has existing description text — checklist), Library (1 toggle
— checklist), Window (1 toggle — checklist). Keep each group's light caption label
(`TextBlock.settingsGroupCaption`) in place of the old `Border.groupHeader`. Keep every `Tag`
anchor key unchanged on whatever element now carries it (the outer group wrapper, so
`PreferenceIndexTests`/search-jump keep working).
**Depends on:** Step 1
**Verify:** `dotnet build`. Manual: this is the first real section converted — confirm it renders
correctly before continuing to the rest (General's rows have no conditional visibility or dynamic
bindings, making it the safest first proof of the pattern).

## Step 4: Convert AppearanceSection (partial)

**Files:** `src/Paperbunkr.App/Views/Preferences/AppearanceSection.axaml` (edit)
**What:** Remove root `<ScrollViewer>`. Convert to `SettingsRow`: Font (1 ComboBox — checklist),
Motion (1 toggle, reuse existing "Shortens UI transitions to effectively instant." as
`Description`), Navigation (1 toggle, reuse existing "When off, the rail only expands while pinned
open." as `Description`), Developer (1 action button, `IsVisible` on the row itself stays bound to
`IsDebugBuild` — checklist for description), Install Skin (2 action-button rows: Browse…/Open
Skins Folder — checklist; keep the conditional error `StackPanel` as-is, not absorbed into a row).
**Leave unchanged:** the Skins group (dynamic repeater — deferred, §4 item 8).
**Depends on:** Step 1
**Verify:** `dotnet build`. Manual: confirm Skins picker still renders/functions identically
(untouched code, but visually now sits directly above/below converted rows in the same section).

## Step 5: Convert ReaderSection

**Files:** `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` (edit)
**What:** Remove root `<ScrollViewer>`. Convert all 5 groups: Right to Left (1 toggle — checklist);
Display (6 rows — High Quality toggle checklist, Default Fit Mode ComboBox reuse existing "Used for
any book that hasn't been set..." text, Auto-rotate toggle checklist, Double-page ComboBox reuse
"Used for any series that hasn't been set..." text, Page Transition ComboBox reuse "Animates turning
the page..." text, Transition Speed Slider — bound `Title`, no existing separate description,
checklist); Zoom & Navigation (Reset Zoom toggle checklist, Mouse Wheel Speed Slider — bound
`Title`, checklist); Image Adjustment (4 Sliders — bound `Title` each, reuse the shared "Default
brightness/contrast/saturation/gamma..." line as a group-level caption *above* the 4 rows rather
than per-row, since it describes the whole group not one setting); Background & Margin (Canvas
Background ComboBox reuse "Auto keeps the app's own background..." text, Background Color — compound
`ComboBox`+`TextBox` content per the survey note above, checklist, Margin Enabled toggle checklist,
Margin Width Slider — bound `Title`, checklist).
**Depends on:** Step 1
**Verify:** `dotnet build`. Manual: confirm all 4 live-updating Slider titles still update as the
slider moves (the binding moved from a standalone `TextBlock` into `SettingsRow.Title`, not
removed); confirm Background Color's conditional `IsVisible` (only when mode = Color) still works.

## Step 6: Convert AdvancedSection (partial)

**Files:** `src/Paperbunkr.App/Views/Preferences/AdvancedSection.axaml` (edit)
**What:** Remove root `<ScrollViewer>`. Convert Rendering (Graphics Backend ComboBox reuse existing
long description text, Prefer Native OpenGL toggle reuse existing description text; the standalone
"Changes take effect after restarting Paperbunkr." note applies to the whole group, not one row —
keep it as a small caption below both rows, same treatment as Reader's Image Adjustment group note)
and Comic File Metadata (3 toggles each reusing their existing description text, plus the "Write all
library metadata to files now…" action button whose `SettingsRow.Description` binds to
`WriteAllMetadataStatus` — live status text, not static).
**Leave unchanged:** File Association (checkbox-per-extension repeater) and Backup Manager
(sub-UI) — deferred, §4 item 7.
**Depends on:** Step 1
**Verify:** `dotnet build`. Manual: confirm the two conditionally-enabled toggles (`IsEnabled=
"{Binding WriteMetadataToFiles}"`) still gray out correctly when the parent toggle is off.

## Step 7: Convert AboutSection (partial)

**Files:** `src/Paperbunkr.App/Views/Preferences/AboutSection.axaml` (edit)
**What:** Remove root `<ScrollViewer>`. Convert the Updates group's 3 pieces to `SettingsRow`:
Version (read-only display — `Content` is just the version `TextBlock`, no interactive control;
checklist), Check for Updates (action button, `Description` binds to `UpdateCheckResultText` with
its existing `IsNotNullOrEmpty` visibility check preserved), Check for Updates on Startup (1 toggle
— checklist).
**Leave unchanged:** Changelog (read-only repeater) and Legal (document-launcher buttons) —
deferred, §4 item 9.
**Depends on:** Step 1
**Verify:** `dotnet build`. Manual: confirm `UpdateCheckResultText`'s conditional visibility still
works (empty vs. populated after a check).

## Step 8: Remove root `<ScrollViewer>` from the 4 fully-deferred sections

**Files:** `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit),
`src/Paperbunkr.App/Views/Preferences/ConnectionsSection.axaml` (edit),
`src/Paperbunkr.App/Views/Preferences/KeyboardShortcutsSection.axaml` (edit),
`src/Paperbunkr.App/Views/Preferences/AutomationSection.axaml` (edit)
**What:** Same mechanical fix as every other section — remove the root `<ScrollViewer>`, keep the
inner `StackPanel` as root. No other change: every group/repeater/modal in these 4 files stays
pixel-for-pixel as it is today, per the design's explicit non-goal. This step exists purely because
a `ScrollViewer` nested inside the new shell's single one is a functional bug (competing scroll
capture), independent of whether a section's content changes at all.
**Depends on:** none
**Verify:** `dotnet build`. Manual: confirm scrolling through Library/Connections/Keyboard
Shortcuts/Automation's content works via the outer page scroll (no dead zone, no double-scroll
capture) and that every existing repeater/modal in these 4 files still functions identically
(add a folder, open a tracker connection dialog, edit a keyboard shortcut, toggle a scheduled task).

## Step 9: New skins + Windows 11 fix

**Files:** `src/Paperbunkr.App/Assets/Skins/cool_technical/theme.json` (new),
`src/Paperbunkr.App/Assets/Skins/vibrant_pop/theme.json` (new),
`src/Paperbunkr.App/Assets/Skins/vintage_paperback/theme.json` (new),
`src/Paperbunkr.App/Services/SkinService.cs` (edit)
**What:** Add the 3 new `theme.json` files with the exact values from the design doc §3. In
`SkinService.cs`, replace the current single-special-case `DefaultSkinKey` handling in
`GetAvailableSkins()`/`TryLoadSkin()` with a small static list of embedded built-in skin keys (e.g.
`private static readonly string[] BuiltInSkinKeys = { "default", "windows_11", "cool_technical",
"vibrant_pop", "vintage_paperback" };`), each loaded via its own
`avares://Paperbunkr.App/Assets/Skins/<key>/` root (generalizing what `DefaultSkinAssetRoot` does
today for just `default`) instead of only `default` reading from `avares://` and everything else
reading from `SkinPaths.ExtractedDirectory`. Installed (user) skins still come from
`SkinPaths.ExtractedDirectory` exactly as today, appended after the built-in list.
**Depends on:** none
**Verify:** New `SkinServiceTests` cases: `GetAvailableSkins()` returns all 5 built-in keys;
`LoadSkin("windows_11")` and each of the 3 new keys parses without throwing; each new skin's
`theme.json` round-trips through the same deserialization `TryInstallSkin` already validates.
Manual: open Appearance → Skins, switch through all 5, spot-check contrast/legibility in each
(especially Windows 11's light theme, now reachable for the first time).

## Step 10: Description checklist

**Files:** `docs/preferences-descriptions-todo.md` (new)
**What:** One line per `SettingsRow` left with an empty `Description` across Steps 3-7 (format:
`- [ ] <Section> → <Group> → <Title>`), grouped by section, generated by grepping the converted
`.axaml` files for `SettingsRow` instances with no `Description=` attribute. Handed to the user as
the tracked deliverable from the design doc's own "Deliverable: description checklist" section —
not a design decision, just making sure it's produced.
**Depends on:** Steps 3-7
**Verify:** Manual — spot-check a few entries against the actual `.axaml` to confirm the list is
accurate and complete.

## Step 11: Full verification pass

**Files:** none (verification only)
**What:** `dotnet build` clean. Targeted `dotnet test` runs (per
`[[project_paperbunkr_full_suite_headless_flake]]` — small `--filter` groups, not the whole suite):
`PreferencesScreenViewModelTests`, `PreferenceIndexTests`, `SkinServiceTests`, `MainViewModelTests`
(for the `GoLibraryFoldersPreferences` change). App smoke-launched (isolated `PAPERBUNKR_DB_PATH`,
not the real database — see this session's own note on why). Full manual pass: scroll the whole
Preferences page top to bottom; click every hub tile and every sticky-strip icon; search for a term
that matches a converted row and one that matches a deferred/untouched group, confirm both still
jump+pulse correctly; open Plugins and confirm "Back to Preferences" restores scroll position;
toggle Reduced Motion and confirm the sticky-strip transition and pulse both go instant; switch
through all 5 skins; confirm every row with an empty `Description` collapses cleanly with no dead
space.
**Depends on:** all prior steps
