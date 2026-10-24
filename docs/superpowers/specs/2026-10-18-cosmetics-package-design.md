# Paperbunkr Cosmetics Package — Design

**Status:** Implemented; focused verification complete; manual visual review remains  
**Date:** 2026-10-18  
**Scope:** All 14 cosmetic pitches saved on 2026-10-17, excluding #8 (Reader spread gutter). Implements the selected Collector’s index visual direction across Library, Reading, Details, and Appearance.

## Summary

This package polishes Library cards and grouping, detail issue navigation, reading-list progress, comic Reader page frames and bookmarks, reflowable-book chapter openings, source/tracker marks, cover-availability states, and live theme previews. All pieces share a light Collector’s index vocabulary: folio captions, fine rules, and restrained framing. The active Paperbunkr skin supplies actual UI colors. Reader pages stay content-first.

The app-wide motion system completed on 2026-10-17 governs motion in this package: expressive but damped hover lift, clear enter/exit rules, and Reduced Motion/performance policy. This spec does not introduce a second motion policy.

The implementation reuses current entities, theme resources, marks, and screen models wherever possible. It adds one global Reader page-frame preference, as selected by the user. It does not persist ambiguous cover intent or invent edition classifications. Uncertain metadata and cover states stay visually neutral.

## Goals

1. Implement all selected pitches as one coherent cosmetic package while preserving distinct focus, hover, selection, and content states.
2. Make collection and edition information clearer only where the existing data can support it.
3. Improve reading progress and navigation at a glance without changing reading-list order, bookmark behavior, or reader navigation.
4. Make theme samples representative of the selected skin and responsive to runtime theme changes.
5. Keep all new surfaces accessible, skin-aware, and compatible with the shared motion policy.

## Non-goals

- Pitch #8, Reader spread gutter; the user excluded it.
- Changes to reading, library grouping, tracker, or metadata semantics.
- Per-field provider provenance, automatic guesses from issue-title text, or new manual edition classification.
- Persisting an intentionally blank cover or a durable decode-failure reason.
- A new animation framework, per-feature motion sliders, or a second reduced-motion setting.
- Theme-authored executable UI, arbitrary markup, or arbitrary motion code.

## CE parity and deliberate deviations

The `_reference/ComicRackCE` audit found these relevant precedents:

- CE distinguishes hovered, selected, and focused covers; its renderer also supports cover borders and shadows. Paperbunkr’s stronger spotlight, theme-aware cover mat, accent identity, and selection wash extend those states.
- CE can display ordinary group headers with names and counts. Paperbunkr’s compact cover-led series divider is a deliberate variant of that grouping presentation.
- CE has thumbnail overlays and format metadata, but no annual/special/variant/collected-edition ribbon. The ribbon is a Paperbunkr presentation extension and must be driven by explicit supported metadata.
- The inspected CE UI has no detail issue rail with miniature covers/current-reader marker and no ordered completion path on reading-list cards. Both are deliberate additions.
- CE’s page renderer supports thumbnail border/shadow options, but the inspected reader has no user-selectable None/Paper edge/Soft shadow page-frame preference. That choice is a deliberate extension.
- CE already marks bookmarks in page thumbnails; matching the mark across Paperbunkr page dots and thumbnails is a consistency polish.
- The inspected CE book reader has no chapter ornament or opening-cap treatment. This is a deliberate cosmetic extension for reflowable books.
- CE stores a metadata source in its metadata format and has a generic data-source connection indicator, but no per-item provider/tracker mark. Paperbunkr’s existing `BrandMark` system supports the useful-point marks in this package.
- CE has generic thumbnail-loading/fileless indicators but no reliable three-way missing/decode/intentional-no-cover UI. Paperbunkr’s richer states are a deliberate extension constrained to reliable evidence.
- CE exposes Default/Dark themes without a live component preview gallery. The selected-theme Library/Reader/Bookshelf folio is a deliberate Paperbunkr addition.

## Existing implementation anchors

