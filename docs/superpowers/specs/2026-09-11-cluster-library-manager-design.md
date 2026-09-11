# Cluster Library Manager — Design Spec

*Date: 2026-09-11 (revised same day after external review — see §2). Scope: a from-scratch port of
ComicRack CE's two plugins — **ComicVineScraper** (v1.0.102) and **Library Organizer** (v2.1.13) —
into one Paperbunkr feature: "Cluster Library Manager," built as the first real consumer of
`docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md` (v4). Reference source for
every CE-behavior claim below is the actual `.crplugin` bundles the user supplied (extracted
IronPython source, not memory/guesswork), per the project's standing rule to verify against
`_reference/ComicRackCE`-equivalent source before assuming any field, default, or behavior. Produced
via a `/grilling` pass (22 rounds, the last 5 revisiting architecture after external review) per
CLAUDE.md's override of `brainstorming`'s default one-question clarification step.*

## 1. Goals and non-goals

**Goal:** ComicVine metadata scraping (search, match, apply) and library file organization
(token-templated move/copy with collision handling) as one coherent feature, reachable through
Paperbunkr's Plugins screen, with CE-parity behavior everywhere CE's behavior was actually verified
from source — deviating only where Paperbunkr's own architecture already does the job better (its
own rule engine, its own audited-write model, its own Activity/Scheduled-Task infrastructure).

**Explicit non-goal: a byte-for-byte UI port.** CE's plugins are WinForms/IronPython; Paperbunkr is
Avalonia/.NET. Behavior (token grammar, scoring formula, sanitization rules, collision semantics) is
ported faithfully; presentation (dialogs, screens) is native Paperbunkr.

