# App-wide UI polish — audit (what to attack)

Status: **audit / inventory, not a design.** This is the "explore context" input to the brainstorming pass that
will turn it into a polish design + plan. Nothing here has been changed in code.

## How this was gathered, and its limits

- A static sweep of all 141 `.axaml` files under `src/Paperbunkr.App` (Views, Controls, Scraper, Plugins, Styles), measured
  against `avalonia-pro-max/review-checklist` and the app's own tokens in `Styles/AppTokens.axaml`.
- Numbers are grep/regex counts (2026-10-19, working tree as of `26c0ca6` plus uncommitted work), so they're approximate: good for
  ranking, not exact.
- **What it can't see:** how things actually look on screen, alignment that only goes wrong at runtime, contrast on real covers,
  light-theme rendering, or the screens that "just feel off." Section 10 lists what we still need from you or from looking at the app.

## Root cause in one line

The design-language foundation (2026-08-24) defined tokens and a handful of primitives, but most screens were built or restyled
since then with **local literals and per-screen style classes**. Every screen is internally fine, but screens don't match each other,
and that mismatch is most of why the app looks un-clean.

---

## 1. Typography: no real type scale in use  ·  impact: HIGH

| Measure | Count |
|---|---|
| Literal `FontSize="…"` values | **~1,400**, across **29 distinct sizes** |
| `FontSize` via a `PbFontSize*` text token | ~40 |
| `TextBlock`s with no style class at all | 1,493 of 2,264 (66%) |

- The body range is a smear of near-identical sizes: **11 / 11.5 / 12 / 12.5 / 13** (208 / 260 / 299 / 212 / 119 uses). Half-point
  steps read as "slightly wrong," not as hierarchy. Below that sit 10.5 / 10 / 9.5 / 9 / 8 / 7.5 / 7, which are too many tiny sizes
  for a desktop app.
- Screen titles don't share one style. `pbTextHeading` is used on Books, Library, Preferences and Wanted. Literal `FontSize` 22
  on Issue/Bulk Properties and Smart, 20 on Insights and Plugins, and 28 on Reader. The rest have no obvious title text.
- Weights: SemiBold 223, Bold 134, Black 5, Medium 4. Bold and SemiBold appear to be used interchangeably for the same role
  (section/card headings).
- There are ~30 home-grown text classes per screen family (`sub`, `csFaint`, `rlFaint`, `hint`, `fieldNote`, `cardCaption`,
  `rowSub`, `sectionLabel`, `sectionTitle`, `cardHeader`, `cardHeading`, `panelTitle`, …) that mostly mean "muted caption" or
  "section heading."

**Fixed looks like:** about 6 named text roles (Display, Title, Section, Body, Meta, Micro) at whole-pixel sizes, one weight per role,
every screen title through one class, and the per-screen caption/heading classes collapsed into those roles.

## 2. Spacing: no spacing scale  ·  impact: HIGH

| Measure | Count |
|---|---|
| Literal `Margin`/`Padding` values | **1,877** |
| Distinct `Padding` values | **104** |
| Values off the 4/8 grid (odd numbers / halves) | 277, e.g. `9,3` · `11,7` · `7,2` · `6,1` · `0,10,0,4` |
| Spacing tokens available | 1 (`PbSpacingUnit = 4`, unused as a scale) |

- **Page gutters vary by screen:** 28 px (Details, Books, Insights, Prefs, Properties, Continuity), 24 (Home, Plugins, Wanted),
  20 (PDF reader), 12 (Compare), 5 (Library root). When you switch screens, the content jumps sideways.
- Chip/button inner padding drifts (`8,3` · `8,2` · `8,4` · `9,3` · `7,2` · `6,1` · `6,2`), so controls of the same kind are a pixel
  or two different heights from screen to screen.

**Fixed looks like:** a 4-px scale (4, 8, 12, 16, 24, 32) as named tokens, one page-gutter token, and standard control paddings owned
by the control style, not by the call site.

