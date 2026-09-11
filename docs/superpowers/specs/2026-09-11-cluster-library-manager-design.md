# Cluster Library Manager — Design Spec

*Date: 2026-09-11. Scope: a from-scratch port of ComicRack CE's two plugins — **ComicVineScraper**
(v1.0.102) and **Library Organizer** (v2.1.13) — into one Paperbunkr feature: "Cluster Library
Manager." Reference source for every CE-behavior claim below is the actual `.crplugin` bundles the
user supplied (extracted IronPython source, not memory/guesswork), per the project's standing rule
to verify against `_reference/ComicRackCE`-equivalent source before assuming any field, default, or
behavior. Produced via a `/grilling` pass (17 rounds) per CLAUDE.md's override of `brainstorming`'s
default one-question clarification step.*

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
features, not ported as plugins" — this spec's architecture (§2) independently arrived at the same
conclusion via the current grilling pass, and this is the first spec to act on it concretely.

## 2. Architecture: where this lives

Paperbunkr plugins are sandboxed `.csx` script bundles (`plugin.xml` + script files) invoked through
a fixed `IPluginEnvironment` — no typed `HttpClient`, no custom compiled view, no DI (confirmed: zero
`IServiceCollection`/`AddSingleton`/`ServiceProvider` usage anywhere in `src/`). The host app itself
has no DI container either; it's manually composed throughout (constructor injection of concrete
services, e.g. `PreferencesScreenViewModel`).

Given that, Cluster Library Manager is a **hybrid**, not a pure `.csx` plugin and not a compiled
native-assembly plugin tier:

