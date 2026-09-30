# Grand Comics Database data (bundled extract) — design

*Status: approved by the user 2026-09-27 (grilling Q23–Q28 and G1–G8; the full design presented at once, since every branch was
settled); the user asked for it to be implemented straight away. Third of three sub-projects after the Story Event resolver and
the continuity map.*

## Goal

Use the Grand Comics Database's own data - the richest structured source for **series continuity** (renumberings, name and
publisher changes, merges, reboots) and **on-sale dates** - inside Paperbunkr, from GCD's downloadable dump, with the credit its
CC BY-SA 4.0 licence requires.

## Facts this rests on (verified 2026-09-27)

- **The dump** (the user's download, `current.zip` → `2026-09-15.db`, SQLite, 6.26 GB): 232,820 series, 2,615,672 issues (variants
  included), 6,798 series bonds, 582 story arcs, 17,698 publishers. Top languages: en 109k series, fr 43k, de 27k, nl 12k. Only
  725,334 issues carry an `on_sale_date`; `key_date` (yyyy-mm-dd, GCD's sort date) is far more common.
- **Bond types** (`gcd_series_bond_type`): 1 minor_name_numbering_continues (1,414), 2 major_name_numbering_continues (1,145),
  3 publisher_numbering_continues (3,806), 4 subnumbering_continues (122), 5 merge_numbering_continues (34), 6 merge (221),
  7 reboot (56). Bonds run origin (older) → target (newer), with the issues where they happen.
- **Trial extract** (comic publications only, no variant covers, the columns below): 229,836 series, 2,203,939 issues, 6,798 bonds →
  **99.7 MB**, **33.1 MB zipped**.
- **GCD's API** has no bonds or arcs and is throttled to 30 requests/hour anonymously - not used. The dump is downloaded by a logged-in
  user who accepts GCD's licensing terms; data is CC BY-SA 4.0 with "Grand Comics Database™" credit and a link to each object's page.
- **Metron** records carry `gcd_id` (series and issues), so Metron-scraped series map to GCD without guessing. `MetronClient` reads
  `cv_id` today but not `gcd_id`. Provider ids live in `ComicMetadataExternalId` (kinds Series/Issue).
- **Series relations** (`MediaRelation` + `RelationEvidence`) exist, with `Continuation` and `Reboot` types; issues have only a cover
  date (`Year`/`Month`/`Day`).

## Decisions

| # | Decision |
|---|---|
| Q23/G1 | A repo tool, `tools/Paperbunkr.GcdExtract`, turns the dump into a compact extract; the dump never ships or runs on users' machines. |
| Q24 | Extract = series (matching fields), issues (id, series, number, key/on-sale dates), series bonds, a `meta` table. No stories, credits, covers or arcs. |
| G2 | 99.7 MB > 50 MB → an **optional download** attached to a GitHub release, installed from Preferences; not in the installer. |
| G3 | `Series.GcdSeriesId` + `Series.GcdMatchSource` (Metron \| Name), `Issue.GcdIssueId`; GCD is not added to the scraper-provider enum. |
| Q25/G4 | Weekly "Match series to GCD" task (also run after the data is installed/updated): Metron `gcd_id` first, else a unique name + start year + publisher match; issues by number within a matched series. |
| Q26/G5 | Bonds between two owned series → series relations with GCD evidence; bonds to series not owned → read-only Related-tab lines; bonded series adjacent on the continuity map with a "continues as" lane marker. |
| G6 | On-sale dates read from the extract when needed (not copied to issues); chronology prefers them, falls back to cover dates. |
| G7 | GCD story arcs dropped for now. |
| Q27/G8 | Credit in `THIRD-PARTY-NOTICES.md` and About → Legal & notices with the dump date; "View on GCD" links; the extract carries its own licence in `meta`. |
| Q28 | Built after the continuity map, plugged into its chronology and lanes. |

## Design

### 1. The extract (`tools/Paperbunkr.GcdExtract`)

`dotnet run --project tools/Paperbunkr.GcdExtract -- <dump.db> <outDir> [--url <release asset url>]` writes:
- `gcd.sqlite` — tables `series(id, name, sort_name, year_began, year_ended, publisher, language, issue_count)` (deleted = 0,
  is_comics_publication = 1), `issue(id, series_id, number, key_date, on_sale_date)` (deleted = 0, `variant_of_id` null),
  `series_bond(origin_id, target_id, origin_issue_id, target_issue_id, bond_type)`, `meta(key, value)` with `dump_date`, `built_at`,
  `schema_version` = 1, `licence` = "CC BY-SA 4.0", `attribution` = "Grand Comics Database™ (https://www.comics.org)"; indexes on
  series name (NOCASE), issue series, bond origin/target; `VACUUM`.
- `gcd-<dump date>.zip` containing `gcd.sqlite`.
- `gcd-data.json` — `{ dumpDate, url, sha256, sizeBytes, schemaVersion }` for the zip.

The repo keeps `gcd-data.json` at its root (the app fetches it from GitHub's raw URL); the zip is attached to a release by the user.

### 2. In the app: install, update, remove

- **`GcdDataStore`** (Data): opens `%AppData%/Paperbunkr/gcd/gcd.sqlite` read-only when present; `IsInstalled`, `DumpDate`, series
  search by name, bonds by series ids, dates by issue ids. Absent → every GCD feature is a quiet no-op.
- **`GcdDataInstaller`** (App): reads the manifest (repo raw URL), downloads the zip to a temp file with progress (Activity job),
  checks size and SHA-256, unzips to a staging folder, checks `meta.schema_version`, swaps it in, then runs matching. Remove deletes
  the folder and clears `GcdSeriesId`/`GcdIssueId`.
- **Preferences → Connections**: a "Grand Comics Database data" row — not installed: **Download (33 MB)**; installed: "Data from
  2026-09-15" with **Update** (when the manifest is newer) and **Remove**; the credit line with a link to comics.org.

### 3. Matching (`GcdMatcher`, Data)

- Schema (one migration): `Series.GcdSeriesId` (int?), `Series.GcdMatchSource` (`GcdMatchSource?` Metron/Name), `Issue.GcdIssueId` (int?),
  indexes on both ids; `RelationEvidenceProvider.Gcd`.
- **Metron:** series and issues with a Metron id get Metron's `gcd_id` (series via `/series/{id}/`, issues via the details the scraper
  already fetches; `MetronClient` reads `gcd_id`), batched under the Metron rate limit.
- **Name:** the rest by name keys (volume/year ignored) + `year_began` equal to the series' start year (± 0) + normalized publisher;
  accepted only if exactly one GCD series fits. A series matched by Metron is never overwritten by a name match.
- **Issues:** within a matched series, by normalized issue number (leading zeros, "½", whitespace).
- Weekly task **"Match series to GCD"** (default on, local except the Metron step); also runs right after install/update.

### 4. Uses

- **`GcdBondSync`**: bonds between two owned, matched series → `MediaRelation` with `RelationEvidence{Provider = Gcd,
  ProviderRelationType = bond type, ProviderSourceId = origin GCD series id}`; the four numbering-continues types →
  *Continuation* (newer series continues older), reboot → *Reboot*, merge → *Related*; idempotent; a GCD relation whose bond
  disappeared is removed; user relations untouched. Runs after matching.
- **Related tab**: GCD relations show like other relations, marked "GCD", with **View on GCD**; bonds to series not in the library
  show as read-only lines ("Continues as *Hulk (2008)* — not in your library · View on GCD").
- **Continuity map**: lanes of bonded series are placed next to each other (older first) and the older lane's header reads
  "continues as ↓".
- **On-sale dates**: `EventChronology` spans, the continuity map's between-events placement and publication order use GCD's
  `on_sale_date` (else `key_date`) for matched issues, and cover dates otherwise.

### 5. Credit

`THIRD-PARTY-NOTICES.md` and About → Legal & notices: "Contains data from the Grand Comics Database™ (https://www.comics.org), as of
{dump date}, licensed CC BY-SA 4.0" with the licence link; "View on GCD" links go to `https://www.comics.org/series/{id}/` and
`/issue/{id}/`. The extract is itself CC BY-SA 4.0 and says so in `meta`.

## Testing

Tool: extraction from a tiny synthetic GCD-shaped database (filters, variants dropped, bond types, meta, manifest hash). Data:
`GcdDataStore` queries; `GcdMatcher` (Metron id wins, unique name/year/publisher match, ambiguous left alone, issue numbers);
`GcdBondSync` (type mapping, idempotent, removal, user relations untouched); chronology preferring on-sale dates and falling back
without data; forward-only migration test. App: installer (hash mismatch rejected, schema check, remove clears ids) with a fake
HTTP handler; Preferences row states; Related tab read-only lines. No live network; the real dump is used once, by hand, to build
the extract.

## Risks

- **Name matching** on 230k series: false positives are avoided by requiring year and publisher and a unique hit; recall on
  un-scraped libraries will be modest.
- **Hosting**: the download needs a release asset the user publishes; until then the Preferences row says the data isn't available yet.
- **Licence**: ShareAlike covers the extract; the app's code is separate. Attribution must stay visible wherever GCD data shows.

## Implementation notes (2026-09-27)

Built as designed, with these decisions made along the way:

- **The real extract:** the tool ran on the 2026-09-15 dump in 53 s. It holds 229,836 series, 2,203,939 issues and 6,798 bonds; the
  extract is 97.7 MB and the zip 33.7 MB (SHA-256 `6c15f1a9…`).
- **Hosting (changed from the design, at the user's request):** the data lives in its own public repo,
  [heisehis/paperbunkr-gcd-data](https://github.com/heisehis/paperbunkr-gcd-data). It holds `gcd-data.json`, the CC BY-SA 4.0
  licence, and a README with the attribution, the changes from the dump, the format and how to update. It has one release per dump
  (`gcd-2026-09-15`, published 2026-09-27; the download and hash were checked). The app reads the manifest from that repo's raw
  `main` URL, so new data needs no app release. Paperbunkr's root `gcd-data.json` (embedded in the build) points at the same
  release. The repo's homepage links back to Paperbunkr; Paperbunkr links to it from the README, notices, wiki and the Preferences
  row's **Data source** button.
- **No network when Preferences opens.** The manifest is also embedded in the app build, so the row shows "Download (34 MB)"
  without going online. The live manifest is fetched only on **Download** (a newer one wins) or **Check for update**.
- **Metron ids without extra calls:** the scraper now stores Metron's `gcd_id` on each issue (`Issue.GcdIssueId`). The matcher
  maps a series to the GCD series most of its scraped issues belong to (a majority is required). Metron's series record is asked
  only for series scraped before this change, at most 60 a run.
- **Name matching:** the key drops "(yyyy)" and volume markers, then applies CE's strip-down; the start year comes from "(yyyy)", else
  the earliest issue year. Publishers are compared after dropping suffixes like "Comics", "Worldwide" and "Inc". A match is accepted
  only when exactly one GCD series fits. On the real data, English "Hulk (2008)" has two Marvel candidates, so it's correctly left
  unmatched (Metron covers it).
- **Issue ids:** in a name-matched series, or one whose GCD series just changed, issue ids pointing outside the series are dropped
  and re-matched by number. Metron-matched series keep Metron's own issue ids even where GCD splits a run differently.
- **Deleted GCD relations stay deleted:** deleting a relation that has GCD evidence writes a `SeriesRelationDismissal` (migration
  `AddSeriesRelationDismissals`), and the bond sync skips that pair. A relation you made yourself just gains GCD evidence; when the
  bond goes, only that evidence goes.
- **Continuity lanes:** adjacency follows every *Continuation* series relation among the continuity's series. GCD bonds become
  those, and your own continuation relations count too. The older lane's issue count reads "· continues as ↓", with the name in the
  tooltip.
- **Dates:** matched issues' on-sale (else key) dates drive event spans, the connector's inference, and the continuity map's loose-issue
  order and year bands. Cards still show cover dates.
- **Related tab:** GCD-backed relations read "… · GCD". Bonds to series you don't have are listed under "Not in your library" with a
  GCD mark and a link to comics.org. That list's chips are no longer dimmed, since they now carry a link.
- **Credit:** a Data section in `THIRD-PARTY-NOTICES.md` (shown under About → Legal & notices), plus the credit line and licence link
  in the Preferences row. The dump date shows in the row, not in the static notice. `PRIVACY.md` now lists the GCD download, the
  weekly GCD matching, and the weekly "Check story events" Wikidata lookup, which it had been missing.
- **Removing the data** also clears every GCD id and GCD-only relation (`GcdBondSync.Run` with no store).