## 3. Corner radius: literals plus static lookups ignore the theme  ·  impact: MEDIUM-HIGH (also a bug)

- 194 literal `CornerRadius` values across **13 distinct radii** (1, 2, 3, 3.5, 4, 5, 6, 7, 8, 9, 10, and mixed per-corner values),
  next to 133 token uses.
- **Bug:** themes rewrite `PbRadius`/`PbRadiusSm`/`PbRadiusLg` at runtime (`Theming/Runtime/ThemeRuntimeApplier.cs:86`), but 21
  `{StaticResource PbRadiusSm}` uses and all 194 literals never pick that up. A theme with sharper or rounder corners only changes
  some of the app. (The 29 `StaticResource PbRadiusChip` uses aren't theme-driven today, but they would become stale the same way
  if that changes.)

**Fixed looks like:** four radii (sm / md / lg / chip), all `DynamicResource`, no literals.

## 4. Colour: hardcoded hex in views  ·  impact: MEDIUM (bug on light themes and skins)

- **76 hex literals in views** and 20 in shared Styles. Top offenders: `PluginScreen` 15, `DetailHero` 10, `SplashWindow` 8,
  `MainWindow` 6, `DetailTabs` 5, `SmartScreen` 4, `BookReaderScreen` 4.
- These ignore skins and light/dark mode. The 2026-08-30 review already found this exact defect class once. Some are deliberate
  (cover scrims, splash art) and should become named tokens. The rest need to move to semantic brushes.
- No direct use of FluentAvalonia's internal brush keys was found, which is good.

## 5. Component sprawl: the same control built many ways  ·  impact: HIGH

This is likely the biggest source of the "not clean" feeling.

- **120 distinct `Button` classes**, and **600 inline `<Style Selector>` blocks inside views** (LibraryScreen 39, MainWindow 36,
  LibraryPreviewPanel 31, LibraryToolbar 29, HomeStyles 26, InsightsHistory 24, DetailTabs 24, ReaderScreen 23, …).
- **Tabs / segmented controls: about 12 implementations.** `tab`, `seg`, `segItem`, `segToggle`, `segTab`, `ipTab`, `lensTab`,
  `ecTab`, `detailsSubTab`, `modeOption`, `pillOption`, `prefNavItem`.
- **Chips / pills: about 18 implementations.** `pbChip`, `chip`, `bandChip`, `filterChip`, `vtChip`, `valueChip`, `tagPill`,
  `previewChip`, `issueChip`, `contrastChip`, `rlChip`, `rangeChip`, `ksChip`, `heroChip`, `countPill`, `toolbarPill`, `clusterPill`,
  `chapterFilterPill`.
- **Cards:** `Border.card` exists (91 uses), but there are also 59 ad-hoc `Border`s with their own background and radius, plus
  `groupBox`, `sectionCard`, `statCard`, `heroTile`, and `floatingPanel` variants.
- 74 `Button`s have no class at all, so they render as stock Fluent buttons inside a custom-styled app.
- The same class name gets redefined locally with different values. `TextBlock.fieldLabel` (the form-label style) is defined
  separately in **12 files**: 9 at 10.5 px, 3 at 10 px, with weight Normal, SemiBold or Bold depending on the file. So identical-looking
  dialogs have labels that differ by a pixel.

**Fixed looks like:** one tab/segmented control (with a `compact` variant), one chip family (neutral / accent / status × filter /
toggle / static), one card and one floating panel, and the per-view style blocks deleted in favour of those.

## 6. Icons: mixed icon systems  ·  impact: MEDIUM

- FluentIcons (`fi:SymbolIcon`) are the official family, but views also draw "icons" with Unicode text: `→` ×29, fullwidth `＋` ×14,
  `✕` ×13, `▾ ▲ ▼` ×11, `↑ ↓ ↔` ×11, `⋯` ×4, `✎` ×3, `✓` ×2, `★`, `⠿`, `↻`, `↵`, `▶`, and one `🔖` emoji.
