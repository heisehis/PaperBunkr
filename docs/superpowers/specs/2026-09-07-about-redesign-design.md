# About Redesign — Design (Phase 7 of Preferences)

## Background

`AboutSection.axaml` was left untouched by Phase 1 (the tile-hub redesign) except for its
`Updates` group, which already got the `SettingsRow` treatment (Version, Check for updates,
Check for updates on startup). Phase 1's design doc, §4 item 9, deferred the other two groups —
Changelog and Legal — to their own brainstorm, "confirmed in chat as needing a genuine redesign,
not a reskin." This doc is that brainstorm.

Facts gathered before designing (per the project's standing CE-parity rule and the shared-model
constraint below):

- **CE has no equivalent About screen.** `ComicRackCE/ComicRack/MainForm.cs`'s
  `ShowAboutDialog()` reuses the splash screen (`Dialogs/Splash.cs`) — three lines of text
  (copyright, version+git-hash, bitness) overlaid on splash art, dismissed on any click. No
  changelog, no legal links, no credits, no update-check button. Paperbunkr's Updates/Changelog/
  Legal structure is a Paperbunkr addition with no CE precedent to preserve — this redesign is
  free to shape it however serves Paperbunkr's own users.
- **`ChangelogEntry(Version, Date, Body)` and `ChangelogParser`
  (`src/Paperbunkr.App/Services/ChangelogParser.cs`) are shared** with the update-available
  overlay elsewhere in the app, which renders just the newest entry's `Body`. Nothing here changes
  that record's shape or the parser — any new structure (category tags) is built as a
  presentation-layer parse of the existing `Body` string, in the About view only.
- **Legal docs** (`LICENSE`, `PRIVACY.md`, `TERMS.md`, `COMICVINE_NOTICE.md`) are bundled next to
  the exe via the csproj's `CopyToOutputDirectory` items and opened today by
  `OpenLegalDocument(string fileName)` via `Process.Start(UseShellExecute: true)`. All four are
  short (40-60 lines) and use only a narrow markdown subset: `#`/`##` headers, `> ` blockquote,
  `**bold**`, `- ` bullets, backtick inline code, and two plain-text URLs — confirmed by reading
  all four files, not assumed.
- **`OverlayShell`** (`src/Paperbunkr.App/Controls/OverlayShell.cs`) is the shared modal chrome
  built in the Feedback & Notification System phase — scrim + centered card + close button,
  `ContentControl`-based so it can host arbitrary content. Reusable as-is for a document viewer;
  no new overlay infrastructure needed.
- **App icon**: `src/Paperbunkr.App/Assets/paperbunkr-logo-source.png` exists alongside the
  `.ico` used for `ApplicationIcon` — usable directly for an identity header.
- `OpenLegalDocument` has no existing test coverage (confirmed: no hits in
  `PreferencesScreenViewModelTests`) — the tests this doc calls for are new, not updates.

## Goals

1. **Identity header.** Replace the plain "About" `TextBlock` with an icon (the bundled PNG) +
   "Paperbunkr" + `CurrentVersion` (existing binding, unchanged) + a one-line tagline. Matches the
   polish level every other redesigned Preferences area now has instead of a bare heading.
2. **Legal → `SettingsRow` list.** Replace the button/`WrapPanel` row with four `SettingsRow`s
   (icon + title + short description), each ending in an "Open" affordance — the same shape every
   other redesigned Preferences area uses.
3. **Legal docs open in-app.** Clicking a row opens the document in an `OverlayShell`-hosted
   viewer instead of shelling out to the OS. Rendered with light markdown formatting (headers,
   bold, bullets, blockquote) via a small new parser scoped to exactly what these four files use —
   not general CommonMark, and not a new NuGet dependency. No "open externally" escape hatch is
   kept; the in-app viewer is the only path going forward (explicit call, see Non-goals).
4. **Changelog → accordion.** Each version becomes its own collapsible item. The version matching
   `CurrentVersion` starts expanded with a "Current" badge; others start collapsed. Any number of
   items can be open at once — no auto-collapse-the-others accordion behavior.
5. **Changelog category tags.** Within each entry, lines are grouped by their `### Added` /
   `### Fixed` / etc. sub-heading and shown as small labeled tags, parsed from the existing `Body`
   string at the view layer — `ChangelogEntry`/`ChangelogParser` stay untouched since the
   update-available overlay depends on their current shape.

## Non-goals

- **Updates group** — already `SettingsRow`'d in Phase 1; untouched here.
- **External "open in default app" fallback** for legal docs — dropped on purpose. Users who want
  to print/search/save a legal doc use their own file browser; not worth a second code path for a
  document opened rarely.