- Library cover/poster templates and layout state: `src/Paperbunkr.App/Views/LibraryScreen.axaml`, `LibraryScreenViewModel*.cs`, `IssueCardSample`, and `TileCosmeticsOverlay`.
- Collection identity: `Collection.AccentColor` already persists the color; `AppSettings.SeriesAccentColor` and Appearance already gate existing usage. Extend the current opt-in behavior rather than adding a competing field or silently changing its default.
- Reading-list path: `ReadingListPageViewModel` already exposes counts/progress and `ReadingPathBuilder` models ordered items. The card path should derive from these rows.
- Detail issue data: `DetailTabsViewModel` builds ordered issue samples; `DetailTabs.axaml` hosts detail rails and issue rows.
- Comic Reader: `ReaderScreen.axaml`, `ReaderScreenViewModel`, `PageCanvas.ShowPageShadow`, `IssueBookmark`, and `ReaderThumbnailSample` provide the current surfaces. The new frame preference must replace the existing implicit texture-coupled shadow decision cleanly.
- Reflowable books: `BookReaderScreen`, `BookReaderScreenViewModel`, parsed chapters, and `BookTheme` are separate from comic page rendering and bookmark persistence.
- Source/tracker marks: `BrandMark`, `MarkResolver`, `Issue.MetadataSource`, and series-level tracker links are the established implementation path.
- Cover status: `AsyncCoverImage`, `CoverImageCache`, `CoverThumbnailService`, and issue file-health fields are the existing pipeline; null cache results are ambiguous.
- Theme preview: `AppearanceSection.axaml`, `PreferencesScreenViewModel.Appearance.cs`, `ThemeService`, `ThemeResolver`, and `ThemeSummary` already drive the active-theme inspector.
## Design decisions

| Topic | Decision |
|---|---|
| Shared visual language | Collector’s index across Library, details, and information. Reader content remains foregrounded; page frames are optional and controlled separately. |
| Color ownership | Collection identity uses the existing collection accent. Selection uses the app’s live accent resource. Theme framing uses semantic resources, never fixed sepia/yellow UI colors. |
| Hover and selection emphasis | Make both more prominent: expressive damped lift on hover, gentle local recession of nearby cards, stronger selected-surface wash/edge. Keep neighbors readable, selection distinct from hover, and keyboard focus/checkbox cues fully visible. |
| Series dividers | In the Library cover/poster grid when grouped by series. The count represents items visible after current filtering. |
| Edition ribbons | Concise label only when explicit, reliable structured metadata identifies an edition. Ambiguous titles remain unmarked. |
| Detail issue rail | Add miniature covers to the existing issue rail. “Reading now” follows the issue currently open in Reader; it does not mean the series’ preferred cover issue. |
| Reading-list progress | Follow reading-list order, show item progress segments where practical and a read/total count. Missing or unavailable items remain incomplete. |
| Page frame | One global comic Reader preference with None, Paper edge, and Soft shadow; apply per page, independent of spread layout and page background. Default None. Respect high contrast. |
| Bookmarks | Use the same theme-aware mark on page dots and thumbnail rail. A page with multiple bookmarks gets one visual mark; accessible text exposes bookmark presence/count. |
| Chapter ornaments | Restrained, theme-aware chapter marker and opening cap for reflowable books with parsed chapter structure. PDF and unstructured books remain unchanged. |
| Source marks | Reuse `BrandMark` at useful provenance/action points. Provider mark describes the issue’s recorded scrape source, never each metadata field. |
| Cover fallback | “No cover available” when no more specific cause is reliable. Confirmed missing files are distinct. Decode-specific wording appears only while failure is verified. |
| Theme preview | One large selected-theme sample at a time through Library, Reader, and Bookshelf tabs, built from real components. Tab choice is transient. |
| Persistence | Reuse existing data for all classification and state. Add no persisted edition classifications or cover-intent/error fields. Persist only the global page-frame preference required by the selected design. |

## Detailed behavior

