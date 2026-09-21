# Scraping and Organizing

Paperbunkr can fill in a comic's details from **ComicVine** and can move or copy your files into folders named by a template. Both used to be the *Cluster Library Manager* plugin; they are now built in, so there is nothing to install.

## Before you start

Add your ComicVine API key, a Metron login, or both, under **Preferences → Connections**. Every key and password lives there. Scraping without one just tells you where to add it.

**Metron** is an alternative to ComicVine. Under **Preferences → Organize & Scrape → Source** choose which one a scrape starts on. The match dialog has a source box too, to switch a single run (its search and issue list follow it). A re-scrape starts on the source most of the chosen comics were scraped from before; scheduled scrapes use the one from Preferences.

## Scrape

Right-click a comic, several comics, or a series and choose **Scrape…**.

1. Paperbunkr searches ComicVine and ranks the series it finds. You confirm the right one in a dialog (or turn on *Choose the best match automatically* to skip it).
2. It then shows the ComicVine issue it matched, so you can correct it.
3. The details (credits, summary, characters, dates, and more) are written into your library, and the files are queued for tag write-back if you have that on.

A batch shows one progress header across all of its dialogs, and the whole run is one job in the Activity Center. You can cancel it there.

A comic's series page also has a **Comic details** panel (Details → Linking) with a one-click scrape of the whole series. It isn't offered for manga, which use different sources.

### Options: Preferences → Organize & Scrape

- Choose the best match automatically, and confirm the issue too.
- Overwrite existing values, or only fill in what is empty. Never blank a field.
- Which fields a scrape may write.
- Filters that drop unlikely results before scoring: years, publishers, words to leave out of searches, imprint to parent-publisher mappings, and a cap on results considered.

## Organize

Right-click comics or a series and choose **Organize…**. Pick a profile (if you have more than one) and Paperbunkr moves or copies each file to the folder and file name the profile's templates describe. If a file already exists at the destination you choose Replace, Rename or Skip, and can apply the choice to the rest.

Profiles are managed in **Preferences → Organize & Scrape**: a name, a base folder, folder and file templates, Move / Copy / Simulate, what to do about collisions in unattended runs, and optional conditions that exclude comics. **Simulate** shows what would happen without touching a file.

Templates use `{<token>}` groups: `{<series>}`, `{ #<number2>}` (a digit after the name zero-pads it), `{ (<year>)}`. Text inside the braces next to a token disappears when the value is empty.

**Undo last organize** (same section) puts the most recent batch of moved files back. Undone moves are kept in the history, not deleted.

## Automatically

Two tasks in **Preferences → Automation**, both **off** until you turn them on:

- **Scrape unscraped comics with ComicVine** matches comics that have no ComicVine details yet. It never asks anything, so anything that would need a choice is skipped unless *Choose the best match automatically* is on.
- **Organize library** runs the profile you marked *Use for the scheduled Organize library task*.

Downloaded issues (see Acquisition) get their ComicVine details automatically by the exact issue you asked for; failures and their retries appear under **Wanted → Queue → Needs details**.

## If you used the plugin

An installed Cluster Library Manager is no longer loaded; the Plugins screen says it is now built in, and you can remove it. Profiles you made in the plugin were not carried over; create them again under Organize & Scrape.
