# Scraper & Organizer A: Reliability (Metron API token + leftover swap-file cleanup)

Date: 2026-10-21. Part 1 of 3 (A reliability, [B speed core](2026-10-21-scraper-organizer-b-speed-core-design.md),
[C automation and UX](2026-10-21-scraper-organizer-c-automation-ux-design.md)). Build order A, B, C: B and C assume Metron is reachable
through a token.

## Goals

1. Metron authenticates with an API token instead of username/password. Existing logins keep working until the user switches.
2. Leftover swap files (`*.crswap`, `*.pbwrite-*.tmp`, `~RF*.TMP`) left beside comics after metadata embedding or organizing are found and
   removed safely, and the cause of the `.crswap` ones is found and fixed.

## Facts established (2026-10-21 code survey)

- Metron credentials are `ProviderCredential` rows (provider "Metron"), DPAPI-encrypted, kinds Username and Password
  (`Paperbunkr.Data/Entities/CredentialKind.cs`, stored as int: new values are appended only).
- Basic auth is built in three places: `MetronClient` (header per request, `MetronLoginGuard` on 401s), `ReadingLists/Sources/MetronSource`,
  `ComicVine/MetronAccountClient`. Credentials are read by `ComicProviderFactory`, `ReadingListSourceRegistry`, `PullListSourceFactory`,
  `MetronAccountSyncRunner`, `PreferencesScreenViewModel` (load, `HasCredentials`, save, disconnect) and
  `OrganizeScrapeSettingsViewModel` (via `ComicProviderFactory.IsAvailable`).
- Earlier specs list Bearer-token login as out of scope (`2026-10-05-metron-account-sync-design.md:119`,
  `2026-10-05-metron-api-efficiency-and-matching-design.md:83`); this spec reverses that.
- Metron's own API docs were not reachable during design (bot protection), so the header scheme (`Token` vs `Bearer`) is not confirmed.
- No source, built DLL, installed file, app-data file or log contains the string `crswap`. The Organizer (`LibraryOrganizerService.PerformMoveAsync`)
  uses a plain `File.Move`. Metadata write-back (`MetadataFileWriteBackService.UpdateZipEntries`) uses `<comic>.pbwrite-<guid>.tmp` +
  `File.Replace`. The user's leftovers are `<comic>.cbz.crswap`, full-size copies, produced after a bulk scrape followed by Organize.
- CE has no equivalent sibling-swap pattern (its temp files live under `Path.GetTempPath()`); the cleanup here is a deliberate deviation.

## Part 1: Metron API token

### Data
- Append `ApiToken` to `CredentialKind`. Add `MetronAuthScheme` (`Token` | `Bearer`) as a per-credential setting stored with the token row
  (same encrypted store; the scheme is not secret but lives beside it so they stay together).
- No data migration: legacy Username/Password rows stay as they are. Resolution order when building auth: token if present, else Basic.

### One auth builder
- New `MetronAuth` in `Paperbunkr.Data/ComicVine/`: `static AuthenticationHeaderValue? Build(CredentialStore store)`, plus `string Key` (used by
  `MetronLoginGuard` so a switch between token and login resets the guard).
- `MetronClient`, `MetronSource`, `MetronAccountClient` replace their private Base64 code with it. The three factories and
  `MetronAccountSyncRunner` ask `MetronAuth.IsConfigured` instead of checking Username+Password.
- `HasCredentials` for Metron becomes "token present, or username and password present".

### Scheme probing
- The scheme defaults to `Token` until a probe proves otherwise. `Test connection` calls a cheap authenticated Metron endpoint with the saved
  scheme; on 401 it retries once with the other scheme, and on success stores the working scheme. If both fail it reports the token as
  rejected (not a transport error).
- If the user later pastes Metron's documented scheme, only the default constant changes.

### UI (Preferences > Connections, Metron tile)
- One "API token" field (masked, same shape as the existing credential panel in `ConnectionsSection.axaml:226-235`), Save and Disconnect.
- Username/Password move into a collapsed "Legacy login" expander (`chevronToggle`), shown expanded only when a legacy login exists and no token does.
- "Metron login not set" on `OrganizeScrapeSection.axaml:38` becomes "Metron not connected" and links to the tile.
- Design language: tokens, `fieldLabel`, `Button.primary` for Save, `Button.ghost` for Disconnect; no new hotkeys.

