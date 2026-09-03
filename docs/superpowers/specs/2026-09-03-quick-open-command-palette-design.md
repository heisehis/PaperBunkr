# Quick Open — Command Palette — Design

*Part of the "Library browsing extras" backlog (docs/alpha-roadmap.md). Combines that bundle's
"Recent/MRU" and "Quick Open overlay" items into one surface.*

## CE-parity check (standing rule)

Checked `_reference/ComicRackCE` first. CE has **two** unrelated things here, and this spec
deliberately keeps neither shape:

- **`File ▸ Open Recent`** (`MainForm.cs:2903-2934`, `miOpenRecent`) — a menu submenu of the last
  *N* opened comic **file paths** (`Program.Database.GetRecentFiles(Settings.RecentFileCount)`),
  each with a 16px cover; clicking calls `OpenSupportedFile`. Pure file-path MRU.
- **`QuickOpenView`** (`Views/QuickOpenView.cs`, wired in `MainForm.cs:4454-4495`) — a cover wall
  shown **when no book is open** (`OpenBooks.CurrentBook == null && Program.Database.Books.Count > 0`),
  grouping thumbnails by list: `Reading`, `Recently Read`, `Recently Added`, plus any user list
  flagged `QuickOpen`. It is effectively CE's home screen. Sorted by `ComicBook.OpenedTime`.

**Paperbunkr already has both jobs covered except one.** The Home screen
(`docs/superpowers/specs/2026-08-18-home-screen-design.md` + Books strip) already does the
`QuickOpenView` job — "Continue reading" (comics + books), "Recently added", "Because you read", a
spotlight carousel — recency-grouped browsing on launch. What's genuinely missing is a
**keyboard-driven jump** to a *named* thing without browsing to it. So (confirmed with the user):

| CE | Paperbunkr |
| --- | --- |
| `Open Recent` file-path MRU in a menu | **No standalone recents surface.** Recency instead seeds the palette's default list. |
| `QuickOpenView` recency cover-wall on launch | **Already exists as the Home screen.** Not rebuilt. |
| — (CE has nothing like this) | **`Ctrl+P` fuzzy command palette** — jump to any series / issue / book / list / collection / event / screen, or run an action verb. |

This is a **deliberate deviation, not CE parity** — the roadmap already flags it as such.

## Scope

A modal overlay, opened with **`Ctrl+P`**, containing a single text field and a result list. Type
to fuzzy-filter; `↑`/`↓` to move; `Enter` to activate; `Esc` (or click-away) to close. Before you
type anything it shows your **recently-opened items** (comics + books, most-recent-first, capped at
~8) so it doubles as the "recent" list with zero extra UI.

### What's indexed (user chose "content + navigation + actions")

| Kind | Source | Match text | `Enter` does |
| --- | --- | --- | --- |
| Series | `Series.Name` (+ localized/alt titles if present) | name | `GoDetailForSeries(id)` |
| Issue | `Series.Name` + `Issue.Number`/`Title` | "Series #12 – Title" | `GoReaderForIssue(id)` |
| Book | `Book.Title` (+ author) | title, author | `GoBookReaderForBook(id)` |
| Reading list | `ReadingList.Name` | name | `GoReadingWithList(id)` |
| Smart list | `SmartList.Name` | name | navigate to Smart screen + select it |
| Collection | `Collection.Name` | name | `GoLibraryWithCollection(id)` |
| Story event | `StoryEvent.Name` | name | Events screen + select event |
| Continuity | `Continuity.Name` | name | Events screen + select continuity |
| Screen | static list | "Home", "Library", "Books", "Smart Lists", "Reading Lists", "Events & Continuity", "Preferences" | the matching `Go*Command` |
| Action | static registry (below) | verb label | invoke the command |

**Actions (v1 set)** — each maps to an existing `MainViewModel` command, no new behavior:
`Add folder…` (`GoLibraryFoldersPreferences`), `Add issue to library…` (`Library.OpenAddIssueCommand`),
`Scan libraries now` (the existing scan command), `Back up database now` (Advanced-tab backup command),
`New reading list…` (`OpenNewReadingListDialog`), `Import from ComicRack…` (`OpenMigrationOverlay`),
`Check for updates`. The registry is a `IReadOnlyList<QuickOpenAction>` (`Label`, `Icon`, and an
`ICommand` ref or `Action<MainViewModel>`) defined in one file; adding a verb later is one line.

### Trigger scope (user chose "everywhere except the reader")

`Ctrl+P` is live on every shell screen. It is **inert inside the reader** (`CurrentScreen` is
`reader` / `bookReader` / `pdfReader`) — those keep their dense page/zoom keymaps and you rarely
jump away mid-read. No carve-out needed in `PageCanvas`; the shell-level handler simply checks
`CurrentScreen` and does nothing there.

### Explicitly out of scope

- A `File ▸ Open Recent`-style menu, or a "Recent" row on Home (user: "no separate surface").
- Searching *within* an issue's pages / a book's text.
- Searching tags, people (writers/artists), publishers as their own result kind — those stay in
  the Library search box's field modes. (Could be added to the index later; not v1.)