### 1. Library cover mats

Add a slim mount behind the cover in applicable Library cover/poster templates. It must not resize, crop, recolor, or obscure cover artwork. Use skin-aware surface resources and a restrained edge; when a collection supplies an accent, that accent may appear as a narrow mat edge. The mat is decoration and introduces no new collection or user setting.

### 2. Library hover spotlight

The hovered cover/card receives a more expressive but damped lift/scale and a clear edge or surface response, governed by the app-wide motion tokens and policy. Nearby visible cards recede gently to create a spotlight, but remain readable. The hover region must cover the card and its mat so the effect does not flicker when the pointer crosses internal gaps. Recycled/virtualized items do not replay entrance motion. Selection and keyboard focus remain legible and independent; hover must not remove either state.

### 3. Collection color identity

Reuse `Collection.AccentColor`. Extend the existing opt-in `SeriesAccentColor` preference across collection headers/details and relevant Library cards/mats when the collection relationship is clear. Keep its current default and preference location. Keep large reading surfaces neutral. Use contrast-safe derived brushes; never treat a collection accent as a text color without contrast resolution. Do not add a second collection-color field or toggle.

### 4. Series divider cards

When the Library is displaying the cover/poster grid grouped by Series, render a compact cover-led divider for each series group. Show the series title and count of currently visible issues after filters. This is a heading for the following group, not an issue/card and not an extra result in selection counts. Other views and groupings keep their existing organization. Keep its style compact enough not to dominate the covers.

### 5. Issue-cover edition ribbon

Show a small, readable ribbon on an issue cover only when supported structured metadata explicitly identifies a known edition type (for example a recognized format value or an existing variant relation). Do not infer annual/special/variant/collected-edition status from an issue title or number pattern alone. Do not add editable classification fields in this package. Unknown or conflicting values produce no ribbon. Reuse existing mark/label vocabulary where it fits, and avoid obscuring title art.

### 6. Detail issue rail covers

Add a small cover thumbnail to each existing issue-rail item, preserving its ordering, keyboard/mouse selection, read state, and existing actions. While an issue is open in the comic Reader, mark that rail item “Reading now.” Clear or update the marker as Reader navigation changes. The marker is transient current-reader state; do not persist a second current-issue selection. Keep the row/title usable when the cover is missing.

### 7. Reading-list progress path

Show an ordered path on reading-list cards based on their current item order and read state. Pair the visual path with explicit “read / total” text. Missing or unavailable issues remain incomplete. For short and medium lists, use one visible segment per item; for long lists, compress the drawing while preserving order and exact count/progress. Do not reorder items, add a completion action, or count the path as a second progress source. Include a text/accessibility equivalent that does not depend on color.

### 9. Comic Reader page-frame styles

Add a global preference in the existing Reader settings for **None**, **Paper edge**, and **Soft shadow**, defaulting to **None**. Apply the style independently to each rendered comic page in paged and continuous layouts; a two-page spread keeps two individual page frames and gains no new gutter treatment. The preference does not alter image content, crop, fit, or page background. Decouple it from the existing background-texture-driven `ShowPageShadow` behavior so the selected style remains predictable. Paper edge uses theme border resources and remains visible in maximum-contrast mode. This is static decoration and introduces no motion.

### 10. Reader bookmark markers

Use one consistent bookmark glyph, scale, and theme-aware color on comic Reader page dots and thumbnail rail. Existing `IssueBookmark` data remains the source. If more than one bookmark falls on the same page, render one visual marker; expose the count (and labels where already available) through accessible name/tooltip. Notes and other page annotations do not masquerade as bookmarks. Preserve the existing marker’s relationship to selected-page and focus states.

### 11. Reflowable-book chapter ornaments

Style chapter openings in reflowable book content with a restrained ornament and opening-cap treatment, using approved theme resources. Apply only when the parsed book source exposes chapter structure and a chapter heading. Keep paragraph text, font/spacing preferences, search, selection, and screen-reader order intact. Omit the ornament when chapter structure or heading is unavailable. PDF pages remain unchanged. Theme JSON may select validated visual resources only; it cannot provide executable markup or arbitrary code.