**Confirms prior project intent.** `docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md` §1
already named "Comic Vine Scraper" among CE plugins to be "retired and absorbed as core Paperbunkr
features, not ported as plugins" — an early draft of this spec's architecture independently arrived
at a version of the same conclusion (a core-app hybrid), before external review and a direct
statement of intent from the project owner (a genuinely modular app, third-party devs able to ship
real standalone plugins, matching what CE's own `.crplugin` bundles offered) pushed the design one
step further, to §2 below.

## 2. Architecture: where this lives

**Revision note:** this section originally proposed a "hybrid" — compiled services living inside
`Paperbunkr.Data`/`Paperbunkr.App`, with thin `.csx` wrapper commands exposing them through the
existing sandboxed plugin tier. That design was reopened after external review (via Gemini, relayed
by the project owner) correctly identified it didn't deliver a standalone plugin at all — the compiled
logic would have been core-app code wearing a plugin manifest, not something a third party could ship
independently. That review also forced a real correction to this document's own earlier reasoning:
it had claimed a compiled native-assembly plugin tier would necessarily be "fully unsandboxed," which
isn't accurate — the actual enforcement mechanism in Paperbunkr's plugin design is *what interfaces a
plugin is given a reference to*, not whether its code arrives as script text or a compiled assembly.
That correction, combined with the project owner's explicit statement that the plugin system exists
specifically so third-party developers can add standalone pieces to a modular app, is what motivated
building a real native tier rather than patching the hybrid further.

Cluster Library Manager is now a genuine **native-tier plugin**, per
`docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md` (v4) — a real compiled .NET
project, shipped as its own `.pbplugin` package, installed independently of the Paperbunkr source
tree:

- **`OrganizerScraperPlugin`** is the plugin's `INativePluginModule` implementation (v4 §3) — this is
  the literal fulfillment of the original ask, not a reinterpretation of it. It implements
  `Initialize`, `RegisterCommands` (wiring `Library`/`Startup`/etc. hook handlers), and
  `CreateSettingsView`.
- **`ComicVineService` and `LibraryOrganizerService` ship inside the plugin's own compiled project**
  (its own `.csproj`, its own `.dll` in the installed `.pbplugin` package) — not `Paperbunkr.Data`,
  not `Paperbunkr.App`. Nothing about this feature's logic lives in the core solution.
  `TrackerHttpClients`'s static-readonly-`HttpClient` idiom (`src/Paperbunkr.Data/Tracking/TrackerHttpClients.cs`)
  is still the right pattern to follow for `ComicVineService`'s own `HttpClient` field — that
  precedent's rationale (no DI container anywhere in this codebase, single-user scale) applies to the
  plugin's own code exactly as it does to the host's.
- **`SettingsViewModel`/`SettingsView.axaml` are the plugin's own compiled Avalonia view and
  viewmodel**, returned from `CreateSettingsView` and hosted by the app through the new
  `INativePluginEnvironment.ShowModalAsync<TResult>(Control)` primitive (v4 §3) — a real,
  plugin-authored settings screen, not a declarative schema the host renders generically.
- **Trust model**: per v4 §2 (grilling Q20=B), this is a **full-trust** native plugin — it receives
  its own `PaperbunkrDbContext` via `INativePluginEnvironment.CreateDbContext` and writes directly,
  not through the audited `IMetadataWriter` gate the `.csx` tier uses. The Plugin screen shows this
  plugin with the "full read/write access to your library database" notice v4 §2 specifies.

## 3. `ComicVineService`

Compiled class inside the plugin's own project (static-readonly `HttpClient`, per §2). All facts
below verified directly against the extracted CE source (`cvconnection.py`, `cvdb.py`, `utils.py`,
`matchscore.py`, `cvimprints.py`, `configuration.py`).

**Endpoints** (all four CE-verified endpoints, base `https://comicvine.gamespot.com/api/`):
| Method | CE endpoint | Notes |
|---|---|---|
| `TestConnectionAsync(apiKey)` | `/search/?resources=volume&limit=1` | success = non-error `status_code` |
| `SearchVolumesAsync(query, page)` | `/search/?resources=volume&field_list=name,start_year,publisher,id,image,count_of_issues&query={q}` | paging via `&page=`, omitted on page 1 (CE comment: omitting page=1 avoids a real CV search bug) |
| `GetVolumeDetailsAsync(volumeId)` | `/volume/4050-{id}/` | `4050-` is CV's volume resource-type prefix |
| `SearchIssuesAsync(volumeId, page)` | `/issues/?filter=volume:{id}` | |
| `GetIssueDetailsAsync(issueId)` | `/issue/4000-{id}/` | `4000-` is CV's issue resource-type prefix |

**Deliberate deviations from CE** (both purely mechanical, not behavior changes):
- `format=json` instead of CE's `format=xml` — idiomatic `System.Text.Json` DTOs instead of an XML
  parser; same fields, same endpoints.
- User-Agent `PaperBunkrComicVinePlugin/1.0` (per the original spec) instead of CE's
  `ComicVineScraper/1.0.102 (...)` — this is Paperbunkr's own plugin identity, not a CE clone.

**Auth**: `api_key` query param only, no header. No client-id constant is ported (CE's
`&client=cvscraper` was CE's own app identification, not required by the CV API itself).

**Throttling** (`cvconnection.py` `__QUERY_DELAY_MS=1100`, `wait_until_ready`): every request waits
until ≥1100ms has elapsed since the previous one. On failure/malformed response: one flat 2500ms
retry (`__get_dom`'s `lasttry` pattern), then propagate. Not exponential backoff — ported as CE has
it.

**DTOs**: `ComicVineVolumeSearchResult` (id, name, start_year, publisher.name, count_of_issues, image
url — tried in CE's priority order small→medium→large→super→thumb), `ComicVineVolumeDetails`,
`ComicVineIssueSummary` (issue_number, name, id, image), `ComicVineIssueDetails` (id, volume,
issue_number, site_detail_url, name/title, cover_date→pub y/m/d, store_date→release y/m/d,
description→HTML-stripped summary, story arc/character/team/location/person credits with CE's
person-role map).

**Imprint resolution**: CE's ~70-entry static imprint→publisher `Dictionary<string,string>`
(`cvimprints.py`), exact-match only per CE's own docstring ("must EXACTLY match... case, punctuation,
etc."), plus a user-editable override list (CE's advanced `IMPRINT=X-->Y` lines) exposed as a
settings field.

## 4. Match scoring and review flow

`MatchScoreCalculator` ports CE's `matchscore.py` formula verbatim — six independent terms summed:
1. **namescore** — bag-of-words overlap between book series-name and candidate volume name (+5/match
   word, -1/unmatched book word, -1 per unmatched-remaining series word).
2. **priorscore** — flat +7 if this volume id was previously chosen for a similarly-named book.
   Needs new state: `ComicVineMatchMemory` (search key → chosen volume id), since nothing like CE's
   flat `SERIES_FILE` exists in Paperbunkr today (confirmed by grilling — included per Q12).
3. **publisherscore** — flat -6 for CE's hardcoded mirror/reprint-publisher list (`panini`,
   `deagostina` substrings; exact `marvel italia`/`marvel uk`/`semic_as`/`abril`).
4. **bookscore** — ±100 step function comparing parsed issue number against `count_of_issues` (always
   100 if the series has >100 issues — CE treats long runs as always-compatible since CV's count is
   often stale for them).
5. **yearscore** — 0 normally, -100 if book has a year but series doesn't, -500 if series starts
   after the book's year.
6. **recency_score** — small negative tiebreaker favoring newer series data.

**Review UX** (modal-per-book, per grilling Q14 — consistent with §6's collision dialog mechanism):
as each book is processed, a new `ComicVineMatchReviewDialog` shows ranked candidates for that one
book; user picks one or skips. This is gated by a plain boolean, matching CE exactly — CE's
`autochoose_series_b`/`confirm_issue_b` are mutually-exclusive checkboxes, not a numeric confidence
threshold (verified: no scoring cutoff exists anywhere in `configuration.py`/`configform.py`). When
auto-choose is on, the top-scored candidate is applied without showing the modal at all; when it's
off, the modal always shows, regardless of how high the top score is.

**Write path**: applied fields write **directly** to `Issue` — respecting overwrite-existing /
ignore-blank-value toggles (CE's `ow_existing_b`/`ignore_blanks_b`), same mechanism as Bulk Issue
Editing already uses. No `MetadataProposal` detour: the review dialog itself is the confirmation
step, so a second proposal-acceptance layer on top would be redundant (grilling Q11).

**Persisted per book, not batched into one final `SaveChanges()`.** Each book's match is confirmed
individually in the review dialog (§4 above), so it's saved individually too, right after
confirmation — matching the granularity the user already interacted at. Batching hundreds of
confirmed books' changes into one `PaperbunkrDbContext` and calling `SaveChanges()` once at the end
would mean a single constraint violation on book #742 rolls back all 1,000 already-user-confirmed
books by default EF behavior — discarding legitimate, explicitly-approved work over one bad row.
Same principle as §6's file-move batch: the unit of atomicity is one item, failures are logged and
skipped, the run continues.

**Field-toggle matrix**: CE's 20-entry `CheckedListBox` (series, number, published, released, title,
crossovers, writer, penciller, inker, cover_artist, colorist, letterer, editor, summary, imprint,
publisher, volume, characters, teams, locations, webpage) maps onto real `Issue` properties
(`src/Paperbunkr.Data/Entities/Issue.cs`) — credits stay flat `string?` fields (`Writer`,
`Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`, `Editor`), matching CE's own ComicInfo
convention; `characters`/`teams`/`locations` likewise flat strings. All default enabled, per CE.

## 5. `LibraryOrganizerService` — token engine and sanitization

**Token grammar** (full CE grammar, per grilling Q3=B) — `{prefix<name(args)>postfix}`, not simple
`{TokenName}` braces (the user's own prompt example used the simpler style, but that's not what CE
actually does, and the grilling decision was to port CE's real grammar faithfully). Regex root
(`lobookmover.py:1211`):
```
{(?P<prefix>[^{}<]*)<(?P<name>[^\d\s(>]*)(?P<args>\d*|(?:\([^){}]*\))*)>(?P<postfix>[^{}]*)}
```
~50 case-sensitive token names verified from `template_to_field` (`lobookmover.py:1199-1209`):
`series, number, count, Day, ReleasedDate, AddedDate, EndYear, EndMonth, EndMonth#, month, month#,
year, imprint, publisher, altSeries, altNumber, altCount, volume, title, ageRating, language, format,
startyear, writer, tags, genre, characters, teams, scaninfo, manga, seriesComplete, first, read,
counter, startmonth, startmonth#, colorist, coverartist, editor, inker, letterer, locations,
penciller, storyarc, seriesgroup, maincharacter, firstissuenumber, lastissuenumber, Rating,
CommunityRating, Custom`. Numeric padding via a bare digit-count arg (`<number2>` = 2-digit pad, not
printf-style `{Issue:000}`). Conditional groups (`?` prefix) and inversion groups (`!` prefix) ported
per CE's rules (`lobookmover.py:1348-1431`).

**Mapped onto Paperbunkr's real model**, not CE's ComicRack fields — critically, through the
`Effective*` extension methods (`src/Paperbunkr.Data/Metadata/IssueMetadataExtensions.cs`:
`EffectiveTitle()`, `EffectiveYear()`, `EffectiveVolume()`, `EffectiveNumber()`, `EffectiveCount()`,
`EffectiveFormat()`), not raw `Issue` properties — this is Paperbunkr's own equivalent of CE's
`Shadow*` filename-fallback fields, backed by the auditable `MetadataProposals` table rather than a
computed guess. `genre`/`tags` tokens filter `Issue.Tags` (`List<IssueTag>`) by `Field`; `Custom(key)`
looks up `Issue.CustomValues` (`IssueCustomValue`, name/value pairs — Paperbunkr's real analog of
CE's `Custom` token, not a packed string). Multi-value tokens (writer, genre, tags, characters, teams,
locations, credit roles) join with the token's own separator arg — CE's grammar already carries
`(separator)(series|issue)` args on these; per grilling Q8, no interactive picker is built, they just
join deterministically.

**Default template** (CE's own fallback, `losettings.py:396-397`, used only for a first-run
`OrganizerProfile`):
```
File:   {<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}
Folder: {<publisher>}\{<imprint>}\{<series>}{ (<startyear>{ <format>})}
```

**Sanitization** — CE's exact replace-map (`losettings.py:68`, `lobookmover.py:1965-1970`), verified,
per grilling Q5=A:
| char | replacement |
|---|---|
| `?` `/` `\` `*` | stripped |
| `:` | `" - "` |
| `<` | `[` |
| `>` | `]` |
| `\|` | `!` |
| `"` | `'` |

Plus: trailing-period strip per folder segment, whitespace-collapse (`\s\s+` → single space),
overall `.strip()`.

**Excludes**: reuse Paperbunkr's own `IRulesEngine`/`SmartListQueryBuilder` (already exposed to
plugins — see `PluginConditionGroup`/`SmartListField`/`SmartListOperator` in the `FranchiseTools`
sample plugin, `src/Paperbunkr.Plugins.Tests/SamplePlugins/FranchiseTools/rated-not-checked.csx`)
instead of porting CE's bespoke `ExcludeGroup` nested-AND/OR engine — same capability, no duplicate
rule engine in the codebase.

## 6. Planning, execution, and collisions

Two-phase, modeled on `MigrationViewModel`'s Locate→Preview→Conflicts→Commit flow
(`src/Paperbunkr.App/ViewModels/MigrationViewModel.cs`) rather than a single move-everything call:

- **`PlanAsync(books, profile)`** — pure, no I/O. Runs the token engine against each book, builds
  destination paths, flags every path that already exists on disk against `File.Exists` (CE's own
  definition of "duplicate" — a path-existence check, no hash/metadata comparison; verified there is
  no such comparison anywhere in `loduplicate.py`, which is UI only). Returns an `OrganizePlan`.
- **`ExecuteAsync(plan, collisionResolver, activityHandle)`** — performs moves/copies, reporting
  determinate progress via the existing `IActivityService`/`IActivityJobHandle.Report(done, total,
  detail)` (`src/Paperbunkr.App/Services/IActivityService.cs`), following the same idiom as
  `LibraryScreenViewModel.ImportDroppedPathsAsync`. Move/Copy/Simulate modes match CE's `Mode` enum
  (`locommon.py:165-168`); default is Move, per CE's own default.

**Failure isolation — per-item, not one batch-wide transaction.** Review correctly flagged that this
spec left partial-batch failure behavior unstated. The fix is *not* wrapping the whole batch in one
EF transaction, though — that would be actively wrong for this workload: a file move is a real
filesystem side effect a database transaction cannot roll back, so if item 400 of 1,000 failed and
the DB transaction for the whole batch rolled back, files 1–399 would already be physically sitting
at their new paths while the database still pointed at their old ones — a worse, actively-corrupted
state than no rollback at all. A single long-lived transaction spanning a large batch's worth of file
I/O is also its own anti-pattern against a SQLite-backed store (lock contention across a
long-running operation). The correct unit is **per-item**: move (or copy) one file, then immediately
persist that one `Issue`'s updated path, as one small step; on failure, log it, leave every
already-completed item's file and database state exactly as it landed, and continue to the next item
— exactly CE's own verified behavior (`lobookmover.py`'s `process_books`: a single book's `Failed`
result increments a counter and the loop continues, the batch is never aborted by one item's
failure). The Undo log (§8) already gives a real, if manual, way to reverse a batch after the fact,
which is the actual answer to "what if it partially fails" for an operation whose real-world side
effects can't be transactionally undone in the first place.

**Collision resolution — modal-per-file** (grilling Q9=B, the literal-CE-style option): a **new**
purpose-built dialog (`FileConflictDialogViewModel`/`View`, grilling Q15=B — not an extension of the
existing core-app `ConfirmDialog`, which only supports two buttons, no checkbox, and no custom
content slot, confirmed by direct inspection of `src/Paperbunkr.App/Services/IDialogService.cs` and
`ConfirmDialogView.axaml`). Since this is now the plugin's own compiled view (§2), it isn't added into
`MainWindow.axaml` or exposed through the core `IDialogService` — it's shown via
`INativePluginEnvironment.ShowModalAsync<TResult>(Control)` (v4 §3), the same generic hosting
primitive `CreateSettingsView`'s view uses, awaited inline from `ExecuteAsync`. Shows: Replace /
Rename / Skip buttons, a "do this for all remaining conflicts" checkbox, side-by-side cover thumbnail
+ metadata for the incoming vs. existing book (mirrors CE's `DuplicateForm`). Rename uses CE's exact
numeric-suffix algorithm (`lobookmover.py:670-689`): strip an existing `" (N)"` suffix, then try
`" (1)"`, `" (2)"`, … up to 100 attempts. `ExecuteAsync` awaits this per collision inline in its move
loop (the same non-blocking `await`-inside-a-`foreach` idiom already used at
`PreferencesScreenViewModel.cs:3178-3228`'s `OpenBulkRemoveConfirm` — confirmed during external
review that this does not block Avalonia's render thread; the loop *pausing* to wait for a user
decision is the intended behavior, not a defect). A pre-scan staged conflicts screen
(`MigrationViewModel`-style, resolving every collision before any move starts) was raised twice during
review as the more batch-robust alternative and is still on the table if large-batch collision counts
turn out to make modal-per-file too tedious in practice — the "apply to all remaining" checkbox is
this design's mitigation for that in the meantime, not a claim the concern is fully moot.

## 7. Profiles

Full CE-parity multiple named profiles (grilling Q16=B, not a single active configuration).
`OrganizerProfile` — name, folder/file templates, mode, sanitization overrides, exclude-rule
reference (into the reused `IRulesEngine`), remove-empty-folders flag, etc. — with CRUD in its own
view within the plugin's compiled settings UI (§2), not a declarative schema the host renders.
Selectable per organize run, matching CE's own profile-switch UX. Persistence: see §10 — the
plugin's own database, not a core Paperbunkr migration.

## 8. Undo

Lightweight first pass (grilling Q17=A, explicitly scoped to extend post-implementation): each real
Move batch writes a compact log of old-path→new-path pairs; a single "Undo last organize" action
reverses them. Not CE's full `UndoMover`/`undo.dat` machinery — just enough to make Move mode safe to
use before richer undo (partial-batch undo, undo history beyond the last run) gets designed later.

## 9. Automation integration

"Scrape with ComicVine" and "Organize Library" register as new task types in Paperbunkr's existing
Scheduled Tasks system (Preferences → Automation — see project memory: 7-task scheduler shipped
2026-09-06) rather than shipping a second, parallel automation UI local to this plugin (grilling
Q13=A). **Unverified, flagged rather than assumed**: this requires the Scheduled Tasks system to
expose a public registration point a native plugin assembly can add a task type to. That surface
wasn't inspected during this grilling pass — the implementation plan needs to check it directly
before committing to this approach; if no such extension point exists, the fallback is a
plugin-local automation setting in its own settings view (§2) instead.

## 10. Persisted state — the plugin's own database, not core migrations

Two earlier drafts of this section are both superseded here, for different reasons:
- The original draft proposed new EF Core migrations directly on `Paperbunkr.Data`'s core schema —
  rightly flagged during external review as coupling the core migration history to one optional
  plugin's concerns ("database schema contamination").
- An intermediate revision proposed avoiding that by cramming `OrganizerProfile`/`ComicVineMatchMemory`
  into `IPluginConfig`'s flat JSON-blob settings storage instead. That was a reasonable compromise
  under the *previous* sandboxed-extension architecture, where the plugin had no database access at
  all. It's no longer the right call now that this is a full-trust native plugin (§2) with its own
  `PaperbunkrDbContext` access and its own compiled project — relational data belongs in a real
  schema, not serialized into a settings key-value store, once there's no sandboxing reason forcing
  that compromise.

**Resolved approach, revised after external review**: the plugin ships its **own separate embedded
database file**, isolated from the core `paperbunkr.db` schema and migration history, stored under
`%AppData%\Paperbunkr\plugins\cluster-library-manager\`. This uses **LiteDB**, not SQLite/EF Core —
a pure-managed, zero-native-interop embedded .NET database. Review correctly flagged a real packaging
bug in the original SQLite choice: `Microsoft.Data.Sqlite` needs a native binary
(`e_sqlite3.dll`) resolved from a `runtimes/<rid>/native/` path structure that the plugin
install pipeline was flattening away (see v4 §4 for the general fix to that pipeline, which now
preserves subfolders for `Native`-tier packages regardless). LiteDB sidesteps the problem at this
plugin's own level, on top of that general fix, since it has no native binary to place correctly in
the first place — simpler, and one fewer native-library-loading edge case for this specific plugin's
install to get right. Collections:
- `ComicVineMatchMemory` — search key → chosen volume id (matchscore priorscore).
- `OrganizerProfile` — named organizer profiles.
- A compact move-log collection for Undo (old path, new path, batch/job id, timestamp).
- Simple scalar settings (API key, toggles, mode) live here too rather than `IPluginConfig`, now that
  the plugin manages its own storage end to end — one database file, not two storage mechanisms for
  one plugin's state.

`INativePluginEnvironment.CreateDbContext` (v4 §3) is specifically the *core* `PaperbunkrDbContext`
factory, for reading/writing library data (`Issue`, `Series`, etc.) — this plugin-owned SQLite file is
a second, separate database the plugin manages itself for its own concerns, not something routed
through that factory.

## 11. Testing approach

- `ComicVineService`: unit tests against a fake `HttpMessageHandler` (no live network) covering the
  four endpoint URL shapes, throttle timing (with a fake clock), retry-once behavior, and DTO
  deserialization against fixture JSON.
- `MatchScoreCalculator`: table-driven unit tests, one per scoring term, using known CE-equivalent
  input/output pairs derivable from `matchscore.py`'s own logic.
- Token engine: unit tests per token type (scalar, padded numeric, multi-value+separator,
  conditional group, inversion group) plus the full default template end-to-end against a fixture
  `Issue`.
- Sanitization: table-driven test over the exact CE replace-map plus trailing-period/whitespace
  rules.
- `LibraryOrganizerService.PlanAsync`: pure-function tests (temp-dir-free, since it does no I/O) for
  collision detection and path generation.
- `ExecuteAsync`: integration-style tests against a temp directory, covering Move/Copy/Simulate modes
  and each collision-resolution branch (Replace/Rename/Skip) via a stub resolver delegate (no real
  dialog in tests).
- Lives in the plugin's own test project (e.g. `ClusterLibraryManager.Tests`), following this
  project's existing `Paperbunkr.App.Tests`/`Paperbunkr.Plugins.Tests` conventions in style but built
  and run independently of the host solution, matching §2's standalone packaging. No UI automation
  planned for this pass beyond what the existing FlaUI/UIA3 harness already covers, if a smoke test
  targeting the installed plugin is added later.

## 12. Out of scope / future work

- CE's full advanced-settings text box (`IGNORE_BEFORE_YEAR`, `MAX_SEARCH_RESULTS`, etc.) — only the
  fields load-bearing for the methods in scope are exposed initially; the rest can be added later
  without architecture changes.
- Richer Undo (partial-batch, multi-run history) — noted in §8 as intentional follow-on work.
- CE's batch-level `scrape_in_groups_b`/summary-dialog behaviors beyond what §4's review flow covers.
- Everything v4 itself scopes out (collectible-ALC live unload, a published/versioned NuGet-style
  distribution channel for `Paperbunkr.Plugins.Abstractions`) — this plugin ships against v4 as
  designed, restart-to-apply included.
