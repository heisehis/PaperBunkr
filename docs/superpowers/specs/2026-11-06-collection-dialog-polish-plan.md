# Edit Collection dialog polish — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-06-collection-dialog-polish-design.md*

Order is chosen so each step compiles and is testable alone. No migration anywhere. The tree already has many uncommitted edits from other
work: touch only the files named here, run only the targeted tests, never start a second `dotnet build`/`test` at once, and check
`Get-Process testhost` first. Before any XAML step, `Read` `~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md` (and the
layout/styling/data-template subskills it points to) from disk.

Every step ends with: build `src/Paperbunkr.App` (verify `CompileAvaloniaXaml` ran, not just "0 errors"), and the lint trio
`--filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"`.

## Step 1: Pure helpers (hex parse + contrast hint)
**Files:** `src/Paperbunkr.App/Services/AccentColorInput.cs` (new), `src/Paperbunkr.App.Tests/AccentColorInputTests.cs` (new)
**What:** `TryParseHex(string, out Color)` accepting `#RGB`/`#RRGGBB` with or without `#`, `ToHex(Color)` (upper-case `#RRGGBB`),
HSV<->RGB conversion for the picker, and `IsLowContrast(Color accent, Color surface)` using `ThemeColorMath.ContrastRatio` with the 3:1 threshold.
**Depends on:** none
**Verify:** new test class (valid/invalid hex, round-trip HSV, threshold edge cases including near-black/near-white on dark and light surfaces).