- Text glyphs follow the *text* font (including the user's font override), so their weight, size and baseline drift next to the real
  icons. That's a classic source of things looking slightly misaligned.

## 7. Accessibility gaps that are also polish  ·  impact: MEDIUM

- **56 of 122 icon-only buttons have no `AutomationProperties.Name`.** By file: ReaderScreen 13, MainWindow 11, IssuePropertiesScreen 10,
  BulkIssueProperties 5, QuickRateOverlay 5, and others with 1–2 each.
- Not checked statically, and needs the running app: hit-target sizes (≥36×36), focus-ring consistency on the restyled controls,
  contrast of `PbTextFaint` on `PbSurface3`, and light-theme contrast.

## 8. Motion: small, specific fixes  ·  impact: LOW-MEDIUM

- 11 of 68 transitions have no explicit `Easing`.
- 3 transitions animate `Width`, which forces a layout pass every frame: `MainWindow.axaml:69` and `:90` (sidebar) and
  `ReaderScreen.axaml:126`. Move them to a `RenderTransform` or clip-based reveal.

## 9. Things that are already in good shape (don't touch)

- The token architecture: `AppTokens.axaml`, `DynamicResource` for colours, and the theme runtime writing live values.
- The input and focus-ring system (see CLAUDE.md, Input handling).
- The motion vocabulary (`PbMotion*` plus the reduced-motion policy).
- The icon-size scale (`PbIconSize*`), which is well adopted: ~480 uses.
- `Border.card`, `floatingPanel`, and the `headerAction primary / secondary / ghost` button trio, which are the right primitives and
  just need everything else folded into them.

## 10. Your answers (2026-10-19)

1. **Worst areas:** Library, the reader, and dialog boxes.
2. **Kind of problem:** mainly inconsistency. "Almost everything looks out of line, even ever so slightly."
3. **Themes:** light themes and skins have to look finished too, not only dark.
4. **Visual companion:** yes. Show rendered options before building.

## 11. Screenshot findings (20 shots, Matrix / Default / Cool / Vibrant / Daylight themes)

Each finding is traced to its source where the code shows it. "Shot n" is the order you sent them in.

### 11a. The cross-cutting causes (these explain most of what you saw)

- **Text boxes have no app-wide style.** `Styles/FormControls.axaml` styles `ComboBox`, `AutoCompleteBox`, `NumericUpDown` and
  `ToggleSwitch`, but not `TextBox`; only the `TextBox.searchBox` class exists. So every form field is a stock Fluent text box, with its
  bottom accent underline, its own background, and Fluent's placeholder brush. That's why:
  - fields underline on some dialogs and not others (shots 1, 18, 20);
  - focused and unfocused fields have very different backgrounds (shot 18: Publisher vs Imprint);
  - in Matrix, the **placeholder "optional" is as bright as a real value** (shots 1, 20).
- **Flyouts and tooltips have no app-wide style.** There are 25 `<Flyout>`s, 0 `FlyoutPresenterClasses`, and no `FlyoutPresenter` or
  `ToolTip` style. That's why the "77 series … aren't in this continuity" popup is **neutral grey inside a purple theme** (shot 19,
  `ContinuityOverviewView.axaml` ~250) and why popovers look different from panels.
- **The dialog close button sits outside the dialog.** `Styles/Overlays.axaml:46` puts the ✕ at `Margin="0,-14,-14,0"`, straddling the
  card's corner. It floats half off the card (shots 1, 10, 20) and is clipped by the window edge when a dialog is tall (shot 2).
- **Dialog titles come in three styles:** Bebas uppercase "EDIT CONTINUITY" / "NEW READING GOAL" (shots 1, 10, 20), serif title case
  "Edit Series" (shot 18), and body text `Edit ""Batman" Dark Nights"` (shot 2).
- **Form labels come in two casings, sometimes in the same form:** "Publisher" / "Imprint" in title case next to "PUBLICATION STATUS" /
  "READING MODE" in caps (shot 18).
