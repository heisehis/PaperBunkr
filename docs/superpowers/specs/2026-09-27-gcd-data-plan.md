# Grand Comics Database data — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-27-gcd-data-design.md*

Survey notes (2026-09-27):
- **Series has no start-year column.** The match year comes from a "(yyyy)" name suffix, else the earliest issue year.
- **`MediaRelation` semantics are "source is the {type} of target"**, so a *Continuation* source is the newer series, consistent
  with the event conventions. `MediaRelationResolver.TryCreate` adds User evidence.
- **Provider ids** are in `ComicMetadataExternalIds` (Series/Issue × ComicVine/Metron). `AppDataPaths.Root` has a test override.
- **Extraction logic goes in `Paperbunkr.Data/Gcd/`** so `Paperbunkr.Data.Tests` covers it; the console tool is a thin wrapper.

## Step 1: Data — the extract and the store
**Files:**
- `Gcd/GcdExtractor.cs`: dump → extract + zip + manifest
- `Gcd/GcdDataStore.cs`: a read-only reader
- `Gcd/GcdManifest.cs`
- tests using a synthetic GCD-shaped database

## Step 2: Data — schema and matching
**Files:**
- `Series` (+`GcdSeriesId`, `GcdMatchSource`), `Issue` (+`GcdIssueId`), `GcdMatchSource` enum, `RelationEvidenceProvider.Gcd`
- migration `AddGcdIds` + a test
- `MetronClient`: read `gcd_id` (series detail, issue details → `ComicVineIssueDetails.GcdId`)
- `Gcd/GcdMatcher.cs`: the Metron step and the name step, then issues
- `Gcd/GcdBondSync.cs`
- tests

## Step 3: Data — chronology uses on-sale dates
**Files:**
- `EventChronology.LoadSpans` and the chronology inference's dates: GCD on-sale/key date for matched issues (optional store parameter)
- `ContinuityMapLoader`: row dates for matched issues, and bonded-lane adjacency
- tests

## Step 4: Tool
**Files:** `tools/Paperbunkr.GcdExtract` (console, references Data), added to the solution.
**Run:** once on the user's dump, output kept outside the repo; `gcd-data.json` goes at the repo root.

## Step 5: App
**Files:**
- `Services/Gcd/GcdDataInstaller.cs`: manifest, download, hash check, swap, remove
- Preferences → Connections row
- scheduled task "Match series to GCD"
- `DetailTabsViewModel` Related tab: the GCD mark and read-only bond lines with View on GCD
- continuity map lane marker
- About → Legal & notices, and `THIRD-PARTY-NOTICES.md`
- App tests with fake HTTP

## Step 6: Docs and verification
Wiki, todo, design notes; Data + App targeted runs.