## Step 2: View-model state — colour, preview, description, sections
**Files:** `ViewModels/CollectionPropertiesScreenViewModel.cs` (edit), `CollectionPropertiesScreenViewModelTests.cs` (edit)
**What:** add `IsCustomAccent` (accent not in `AccentSwatches`), `CustomAccentHex` + `ApplyCustomAccentCommand` (uses step 1; invalid keeps last good and sets
`CustomAccentError`), `AccentContrastWarning` (recomputed when `AccentColor` changes, against the active skin's `PbSurface2` resolved through a small
injected `Func<Color>` seam so tests don't need Application), `DescriptionLength`/`DescriptionOverLimit` (soft 500, never blocks Save), and section
state `IsAppearanceOpen`/`IsMembersOpen` (default true), `IsRulesOpen`/`IsRelatedOpen` (default false) backed by static session fields,
plus `RulesSummary`/`RelatedSummary` header captions. Correct the stale "no resolver" doc comment.
**Depends on:** Step 1
**Verify:** tests: custom colour detection, invalid hex handling, warning flips with the injected surface, description counter, section defaults and
session persistence across two instances, Save still passes `NullIfEmpty` values.

## Step 3: Member rows with covers, sort, drag-order logic
**Files:** `ViewModels/CollectionMemberRowViewModel.cs` (edit), `CollectionPropertiesScreenViewModel.cs` (edit), tests (edit)
**What:** each row exposes a lazily-resolved cover (`CoverImage` via `LibraryTile.FromMember` + `CoverImageCache`, loaded on realization off the UI
thread; `CoverBrush` fallback from `SeriesCardSample.CoverBrushFor`). Add `SortMembersCommand` (Title A–Z / Z–A / Type) that reorders only manual rows
(rule-matched rows keep their position), and `MoveMemberTo(dragged, target)` that clamps to the manual block. Both buffered; Cancel discards.
Add `PreviewCover` that follows the first manual member in the *current* order unless a manual file is set (feeds step 5).
**Depends on:** Step 2 (same VM; no code dependency beyond file)
**Verify:** tests: sort leaves rule-matched rows fixed, drop clamps, Cancel leaves DB order untouched, Save persists the new order via `ReorderItems`, preview cover follows reorder.

## Step 4: Drag behaviour
**Files:** `Views/CollectionMemberRowDrag.cs` (new, modelled on `Views/Home/HomeSectionRowDrag.cs`), no tests beyond step 3 logic
**What:** attached `Handle`/`Owner` properties; in-process `DataFormat`; handle pressed starts `DragDrop.DoDragDropAsync`; drop calls `MoveMemberTo`. Rule-matched
rows never start a drag. Drop-line indicator state is a bool on the row (`IsDropTarget`) set during `DragOver`. Defer any collection mutation that
originates inside a routed event by one dispatcher tick (`Dispatcher.UIThread.Post`).
**Depends on:** Step 3
**Verify:** build + lint; behaviour confirmed on screen in step 8 (headless drag is not worth faking).

## Step 5: Preview card control
**Files:** `Views/CollectionPreviewCard.axaml` + `.axaml.cs` (new, code-behind in the same change), `CollectionPropertiesOverlay.axaml` (edit)
**What:** non-interactive card mirroring the Home shelf card (reuse `PosterTile`/existing card template pieces; read the buffered Name, AccentColor, cover,
member count). Not focusable; `AutomationProperties.AccessibilityView="Raw"`. Placed inside the Appearance card right of the swatches.
**Depends on:** Steps 2, 3
**Verify:** build (confirm weave ran, per CLAUDE.md gotcha); a headless render in default/daylight/matrix via `FoundationStyleTests.WithFoundation`.

## Step 6: Colour picker UI
**Files:** `Views/AccentColorFlyout.axaml` + `.axaml.cs` (new), `CollectionPropertiesOverlay.axaml` (edit)
**What:** remove the duplicate `Ellipse`; swatch buttons get a `.selected` state ring (state style, not a family restyle); trailing "+" swatch opens a
`Popup` with `controls:PopupFit.ToWindow="True"`, one `ScrollViewer`, a hex `TextBox`, a hue strip + SV square (small custom control, `Slider`-style tokens),
Enter applies/closes, Esc closes. Names on swatches ("Accent #C9803F", "Custom colour"). The contrast warning line sits below the swatches (`pbTextCaption`).
Popup close from inside content is deferred one tick.
**Depends on:** Steps 1, 2
**Verify:** lint trio; headless render; on screen in step 8. If the HSV area becomes unwieldy, ship hex-only (documented fallback).

## Step 7: Description, accordions, member list XAML
**Files:** `CollectionPropertiesOverlay.axaml` (edit), `Views/CollectionPropertiesOverlay.axaml.cs` (edit if needed)
**What:** bordered multiline description using the shared field style, counter + hint line (`pbTextCaption`); wrap Appearance, Members, Rules (the three
rule cards merged), Related (both related cards merged) in accordion cards: header is a `Button` with `chevronToggle` icon (`Classes.open`), body in
`controls:Reveal.IsOpen`; headers use `pbTextSection`, with the summary captions. Members template gains the cover, drag handle (`Handle`/`Owner`
from step 4), Sort `iconButton` + `Border.dropdown` menu. Replace local `FontSize`/`FontWeight` in the touched sections with type classes.
**Depends on:** Steps 2-6
**Verify:** lint trio (baselines may only go down); `RealWindowKeyboardTests`-style tab-order check not needed, but walk Tab through every new control on screen.

## Step 8: Markdown description in the collection header
**Files:** the Library collection header view that shows `Description` (locate with grep for the header bound to the collection description; likely `LibraryScreen.axaml` / `CollectionHeader*`), tests (edit/new)
**What:** render the description with `Controls/MarkdownView` (`MarkdownLite`) there; Home shelf and tooltips stay plain text.
**Depends on:** none (independent; can run any time)
**Verify:** a `MarkdownLite` rendering test for bold/italic/link; on screen.

## Step 9: Review and on-screen pass
**Files:** none new
**What:** run `avalonia-pro-max/review-checklist`; full targeted tests: `CollectionPropertiesScreenViewModelTests`, `AccentColorInputTests`, the lint trio, and
`--filter "Speed!=Slow"` once at the end only if the user asks. Then user screen check at 1366×768 with the 13-member and a 150+ member collection
(covers, drag, sort, accordions, colour flyout, low-contrast hint on Daylight, Matrix). Record deviations in the spec's "Implementation notes" and add a
cosmetics-pitch-3 Slice 1 line to `docs/paperbunkr-todo.md` by hand (what was verified, not just the commit message).
**Depends on:** Steps 1-8
**Verify:** the items above; do not claim done until the user has seen it on screen.

## Risks to watch while building
- Drag among fixed rule-matched rows (clamp tests in step 3).
- 150+ covers decoding at once (realization-time load; check memory).
- New `.axaml` files must ship with code-behind in the same step or `AVLN2000` bites; verify the weave, not just "0 Errors".
- A running Paperbunkr locks `bin`; compile-check with a scratch `-o` directory and never kill the app.

## Status (2026-11-06)

Steps 1-7 and 9 (build, targeted tests, lint, headless render) done; Step 8 not built (no collection header exists - see the design doc's
Implementation notes). Step 5 shipped as inline XAML, not a separate control. On-screen check by the user is outstanding.
