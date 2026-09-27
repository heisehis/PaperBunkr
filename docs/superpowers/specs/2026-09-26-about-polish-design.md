# About Polish — Design

Follow-up to `2026-09-07-about-redesign-design.md`. The user asked to "polish everything in the
about section, from the UI, the changelog, to what's new, to legal texts". Decided in a grilling
pass on 2026-09-26 (Q1–Q16, with the visual companion for the layout, changelog-tag and viewer
choices).

## Background: what is wrong today (verified in code, not assumed)

**Rendering (both surfaces share the same root cause).**
- `ChangelogBodyFormatter` turns every *source line* into its own `TextBlock`. `CHANGELOG.md`
  hard-wraps long bullets, so "Comic acquisition" in 0.7.0 renders as ten broken paragraphs.
  `**bold**` shows as literal asterisks and the `- ` bullet marker is stripped with nothing drawn in
  its place.
- `LegalDocumentParser` also works line-by-line, and the viewer lays inline runs out as separate
  `TextBlock`s in a `WrapPanel`, so a run can't wrap into the next one: "(see `LICENSE`). These"
  breaks and the next source line starts a new paragraph. A 3-line blockquote becomes 3 quotes.
  `LICENSE` (plain AGPL text, no markdown) goes through the same parser and loses its layout.

