# Metadata & Editing

PaperBunkr stores metadata in its own database. It reads `ComicInfo.xml` from your files
on scan, but edits you make in the app stay in the database unless you export them back.

## Editing one issue

Right-click an issue → **Edit Properties…** (or the **Edit** button on a detail page).
A borderless overlay opens with tabbed fields:

- **Details / Main** — Series, Title, Number, Volume, Count, Story Arc (+ number),
  Alternate Series/Number, Series Group, Format, Imprint, Publisher, Web, Language,
  Age Rating, dates (Year / Month / Day), Page Count, Color mode.
- **Credits** — Writer, Penciller, Inker, Colorist, Letterer, Cover Artist, Editor,
  Translator.
- **Plot & Notes** — Summary, Review, Notes; Characters, Teams, Locations, Main
  Character/Team, Genre, Tags.
- **Ratings** — My Rating, Community Rating.
- **Genre Details / Tags Details** — structured, categorised entries (e.g. *Theme*,
  *Setting*).

Edits are held in a buffer — **Save** commits, **Cancel** discards. Multi-level
**undo/redo** is available while editing.

### Copy / paste fields & tokens

- **Copy** grabs every field on the screen; **Paste** applies the last copied set to
  another issue — fast way to propagate shared metadata.
- **Insert a token** builds text fields from placeholders (series, number, etc.).

## Editing many issues at once

Select multiple issues, right-click → **Edit Properties…** opens the **bulk editor**.

> Only **checked** fields are applied — leave a field unchecked and each issue keeps its
> own value. List fields (Genre, Tags, Characters…) can be merged rather than replaced.

There's also a **bulk series properties** editor for series-level fields.

## Quick Rate

Right-click → **Quick Rate…** for a fast rating + short review popup without opening the
full editor.

## Covers

On a detail page, right-click the cover (or use the tab menu):

- **Set Cover…** — pick any image file, or choose a different page from the issue.
- **Reset Cover** — back to the default (first page).

## Online metadata lookups

From a series **detail page**:

- **External Metadata → + Link External Metadata** — search a provider (**AniList**,
  **MangaBaka**), match your series to its record, and **Link** it.
- Once linked, you can **apply** fields from the provider — title, summary, genres,
  status, cover — as a **proposal** you accept or reject per field ("via AniList" /
  "via MangaBaka" is shown as the source).
- **Trackers → + Link for Tracking** links for progress sync; **Sync to Trackers** pushes
  your read state out. Some providers need credentials (entered in your OS credential
  store, not stored by PaperBunkr in plain text).

Manga series with a provider match get the richer **manga detail** view — chapter list
with filters (All / Unread / Missing / Bookmarked), sort by number or date, and a
provider-sourced synopsis.

## Getting edits back into your files

**Preferences → Libraries → Sync Metadata** re-reads files into the database. Writing the
database back out to `ComicInfo.xml` is limited in the current alpha — keep external
backups if your `ComicInfo.xml` files matter to you.
