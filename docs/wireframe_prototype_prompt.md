# Paperbunkr Wireframe/Prototype — Prompt for Claude (design pass)

*Drafted from `docs/onboarding.md` §12 (Wireframes & Avalonia translation) and the reference screenshots in `Paperbunkr wireframe ref/`. Paste the prompt below into a Claude design session (e.g. an Artifacts-capable chat) to generate the actual mockup.*

## Reference audit — what's in the folder, and what it's good for

| Folder | Source app | What to pull from it |
|---|---|---|
| `Already existing interface(ComicRack)` | ComicRack CE (the app being replaced) | Sidebar with **Smart Lists** node + user-created smart-list folders and live counts; tabbed book-detail dialog (Summary / Details / Plot & Notes / Pages / Colors); Scripts/plugin preferences list; a *nested* smart-list example (`Events > Smart lists > 1.1 Commit Proposed Values...`) showing power-user folder organization |
| `CBL manger` | CBL Manager plugin + Omnibus | Reading-list overview with an owned/missing completion table (`14 owned / 16 total`); Omnibus's Reading Lists dashboard (Auto-Build Story Arc, Import from .CBL/.CSV/AniList/MAL, grouped-vs-flat toggle, missing-issue badge) |
| `Mangayomi` | Mangayomi (Mihon-family manga reader) | Left icon-rail navigation with badge counts — this is the cleanest existing precedent for "a plugin adds its own top-level window"; Filter/Sort/Display bottom-sheet pattern; grid density slider; per-item badges (unread count, language, downloaded) |
| `Reader ui` | OpenComic | Reader chrome: top toolbar, collapsible thumbnail rail, bottom reading bar, reading-mode dropdown (Vertical / L-to-R / R-to-L / Vertical continuous / Webtoon / Horizontal continuous (+RTL)) — this list of modes matches the `ReadingMode` enum already decided in §6 |
| `open Stack interface` | Omnibus (Kavita-derived) | **Metadata display** — Writer/Artist/Colorist/Letterer as labeled columns, Teams/Locations/Genres & Concepts as colored pill rows; **Related** tab as a horizontal cover carousel |

## New features already scoped (from your message)

1. **Smart Lists** — saved dynamic filters (ComicRack already has the pattern: `My Favorites`, `Recently Added`, `Never Read`, `Reading`, `Read`, user-defined lists below a divider, live item counts, nestable into folders).
2. **Plugin-contributed windows** — a plugin can register its own top-level view, not just a settings panel or context-menu hook.
3. **Metadata display "from Omnibus"** — the pill-row/labeled-column layout shown above, replacing ComicRack's cramped tabbed dialog.
4. **"Related" feature from Kavita** — a horizontal carousel of series/issues related to the one being viewed.

## Extra patterns worth including that you didn't call out