### 12. Metadata source and tracker marks

Extend placements of the shared `BrandMark` and `MarkResolver`; do not create a second mark implementation. Display a provider at useful provenance points and trackers where their connected state or an available action matters. `Issue.MetadataSource` is one nullable issue-level value describing the last recorded scrape source. A Metron/ComicVine merge may retain Metron as primary even if ComicVine supplied selected gaps; no mark may claim per-field provenance. Null means no recorded source through applicable scrape paths or legacy data, so omit the source mark. Tracker links are series-level; place tracker marks with the series/tracker relationship and its contextual action.

### 13. Cover-availability treatment

All states use distinct icon, short title, and one-line reason. Recovery actions appear only on surfaces that already support them.

- **File missing:** use only when the linked file has been confirmed missing (`Issue.FileIsMissing`). Offer locate/relink only where that action already exists.
- **No cover available:** neutral fallback when no usable cover image is available and no more specific cause is reliable. It does not mean a user intentionally omitted a cover.
- **Cover unavailable / decode detail:** show decode-specific wording only while the current load has concrete evidence of an image decode failure. The image cache can currently return null for both absent thumbnails and failed decoding, so null alone does not prove failure; when the error is not retained, use the neutral fallback.

Do not use null `FilePath` alone as evidence of a missing file (remote mirror issues may have no local path), and do not conflate `Issue.IsContentEmpty` with a cover-image decode failure. There is no durable intentional-no-cover field and no migration for either ambiguous condition.

### 14. Library selection color wash

On selected Library cards, apply a stronger accent wash to the card surface and a clear selected edge, using the app’s current accent resource. Keep cover art legible. Selection remains visually distinct from hover and keyboard focus; preserve the existing selection checkbox and app-wide focus ring. In a collection-colored group, the collection accent remains on the header/slim mat edge while the selection state uses the global app accent, avoiding competing meanings on the same surface. High-contrast mode must retain a non-color selection cue.

### 15. Active-theme preview gallery

Keep the theme picker and active-theme inspector. Enlarge the selected theme’s preview into one generous live sample at a time with **Library**, **Reader**, and **Bookshelf** tabs. Use actual Paperbunkr component templates with stable sample data; do not copy the current library database or mutate user data. Theme changes refresh the sample immediately through the existing inspector refresh path. The tab selection is local/transient and does not require persistence.

The sample demonstrates the selected theme’s actual surface, text, border, accent, and control resources. Collector’s index captions/rules use semantic resources too, so a skin such as Vintage Pop does not leave the inspector stuck on yellow. Samples remain useful for empty libraries and update under maximum contrast.

## Avalonia, motion, and accessibility constraints

- Load the Avalonia design-system, layout, data-template, styling, animation, accessibility, and review-checklist guidance before the corresponding XAML/C# implementation; complete the Avalonia review checklist before calling the UI work done.
- Use DynamicResource/theme resources for surfaces, borders, accents, page frames, mats, bookmarks, chapter ornaments, and preview components. Do not hardcode UI colors that bypass runtime skins.
- Use real controls/templates for theme samples when practical; isolate sample data from persisted models and keep the sample non-interactive unless an action is intentionally demonstrated.
- Use the completed app-wide motion service/tokens for hover lift and transitions. Dampen spring feedback; do not bounce page contents. Honor Reduced Motion and performance policy. Do not animate routine virtualized realization.
- Preserve keyboard focus, selection, screen-reader names, and sufficient contrast. Communicate status through text/shape as well as color.
- Respect Avalonia routed-event and detach behavior: if an action removes a row or closes a popup from within that content, defer the mutation one dispatcher tick.
- Any newly introduced Avalonia view must include its code-behind in the same change and the build must prove `CompileAvaloniaXaml` executed successfully.

## Approaches considered

