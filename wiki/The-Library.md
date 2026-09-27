# The Library

The **Library** (navigation rail → *Library*) is where you browse everything PaperBunkr
has scanned. It shows your collection as a grid of covers with a toolbar across the top
and a filter sidebar on the left.

## The sidebar

- **All Series** — the whole library, with a count.
- **Collections** — buildable groupings you define (see *Collections* below). Hover a collection
  (or Tab to it) for its **⋮** menu (Edit, Move up / Move down) and a delete button.
- **Content Type** — quick filters for *Comic*, *Manga*, *Manhua*, *Manhwa*.

The status bar at the bottom shows how many issues the library holds and their total size on disk.

## The toolbar

### Back / Forward

The ◀ ▶ buttons walk your **browse history** — every collection, filter, and search you
moved through, just like a browser. Handy after drilling into a series and wanting to get
back.

### Search + search mode

Type in the search box, then click the **mode pill** (e.g. `All ▾`) to choose what the
text matches against:

| Mode | Matches |
|---|---|
| **All** | everything below, combined |
| **Series** | series name |
| **Writer** | writer credit |
| **Artists** | penciller / inker / artist credits |
| **Descriptive** | summary, notes, genre, tags |
| **File** | file name / path on disk |
| **Catalog** | publisher, imprint, and catalog-style fields |

The search runs a moment (about 150 ms) after you stop typing, so a large library
stays responsive while you type, and clearing the box brings everything back instantly. Your search
text is remembered across restarts. When the Library shows **per-issue tiles**, a field search such
as `writer:miller` lists only the comics that match; searching a series name still lists all of that
series' comics.

### View & Sort

The **View & Sort** button opens a popup with three tabs. When there's more below than fits, the
bottom edge fades; scroll with the mouse wheel.

**View**

- **Display mode:** *Poster grid* (cover-forward), *Panorama grid* (covers at their real width),
  *Tiles* (small cover plus details), *List*, or *Details* (a table with columns you choose; click a
  header to sort).
- **Card content:** **Series cards** (one card per series) or **Per-issue tiles** (every issue as
  its own tile).
- **Show titles** (Poster grid) and **Grid density** (Poster grid and Tiles): how big the cards are.
- **Overlay:** what shows on the covers — **Unread badge**, **Publisher badge**, **Language badge**
  and **Show language flag** (List and Tiles), **Continue reading button**, **Fade in thumbnails**
  (covers fade in the first time they appear), **Dog-ear preview** (hovering a cover peeks at its
  second page in the corner), **Tooltips** (a details card after hovering a cover), **Smooth
  scrolling**, and **Numeric rating badge** (only on issues you have rated). Each switch is shown only
  in the display modes where it does something.

Publisher badges use ComicRack CE's publisher logo pack, including logos that change with the year.

**Sort**

Dozens of fields — series, title, number, dates, credits, ratings, read progress, file size and date,
and any Virtual Tags you defined — ascending or descending. Like ComicRack CE, text sorts ignore case
and a leading article (*The Flash* sorts under **F**), and numbers sort naturally (*Vol 2* before
*Vol 10*). A changed sort shows as a chip under the toolbar; click it to change it again.

**Group**

Break the view into labelled sections by the same kinds of fields, by **Alphabetical** (first
letter), by a Virtual Tag, or **None**.

While sorted by series, an **A–Z rail** on the right jumps to a letter. It follows the same rule, so
*The Flash* is under **F**.

### Filters

Toggle quick filters: **Unread only**, **Missing issues**, and **Tracked series**. If you use another
computer's shared library, pick which library's books to show here too.

### Preview panel

The panel button (or **Ctrl+B**) shows a panel on the right with the selected series or issue: a large
cover, details, reading progress and quick actions. For a series it lists its issues, which you can
open from the panel. Sections can be folded and stay folded. It isn't shown in the *Details* table.

## Scrolling

Scrolling through thousands of covers stays smooth: covers are cached at the size they're shown and
loaded a couple of screens ahead of where you are, so they no longer pop in as you scroll.

The toolbar's **View & Sort** popup has a **Smooth scrolling** switch, in the same group as the
other display toggles (on by default). With it on, each
mouse-wheel notch glides to its place instead of jumping. It only affects a mouse wheel — a
touchpad, the scrollbar, keyboard and touch scroll exactly as before — and it stays off if Windows
is set to reduce motion. Turn it off if you prefer the instant, stepped feel.

## Saved list layouts

Your sort field, direction, grouping, display mode, and filters are **remembered per
view** and restored when you come back — including across app restarts. Different
collections and Smart Lists keep their own layout.

## Opening things

- **Double-click a cover** (or press **Enter**) — an issue opens in the **[reader](Reading)**; a
  series opens its **detail page**. A single click selects it; arrow keys move between cards.
- **Right-click** — a context menu with:
  - **Open**, **Go to Series**, **Show in Explorer**, **Edit Properties…** (Ctrl+I), **Quick Rate…**
  - **Read / Unread**, **Add to Reading List**, **Add to Collection**
  - **Content Type**, **Reading Direction** (manga family only), **Publication Status**,
    **Reading Status**
  - **Scrape…** and **Organize…** (see [Scraping & Organizing](Scraping-and-Organizing)),
    **Write metadata to file**, **Compare files…** (with exactly two issues selected; see
    [Comparing Duplicates](Comparing-Duplicates))
  - **Delete…**, with a choice to delete the file or **Remove from the library, keep the file**
    (the file isn't imported again at the next scan)
- **Continue Reading** — a button on series covers jumps you to where you left off.

### Selecting several

**Ctrl+click** or **Shift+click** (or **Ctrl+A**) selects several cards, and a bar appears under the
toolbar. For issues it offers **Bulk Edit**, **Plugins**, **Mark Read**, **Mark Unread**,
**Add to List**, **Delete** and **Clear**. For series cards it offers the same except Plugins; Mark
Read, Mark Unread and Add to List then apply to every issue in the selected series, in order.

## The series detail page

Opening a series shows its cover, metadata pills, summary, and an **issue list**, plus
tabs for related content — see [Story Events & Relations](Story-Events-and-Relations) and
[Metadata & Editing](Metadata-and-Editing). For manga with an online match, a richer
manga detail view is shown.

## Collections

Collections are named groups you assemble yourself (think shelves). Add series to a
collection, then pick it in the sidebar to browse just that set. Collections are also
wired into the series detail page's relationship tabs.