- **Heavy logic is plain compiled C# classes** in `Paperbunkr.Data`/`Paperbunkr.App` —
  `ComicVineService`, `LibraryOrganizerService`, the token engine, `MatchScoreCalculator` — composed
  manually at app startup, same idiom as `TrackerHttpClients` (`src/Paperbunkr.Data/Tracking/TrackerHttpClients.cs`):
  static-readonly `HttpClient` fields, no `IHttpClientFactory` (that app-wide precedent's own doc
  comment: "no DI container/`IHttpClientFactory` in this app, and this app's single-user scale
  doesn't need one").
- **The plugin surface is thin `.csx` wrapper commands** (`plugin.xml` with `Library`/`Startup`/etc.
  hooks) so the feature still appears, enables/disables, and is discoverable through the existing
  Plugins screen like any other plugin — but the wrapper scripts call into the compiled services
  rather than reimplementing logic in script.
- Rejected: a native-assembly plugin tier (reflection/`AssemblyLoadContext`-loaded compiled plugin
  DLLs). This was the most literal reading of the original ask (`OrganizerScraperPlugin.cs`
  registering services, a plugin-authored `SettingsView.axaml`) but was explicitly ruled out during
  grilling — it would run fully unsandboxed code, and this codebase has deliberately built walls
  against exactly that (`Paperbunkr.Data`'s query-builders are being made `internal` with
  `[InternalsVisibleTo("Paperbunkr.App")]` specifically to keep `.csx` scripts off them per
  `docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md` §7; every plugin write
  already goes through an audited `IMetadataWriter` gate). A second, parallel unsandboxed plugin tier
  would undercut both.

### 2.1 New plugin-engine capabilities

Two small, deliberately general additions — usable by any future plugin, not special-cased to this
one:

**Host-service bridge.** `IPluginEnvironment` gains:
```csharp
T? GetHostService<T>(string key) where T : class;
```
Core-app startup populates a small `IPluginServiceRegistry` (in-memory, not persisted) —
`registry.Register<IComicVineService>("comicvine", comicVineService)` — and `.csx` command scripts
pull typed instances out by key. This is a keyed lookup, not a general DI container: it exists so a
compiled host service can be reached from script without the engine growing full dependency
injection.

**Declarative settings schema.** `plugin.xml` gains an optional `<Settings>` block, sibling to
`<Command>` elements:
```xml
<Settings>
  <Field key="ApiKey" type="Password" label="ComicVine API Key" testCommand="TestConnection" />
  <Field key="AutoChoose" type="Checkbox" label="Auto-select best match" default="false" />
  <Field key="FolderTemplate" type="TemplateEditor" label="Folder Template" />
  <Field key="Profiles" type="ProfileManager" label="Organizer Profiles" />
</Settings>
```
The engine renders this through **one new generic `PluginSettingsView`/`PluginSettingsViewModel`**
(property-bag driven), reachable from the existing gear icon on `PluginCommandRowViewModel`
(`src/Paperbunkr.App/ViewModels/PluginCommandRowViewModel.cs`). Field values persist through the
existing `IPluginConfig.GetSetting`/`SetSetting` (already auto-scoped by `PluginKey`). This is
additive: a plugin with no `<Settings>` block keeps using today's `ConfigScript`-hook path unchanged.
`type="ProfileManager"` is the one field type that doesn't render inline — it opens the dedicated
compiled profile-CRUD sub-screen described in §8, because CE-parity multi-profile management (named
profiles, each with a full template/mode/exclude-rule set) is genuinely too rich for a flat
declarative field list.

## 3. `ComicVineService`

Compiled class (`Paperbunkr.Data` or `Paperbunkr.App`, static-readonly `HttpClient`). All facts below
verified directly against the extracted CE source (`cvconnection.py`, `cvdb.py`, `utils.py`,
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

**Collision resolution — modal-per-file** (grilling Q9=B, the literal-CE-style option): a **new**
purpose-built dialog (`FileConflictDialogViewModel`/`View`, grilling Q15=B — not an extension of the
existing `ConfirmDialog`, which only supports two buttons, no checkbox, and no custom content slot,
confirmed by direct inspection of `src/Paperbunkr.App/Services/IDialogService.cs` and
`ConfirmDialogView.axaml`). Hosted the same way as `ConfirmDialog` — a shared instance inside its own
`OverlayShell` in `MainWindow.axaml`, exposed through `IDialogService`. Shows: Replace / Rename /
Skip buttons, a "do this for all remaining conflicts" checkbox, side-by-side cover thumbnail +
metadata for the incoming vs. existing book (mirrors CE's `DuplicateForm`). Rename uses CE's exact
numeric-suffix algorithm (`lobookmover.py:670-689`): strip an existing `" (N)"` suffix, then try
`" (1)"`, `" (2)"`, … up to 100 attempts. `ExecuteAsync` awaits this per collision inline in its move
loop (following the plain-`await`-inside-a-`foreach` idiom already used at
`PreferencesScreenViewModel.cs:3178-3228`'s `OpenBulkRemoveConfirm`). A pre-scan staged conflicts
screen (`MigrationViewModel`-style, resolving every collision before any move starts) was considered
during grilling and rejected in favor of matching CE's own per-file interaction model literally.

## 7. Profiles

Full CE-parity multiple named profiles (grilling Q16=B, not a single active configuration). New
`OrganizerProfile` entity — name, folder/file templates, mode, sanitization overrides, exclude-rule
reference (into the reused `IRulesEngine`), remove-empty-folders flag, etc. — with CRUD in a
dedicated compiled sub-screen opened from the `ProfileManager` settings field (§2.1). Selectable per
organize run, matching CE's own profile-switch UX.

## 8. Undo

Lightweight first pass (grilling Q17=A, explicitly scoped to extend post-implementation): each real
Move batch writes a compact log of old-path→new-path pairs; a single "Undo last organize" action
reverses them. Not CE's full `UndoMover`/`undo.dat` machinery — just enough to make Move mode safe to
use before richer undo (partial-batch undo, undo history beyond the last run) gets designed later.

## 9. Automation integration

"Scrape with ComicVine" and "Organize Library" register as new task types in Paperbunkr's existing
Scheduled Tasks system (Preferences → Automation — see project memory: 7-task scheduler shipped
2026-09-06) rather than shipping a second, parallel automation UI local to this plugin (grilling
Q13=A).

## 10. New persisted state (EF migrations)

- `ComicVineMatchMemory` — search key → chosen volume id (matchscore priorscore).
- `OrganizerProfile` — named organizer profiles.
- A compact move-log table for Undo (old path, new path, batch/job id, timestamp).
- No changes needed to `PluginSettingState`/`IPluginConfig` — simple settings (API key, toggles,
  mode) still fit its existing sparse key-value shape; only profiles and match-memory need real
  tables.

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
- Follows this project's existing `Paperbunkr.App.Tests`/`Paperbunkr.Plugins.Tests` conventions; no
  UI automation planned for this pass beyond what's already covered by the existing FlaUI/UIA3
  harness if a smoke test is added later.

## 12. Out of scope / future work

- CE's full advanced-settings text box (`IGNORE_BEFORE_YEAR`, `MAX_SEARCH_RESULTS`, etc.) — only the
  fields load-bearing for the methods in scope are exposed initially; the rest can be added later
  without architecture changes.
- Richer Undo (partial-batch, multi-run history) — noted in §8 as intentional follow-on work.
- CE's batch-level `scrape_in_groups_b`/summary-dialog behaviors beyond what §4's review flow covers.