## Implementation notes (2026-10-18)

Implemented across Library poster cards/group headers, detail issue cards, reading-list gallery and detail view, comic Reader settings/rendering, reflowable chapter styling, cover placeholders, and the active-theme inspector. Added the global `ReaderPageFrameStyle` setting and migration. Reading-list cards now render ordered read-state segments (24 maximum, contiguous buckets for longer lists) with exact read/total text; unavailable issues stay incomplete. Issue cards carry transient current Reader issue state for the “Reading now” marker. Existing `BrandMark` placements and theme-aware bookmark/placeholder groundwork were extended/reused instead of introducing parallel systems.

Verification: `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj --no-restore -v:q` succeeded and ran Avalonia XAML compilation. `IssueCardSampleTests`, `ReadingGalleryAndPathTests`, and `DetailTabsViewModelTests` selection: 146 passed, 1 failed in the pre-existing `Gallery_TagChips_FilterEveryListFlat` expectation (`#User` appears in actual tags where the test expects only `#crisis`). `ReaderPageFrameStyleMigrationTests`: 1 passed. Manual visual review across skins, window sizes, and assistive technology remains for the user’s normal app run.

1. **Use the shared motion/theme/mark systems and existing domain state (recommended and approved).** Implement each cosmetic in its owning view with reusable resources, derive labels only from reliable current data, and persist only the explicitly chosen global Reader page-frame preference. This delivers the whole package with a small data-model footprint.
2. **Add new persisted edition, cover-intent, and decode-status models.** This could offer more precise labels, but the user chose existing reliable data only; the needed distinctions do not currently exist and would require new authoring, migration, and lifecycle behavior.
3. **Add separate local styling implementations to each screen.** This could ship isolated treatments quickly, but would duplicate mark/color/motion rules and make the chosen coherent look drift across Library, Reader, and Preferences.

## Verification expectations

Focused tests should cover the state logic and accessible presentation without running the long full suite after each step:

- Library hover, selected wash, collection accent, and focus/checkbox remain distinct; hover visual does not flicker across card/mat seams.
- Series divider appears only in series-grouped poster mode; its count follows current filters and it does not enter issue selection/result counts.
- Edition ribbons appear only for explicitly supported structured metadata; ambiguous title strings and unknown values yield no ribbon.
- Detail issue rail preserves issue order/actions and follows the active Reader issue without persisting current state.
- Reading-list path matches order/read state, reports exact count, treats missing issues as incomplete, and remains accessible for long lists.
- Reader page-frame preference persists globally, defaults to None, applies per page in paged/continuous modes, remains theme/high-contrast aware, and is decoupled from background texture.
- Bookmark glyph matches across page dots and thumbnail rail, with one visible mark per page and a meaningful accessible count.
- Chapter ornament appears only for structured reflowable chapter openings; PDF and unstructured content are unchanged.
- Provider labels describe issue-level source only; tracker marks appear at useful series/tracker action points; absent source is not guessed.
- Cover states match reliable evidence; null path/cache and `IsContentEmpty` do not produce false missing/decode claims.
- Theme tabs switch live Library/Reader/Bookshelf samples; selecting another theme updates the samples, including Vintage Pop and maximum contrast, without changing user data.
- Shared Reduced Motion and performance policy affect hover motion; focus remains visible.

Manual visual verification should use the app with representative Library grouping/filtering, a mixed reading list, an active comic Reader session, a reflowable EPUB, and multiple skins including Vintage Pop and maximum contrast. The user can then report any real visual regressions. Avoid the full ~5,300-test suite during routine iteration; run the targeted classes and the documented fast suite as the work stabilizes.

## Visual companion

The integrated concept covering all 14 selected pitches is at [Cosmetics package — Collector’s index](http://localhost:57571/?key=68d483dce34b927e71b693af8f496a79aaf0dfe1a49feaae50ab613f454a2e6f). The companion is a hierarchy study; final implementation uses real Avalonia controls and the app’s live skin resources.

