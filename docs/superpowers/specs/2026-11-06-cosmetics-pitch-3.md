# Cosmetics pitch 3 — personalization + Collections (30 items)

Pitch only; nothing built. Per the standing rule, each item must be surveyed against the real code (and CE where CE has the
feature) before it is specced; earlier pitches repeatedly found items that already half-existed. Everything uses tokens
(no hex, no oval pills), `PbMotionEase`, and the existing shared controls.

Anchored on the Edit Collection dialog (`CollectionPropertiesOverlay.axaml`), `Collection.AccentColor/CoverImagePath/IsAutoCover`,
and the 2026-10-18 cosmetics package (collection accent already drives header/mat when `SeriesAccentColor` is on).

## A. Edit Collection dialog (1-8)

1. **Live preview card** — a small card at the top of the dialog showing the collection as the Library and Home shelf will render it
   (cover, accent edge, name, count), updating as name/colour/cover/icon change.
2. **Custom colour picker** — the 7 swatches stay as presets; add a "+" swatch opening hex/HSV entry, and remember the last 6 custom
   colours. Drop the duplicate "current colour" dot (the selected swatch gets a ring instead).
3. **Selected-swatch ring + contrast hint** — ring on the active swatch, and a quiet warning when the chosen colour has poor contrast on
   the active skin (the derived brush is still used for text).
4. **Collection icon/glyph** — pick one FluentIcons glyph (book stack, star, heart, skull, rocket…) shown in the sidebar row, chips and
   header instead of only a dot. Optional; null keeps the dot.
5. **Cover preview + crop** — show the resolved cover thumbnail beside the Auto/Choose buttons, with focal-point crop (drag a
   rectangle) so a manual cover fits the poster and banner ratios. "Auto" also offers "pick which member" instead of always first.
6. **Member rows with thumbnails** — a small cover on each member row (the dialog currently shows text only), plus drag handle
   reorder alongside the chevrons, and a sort button (title / year / read state) that rewrites the manual order.
7. **Description editor polish** — bordered box with a character hint, and inline MarkdownLite (bold/italic/links) rendered in the
   collection header.
8. **Section accordions in the dialog** — Appearance / Members / Related collapse independently and remember their state, so a 100-member
   collection no longer pushes Appearance and Related off-screen.

## B. Collection surfaces (9-18)

9. **Collection banner header** — in a collection view, a wide header using the cover blurred behind the name, description and
   stats (items, read %, total pages), tinted by the accent.
10. **Collection cover styles** — per-collection choice: single cover, 2×2 mosaic, fanned stack, or spine row (reusing the Bookshelf
    spine tile). Applies to Home shelf cards and the sidebar hover.
11. **Pinned / favourite collections** — a star pins a collection to the top of the sidebar and the first slot on Home; pinned rows
    get a subtle accent underline.
12. **Collection progress ring** — the read-progress ring from the Library tile, aggregated per collection, on cards and in the
    sidebar row (opt-in, same toggle family as the series ring).
13. **Collection badges** — optional corner badge on member tiles showing which collection they belong to (accent dot, +N if several),
    so a series in three collections is legible in the Library grid.
14. **Collection sidebar density + dot shapes** — compact/comfortable row height and dot, rounded-square or glyph (from #4); drag to
    reorder with a ghost row and accent drop line instead of the Move up/down menu.
15. **Group by collection divider cards** — extend the series divider cards to a "Group by collection" mode: cover-led divider with
    the collection accent, name and filtered count.
16. **Collection detail tabs underline** — the collection view's tabs (Members / Gaps / Related) share the tab-strip accent underline
    and take the collection accent when the preference is on.
17. **Collection empty states** — illustrated empty states per collection style (accent-tinted glyph, "drag issues here" hint
    with a real drop target), replacing the plain muted sentence.
18. **Collection cards on Home** — larger hero card variant for the most recently opened collection with a "Continue" chip that jumps
    to its next unread member.

## C. App-wide personalization (19-30)

19. **Accent colour override** — one global accent picker (presets + custom) layered over the active theme, with a "reset to theme"
    link and live preview in the Appearance inspector.
20. **Per-library accent** — each library root/section (Comics, Manga, Books) can carry its own accent that tints its sidebar header
    and toolbar, so the three feel like different rooms.
21. **Cover corner radius + mat width** — sliders (square → generously rounded, mat none → wide) previewed on a sample cover; stored
    as tokens so grid, list and detail cohere.
22. **Card shadow / elevation levels** — Flat, Soft, Lifted (three steps) for tiles, applied through the same glow/shadow tiers as
    pitch #3; respects Reduced Motion and high contrast.
23. **UI font pairing** — choose heading and body font from a curated set (bundled + installed), with a size scale of 90 / 100 / 110 /
    125 %; the 10–22 type classes scale proportionally, so the 1366×768 minimum still holds.
24. **Sidebar style** — icons-only rail, icon + label, or labels with accent pill selection; width remembered; optional section
    group headings.
25. **Toolbar button style** — filled, outlined or text-only icon buttons and a toolbar density, driven by the shared `iconButton`
    family rather than per-screen styling.
26. **Background treatment** — plain, subtle grain, soft gradient from the accent, or the current cover blurred (per screen opt-in);
    all behind a surface scrim so text contrast is unchanged.
27. **Custom badge set** — choose which tile badges show (unread dot, format, edition ribbon, Remote pill, collection dot) and where,
    with a drag-to-position preview tile; fixes the known bottom-right overlap of ring / dog-ear / Remote pill.
28. **Selection & focus style** — pick selection treatment (wash, edge only, checkmark corner) and focus-ring thickness/colour;
    high contrast forces the non-colour cue.
29. **Skin import/export + share card** — export the user's overrides (accent, radius, font, badges) as a `.pbskin` JSON and import
    one; validated resources only, no executable content (same rule as theme JSON).
30. **Cosmetic presets** — named bundles ("Quiet", "Collector", "Vivid") that set the items above in one click, with an Undo
    toast and a "Custom" state when any value is then changed; the starter set ships beside the theme gallery.

## Suggested slicing

- **Slice 1 (dialog, low risk, mostly XAML/viewmodel):** 1, 2, 3, 6, 7, 8.
- **Slice 2 (collection identity):** 4, 5, 10, 11, 12, 14.
- **Slice 3 (collection surfaces):** 9, 13, 15, 16, 17, 18.
- **Slice 4 (personalization core, needs settings + migrations):** 19, 21, 22, 23, 24, 28.
- **Slice 5 (stretch):** 20, 25, 26, 27, 29, 30 (30 depends on most of Slice 4).

Likely new persisted data: `Collection.IconGlyph`, `Collection.CoverStyle`, `Collection.IsPinned`, `Collection.CoverFocalPoint`;
AppSettings for accent override, radius, mat width, elevation, font pair/scale, sidebar style, badge layout, preset name.
Remember: add new migrations (never regenerate one the user has launched); AppSettings migrations get a no-op `Down()`.
