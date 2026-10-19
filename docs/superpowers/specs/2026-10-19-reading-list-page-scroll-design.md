# Reading List page scrolling and tag-wall design

**Status:** Draft for review  
**Date:** 2026-10-19

## Problem

On a Reading List detail page, the list's own tags wrap into as many rows as needed. A list with many tags can make the hero taller than the window, pushing the checks and issue content below the visible area. The issue content already has its own scroll viewer, but it cannot help while the unbounded hero and checks occupy the page. The page also lacks the requested scroll-linked depth on its hero artwork.

The attached example shows a dense tag wall in a 1366×720 window. The user wants the tag wall collapsed to two lines, the page content to be scrollable, and parallax while scrolling.

## Goals

- Keep the default hero compact by showing at most two complete wrapped rows of list tags.
- Allow the user to reveal and hide the remaining tags without losing access to any tag action.
- Make the hero, checks, and list content reachable by vertical scrolling, including when the window is short or the header is unusually tall.
- Preserve virtualization for long path and cover lists.
- Add subtle parallax to the hero artwork, using the existing motion policy and disabling continuous motion under Reduced Motion.
- Keep the existing Reading List data, tag search/reweight/removal rules, and user settings unchanged.

## Non-goals

- Changing ComicRack CE parity for list data or importing new list/tag metadata.
- Adding persisted per-list collapse state or new appearance preferences.
- Applying parallax to issue rows, cover cards, the side rail, or the Reader.
- Replacing the existing issue row templates or virtualization panels.

## CE reference check

The `_reference/ComicRackCE` source has no equivalent Reading List detail tag wall, two-line collapse behavior, or parallax effect. CE does use ordinary scrolling for comic-list content (`cYo.Common.Windows.Forms.ScrollControl` and `ItemView`). The two-line tag wall and hero parallax are deliberate Paperbunkr presentation additions; they do not introduce or change persisted CE data.

## Design

### 1. Collapsible tag wall

- The default state is collapsed. The layout shows at most two complete rows at the current available width; it must not cut a chip in half.
- When more tags exist below those rows, show an accessible `Show all tags (N more)` control, where N is the number of currently hidden tags. Expanded state shows every tag and changes the control to `Show fewer tags`.
- Determine overflow from the actual arranged width and chip sizes, so resizing the window, changing display scaling, or changing fonts updates the two-row boundary correctly. Do not approximate two rows with a fixed item count.
- Collapsed-away chips are removed from keyboard navigation and the accessibility tree until the wall is expanded. Visible chips retain their existing search commands and tooltips.
- Keep the existing `Tag`/add-tag control reachable while collapsed, visually separate from the capped chip rows. Adding a tag must not force the wall into expanded state.
- If the current tags fit in two rows, omit the expand/collapse control. The expansion state is transient to the open page and resets to collapsed when another list is opened.

### 2. Page scrolling and virtualization

- Make the main Reading List surface vertically scrollable so the hero, checks/status content, and issue area are all reachable. The cover rail keeps its existing independent scroll behavior.
- Preserve `TidyVirtualizingStackPanel` for Path mode and `VirtualizingWrapPanel` for Covers mode. Do not put an issue panel under an infinite-height measure or replace it with a plain `WrapPanel`/`StackPanel` that realizes the entire list.
- Keep the issue viewport bounded to the space available in the page. When its own viewport reaches an end, normal scroll chaining passes further movement to the page; when the pointer is over other page content, the page scrolls directly.
- Preserve keyboard focus restoration and `BringIntoView` behavior for list rows when the viewport/layout changes. The correct scroll container must remain discoverable by the existing focus and navigation code.
- Use the app's normal scroll behavior and scrollbar affordance; do not add a Reading List-specific speed or intensity preference.

### 3. Hero parallax

- Use the existing overscanned `ParallaxBackdrop` for the hero artwork, behind the current theme-controlled gradient and foreground content.
- The backdrop moves subtly in response to the page's scroll position and stays within its overscan bounds. It does not change layout size or intercept input.
- Respect `MotionPolicy` with `MotionClass.Continuous`; under Reduced Motion the offset remains zero. Do not add a second animation or preference.
- If no hero image is available, retain the existing theme background and gradient with no parallax surface.

## Alternatives considered

1. **Cap tags only and leave the hero fixed.** This is the smallest change and would reduce the worst overflow, but it would not meet the request to make the whole page scrollable when other header content is tall.
2. **One unbounded scroll viewer for every element.** This gives a single continuous scrollbar but causes Avalonia to measure virtualized issue panels with infinite height, realizing every item. This is unsuitable for large lists.
3. **Scrollable page with a bounded, virtualized issue viewport (recommended).** This makes all page regions reachable, retains list virtualization, and uses scroll chaining at the nested viewport boundary. It has a more complex layout than option 1 but fits the user's requirements without changing large-list behavior.

## Accessibility and input

- The expand/collapse control is a keyboard-focusable Button with a stateful accessible name and an automation identifier.
- Collapsed tags outside the two visible rows cannot receive focus; expanding reveals them in the normal tab order.
- Preserve tag chip button labels and existing commands. The add-tag editor remains reachable in both states.
- Keep vertical scrolling available to mouse wheel, touchpad, scrollbar, and existing keyboard navigation. Do not add screen-level key handlers or shortcuts.
- Parallax is decorative and never conveys state.

## Validation

- View tests load the compiled Reading List view and verify the tag wall's collapsed/expanded states, the two-row cap, overflow-control visibility, add-tag availability, and that hidden chips are not focusable.
- Layout coverage verifies the cap responds to a width change and does not split a chip across the clipped boundary.
- Scrolling tests verify that the hero/checks can move, path and cover content remain scrollable, scroll chaining works at the nested viewport edge, and the existing virtualizing panels remain in use.
- Parallax tests cover zero offset at rest, bounded movement while scrolling, no layout/input impact, and zero offset under Reduced Motion.
- Run the focused Reading Lists view tests and any added pure layout/parallax tests; do not run the full App suite for this UI change.
- Visually inspect a list with many tags at a short window height and confirm that Continue/actions, checks, and issue content are reachable without the tag wall blocking selection.

## Risks and mitigations

- **Nested scroll ambiguity:** wheel input may go to the issue viewport when the user expects page movement. Keep normal scroll chaining enabled and verify both directions at each viewport boundary.
- **Responsive chip layout:** a fixed-height clip can leave visually hidden chips keyboard-focusable. Use a width-aware two-row layout that excludes hidden items from focus/accessibility rather than relying only on clipping.
- **Virtualization regression:** an outer scroller can measure its content with infinite height. Keep the issue viewport explicitly bounded and test that the virtualizing panels remain active.
- **Parallax image edges:** use the existing overscan implementation rather than translating a frame-sized image.
- **Existing local edits:** `ReadingListPageView.axaml` and `ReadingListPageViewModel.cs` already contain unrelated uncommitted work in this checkout. Implementation must preserve and build on those edits.

## Open questions

None. The user approved the two-row collapsed wall, scrollable page with virtualization preserved, and Reduced-Motion-aware hero-only parallax on 2026-10-19.
