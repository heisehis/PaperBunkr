# Edit Collection dialog polish — Design

Slice 1 of [cosmetics pitch 3](2026-11-06-cosmetics-pitch-3.md) (items 1, 2, 3, 6, 7, 8). Grilled 2026-11-06; all nine recommended
answers accepted. Status: **design for review — nothing built.**

## Summary

`CollectionPropertiesOverlay` (the Edit Collection `DialogCard`) is a long stack of equal-weight cards: a bare name/description,
an Appearance card whose "current colour" dot looks like a seventh swatch, a text-only member list, then three rule cards and two
related-item cards. This slice makes it read as a small editor: a live preview, a real colour choice, a member list with covers, drag
and sort, a bordered description with light markdown, and collapsible sections so a big collection no longer buries everything under
the members. **No database migration.** All edits stay buffered until Save, except the rule/relations rows, which keep their current
immediate-write behaviour.

## Goals / non-goals

Goals: items 1, 2, 3, 6, 7, 8 as decided below; reuse shared families and tokens; keyboard-complete; 1366×768 safe.

Non-goals (later slices): collection glyph (#4), cover crop/pick-member (#5), recents for custom colours, pinned/cover styles,
anything outside this dialog except the markdown rendering of the description in the collection header.

## CE parity

ComicRack CE has no collections with accents/covers (its closest concept is a library list/folder), so there is nothing to verify
against `_reference/ComicRackCE`; these are deliberate Paperbunkr features.

## Existing anchors (verified 2026-11-06)

- `Views/CollectionPropertiesOverlay.axaml` (+ `.cs`), `ViewModels/CollectionPropertiesScreenViewModel.cs`, `CollectionMemberRowViewModel.cs`.
- Sections today, in order: Name/Description fields, Appearance, Members, Issues rule, Series rule, Novels rule, Related Collections, Related (series).
- `Collection.AccentColor` (hex), `CoverImagePath`, `IsAutoCover`; `CollectionService.SetAppearance`, `ReorderItems`; `CollectionResolver.GetMembers`.
- Cover resolution: `LibraryTile.FromMember` + `CoverImageCache`, already used by `HomeCollectionCard.FromCollection` (which already supports
  mosaics). The view model's doc comment claiming "no shared resolver" is stale and gets corrected.
- `Controls/MarkdownView.cs` and `Services/MarkdownLite.cs` exist. Drag reorder pattern: `ReadingListPageView.axaml.cs`, `HomeSectionRowDrag.cs`.
- Expand/collapse idiom: `fi:SymbolIcon ChevronRight Classes="chevronToggle" Classes.open="{Binding IsExpanded}"` (QueueView, ReaderScreen).
- **No colour-picker control is referenced** (`Avalonia.Controls.ColorPicker` is not in `Paperbunkr.App.csproj`; FluentAvalonia 3.1.0 is).

## Decisions

| Topic | Decision |
|---|---|
| Colour picker | Keep the 6 preset swatches. A trailing "+" swatch opens a flyout (`PopupFit.ToWindow`, one ScrollViewer) with a hex field and a hue/saturation-value area built from shared slider styling; no new package. A custom colour appears as a 7th, selected swatch. The duplicate current-colour ellipse is removed; the active swatch gets a ring (a selection state style, not a family restyle). "None" stays. |
| Recents | Not in this slice (would need a persisted setting + migration). |
| Contrast hint | One `pbTextCaption` muted line under the swatches when the chosen colour is below 3:1 against the active skin's `PbSurface2` ("Low contrast on this theme"). Advisory, never blocks Save. Uses the existing contrast helper if one exists; else a small pure function with tests. |
| Live preview | Inside the Appearance card, right of the swatches. Mirrors the Home shelf card (cover, accent edge, name, count). Reads the buffered Name, AccentColor and cover (including a pending, unsaved file). Non-interactive, not focusable, `AutomationProperties` text "Preview". Reuses `PosterTile`/the card template rather than copying it. |
| Member covers | 36×54 cover per row via `AsyncCoverImage`/`LibraryTile.FromMember`; decode off the UI thread. Inline list. Above ~150 members covers load lazily as rows realize (the rows live in a virtualizing panel only if the inline list proves slow; otherwise unchanged). |
| Drag + sort | Drag reorders manual rows only (rule-matched rows are fixed and not draggable, as today). Chevrons stay as the keyboard path. A "Sort" menu (`Border.dropdown`/`menuRow`): Title A–Z, Title Z–A, Type. Reorders manual rows only, buffered until Save, so Cancel undoes it. |
| Description | Shared multiline field with a visible border (an `AcceptsReturn` TextBox using the shared style; the underline-only field is kept for single line), a soft 500-character counter (no hard cut-off), a `pbTextCaption` hint line for `**bold**`, `*italic*`, `[links]`. The collection header renders it through `MarkdownLite`; Home shelf and tooltips use plain text. |
| Accordions | Appearance and Members open; **Rules** (the three rule cards merged into one section, three rows) and **Related** (collections + series) collapsed. Chevron per the idiom above; header is a focusable `Button`. State lives in the view model for the session only (static, resets on restart). A section with content (a set rule, existing relations) shows a small count/"set" caption in its header even while collapsed. |
| Persistence | None added. |

## Behaviour details

- **Colour flyout:** hex accepts `#RGB`/`#RRGGBB` with or without `#`; invalid text keeps the last good value and shows the shared
  `Border.inlineError`. Enter applies and closes, Esc closes without change. The close/removal callbacks that originate from inside the
  popup are deferred one dispatcher tick (CLAUDE.md routed-event gotcha).
- **Drag:** drag handle is the row's left edge, not the whole row, so the remove/chevron buttons stay clickable. A drop line in the accent colour
  shows the target. Moving through a rule-matched row's position is not possible; the drop clamps to the manual block order.
- **Preview cover while editing:** "Auto" resolves from the first manual member in the *current buffered order*, so reordering updates
  the preview; "Choose file…" shows the picked file immediately.
- **Description counter** turns `PbDangerBrush` text only at >500 and never blocks Save.
- **Accordion header** text uses `pbTextSection`; the old per-card `FontSize/FontWeight` locals in this file are replaced by type classes.
- **Keyboard:** all new controls reachable by Tab/arrow; drag is never the only way (chevrons, plus Sort). Do not add `KeyDown`
  hotkeys; nothing here needs an action.

## Approaches considered

1. **Extend the existing cards in place (recommended).** Add the pieces to `CollectionPropertiesOverlay` and its view model; reuse the
   existing preview/cover pipeline. Smallest blast radius, matches the buffered Save/Cancel model.
2. **Split into a tabbed dialog (General / Members / Rules).** Cleaner for huge collections but changes the dialog's structure and
   hides Appearance from the preview-while-editing flow; revisit if accordions prove insufficient.
3. **Add `Avalonia.Controls.ColorPicker` and use `ColorView`.** Less custom code, but a new dependency, its own theming, and a look
   that needs restyling to match the design language. Rejected for this slice; reconsider if the hand-built picker is awkward.

## Avalonia / design-language constraints

- Use `paperbunkr-design-language`: type classes not FontSize attributes, tokens only (swatch hexes are data, not view colours),
  4 px grid, shared families (`iconButton` for chevrons/sort, `ghost`/`secondary` for text buttons), `DialogCard` stays the frame.
- Motion via `Reveal.IsOpen` for the accordion bodies and the `chevronToggle` idiom; no transition on layout properties without a
  `motion: reflow` note.
- Load the `avalonia` subskills (layout, styling, data-templates, accessibility, review-checklist) from disk before implementing.
- No new `.axaml` file is planned. If one is (e.g. a `ColorFlyoutContent` view), add its code-behind in the same change and verify
  `CompileAvaloniaXaml` actually ran (CLAUDE.md build gotcha).
- Pre-existing local `FontSize`/`FontWeight` in this file are cleaned as the touched sections are rebuilt; the lint baselines may only go down.

## Accessibility

Swatches and "+" have names ("Accent #C9803F", "Custom colour"); the active one exposes selected state. Accordion headers expose expanded
state. Member rows keep "Move up/down/Remove" names; the drag handle has a name and a tooltip pointing at the chevrons. The preview is
decorative (`AutomationProperties.AccessibilityView="Raw"`). The contrast hint is real text, not colour only.

## Verification

Targeted tests (no full suite): view-model tests for buffered sort/drag order, rule-matched rows staying fixed, Cancel discarding sort,
hex parsing/contrast helper, accordion defaults and session memory, preview reading buffered state; XAML lint trio from the design-language skill;
a headless render of the dialog in default/daylight/matrix (`FoundationStyleTests.WithFoundation`); then the user's screen check at 1366×768 with a
13-member and a 150+ member collection.

## Risks

- Drag in a list that also holds fixed (rule-matched) rows: clamp logic needs tests.
- Cover decode for many rows: relies on `AsyncCoverImage`; watch memory with 150+ members.
- A hand-built HSV area is the largest piece of new UI; keep it small (hue strip + SV square) and fall back to hex-only if it drags on.

## Implementation notes (2026-11-06)

Built per the plan, with three deviations: (1) the markdown rendering of the description in a "collection header" was **not built** - the Library
has no collection header that shows the description (this spec's premise was wrong); it is folded into pitch item 9 (banner header) and the dialog hint
that promised it was removed; (2) the preview card is inline XAML in the dialog rather than a separate `CollectionPreviewCard` control; (3) the member
list stays an inline `ItemsControl` (not virtualized) with lazily resolved covers. New code: `Services/AccentColorInput`, `Controls/HsvColorPicker`,
`Views/CollectionMemberRowDrag`, `Models/AccentSwatchItem`; view-model additions in `CollectionPropertiesScreenViewModel` and
`CollectionMemberRowViewModel`. Not yet seen on screen.