## Part 2: Leftover swap-file cleanup

### Root cause first
A test (`WriteBackVsOrganizerRaceTests`, Paperbunkr.Data.Tests/Organizing) creates throwaway `.cbz` files, enqueues write-back through
`MetadataWriteBackQueue` and runs `LibraryOrganizerService` Move on the same files concurrently, then asserts no sibling leftovers remain
and the library path is correct. Two outcomes:
- It reproduces a leftover: fix the coordination (the chained job in Spec C is the product fix; here, `FileWriteBackCoordinator` gains a
  per-path "settled" signal the Organizer awaits, and write-back checks the path still exists before `File.Replace`).
- It does not: record that in the spec's follow-up and rely on the sweeper; the origin of the `.crswap` name stays an open item that the
  sweeper tolerates by matching on the extension.

### SwapFileSweeper (`Paperbunkr.Data/Library/SwapFileSweeper.cs`)
- Patterns: `*.crswap`, `*.pbwrite-*.tmp`, `~RF*.TMP`, only inside the library's root folders, never recursing into hidden folders.
- For each leftover derive the original (`X.cbz.crswap` -> `X.cbz`; `X.cbz.pbwrite-*.tmp` -> `X.cbz`; `~RF*.TMP` -> matched by sibling stem).
  - Original exists and opens as a valid archive (zip central directory reads, or the Engine's provider opens it): leftover is deleted.
  - Original missing or unreadable: leftover is kept and reported as `RestoreCandidate` (path, size, modified, whether it opens as a valid
    archive). Never deleted automatically.
  - Leftover newer than 2 minutes, or locked: skipped this pass (a write may be in flight).
- Delete failures retry on the next pass; no exception escapes.
- Returns `SweepResult { Deleted, Skipped, RestoreCandidates, Errors }`.

### Triggers
- App start (background, low priority, after the scan is idle), after each write-back batch completes, and a manual "Clean up leftovers"
  button in Library Health.
- Library Health gets a "Leftover files" section: counts, the restore candidates list, and per-row "Restore from swap" (moves the swap over
  the missing original with `overwrite: false`) and "Delete".
- Results go to the Activity Center only when something was deleted or needs the user (no toast for a clean pass).

### Write-back hygiene
- `UpdateZipEntries` deletes its scratch in a `finally`; `TryDelete` catches `UnauthorizedAccessException` as well as `IOException`.
- The existing `EmbeddedSyncActions.CleanLeftoverTempFiles` is folded into the sweeper (one implementation).

## Testing
- `MetronAuthTests`: token wins over login, header scheme, key changes reset `MetronLoginGuard`, `IsConfigured` rules.
- `CredentialStoreTests`: `ApiToken` round-trip, DPAPI failure case, legacy rows untouched.
- `MetronClientTests` / `MetronAccountClientTests` / `ComicProviderAdoptTests`: updated header assertions (Basic and token both covered).
- `MetronSchemeProbeTests` with a fake handler (first scheme 401, second 200 -> scheme stored).
- `SwapFileSweeperTests`: valid original deletes; missing original is a restore candidate; locked and fresh files skipped; each pattern.
- `WriteBackVsOrganizerRaceTests` as above.
- Preferences view-model tests: token save/disconnect, legacy expander state. Lint suite per the design-language skill.

## Out of scope
OAuth; storing a token for ComicVine (it already has its key); deleting restore candidates without the user's say.

## As built (2026-10-21)

- The token scheme is a second appended credential kind (`AuthScheme`) rather than a setting on the token row; same effect.
- One more Metron caller than the survey found: the daemon's `AcquisitionCycle` (pull list + acquisition client); switched too.
- The race test is a unit test of the settle signal (`FileWriteBackSettleTests`) plus an organizer test that the move waits on it; a full
  write-back-queue-versus-organizer reproduction was not built, and the origin of the `.crswap` name is still unknown.
