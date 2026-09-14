# Metadata editors redesign — Issue Properties + Bulk Issue Properties

Date: 2026-09-14
Status: Approved for implementation (via visual companion brainstorming session)

## Scope and how it was decided

Second of the two subsystems decomposed at the start of this multi-day redesign request (Detail screens already shipped — [2026-09-13-detail-screens-redesign-design.md](2026-09-13-detail-screens-redesign-design.md), committed `4b995d9`). This spec covers the single-issue **Issue Properties** editor ([IssuePropertiesScreen.axaml](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml)) and the **Bulk Issue Properties** editor ([BulkIssuePropertiesScreen.axaml](../../../src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml)).

Both currently share the same `groupBox`/`groupHeader` chrome-bar form styling from the 2026-08-23/24 borderless-overlay pass — untouched by the Detail-screens redesign, so they now read as visually older next to it (light chrome-bar section headers vs. the new dark `Border.card` sections).

**Redesign drivers** (user-selected, multiple): visual consistency with the new Detail screens, field density/information architecture, and the Bulk editor specifically. Two follow-up findings from reading the actual files changed scope from the initial framing:

- The Bulk editor is **already** grouped (Main/Artists/Plot & Notes, three separate `groupBox` sections, ~19/8/6 fields) — confirmed by reading [BulkIssuePropertiesScreen.axaml:211-243](../../../src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml#L211-L243). The user confirmed its only real need is the visual restyle, not new grouping.
- The single-issue editor's **Details tab is not grouped at all** — one `groupBox` containing ~25 fields (Number through Is-Final-Issue) in a single `WrapPanel` ([IssuePropertiesScreen.axaml:251-414](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml#L251-L414)). This is the real density gap, and where this spec's one structural change applies.

## 1. Border.card everywhere

Every `Border Classes="groupBox"` + `Border Classes="groupHeader"` pair, in both files, becomes a single `Border Classes="card"` (the same shared style added to `Styles/DetailChrome.axaml` in the Detail-screens redesign, [DetailChrome.axaml](../../../src/Paperbunkr.App/Styles/DetailChrome.axaml) — already globally available via `App.axaml`'s `StyleInclude`, no new style needed). The section title text (e.g. "Overview", "Ratings", "Main", "Artists") moves from the separate `groupHeader` bar into a plain caption `TextBlock` at the top of the card's own content, matching the Detail tab's own card-section header convention (`FontSize="10.5"`, `PbTextFaintBrush`, letter-spaced).

Both files' local `Style Selector="Border.groupBox"` / `Border.groupHeader"` definitions are deleted (dead once nothing references them).

## 2. Issue Properties — Details tab split into Core Details / Credits

Two cards replace the current single "Details" `groupBox`:

- **Core Details**: Number, Volume, Count, Title, Alternate Series, Alternate Number, Alternate Count, Story Arc, Story Arc Number, Series Group, Publisher, Imprint, Format, Book Age, Year, Month, Day, Genre, Tags, Age Rating, Language, Color Mode, Final Issue.
- **Credits**: Writer, Penciller, Inker, Colorist, Letterer, Cover Artist, Editor, Translator.

Same fields, same `WrapPanel`-of-240px-`field`-StackPanels layout within each card — this is a grouping/framing change, not a field-by-field redesign. No `IssuePropertiesScreenViewModel` logic changes; this is purely `IssuePropertiesScreen.axaml` markup reorganized into two `Border Classes="card"` blocks instead of one.

The Bulk editor's existing Main/Artists/Plot & Notes split is **unchanged** in structure (per the user's own confirmation this pass is visual-restyle-only there) — only its `groupBox`→`Border.card` swap applies.

## 3. Icon per field label

Every field label across both editors gets a small leading icon, not just Format/Age Rating/Language (which already use `pbc:BrandMark` inline today, [IssuePropertiesScreen.axaml:366-367,389-391,396-398](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml#L366-L398)). This matches the Detail tab's own read-view convention (icon + caption, e.g. `Symbol="Building"` next to "PUBLISHER", [DetailTabs.axaml](../../../src/Paperbunkr.App/Views/DetailTabs.axaml)).

**Icon mapping** — Format/Age Rating/Language keep their existing `pbc:BrandMark` (unchanged, already icons). Every other field gets a `FluentIcons.Common.Symbol`, reusing the same glyph the Detail tab's own read-view already uses for that concept where one exists (e.g. `Symbol="Building"` for Publisher, matching [DetailTabs.axaml:513](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L513)), and a sensible pick from the existing `Symbol` set for fields with no read-view precedent (Number, Year/Month/Day, Genre/Tags, each credit role). Exact per-field glyph choices are an implementation-time detail - the mockup used placeholder emoji purely for the visual-companion demo, not real icon names.

Applied to **every field** in both editors (Core Details, Credits, the Bulk editor's Main/Artists/Plot & Notes rows, Summary tab, Plot & Notes tab) - the user approved the broad application, not a narrower "Credits only" scope.

## 4. Number-spinner restyle

`TextSpinner`'s current rendering ([TextSpinner.cs:61-85](../../../src/Paperbunkr.App/Behaviors/TextSpinner.cs#L61-L85), styled at [FormControls.axaml:157-173](../../../src/Paperbunkr.App/Styles/FormControls.axaml#L157-L173)) is two bare 16×10px transparent chevron `RepeatButton`s stacked in the textbox's `InnerRightContent`, nearly invisible until hover.

**New treatment** (option B from the visual comparison): a bordered pill stepper — the two chevron buttons share one rounded container (`CornerRadius`, `PbBorderBrush` border) with a divider between them, background fills solid accent (`PbAccentBrush`) with dark text on hover instead of just a foreground-color change. Pure `Styles/FormControls.axaml` restyle of `RepeatButton.textSpinner` (and its container) - `TextSpinner.cs`'s behavior/nudge logic is untouched, this is CSS-equivalent only.

## 5. Insert-token button restyle

The `{ }` token-insert `Button.toolbarPill` (Title/Alternate Series/Story Arc/Series Group fields in Issue Properties, and the equivalent in the Bulk editor's text-kind fields) currently uses the same chrome-bar language as the old `groupHeader` (`PbChromeBrush` background, `PbBorderBrush` border, fully-rounded neutral pill). Restyled to an accent-tinted nub matching the new spinner's visual language: soft accent-tinted background (`PbAccentBrush` at low opacity), accent-colored border and glyph, solid accent fill with dark text on hover - same interaction states as the spinner, different context (a token-insert action, not a stepper), consistent design vocabulary between the two.

Both files' local `Style Selector="Button.toolbarPill"` definitions get this same updated Setter set (still two separate local style blocks per each file's own existing "Avalonia styles don't share across files, so this mirrors rather than references them" convention already documented in both files - not worth extracting to a shared file for two small blocks).

## 6. Genre/Tags Details rows

The Category/Weight editor rows (`TagEditRowViewModel`, [IssuePropertiesScreen.axaml:421-457](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml#L421-L457) - "Genre Details"/"Tags Details" cards, shown only when `HasGenreTagRows`/`HasTagsTagRows`) get two changes:

- **Value → chip**: the plain bold `TextBlock` showing the tag value becomes a small accent-tinted pill/chip (read-only - Category/Weight are what's editable here, the value itself is edited via the Genre/Tags text field above, per the existing doc comment: "adding/removing values stays on the GENRE/TAGS text boxes above").
- **Weight → segmented picker**: the free-typed `pbc:SuggestBox IsStrict="True"` (strict-mode autocomplete against `WeightNames`) becomes a real segmented control - a row of clickable labeled segments, one per `IssueTagWeight` value, the active one highlighted in accent.

**Correction from the visual-companion mockup**: `IssueTagWeight` has **5** values - `Unset`, `Incidental`, `Recurrent`, `Defining`, `Core` ([IssueTag.cs:20-27](../../../src/Paperbunkr.Data/Entities/IssueTag.cs#L20-L27)) - the mockup shown to and approved by the user only displayed 4 (omitted `Incidental`). The real segmented control must have all 5 segments; this is corrected here before implementation, not left as a 4-option control.

`TagEditRowViewModel`'s `WeightText`/`WeightNames` binding surface doesn't need to change - a segmented control still binds to the same string property, just via a different XAML control (a `StackPanel` of toggle-style buttons with `Command="{Binding SetWeightCommand}"`-per-segment, or reusing whatever this app's existing segmented-control idiom is - `DetailTabs.axaml`'s Poster/List/Card view-mode segment at [DetailTabs.axaml:253-277](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L253-L277) is the closest existing precedent to mirror, though that one binds to a command with an enum `CommandParameter` rather than a string - `TagEditRowViewModel` needs checking at implementation time for whether it already exposes a per-weight command or only the free-text `WeightText` setter, and a small ViewModel addition (5 relay commands or one parameterized one) if not).

## Explicitly out of scope

- Any change to which fields exist, or what they write to (`BulkFieldRegistry`/`Issue` entity untouched).
- The Bulk editor's Main/Artists/Plot & Notes grouping (confirmed fine as-is, restyle only).
- The Summary tab's cover/ratings layout, and the Plot & Notes tab's Plot/Notes cards - no complaint raised, get only the universal Border.card + icon-per-field treatment, no structural change.
- Any change to `TextSpinner.cs`'s nudge/step logic, or the token-insertion logic itself - both restyled, not rebuilt.

## Testing

- No new ViewModel logic for most of this (pure XAML/style restyle) - existing `IssuePropertiesScreenViewModelTests`/`BulkIssuePropertiesScreenViewModelTests`-equivalent suites should pass unchanged since no bound property names change.
- If `TagEditRowViewModel` needs new per-weight relay commands for the segmented picker (§6), add unit tests asserting each command sets `WeightText`/the underlying weight to the right value.
- On-screen verification required for every visual change in this spec (card layout, icons, spinner, token button, segmented picker) - no automated test substitutes for "does this look right." Same shared-working-tree caveat as this session's prior work: check concurrent-session activity before launching the app.
