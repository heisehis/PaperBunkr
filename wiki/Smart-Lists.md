# Smart Lists

A **Smart List** is a saved view defined by **rules**, not by hand. It always shows
exactly the issues that currently match its conditions — add a matching book to your
library and it appears in the list automatically.

Open **Smart Lists** from the navigation rail.

## Built-in lists

The sidebar ships with ready-made lists (e.g. recently added, unread, in progress), plus a
collapsible **Maintenance** group:

- **Missing Files** — issues whose file can't be found on disk.
- **Duplicate Candidates** — issues that look like duplicates of each other.

## Creating your own

1. Click **New Smart List** in the sidebar.
2. Give it a name.
3. Under **Match ALL of the following**, click **Add condition**.
4. For each condition pick a **field**, an **operator**, and a **value**:
   - **Text** fields (Series, Writer, Genre, Tags, Publisher, Story Arc, …): *is / is not /
     contains / contains any of / contains all of / starts with / ends with*.
   - **Number** fields (Year, Page Count, Rating, Read %, Bookmark count, File size, …):
     *is / is not / greater than / less than / in range*.
   - **Toggle** fields (Read, Missing, Linked, Black & white, Has custom values):
     *is / is not*.
   - **Date** fields (Added, Opened, Released, Modified): *is / after / before / in range /
     within last N days*.
5. **Currently matches N issues** updates live as you edit. Click **Apply**, then **Save**.

The condition list matches ComicRack CE's matcher catalog field-for-field, so lists you
relied on in CE can be rebuilt here.

## Importing a ComicRack smart list

A smart list you exported from ComicRack is a `.cbl` file, the same extension as a reading list.
Click the **import** button next to **New Smart List**. Dropping the file on the Library or on a reading
list, or picking it in **New Reading List → Import**, works too: the file is recognised by what it holds.

- The list arrives as a normal smart list, with every rule that has an equivalent here.
- A rule that has **no equivalent** is left out, never guessed at, and one alert in the Activity Center
  names it and says whether the list may now match **more** or **fewer** books. The usual cause is a
  ComicRack script rule; scripts can't run here.
- Two common scripts do have an equivalent and come across as ordinary rules: **Proposed Values**
  becomes *Has Proposed Value*, and **Same X and different Y** becomes *Same X, Different Y* (both below).
  ComicRack ran that second script only over the books the rules before it had kept; here it looks at
  the whole library. When it was the first rule that is the same thing; when it wasn't, the alert says
  the list may differ.
- A list whose rules were *all* left out isn't imported, because it would match your whole library.
- CE's "Series: Last Number / Last Year / Highest Count / Biggest Gap" rules come across as fields of
  the same names. CE's "Series: last added / opened / released / published" date rules don't yet.

One difference from before: a custom value a comic doesn't have now counts as empty, as in ComicRack,
so a rule such as *Custom value comicvine_issue is (nothing)* finds the comics that were never scraped.

## Two rules that look across your library

- **Has Proposed Value** finds comics whose number, year, title, format, volume or count was worked out
  from the file name but never saved on the comic itself. Leave the box empty for any of them, or name
  the ones you care about (`number, year`). Select the results and use **Tidy up → Commit proposed
  values** in [the Library](The-Library) to save them.
- **Same X, Different Y** groups comics that share the first box's fields and keeps the groups that
  disagree on the second box's. `Series|Volume` and `Format` finds series whose books don't agree on
  their format. Leave the second box empty and you get every comic that shares the first box's values
  with another one: `Series|Number|Format` is a duplicate finder. Separate names with `|`; a name that
  isn't a field is looked up as a custom value.

## Working with a list

- Selecting a list shows its results using the same grid, sort, group, and display
  controls as [the Library](The-Library) — and remembers that layout per list.
- **Duplicate** a list to use it as a starting point for a variation.
- Delete from the sidebar (with a confirm step).

## Virtual tags

Instead of CE's fixed VirtualTag01–20 slots, PaperBunkr lets you define named **virtual
tags** — computed labels you can then filter on with the **Virtual Tag** condition field.