- **The theme's badge colour acts as a second accent.** The Detail band's left stripe and the Virtual Tags chip use `PbBadgeBrush`
  (`Views/DetailBand.axaml:112` and `:221`), which stays gold in the blue Cool theme and the pink Vibrant theme (shots 8, 17). It isn't a
  hex bug. The theme's badge colour is simply being used as decoration, so each screen ends up with two accents.
- **Unicode glyphs show up as visibly off-style icons:** `▸ Read`, `▾` on filter chips, `··· Manage`, `✕` on tracker chips, `✓` on
  tracker rows, and `›` / `→` on links (shots 3, 4, 12, 17, 19).
- **Text gets truncated where there's room to show it, or the control is too narrow.** Examples: sidebar entries, "Double-page" and
  "Auto-rotate t…" in Reader Tools (shots 9, 13), the third tab of the Change Cover popup (shot 5), "Vintage Paperba…" (shot 14), and
  "Shared-universe sug…" (shot 1).
- **Raw error text is shown to the user, styled as normal body text:** `MangaBaka (404: {"status":404,"message":"NOT_FOUND"}) …`
  (shot 4) and `Couldn't connect to MangaDex: 400: You must set an appropriate User-Agent header` (shot 3). Neither uses the danger colour
  or an icon.

### 11b. Dialogs (your priority 3)

- **Edit Reading List (shot 2):**
  - the title is double-quoted (`ReadingListPropertiesScreenViewModel.cs:112` wraps a name that already contains quotes; same pattern in
    `CollectionPropertiesScreenViewModel.cs:91`);
  - the dialog is taller than the window, so its top and close button are clipped;
  - the issue list has no top padding, so the first row is cut in half;
  - the "Cover" and "Arc Info" header bars run wider than the content above them, so the right edges don't line up;
  - "Change Cover…" is a different button style from every other button.
- **MangaDex connect (shot 3):** inputs have placeholders only, no labels; there's an **empty outlined rectangle** top-right (a toggle or
  button with no content); and the error isn't styled as an error.
- **New Smart List (shot 11), the area you boxed:** template cards in the two columns have uneven gaps (a large gap under "Blank list",
  tight gaps elsewhere), so the columns never line up. The scrollbar also sits on top of the right column.
- **New Continuity (shot 20):** the "Designation" helper sentence is rendered bold and bright like a value; the publisher dropdown opens
  over the next field; the Workflow dropdown is narrow while the fields around it are full width.
- **New Reading Goal (shot 10):** unselected options aren't one colour ("This month" is black, "Custom range" is grey); the
  "Read ? issues this year" placeholder leaks a literal `?`.
- **Change Cover popup (shot 5):** a card nested inside a card (two borders with different radii), the third tab clipped, and a "#11"
  badge floating in the middle of the grid.

### 11c. Library (your priority 1)

- **Toolbar (shot 12):** the "625 series" count runs into the "Tracked" chip; the filter chips and Sort/Group buttons are different heights
  and styles.
- **Three heading styles on one screen:** "Library" in serif bold, "CONTINUE READING" in Bebas, "COLLECTIONS" in small caps (shot 12).
- **Continue Reading strip:** the third card is cut off hard at the panel edge, with no fade or snap.
- **Tiles:** some covers have the stacked-card edge and some don't; the rating ("3.0"), check-mark, publisher-chip and count-badge corners
  each use their own style; the publisher chip in Matrix covers part of the cover art (shot 7).
- **The hover card** overlaps neighbouring tiles and the preview panel (shots 6, 7, 12), and in shot 6 it shows an issue title
  ("Chapter One: The Justice League") and "Event en" for a series tile.
- **Preview panel:** the action row mixes `▸ Read` (glyph), a text button, an icon button and `…`, all at different widths.
- **Matrix rain draws tofu boxes** (shots 6, 7). `Views/MatrixRainVisualHandler.cs:196` renders Katakana glyphs (line 70) in Cascadia Code
  / Consolas, which have no Katakana. It needs a typeface that has them, or a fallback that checks for the glyph.