- Multi-step palettes (type `>` for commands, `@` for symbols, VS Code style). One flat list.
- Persisting the palette's own query or recent *searches*. Only opened-item recency is used.
- Fuzzy-search as a NuGet dependency — the scorer is ~40 lines, hand-rolled (below).

## The index

New `QuickOpenService` (`Paperbunkr.App/Services/QuickOpenService.cs`), same no-DI
`Func<PaperbunkrDbContext>` shape as `KeyBindingService` / `WorkspaceService`.

```csharp
public sealed record QuickOpenEntry(
    QuickOpenKind Kind,
    int? EntityId,          // null for Screen/Action rows
    string Primary,         // shown bold; the main match target
    string? Secondary,      // shown dim (series name for an issue, author for a book, …)
    string Icon,            // FluentIcons symbol name
    DateTime? RecencyUtc);  // Issue.OpenedTime / Book.LastOpenedTime, for the pre-type list + ranking boost

public IReadOnlyList<QuickOpenEntry> BuildIndex();      // one call per palette open
```

`BuildIndex()` runs a handful of **projected, `AsNoTracking` queries** — `Select` straight to the
DTO, never materializing entities:

- Series: `Select(s => new { s.Id, s.Name })`
- Issues: `Select(i => new { i.Id, i.Number, i.Title, Series = i.Series.Name, i.OpenedTime })`
- Books: `Select(b => new { b.Id, b.Title, b.Author, b.LastOpenedTime })`
- ReadingList / SmartList / Collection / StoryEvent / Continuity: `{ Id, Name }` each
- Screens + Actions: static, built in code

At 2000+ issues (the perf-sensitive scale flagged in the thumbnail-decode memory) this is a few
thousand rows of two-three short strings — single-digit milliseconds, no cover images, no joins
beyond `Issue.Series.Name`. **Built fresh on each palette open** rather than cached: the open is
user-initiated and infrequent, a stale index (a series renamed in another view) would be a bug,
and there's no mutation hook to invalidate a cache cleanly across every screen. If profiling ever
shows this is slow on a huge library, the fix is a cached index invalidated on
`LoadFromDatabase`-level events — noted, not built now.

## Matching and ranking

Hand-rolled in `QuickOpenMatcher` (`Paperbunkr.App/Services/QuickOpenMatcher.cs`), pure, unit-
tested in isolation:

- **Subsequence match** (VS Code "fuzzy" style): every char of the query appears in `Primary` in
  order, case-insensitive. Non-matches are dropped entirely.
- **Score** rewards: contiguous runs, a match at a word boundary (after space / `#` / `-`), a
  match at index 0, and a shorter target. One integer, higher = better.
- **Ranking** = score, then a **recency boost** (an entry opened in the last 7 days sorts above an
  equal-score entry with no/old recency), then a **kind priority** tiebreak
  (Series ≈ Book > Issue > List/Collection/Event > Screen > Action) so a bare "batman" surfaces the
  series before 400 of its issues.
- **Empty query** → the pre-type list: top ~8 entries by `RecencyUtc` desc (Issues + Books only),
  then the 7 Screen rows. No score involved.
- Display is capped at **50 rows** (`Take(50)` after ranking) — the list is keyboard-navigated,
  nobody scrolls past 50.

Query is debounced one animation frame (re-rank on the UI thread is trivial for a few thousand
in-memory records — no `Task`/threadpool needed, matching how `RebuildView` already re-filters
in-memory).

## UI

New `QuickOpenOverlay.axaml` (+ `.axaml.cs` minimal code-behind — the AVLN2000 rule) and
`QuickOpenViewModel`, hosted by `MainViewModel` exactly like every other overlay: an
`_isQuickOpenOverlayOpen` `[ObservableProperty]`, an `OpenQuickOpenCommand`, and a slot in
`MainWindow.axaml`'s overlay layer.

Layout — a centered panel (~560 wide, max ~60% viewport height), `Border.dropdown` styling
(reusing the shared token style), over a scrim:

```
┌──────────────────────────────────────────┐
│  🔎  batman|                              │   search TextBox, autofocus
├──────────────────────────────────────────┤
│  📖  Batman                     series    │   ← selected row: PbAccentSoft bg
│  📕  Batman: Year One            book     │
│  📄  Batman #404 – Year One    Batman     │
│  📄  Batman #405               Batman     │
│  ▤   Batman (reading list)       list     │
│  …                                        │
└──────────────────────────────────────────┘
   ↑↓ navigate    ↵ open    esc close        ← dim footer hint
```

- One `ListBox` (or `ItemsRepeater`) over `QuickOpenViewModel.Results`
  (`ObservableCollection<QuickOpenEntry>`), `SelectedIndex` driven by `↑`/`↓` from the search
  box's `KeyDown` (arrows don't leave the TextBox — same pattern the Library search-suggestions
  popup already uses).