- **Third-party/NuGet dependency license attribution list** — a separate legal-compliance
  initiative (auditing every package's license), not a UI-redesign concern. Flagging it here so
  it isn't silently folded in or silently dropped — raise it separately if it's wanted.
- **General markdown support** (tables, images, nested lists, clickable hyperlinks) — the parser
  targets exactly the subset the four bundled docs use. A future doc that needs more would need
  the parser extended, not a rewrite, since it's a small isolated class.
- **Changelog pagination/virtualization** — two entries exist today; not warranted.

## Architecture

### 1. Identity header

A new header block at the top of `AboutSection.axaml`, above the `UPDATES` group: `Image` bound to
`paperbunkr-logo-source.png` (via whatever resource-loading pattern the app already uses for its
icon elsewhere — confirm against `TrayIconService`'s pattern in the plan, don't invent a second
one), "Paperbunkr" title text, `CurrentVersion` (existing property, no change), and a hardcoded
one-line tagline. No new ViewModel property beyond what already exists.

### 2. Legal section → `SettingsRow` list + in-app viewer

- Replace the `groupBox`/`WrapPanel`/`Button` block with four `SettingsRow`s (`Icon`, `Title`,
  `Description`), each with a chevron or "Open" button in `SettingsContent`, bound to
  `OpenLegalDocumentCommand` with the same `CommandParameter` (file name) as today.
- `OpenLegalDocument(string fileName)` body changes: instead of `Process.Start`, read the file's
  text (same `File.Exists` tolerance as today — missing file does nothing, doesn't crash), parse
  it into a small block list (below), populate a new `SelectedLegalDocument`-shaped state, and set
  `IsLegalDocumentViewerOpen = true` — following the same `IsXOverlayOpen` bool + dedicated close
  command convention `OverlayShell`'s own doc comment describes for every other overlay in the app.
- A new view (inlined in `AboutSection.axaml` or its own file, `writing-plans` to decide) hosted by
  `OverlayShell`, rendering the parsed blocks via an `ItemsControl` + per-block-kind
  `DataTemplate`s — the same "parse to a small model, render via `ItemsControl`" shape
  `ChangelogEntries` already uses, not a new UI paradigm for this codebase.
- New `LegalDocumentParser` (mirrors `ChangelogParser`'s shape: static class,
  `Parse(string markdown) -> IReadOnlyList<LegalDocumentBlock>`), recognizing `# `/`## ` headers,
  `> ` blockquote lines, `- ` bullets, `**bold**` inline runs, backtick inline code, and plain
  paragraphs. `[text](url)` links render as plain text, not clickable — Avalonia has no built-in
  `Hyperlink` inline the way WPF does, and the two URLs in these docs are informational, not
  something a legal-text viewer needs to launch a browser for.
- Exact `LegalDocumentBlock` shape (a block-kind enum + per-block inline runs, vs. a small type
  hierarchy with per-type `DataTemplate.DataType`) is left for `writing-plans` — this design fixes
  the responsibility split (parsing lives in `Services`, no XAML dependency) and the no-hyperlink
  call, not the exact record layout.

### 3. Changelog → accordion + category tags

- The existing `ItemsControl` over `ChangelogEntries` gets a new per-entry template: a header row
  (chevron + version + date + a "Current" badge shown when `Version == CurrentVersion`) that
  toggles the body's visibility, plus the body itself.
- Expand/collapse state is per-item UI state with no persistence requirement (it resets every time
  Preferences is reopened regardless) — handled via a `ToggleButton`-driven `IsChecked`/`IsVisible`
  pattern in the view, not a new ViewModel collection wrapping `ChangelogEntry` with an `IsExpanded`
  flag. Simpler, and avoids inventing VM state for something that's pure presentation.
- Category-tag parsing is a small new helper (view-layer, not touching `ChangelogParser`) that
  splits `Body` on `### ` sub-headings into `(Category, Lines)` groups, rendered as tag+text rows
  nested inside the entry body. A `Body` with no `### ` sub-headings (a future free-form entry)
  falls back to rendering the raw text as today — not a hard parse failure.

## ViewModel changes (summary)

- New: `IsLegalDocumentViewerOpen` (bool), state describing which document is currently shown
  (title + parsed blocks — exact property shape TBD in plan), `CloseLegalDocumentViewerCommand`.
- Changed: `OpenLegalDocument(string fileName)` — parses and opens the in-app viewer instead of
  `Process.Start`; same file-missing tolerance as today.
- Unchanged: `ChangelogEntries`, `RefreshChangelog`, `ChangelogParser`, `ChangelogEntry`,
  `CurrentVersion`, `CheckForUpdatesCommand`, `CheckForUpdatesOnStartup`, `UpdateCheckResultText`.
- No ViewModel change for accordion expand/collapse or category tags — both are view-layer only.

## Testing

- New `LegalDocumentParserTests` (mirrors `ChangelogParserTests`' shape): one case per recognized
  block kind (heading, blockquote, bullet, bold, inline code, plain paragraph), plus a smoke case
  per actual bundled file (`LICENSE`, `PRIVACY.md`, `TERMS.md`, `COMICVINE_NOTICE.md`) asserting it
  parses without throwing and produces a non-empty block list — real-content coverage, not just
  synthetic snippets, the same lesson the CE-XML plugin-matcher bug taught in the Migration UX
  phase.
- New `PreferencesScreenViewModelTests` cases for `OpenLegalDocument` (currently untested):
  `IsLegalDocumentViewerOpen` flips true and the viewer state is populated for a real bundled file;
  a missing file leaves `IsLegalDocumentViewerOpen` false (same tolerance as `RefreshChangelog`).
- Manual on-screen pass (standing no-computer-use limitation — see project memory): identity header
  shows the right icon/version/tagline; each of the four legal rows opens the in-app viewer with
  headers/bold/bullets visually distinct from plain paragraphs; changelog's current version starts
  expanded with category tags, the older version starts collapsed, and both can be open at once;
  spot-check that the update-available overlay's own changelog rendering is unaffected (it shares
  `ChangelogParser`/`ChangelogEntry`, which this phase doesn't touch, but worth confirming by hand).

## Deliverable

Four new `SettingsRow`s (Legal documents) — add their description copy to
`docs/preferences-descriptions-todo.md`'s existing `## About` section alongside the two Updates
rows already listed there. One new parser (`LegalDocumentParser`) and its model
(`LegalDocumentBlock`), no new NuGet dependency. `OpenLegalDocument`'s `Process.Start` call is
removed entirely — no external-launch code path is kept per the in-app-only call in Goals §3.
