# Story Events & Relations

PaperBunkr models how your series and issues connect — sequels, crossovers, shared
universes, publishing timelines. This drives the series detail tabs and the
[Home recommendations](Home-Dashboard-and-Recommendations).

## Series detail tabs

Open a series and use the tabs:

### Related

Series connected to this one. **+ Add Related Series** to link a title and set the
**relationship** (sequel, prequel, spin-off, adaptation, alternate version, …). The link
is bidirectional and shown on both series.

### Continuities

**+ Add to Continuity** groups the series into a shared timeline / universe (e.g. a
publisher's main continuity, or a self-contained reboot). Series in the same continuity
surface each other as "Same Continuity".

### Trackers

**+ Link for Tracking** connects the series to an external service so its status and issue
list can be checked. **Sync to Trackers** pushes your progress out (where the adapter
supports it).

### External Metadata

**+ Link External Metadata** attaches an online source record (see
[Metadata & Editing](Metadata-and-Editing)) used for lookups and enrichment.

### Activity

A log of recent changes and reading activity for the series.

## Story Events

**Story Events** (navigation rail) are curated event timelines — a company-wide crossover,
an arc that spans several series. Each event has an **ordered member list** of issues.

1. Open **Story Events**, create one from the sidebar.
2. **Search your library by series or number…**, click **Add** to add issues.
3. Reorder members with **↑ / ↓**.

### Suggested Story Events and Continuities

PaperBunkr can propose Story Events and Continuities for you. **Nothing is created until you click
Accept**, and anything you **Dismiss** stays dismissed.

- **Story Events** — issues that share a Story Arc (per publisher) are grouped into a suggested
  event, optionally checked against ComicVine or Metron. Suggestions refresh weekly in the
  background, and the Story Events sidebar has a queue of them. In **Issue Properties**, a look-up
  button checks a single issue.
- **Continuities** — PaperBunkr matches your series to shared universes using Wikidata (and the
  recurring characters in your series), leaving out things like Elseworlds so they don't get mixed
  into a main timeline. Click **Check Wikidata** in the Story Events sidebar, or use **Look up on
  Wikidata** on a series' **Continuities** tab. Accepting a suggestion fills in the continuity's
  Wikidata ID, description and publisher. It also runs daily in the background.

Progress for the manual checks shows in the Activity Center.

A [Reading List](Reading-Lists) can be **linked** to a Story Event so the two stay in
sync. Issues that share an event show "Same Event" on their detail pages.

## Roles are detected for you

Each issue in a story event or reading list can be a **Prologue, Core, Tie-in, Epilogue, Optional or Aftermath**. Paperbunkr suggests these from what it can see:

- a reading-order site's own section headings and notes ("Prelude", "Tie-Ins"), when the list was built from a story arc;
- the issue's **Format** (Prologue, Minus 1, Epilogue);
- words in the issue's title - *prologue*, *prelude*, *epilogue*, **aftermath**, *tie-in* - or an issue number of 0 or -1;
- for lists built from ComicVine, the issue's story title there (which exists even for issues you do not own yet) counts like a title in your library, and its one-line summary is a weak hint;
- weaker hints: the same words only in the series name, or an issue from a different series than the rest of the arc.

When the evidence is strong the role is filled in for you and marked **· auto** (hover for why). A role you set yourself is never changed. A weaker guess, or a different idea about a role you set, appears as a **Suggested** chip with **Accept** and **Dismiss**. **Detect roles** in the ⋯ menu of an event or list runs detection over everything in it, and **Clear detected role** takes an automatic role back off. *Optional* is never suggested - only you mark an issue optional.