- Per-row: `fi:SymbolIcon` + bold `Primary` + right-aligned dim `Kind` label; `Secondary` dim
  under `Primary` when present.
- `Enter` → `QuickOpenViewModel.Activate(SelectedEntry)` → dispatch on `Kind` to the matching
  `MainViewModel` navigation/command (the overlay VM gets those as delegates in its ctor, same as
  `Library`/`Detail` VMs already do), then close the overlay.
- Opening always **clears the previous query** — every invocation starts blank on the recency list.
- `Esc` / scrim click / successful activation all close it. Closing returns focus to whatever had
  it before (Avalonia restores this automatically for a light-dismiss overlay; verified pattern
  from the existing overlays).
- Empty state (query matches nothing): a single dim "No matches" row, not an empty box.

### Trigger wiring

`MainWindow.axaml.cs`'s existing `OnMainWindowKeyDown` tunnel handler (the one that already owns
`Escape`, `BrowserBack`, `Ctrl+,`, `Ctrl+Tab`) gains:

```csharp
if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.Control)
{
    if (viewModel.CurrentScreen is not ("reader" or "bookReader" or "pdfReader"))
    {
        viewModel.OpenQuickOpenCommand.Execute(null);
        e.Handled = true;
    }
    return;
}
```

Placed **before** the `e.Source is TextBox` early-return that the `Ctrl+,`/`Ctrl+Tab` block sits
after — so `Ctrl+P` works even while the Library/Books search box has focus (same reasoning
`Escape` and `BrowserBack` are already handled before that return). `Ctrl+P` is currently unbound
app-wide (verified: `MainWindow`'s `Window.KeyBindings` holds only `Escape`; the tunnel handler
holds the four above). Not added to `KeyboardCommandRegistry` — that registry is reader-scoped and
app-wide hotkeys already live as literals in this handler (per
`docs/superpowers/specs/2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md`). If a
remap surface for shell hotkeys is built later, `Ctrl+P` moves there with the rest.

## Edge cases

- **Deleted entity between index build and activation** (rare — the index is seconds old) — the
  target `Go*` command already handles a missing id by falling back to the parent screen (existing
  behavior, e.g. `LastScreenEntityId`'s "deleted → Home" fallback). The palette adds no new guard.
- **Huge library** — covered above: projected queries, `Take(50)` display, in-memory re-rank.
- **Ctrl+P while an editor overlay is open** (Issue Properties, a naming overlay, migration) —
  `OpenQuickOpenCommand` checks the same "is another modal open" condition the other overlay-open
  commands check and no-ops; you finish or cancel the editor first. (One flat modal layer, per the
  Avalonia components skill's "third floating layer means a dialog" rule.)
- **Two entries with identical `Primary`** (a series and its one-shot issue both "Watchmen") —
  both listed; the `Kind` label + `Secondary` line disambiguate; kind-priority orders them.
- **Recency list on a fresh install** (nothing opened yet) — pre-type list is just the 7 Screen
  rows. Never an empty overlay.
- **Non-Latin / accented titles** — subsequence match is `char`-wise `ToLowerInvariant`; no
  accent-folding in v1 (matches the Library search box's current behavior — consistent, not a
  regression).

## Testing

- **`QuickOpenMatcherTests`** (pure, no DB): subsequence hit/miss, word-boundary and prefix
  scoring beats mid-word, shorter target wins on equal subsequence, recency boost breaks a score
  tie, kind-priority breaks a score+recency tie, empty query returns the recency+screens list.
- **`QuickOpenServiceTests`** (`App.Tests`, context-factory seam): `BuildIndex` produces one entry
  per series/book/list/collection/event/continuity + N issue entries with the right `Secondary`
  (series name), `RecencyUtc` populated from `OpenedTime`/`LastOpenedTime`, Screen + Action rows
  always present.
- **`QuickOpenViewModelTests`**: typing filters `Results`; `Activate` on each `Kind` invokes the
  right injected delegate with the right id (fake delegates, assert captured args — the established
  ViewModel-test pattern here); activation closes the overlay; reopening clears the query.
- **On-screen (`Paperbunkr.App.UiTests`, FlaUI)** — new small driver: `Ctrl+P` on the Library
  screen opens an overlay with a focused search box (`QuickOpenSearchBox` automation id); typing a
  known series name and pressing `Enter` lands on the detail screen; `Ctrl+P` inside the reader
  does nothing. Fuzzy ranking is asserted at the matcher/unit level, not through the UI.

## Roadmap / docs updates on landing

- `docs/alpha-roadmap.md` — mark "Recent/MRU + Quick Open" shipped in the "Library browsing
  extras" section, noting the deviation (a `Ctrl+P` command palette, not a recents menu or a
  cover-wall; Home already covered the latter) and that filesystem folder browsing was **dropped**
  from the bundle by decision (see below).
- `docs/ce-feature-inventory.md` §C — flip `Recent/MRU file list, Quick Open` to shipped-as-
  palette; mark `Filesystem folder browsing mode` as **won't-do** rather than "not started".
