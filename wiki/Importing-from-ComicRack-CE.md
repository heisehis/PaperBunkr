# Importing from ComicRack CE

PaperBunkr can import an existing **ComicRack Community Edition** library — series, issues,
read state, and metadata — in one pass. The importer is **non-destructive**: your CE
install is never touched, and the migration is always safe to re-run.

## Run the migration

1. Open **Preferences → Libraries**.
2. Under **Migrate from ComicRack CE**, click **Migrate…**. The migration overlay opens.
3. **Locate your CE library.** PaperBunkr checks the default CE path automatically
   (`%AppData%\cYo\ComicRack Community Edition\ComicDb.xml`). If it's elsewhere, use
   **Browse…** to point at your `ComicDb.xml`.
4. Click **Scan** for a dry run. You'll see counts of **series**, **issues**, and **how
   many series will land with a guessed content type** — nothing is written yet.
5. Click **Continue** to review **possible duplicate series**. Because PaperBunkr treats
   *Series* as a real entity (CE only grouped by a text field), near-identical names are
   surfaced so you can **Merge** them or **Keep Separate**. Shortcuts: *Keep All Separate*,
   *Merge All Above 90%*. Anything you don't resolve is not blocked — it goes to the
   Needs Review queue.
6. Click **Continue → Start Migration**. Progress is shown; when it finishes you land on
   **Migration complete**.

## What carries over, and what's approximate

CE's data model is less expressive than PaperBunkr's, so some values are **inferred**:

| CE `Manga` value | Content type | Reading direction |
|---|---|---|
| `YesAndRightToLeft` | Manga | Right-to-Left |
| `Yes` | Manga | Left-to-Right |
| `No` | Comic | Left-to-Right |
| `Unknown` | Unknown | Left-to-Right (a default, not a real guess) |

CE cannot distinguish **manhua/manhwa** from manga at all, so every manga-flagged series
imports as *Manga* until you correct it.

## The Needs Review queue

After migrating, open **Preferences → Libraries → Migrate… → Needs Review** (the tab in
the migration overlay). It's a persistent queue, not a dismissible report. It collects:

- **Content-type guesses** — accept or change each.
- **Series-name conflicts** you left unresolved — merge or keep separate.
- **Missing files** — issues whose file could not be found; **Relink…** to a new path,
  **Dismiss**, or delete the record.

Work through it whenever you like; nothing about the rest of the app is gated on it.

## Fixing content type and reading direction later

You don't have to use Needs Review. In the **Library**, right-click any series and use
**Set Content Type**, **Set Reading Direction**, **Set Status**, and **Set Reading Status**.
See [Metadata & Editing](Metadata-and-Editing).