**Data-selection bugs.**
- `CHANGELOG.md` starts with an empty `## [Unreleased]`. `ChangelogParser` parses it (the date is
  optional), so About shows an empty expandable "Unreleased" row, and
  `MainViewModel.OpenWhatsNewOverlayCurrentOnly` (About → What's new → View) shows that empty entry.
- The "Current" badge is an exact string match of the heading against `ReleaseVersion.DisplayString`.
  The csproj is at `0.7.3.0` while the newest heading is `0.7.0-beta`, so no row is marked current or
  starts expanded. (CI refuses to tag a release without a matching heading, so this only happens on
  builds between releases — which is every dev build.)
- The update-available overlay shows `entries[0].Body` of the **bundled** changelog — the notes of the
  version already installed, not the one being offered (today it is the empty Unreleased body). The
  appcast carries no release notes (`release.yml` runs `netsparkle-generate-appcast` without them), so
  the app cannot know the new version's notes offline.

**Layout.** The version appears twice (header and a "Version" row). The changelog sits between
Updates and Legal and pushes Legal below the fold. There are no project links, no way to reach the
logs/data folders, no credits, and no third-party notices (the 2026-09-07 design explicitly deferred
the notices list).

**Legal text accuracy** (two read-only code audits on 2026-09-26):
- `PRIVACY.md` says nothing phones home. Two things run by default: the startup update check against
  GitHub (`AppSettings.CheckForUpdatesOnStartup = true`; skipped on first launch and on the first
  launch after an upgrade) and the daily **Wikidata** continuity task
  (`ScheduledTaskCatalog` `continuity-wikidata-autodetect`, `DefaultEnabled: true`), which sends
  character and series names from the library. AniList, MangaDex and MangaBaka need no credentials,
  so a user-started metadata search reaches them directly.
- It calls Paperbunkr "self-hosted". It is a Windows desktop app with an optional library-sharing
  server (off by default; password required; listens on all interfaces on port 7614 over HTTPS with a
  self-signed certificate; private-network clients only by default; advertises the machine name over
  mDNS).
- Secrets are Windows DPAPI-encrypted per Windows user (`CredentialStore`), not just "stored locally".
- Crash logs (`%AppData%\Paperbunkr\logs`) include exception messages that can contain local paths.
  Nothing is uploaded; there is no telemetry.
- Plugins run in-process with network access; the sandbox is not a hardened boundary.
- `COMICVINE_NOTICE.md` recommends passing the key via an environment variable in containers/CI (no
  such mechanism exists) and gives vague limits. The app enforces one request per 1.1 s and 200 per
  hour (150 reserved for background work), then pauses 1 min → 5 min → 15 min → 1 h on HTTP 420/429.
  Metron (18 requests/minute enforced, of which background may use 14; the daily quota read from
  `X-RateLimit-Sustained-*` headers, background pauses near the end of it) is not covered at all.
- `TERMS.md` is also concatenated into the installer's accept screen (`installer/BuildInstaller.ps1`),
  which already strips bold, italics, code and `[text](url)` links to plain text.

**CE parity:** CE has no About screen beyond a splash reused for version display
(`2026-09-07-about-redesign-design.md` Background). Nothing here has a CE precedent to preserve.

## Decisions

| # | Decision |
|---|---|
| Q1 | Scope: changelog rendering (About + What's New), legal viewer, legal texts, About layout. Nothing else in Preferences. |
| Q2 | Fix the renderer, hide empty entries, and split only the 0.7.0 entry into shorter bullets. Older entries stay as they are. |
| Q3 | Drop the "Template, not legal advice" banner entirely. |
| Q4 | Write for someone running the desktop app, plus one short section on library sharing. |
| Q5 | One source per document: the repo-root files, shown both on GitHub and in the app. |
| Q6 | Keep the modal viewer, widen it to ~680px, add title + "Last updated" header, clickable links, Copy text, `LICENSE` as monospaced preformatted text. |
| Q7 | Add a fifth legal row, "Open-source notices", backed by a new bundled `THIRD-PARTY-NOTICES.md`. |
| Q8 | A Project group: GitHub, Wiki, Report an issue, Releases, Open logs folder, Open data folder. |
| Q9 | Show the version once, in the header, with the build hash and a Copy version info button. |
| Q10 | Keep the per-version accordion; current entry expanded with a "Current" badge; bullets drawn. |
| Q11 | Disclose the Wikidata task and where to turn it off. Its default does not change. |
| Q12 | Privacy notice: a bullet list of what leaves the machine (defaults first) and one "where your data lives" paragraph. No per-host URL list beyond the main providers. |
| Q13 | The update-available overlay stops showing the installed version's notes and links to the offered version's GitHub release instead (corrected from "use the shared renderer" once the appcast was checked). |
| Q14 | "Current" = the newest non-empty entry not newer than the running version. |
| Q15 | Notices: direct packages plus bundled assets, grouped by license, each license text once. Nothing asserted that wasn't verified. |
| Q16 | MS-RL PDFiumSharpV2, unreferenced `libx265`/libheif DLLs, `sharpPDF.dll`, CE publisher-pack/texture licensing, and the inconsistent GitHub URL in user-agent strings are out of scope: reported to the user as follow-ups. |
| Layout | Option C: About becomes tabbed — **Overview / Changelog / Legal & notices** — on the shared `SettingsTabs` strip. |
| Tags | Option 1-B: changelog category tags are coloured by kind, from theme brushes, label always shown. |
| Viewer | Option 2-A: the existing modal card, widened and reflowing. |
| Approach | One small shared markdown renderer owned by the project (no library, no WebView2). |

## Architecture

### 1. `MarkdownLite` — one parser for changelog and legal text

`src/Paperbunkr.App/Services/MarkdownLite.cs` replaces `LegalDocumentParser` (same shape: static
class, pure string parsing, no I/O).

```csharp
public enum MdBlockKind { Heading1, Heading2, Heading3, Paragraph, Bullet, Quote, Preformatted }
public enum MdRunStyle { Plain, Bold, Italic, Code, Link }
public sealed record MdRun(string Text, MdRunStyle Style, string? Target = null);
public sealed record MdBlock(MdBlockKind Kind, IReadOnlyList<MdRun> Runs);

public static class MarkdownLite
{
    public static IReadOnlyList<MdBlock> Parse(string markdown);
    public static IReadOnlyList<MdBlock> Preformatted(string text); // one Preformatted block, verbatim
    public static IReadOnlyList<MdRun> ParseInline(string text);
}
```

Block rules (the subset the bundled docs and `CHANGELOG.md` actually use):
- Blank lines separate blocks. **Consecutive non-blank lines join with one space** into the same
  block — this is the fix for hard-wrapped bullets and paragraphs.
- `#`, `##`, `###` → headings (a heading never continues onto the next line).
- `- ` / `* ` starts a bullet; following lines that don't start a new marker or heading continue it,
  so an indented or unindented continuation both join.
- `> ` starts/continues one quote block (consecutive `> ` lines join).
- A fenced ```` ``` ```` block → one Preformatted block, verbatim.
- `1. ` numbered items render as Paragraph blocks keeping their number (none of the docs nest them).

Inline rules: `**bold**`, `*italic*` / `_italic_` (word-boundary guarded so `snake_case` and `2*3`
stay literal), `` `code` ``, `[text](target)`. Unmatched markers stay literal. No nesting beyond
what these produce (bold inside a link renders as the link's plain text).

### 2. `MarkdownView` — one renderer

`src/Paperbunkr.App/Controls/MarkdownView.cs`, a code-only control (no `.axaml`, so it cannot hit
the new-view XAML-weave build gotcha).

- `Blocks` (`IReadOnlyList<MdBlock>`), `BaseFontSize` (default 13), `BodyBrushKey` (default
  `PbTextBrush`; the changelog passes `PbTextMutedBrush`), `LinkCommand` (`ICommand?`, receives the
  link target).
- Builds a vertical `StackPanel` of `SelectableTextBlock`s, one per block, with `Inlines` (`Run`,
  bold/italic via `FontWeight`/`FontStyle`, code via monospace font + `PbSurface3Brush` background).
  One text block per block means runs wrap together — the root cause of the broken sentences.
- Bullets: a two-column grid with a drawn "•" and the text; quotes: left border in
  `PbBorderBrush`, italic muted text; headings: 16 / 14 / 13 semibold with top spacing;
  preformatted: `Consolas` 11.5, no wrapping, inside a horizontal-only `ScrollViewer`.
- Links: an `InlineUIContainer` holding a `Button.linkText` (the existing text-link style) that
  executes `LinkCommand` with the target. Links are short labels in every bundled doc, so an inline
  button that doesn't wrap internally is acceptable.
- Every brush comes from a `DynamicResource` lookup (`this.GetResourceObservable`), so skins and
  runtime theme switches apply (the avalonia-pro-max review caught hardcoded colours last time).

Link handling (`LinkTargetResolver`, a pure static helper, testable):
- `http://` / `https://` → open in the default browser (`Process.Start` with `UseShellExecute`,
  the app's existing pattern).
- A bundled document name (`LICENSE`, `PRIVACY.md`, `TERMS.md`, `COMICVINE_NOTICE.md`,
  `THIRD-PARTY-NOTICES.md`) → open it in the same legal viewer.
- Anything else → ignored.

### 3. Changelog

- `ChangelogParser` and `ChangelogEntry(Version, Date, Body)` are unchanged (CI's own extraction
  regex and three consumers depend on the heading shape).
- New `ChangelogSelection` (static, `Services/ChangelogSelection.cs`):
  - `Visible(entries)` → entries with a non-blank body (drops an empty `[Unreleased]`).
  - `Current(entries, Version running)` → the first visible entry whose heading parses and is not
    newer than `running` (via `ReleaseVersion.TryParseHeading` / `IsNewerThan`). Unparseable
    headings (a non-empty `Unreleased`) are never "current". Null if none.
- `ChangelogBodyFormatter.Format(body)` now returns
  `ChangelogBodyGroup(string? Category, ChangelogTagKind Kind, IReadOnlyList<MdBlock> Blocks)`:
  split on `### Category` exactly as today, each group's text parsed with `MarkdownLite.Parse`.
  `ChangelogTagKind` = Added, Changed, Fixed, Removed, Security, Other (case-insensitive match;
  "Deprecated" → Other).
- New row model `ChangelogRow(ChangelogEntry Entry, bool IsCurrent, bool StartExpanded,
  IReadOnlyList<ChangelogBodyGroup> Groups)`, built once in the view model instead of per-binding
  converters. `VersionEqualsCurrentConverter` and `ChangelogBodyToGroupsConverter` are deleted.
- Tag colours (`Border.changelogTag` + a kind class), all theme tokens:

  | Kind | Background | Text |
  |---|---|---|
  | Added | `PbSuccessSoftBrush` | `PbSuccessBrush` |
  | Changed | `PbAccentSoftBrush` | `PbAccentTextBrush` |
  | Fixed | `PbBadgeSoftBrush` | `PbBadgeBrush` |
  | Removed, Security | `PbDangerSoftBrush` | `PbDangerBrush` |
  | Other | `PbSurface3Brush` | `PbTextMutedBrush` |

  Colour is never the only signal — the category word is always printed.
- The shared entry view (header row with chevron, version, date, "Current" badge; body with tags and
  `MarkdownView`s) becomes one `Views/ChangelogEntryView.axaml` UserControl (`x:DataType
  ChangelogRow`) with its styles scoped inside it, used by both About and What's New. Today the
  header-reset and chevron styles live in `AboutSection.axaml` only, so What's New's rows silently
  get stock ToggleButton chrome and a non-rotating chevron; one view fixes that too. (A UserControl,
  not a `DataTemplate` resource in an app-level style file, because headless tests load sections
  without the app's styles and a missing `StaticResource` would throw.) Chips use `PbRadiusChip`
  (no pill ovals, per the app-wide rule).
- What's New: `WhatsNewEntryRow` is replaced by `ChangelogRow`. `SelectEntriesSince` also drops
  empty entries. `MainViewModel.OpenWhatsNewOverlayCurrentOnly` shows
  `ChangelogSelection.Current(...)` (nothing when null) instead of `entries[0]`.
- About's `ChangelogEntries` becomes `ChangelogRows` (visible entries only; the current one marked
  and expanded, all others collapsed).
- `CHANGELOG.md`: split the four long 0.7.0 bullets into shorter ones (same facts, no rewording of
  meaning) and add the About polish under `[Unreleased]`.

### 4. Update-available overlay

- `MainViewModel.CheckForUpdatesOnStartupAsync` stops passing a changelog body.
- `UpdateAvailableOverlayViewModel` drops `ChangelogBody`; adds `ReleaseNotesUrl` =
  `https://github.com/heisehis/PaperBunkr/releases/tag/v{Info.Version}` (release tags are
  `v{version}` per `release.yml`) and an `OpenReleaseNotes` command.
- The overlay's scrolling body box is replaced by one line: "See what's new in this release" as a
  `linkText` button.

### 5. About page

`AboutSection.axaml` is restructured, keeping the file and `x:Class`:

- **Header** (always visible above the tabs): 56px logo, "Paperbunkr" in `pbTextHeading`, a faint
  line "Version 0.7.3-beta · build 9cc0b62 · Comic and manga library", and a ghost
  **Copy version info** button on the right. The copied text:

  ```
  Paperbunkr 0.7.3-beta (build 9cc0b62)
  Windows 10.0.26100 (x64)
  .NET 10.0.x
  ```

  (`RuntimeInformation.OSDescription`/`OSArchitecture`/`FrameworkDescription`; composed by a pure
  static `AboutInfo.VersionReport(...)` so it is unit-testable.) The button's label reads "Copied"
  for two seconds as confirmation — local feedback on the control itself, not a notification, so
  it does not go through the Activity Center. The viewer's Copy text button does the same.
- **Tab strip**: `SettingsTabStrip` bound to a new `AboutTabs` (`SettingsTabs`, keys `overview`,
  `changelog`, `legal`). In-memory only, like Reader's.
- **Overview tab**
  - UPDATES: What's new (View), Check for updates (Check now; result as description), Check on
    startup (toggle). The separate Version row is removed.
  - PROJECT: GitHub (`https://github.com/heisehis/PaperBunkr`), Wiki (`…/wiki`), Report an issue
    (`…/issues/new/choose` — the repo has bug/feature issue templates), Releases (`…/releases`), and
    one "Logs and data" row with **Logs** and **Data** buttons opening `AppDataPaths.Combine("logs")`
    and `AppDataPaths.Root` in Explorer (created first if missing, as the app's other folder buttons
    do). URLs live in one `ProjectLinks` static class; `RaiseUpdateAvailableAlert` reuses its
    Releases constant.
- **Changelog tab**: the accordion only, full height.
- **Legal & notices tab**: five `SettingsRow`s — License, Privacy notice, Terms of use, ComicVine &
  Metron notice, Open-source notices — then a faint "Built with" credits paragraph (Avalonia,
  CommunityToolkit.Mvvm, Entity Framework Core + SQLite, FluentAvalonia, Fluent System Icons,
  ScottPlot, Svg.Skia, NetSparkle; metadata from ComicVine, Metron, AniList, MangaDex, MangaBaka and
  Wikidata; inspired by ComicRack and ComicRack Community Edition).
- The documents are described once in a `LegalDocuments` registry (file name, title, description,
  icon, preformatted flag), which both the rows' commands and the viewer read, replacing the
  `switch` in `OpenLegalDocument`.
- Search: `PreferenceIndex` entries gain `SubTab` (`about.updates` and new `about.project` →
  `overview`, `about.changelog` → `changelog`, new `about.legal` → `legal`) and `RevealSubTab`
  handles `PreferencesSection.About`. `MainViewModel`'s "What's New" toast action (which already calls
  `RequestScrollToAnchor("about.changelog")`) then lands on the Changelog tab by itself.

### 6. Legal viewer

`LegalDocumentViewerOverlay.axaml` stays the modal card inside `OverlayShell` (its own close button,
per the 2026-09-07 fix):

- Width 680, `MaxHeight` 640, `PbChromeBrush` card, `PbElevationShadow`.
- Header: title (15 semibold) and, when the document has a `**Last updated:** yyyy-mm-dd` line, a
  faint "Last updated …" subtitle — that line is lifted out of the body so it is not shown twice.
  Right side: a ghost **Copy text** button (copies the raw file text) and the close button.
- Body: `MarkdownView` in a `ScrollViewer`; `LICENSE` uses `MarkdownLite.Preformatted`.
- Links to another bundled document swap the viewer's content in place.
- View model: `SelectedLegalDocument` (the registry entry), `SelectedLegalDocumentBlocks`,
  `SelectedLegalDocumentUpdated` (string?), `CopyLegalDocumentCommand`, `OpenLinkCommand`.

### 7. Legal texts

All five use only the `MarkdownLite` subset, carry `**Last updated:** 2026-09-26`, and read
correctly both on GitHub and in the viewer (relative links like `[LICENSE](LICENSE)` resolve on
GitHub and in-app). No disclaimer banner.

- **`TERMS.md`** — plain-language terms for the desktop app: the license (AGPLv3) governs the code
  and nothing here restricts rights it grants (if the two conflict, the license wins — AGPL §7/§10
  forbid adding further restrictions); your content and its legality; third-party services and their
  terms; plugins are third-party code; library sharing is your responsibility; no warranty (quoting
  the license's own disclaimer in plain words); changes. Must still read well as the installer's
  accept screen.
- **`PRIVACY.md`** — "No account, no telemetry" first; then **What leaves your computer**:
  - *On by default:* update check (GitHub; what it reveals: IP and a normal request; toggle in
    Preferences → About); Wikidata matching (character/series names, daily; toggle in Preferences →
    Automation).
  - *When you use them:* metadata searches (ComicVine, Metron with your credentials; AniList,
    MangaDex, MangaBaka without), reading-order sources (the sites named in the app), trackers you
    connect, Prowlarr/qBittorrent at the addresses you enter (can be plain http on your network),
    library sharing, plugins you install, and books whose pages reference web images (the book
    reader renders EPUB/FB2 HTML in WebView2).
  - **Where your data lives:** `%AppData%\Paperbunkr` (database, covers, backups, logs, plugins) and
    `%LocalAppData%\Paperbunkr\WebView2`; credentials encrypted with Windows DPAPI for your Windows
    account only; crash logs stay local and may contain file paths; files are written to your
    comics only when metadata write-back is turned on (off by default).
  - **Library sharing** (short): off by default, password, HTTPS, private network by default,
    announces your computer's name on the local network while on.
  - Changes are noted in the changelog.
- **`COMICVINE_NOTICE.md`** (file name kept: the csproj and installer reference it; titled "ComicVine
  & Metron notice" in the app) — bring your own key/login; the app's real throttles and cool-offs for
  both; how keys are stored (DPAPI, per Windows user; the ComicVine key travels over HTTPS as a query
  parameter because that is ComicVine's API); don't share keys; data ownership and attribution
  stay with each provider. The container/CI/env-var advice is removed.
- **`THIRD-PARTY-NOTICES.md`** (new; csproj copies it to output) — grouped by license: MIT (the
  Avalonia family, CommunityToolkit.Mvvm, DialogHost.Avalonia, FluentAvaloniaUI, FluentIcons,
  Optris.Icons, Svg.Skia, NetSparkleUpdater, ScottPlot, EF Core/SQLite, SharpCompress, SharpZipLib,
  DynamicExpresso, CoenM.ImageHash, MySqlConnector, Makaretu.Dns.Multicast, Roslyn scripting,
  Microsoft.Extensions/System.* packages), Apache-2.0 (SixLabors.ImageSharp 2.1.13, PDFium via
  bblanchon.PDFium, IronPython), LGPL-3.0 (LibHeifSharp and the libheif natives; 7-Zip `7z.dll`,
  LGPL-2.1+ with the unRAR restriction), BSD (CSJ2K), MS-RL (PDFiumSharpV2), Unlicense
  (VersOne.Epub), SIL OFL 1.1 (Bebas Neue, Source Serif 4, Inter), CC BY-SA 4.0 (`valiant.svg`,
  credited to its Wikimedia Commons source), CC0/public domain (Simple Icons marks, ESRB marks),
  flag-icons (MIT). Then "Trademarks and bundled artwork": publisher and service logos are trademarks
  of their owners and used to identify them; the publisher icon pack and some reader textures come
  from ComicRack CE. Full MIT and Apache-2.0 texts are included once each; LGPL/OFL/MS-RL/CC texts
  are linked to their canonical URLs (keeps the file readable; the MIT/Apache notice requirement is
  the one that needs the text itself). Only licenses verified from package metadata or files are
  asserted; anything else is described, not labelled.

### 8. Housekeeping

- `docs/paperbunkr-todo.md` and the wiki's About page (if it has one) updated to match.
- `README.md` acknowledgements left as they are (the notices file is the complete list).

## Error handling

- Missing bundled file (dev build before the copy step): row click does nothing, as today; the
  changelog tab shows "No changelog bundled with this build." instead of an empty panel.
- Clipboard or shell-open failures are caught and swallowed (the same tolerance as the app's other
  folder/URL buttons); the "Copied" label only appears after a successful copy.
- Malformed markdown never throws: unmatched markers stay literal, unknown lines become paragraphs.

## Testing

- `MarkdownLiteTests` (replaces `LegalDocumentParserTests`): wrapped paragraph/bullet join,
  heading levels, multi-line quote → one block, fenced preformatted, inline bold/italic/code/link,
  unmatched markers literal, `snake_case` not italic, and the existing theory over the real repo files
  extended to `THIRD-PARTY-NOTICES.md` and asserting no block contains a literal `**`.
- `ChangelogBodyFormatterTests` updated: groups carry blocks and tag kinds; a hard-wrapped bullet is
  one block; real `CHANGELOG.md` parses with no literal `**` in any run.
- `ChangelogSelectionTests`: empty Unreleased hidden; current = newest not newer than running
  (0.7.3 → 0.7.0 entry); running older than every entry → null; non-empty Unreleased never current.
- `LinkTargetResolverTests`: http(s), bundled doc names, anything else.
- `AboutInfoTests`: version report shape with and without a build label.
- `PreferencesScreenViewModelTests`: About tabs default to Overview; search hit on `about.legal`
  selects the Legal tab; opening a legal doc lifts "Last updated"; opening `LICENSE` yields one
  preformatted block; a doc link swaps the open document; `ChangelogRows` marks exactly one current.
- `WhatsNewOverlayViewModelTests`: empty entries dropped; rows carry groups.
- `UpdateAvailableOverlayViewModelTests`: release-notes URL from the version.
- Headless view test: `MarkdownView` renders one `SelectableTextBlock` per block and a link button.
- Before done: `avalonia-pro-max/review-checklist`, a forced `CoreCompile` rebuild (new
  `ChangelogEntryView.axaml`), targeted test run, and an on-screen check by the user.

## Non-goals

- General CommonMark (tables, images, nested lists, HTML).
- Changing any default (Wikidata stays on; update check stays on).
- Release notes inside the appcast (would change the release pipeline).
- The follow-ups in Q16.