- **Reading Lists as a sibling of Smart Lists**, not a subset of it — Omnibus and CBL Manager both treat "reading list" (ordered, cross-series, importable/shareable, tracks missing issues) as materially different from "smart list" (dynamic filter over your own library). Worth wireframing both nav entries side by side so the distinction is visible, especially since §11 of the onboarding doc already folds CBL Manager into core.
- **Collection-completion indicator** (CBL Manager's `owned/total (N missing)` table) — pairs naturally with a Smart List like "Missing Issues" and with reading lists; worth a small reusable badge/row component rather than one-off UI.
- **Library toolbar: Filter / Sort / Display as three peer controls**, each opening its own panel (Mangayomi's pattern) — cleaner than ComicRack's single overloaded toolbar, and gives plugins a natural place to contribute custom filters later.
- **Extensible left icon-rail** as the concrete mechanism for feature #2 — core sections (Library, Reading Lists, Smart Lists) pinned at top, plugin-contributed icons appended below a divider, each with an optional badge count. This gives "plugins add their own window" a visual home instead of leaving it abstract.
- **Reader chrome** (toolbar + collapsible thumbnail rail + bottom reading bar + reading-mode dropdown) — not one of your four features, but the reader is the app's flagship screen and the onboarding doc (§8) treats it as the highest-risk component; a wireframe pass should cover it even briefly so the other four features have a home to launch *from* (e.g. reading-mode override per series).
- **Skins/Preferences pattern** (ComicRack's left-icon-rail preferences dialog: Reader / Libraries / Behavior / Scripts / Advanced) — reusable shell for wherever Smart List rules and plugin-window settings end up living.

## Visual direction (synthesized from the refs, needs your confirmation)

Every reference is a dark UI: ComicRack CE reskins go near-black with warm amber/gold accents (matches the CBL/Omnibus reading-list screens), Mangayomi uses near-black with a violet/purple accent, Kavita's default is near-black with green. Given "Paperbunkr" leans into a bunker/archive identity (per §1 of onboarding.md), recommend **near-black base + a single warm accent (amber/copper)** — closest to the CBL/Omnibus screens, distinct from both ComicRack's default light-gray chrome and Kavita's green so Paperbunkr doesn't read as a reskin of either. Flag this as a decision point in the prompt below rather than locking it silently.

---

## The prompt to paste into Claude

```
I'm designing the UI for Paperbunkr, a desktop comic/manga library and reader app (Avalonia/.NET, Windows-first, local-first — think a ground-up rewrite of ComicRack with Mihon/Tachiyomi-style manga features and Kavita-style metadata polish). I need an interactive HTML/CSS wireframe (not production code — this gets manually translated to Avalonia XAML later, so favor clear layout/component boundaries over pixel-perfect fidelity) covering the screens below.

VISUAL DIRECTION
Dark UI, near-black base (#0d0d0f range), single warm amber/copper accent for active states, badges, and primary actions. Clean sans-serif type. Design it as a token system (color, spacing, radius, type scale) expressed as CSS custom properties at the top of the file, not hardcoded per-element — this app skins itself via installable theme packs, so the token boundary matters. I'm open to a different accent if you see a stronger direction; call it out rather than picking silently.

APP SHELL
- Leftmost: icon rail. Pinned core sections at top (Library, Reading Lists, Smart Lists), a divider, then plugin-contributed sections below with optional badge counts (this rail IS the mechanism for "plugins can add their own window" — show at least one example plugin icon here, visually distinct enough to suggest it's not core).
- Next: a collapsible tree/list sidebar scoped to whatever rail section is active (e.g. under Smart Lists: built-ins "My Favorites / Recently Added / Never Read / Reading / Read" above a divider, then user-created smart lists below, each with a live item count; support at least one nested folder to show smart lists can be grouped).
- Main content area: grid of cover thumbnails, standard library view.

LIBRARY TOOLBAR
Three peer controls — Filter, Sort, Display — each a labeled button that opens its own dropdown/panel (not one combined overflow menu). Display panel includes a grid-density slider and toggles for which badges show on covers (unread count, missing-from-collection, downloaded).

SERIES DETAIL SCREEN
- Cover, title, quick actions (Continue Reading, Favorite, Edit).
- Metadata block in the Omnibus style: labeled two-column groups for Writer/Artist/Colorist/Letterer etc., then tag-pill rows for Teams, Locations, and Genres & Concepts — each pill row on its own labeled section with an icon, not one big tag soup.
- Tabs: Issues, Related, Details, Activity.
- Related tab: horizontal scrolling carousel of cover thumbnails (Kavita-style) with left/right nav arrows, title under each cover.

SMART LISTS SCREEN
- Rule builder: plain-language condition rows ("Genre is Horror", "Read = false"), AND/OR grouping, live-updating match count as rules change.
- Show it mid-edit with 2-3 conditions, not empty state.

READING LISTS SCREEN (separate from Smart Lists — this is ordered, cross-series, and tracks a completion state)
- Left: list of reading lists with issue counts.
- Detail: ordered issue list grouped by series/arc, a completion indicator ("14 owned / 16 total, 2 missing" — missing issues visually distinct, e.g. dimmed with a warning glyph), actions for Import (.CBL/.CSV/AniList/MyAnimeList) and Auto-Build from a tracked story arc.

PLUGIN WINDOW EXAMPLE
Pick one plausible plugin (e.g. a "Tracking Sync" or "Duplicate Finder" plugin) and mock its full-window view as it'd appear when its icon-rail entry is selected — should look like a first-class app screen, not a cramped settings tab, to make the point that plugins get real UI real estate.

READER (brief — secondary priority, just enough to anchor the other screens)
- Top toolbar: back/library breadcrumb, page counter, zoom, reading-mode dropdown (Vertical / Left to Right / Right to Left / Vertical continuous / Webtoon / Horizontal continuous / Horizontal continuous RTL).
- Collapsible left thumbnail rail.
- Bottom reading bar with scrubber.

DELIVERABLE
One interactive HTML file, sections/screens navigable via the icon rail (clicking rail icons or tabs actually switches visible content — doesn't need real data logic, just enough interactivity to demo the navigation model). Comment the token block clearly since it becomes the theme-pack schema.
```

---

**Before you run this**: confirm the amber/copper accent direction, or tell me a different one and I'll adjust the prompt. Also worth deciding whether you want the reader screen in this same pass or split into its own dedicated wireframe session later, given §8 of the onboarding doc treats it as the highest-risk, most novel component in the whole app.