### 11d. Reader (your priority 2)

- **Reader Tools panel (shots 9, 13):** content scrolls *under* the "Reader Tools" title, so a "100%" and some icons show through it; the
  two-column tool grid truncates labels; buttons are different sizes and alignments ("Save note" left, "Export notes and clips…" centred,
  "Copy / Save… / Delete" in a row).
- **Top-left collisions (shot 13):** the breadcrumb pill ("… PAGE 6 / 31") runs into the reading-list navigator pill.
- **Bottom-right (shots 9, 13):** faded hint text ("… clips · 1 page") sits behind the Reader Tools panel.
- **Info panel (shot 13):** "Show summary" is a centred text button in a left-aligned panel; "Previous" / "Next" are a different button
  style from "Open details" / "Edit properties."
- **Bottom bars:** the left cluster (Left to Right / Fit Page / 100%) and the centre transport bar are separate floating panels with
  different heights and radii.

### 11e. Elsewhere

- **Series detail (shot 8):** two tabs look selected at once ("Issues" underlined, "Details" with a filled background), and every issue
  shows the same truncated arc name instead of its own subtitle.
- **Manga detail (shot 17):**
  - "Manga" (outlined), "Undo" (large) and "MANGA" (small) sit side by side as three different controls;
  - the "● Ongoing" chip has both a dot and an icon;
  - the `———` rule in the synopsis is raw text.
- **Home in Daylight (shot 15):** the Spotlight carousel keeps a **dark band behind it in a light theme** (the scrim at
  `Views/Home/HomeStyles.axaml:151` is `#8C000000`), and "Needs attention" is cut off by the status bar.
- **Preferences > Connections (shot 3):** tracker logos are all different sizes, and Shikimori (no logo) is indented differently from the
  rows with logos. The same no-logo indent shows up in the Continuity sidebar (shot 20: "Dark Multiverse: Merc…", "Earth 6160").
- **Continuity (shot 19):** under "Apply selected" there's an orange underline with no clear meaning.

### 11f. Not polish: bugs spotted in passing

- MangaDex connect fails: `400: You must set an appropriate User-Agent header` (shot 3).
- MangaBaka tracker sync gets a 404 (shot 4).
- The Matrix-theme status bar says "2,856 comics · 0 MB" and "3,037 comics · 0 MB" (shots 1, 3), while the other themes show real
  sizes.
- Red Hulk shows each issue twice (shot 8). This is a duplicate-issue or variant-grouping question, not a polish one.

---

## Revised attack order

Same principle: foundation first, so screen passes are mostly deletion. The order is re-ranked by your priorities (dialogs and forms
move to the front, since most of the dialog problems come from §11a):

1. **Tokens (§1–3):** spacing scale, page gutter, whole-pixel type scale, radius cleanup. This also fixes the theme-radius bug.
2. **Form and popup foundation:**
   - one app-wide `TextBox` style (background, border, placeholder contrast, no Fluent underline);
   - `FlyoutPresenter` and `ToolTip` styles;
   - an `OverlayShell` close button inside the card;
   - one dialog anatomy: title style, label casing, field spacing, footer, max-height with internal scroll;
   - an error-message style.
   This fixes most of the dialog shots at once.
3. **Primitives (§5):** one tab/segmented control, one chip family, card and floating panel, a button size scale, and classes on the
   unclassed buttons. Decide what the badge colour is for (§11a).
4. **Icons and a11y (§6–7):** glyphs → FluentIcons, the 56 missing automation names, and a no-logo placeholder so list rows line up.
5. **Screen passes:**
   - Dialogs: go through each one against the new anatomy.
   - Library: toolbar, tiles, hover card, preview panel, Continue Reading.
   - Reader: Tools panel, top and bottom bars, Info panel.
   - Then Detail, Home, Continuity, Preferences and the rest.
   Each pass ends with a review-checklist sign-off in **at least one dark and one light theme**.
6. **Motion leftovers (§8)**, the Matrix rain font, and a final DPI sweep.
