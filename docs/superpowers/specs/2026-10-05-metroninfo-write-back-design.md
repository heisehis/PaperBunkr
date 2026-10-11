# MetronInfo.xml write-back and ID import — design

**Status:** built 2026-10-05, uncommitted, not seen on screen. The plan (`2026-10-05-metroninfo-write-back-plan.md`)
lists where the build departs from this document.
**Origin:** 2026-10-05 research into the Metron-Project repos (`metroninfo`, `metron-tagger`, `trok`, `kurin`,
`mokkari`, `darkseid`). Settled over three grilling rounds; every decision below was answered by the user
(recommended option each time).

## Problem

Paperbunkr writes `ComicInfo.xml` (+ the `paperbunkr.json` sidecar) back into `.cbz` files and image folders
([2026-09-03-file-metadata-write-back-design.md](2026-09-03-file-metadata-write-back-design.md)). It never writes
`MetronInfo.xml`, so files we tag are invisible to Metron-Tagger, Codex, Comicbox, Perdoo and ComicTagger's
MetronInfo plugin, and the issue/series/creator IDs we hold are lost the moment metadata leaves the database.
Reading is the mirror image: a MetronInfo-only file imports, but its `<IDS>` are dropped.

## Facts this design is grounded on

- **CE reads MetronInfo and never writes it.** `Changes.txt`: "Read support for the MetronInfo.xml metadata file.
  The ComicInfo.xml still has priority… Exporting a file will remove any MetronInfo.xml that existed, but the
  imported data is preserved in the ComicInfo.xml on Export." Writing it is therefore a **deliberate deviation**
  from CE parity (standing rule: documented here, not assumed).
- **The read side already exists.** `EmbeddedComicInfoReader.TryRead` → archive engine →
  `XmlInfoProviders.Readers.DeserializeAll<T>` (`XmlInfoProviderFactory.cs:62`) tries providers in `Order`
  (ComicInfo, then `MetronInfoProvider`, `[XmlInfoFile("MetronInfo.xml", 1)]`) and returns the **first** that
  parses — no merge. `MetronInfoProvider` is one-way (`ToXml`: MetronInfo → ComicInfo) and flattens everything;
  `ComicInfo` has no ID fields, so `<IDS>` and every `id=` are discarded.
- **`Paperbunkr.Engine/MetronInfo.cs` is byte-identical to CE's copy and covers XSD v1.0 completely** (IDS with the
  10 sources, Publisher/Imprint, Series incl. alt names, Prices, GTIN, Credits with 42 roles, Stories, Arcs,
  Characters, Teams, Universes, Locations, Genres, Tags, Reprints, URLs, AgeRating, Notes, LastModified).
- **MetronInfo v1.1 (published 2026-07-19) is purely additive:** optional `AlternativeNumber` (sibling of
  `Number`) and optional `CommunityRating` (`AverageRating` decimal 0–5, optional `RatingCount`). `Genre` becomes a
  plain `resourceType` (still carries `id`). v1.0 documents remain valid under v1.1; a *strict v1.0 validator* would
  reject a document that contains the new elements.
- **XSD 1.1 only.** The schema's "at most one `primary` ID / URL" rules are XSD 1.1 assertions; .NET's
  `XmlSchemaSet` is XSD 1.0, so runtime XSD validation is not available. Trok enforces them in code.
- **`id=` means "the identification number from the source of information"** (schema docs) — the document's
  primary source, not always Metron.
- **Our data:** credits are eight comma-joined strings on `Issue` (Writer, Penciller, Inker, Colorist, Letterer,
  CoverArtist, Editor, Translator); `CreatorCredit.Role` holds only that field name. Issue/Series external IDs
  live in `ComicMetadataExternalId` (`ComicMetadataEntityKind.Issue`/`Series`, provider `ComicVine`/`Metron`);
  `Issue.GcdIssueId` holds GCD. Creator/Character/Team/Location/Publisher entities have external IDs in the same
  table. `Issue.MetadataSource` (`ComicProvider?`) names the scrape source.
- **Write-back shape today:** `MetadataFileWriteBackService` (`.cbz` + folders only; temp copy + `File.Replace`;
  `FileWriteBackCoordinator.Suppress` around it), `IssueToComicInfoMapper`, `PaperbunkrSidecar`,
  `MetadataFileFieldSnapshot` (serializes exactly what would be written; trigger sites compare before/after so a
  no-op edit writes nothing). Settings: `WriteMetadataToFiles` (master), `WriteMetadataAutomatically`,
  `WriteNativeSidecar`, shown as `SettingsRow` + `ToggleSwitch` in `AdvancedSection.axaml`.

## Decisions

| # | Decision |
|---|---|
| D1 | Write `MetronInfo.xml` as an **opt-in** setting, default off. Deliberate deviation from CE. |
| D2 | New bool `AppSettings.WriteMetronInfo`, default false, gated by the master `WriteMetadataToFiles` (same as `WriteNativeSidecar`). Needs a migration. |
| D3 | Write everything we hold: structured credits, characters, teams, locations, arcs, universes, genres, tags, age rating, GTIN, prices, URLs, community rating, alternative number, IDs. Omit what we don't hold. |
| D4 | `<IDS>`: one `<ID>` per known source — Metron and Comic Vine from `ComicMetadataExternalId(Kind=Issue)`, GCD from `Issue.GcdIssueId`. The issue's `MetadataSource` is `primary="true"`; no `MetadataSource` → no primary. Never invent an ID. |
| D5 | `id=` attributes (Publisher, Series, Creator, Character, Team, Universe, Arc, Genre, Tag…): use the **provider of the document's primary `<ID>`**, only where we hold that entity's ID from that provider. Never mix providers in one document. No primary → omit every `id=`. |
| D6 | `CoverDate`: write when year **and** month are known (day defaults to `01`, matching Metron's own convention); otherwise omit. `StoreDate` is never written (not held on `Issue`). |
| D7 | Omit `LastModified`. A "now" stamp would make `MetadataFileFieldSnapshot` differ on every check; nothing reads it from us. **Cost:** tagging software uses it to judge whether its source is newer than the file, so Metron-Tagger may treat our files as always stale. |
| D8 | No runtime XSD validation. Enforce "≤1 primary ID, ≤1 primary URL" in code; cover with serializer round-trip tests. If MetronInfo generation throws, still write ComicInfo.xml + sidecar and report a partial failure. |
| D9 | Existing `MetronInfo.xml` when `WriteMetronInfo` is **off**: leave it untouched, never delete (CE deletes; we don't). |
| D10 | Existing `MetronInfo.xml` when it is **on**: overwrite, carrying over elements we don't model (`MangaVolume`, `CollectionTitle`, `Reprints`, `URLs`, `StoreDate`, `Stories` beyond the title) from the existing file — the same overlay approach ComicInfo uses. |
| D11 | Target **MetronInfo v1.1**. Add `AlternativeNumber` and `CommunityRating` to our model (deviation from CE's v1.0 copy); emit them only when a value exists, so a document without them stays v1.0-valid. |
| D12 | Read side: **no new fallback** (it exists). Carry `<IDS>` and the v1.1 fields through import; link the issue/series/GCD IDs **only when the issue has no existing link**. The first-match reader rule is unchanged. |

## Design

### Components (all new unless noted)

- **`Paperbunkr.Engine/MetronInfo.cs`** (edit) — add `AlternativeNumber` (string) and `CommunityRating`
  (`AverageRating` decimal, `RatingCount` int?, with `*Specified` flags like the rest of the file). Hand-edited, not
  regenerated; note the divergence from CE in a header comment.
- **`Paperbunkr.Data/CeMigration/IssueToMetronInfoMapper.cs`** — `Apply(Issue, MetronInfo target, MetronIdContext)`,
  the inverse of `MetronInfoProvider.ToXml`, overlaying onto the file's current document (D10). Credit map:
  Writer→`Writer`, Penciller→`Penciller`, Inker→`Inker`, Colorist→`Colorist`, Letterer→`Letterer`,
  CoverArtist→`Cover`, Editor→`Editor`, Translator→`Translator`. One `<Credit>` per distinct creator name (schema:
  each creator once) with all of that person's roles; names split on `", "` as the forward mapper joins. Format/
  AgeRating/Genre/Tags/Characters/Teams/Locations map by the reverse of the existing tables.
- **`MetronIdContext`** — a small record the write-back service builds from the DB (issue IDs per provider, primary
  provider, entity-ID lookup by provider). Keeps the mapper pure and testable.
- **`MetronInfoXml` serialization** — `XmlUtility.GetSerializer<MetronInfo>()` (already used by the reader);
  `ToArray()` extension next to `ComicInfo.ToArray`. Primary-uniqueness check (D8) lives here.
- **`MetadataFileWriteBackService`** (edit) — add `includeMetronInfo`; when true, build the document and add
  `["MetronInfo.xml"]` to the `entries` dictionary (`UpdateZipEntries` already add-or-replaces, case-insensitively).
  Failure in the MetronInfo branch is caught separately (D8) and surfaces as a new partial-failure outcome.
- **`MetadataFileFieldSnapshot`** (edit) — `Capture(issue, includeMetronInfo)` adds a `MetronInfoContent` field
  (empty when off) so the no-op guard covers the new file. Call sites that hold settings pass the flag.
- **`MetadataWriteBackQueue`** (edit) — read `settings.WriteMetronInfo` at flush time alongside `includeSidecar`.
- **`EmbeddedComicInfoReader` / import** — add `EmbeddedMetronIds.TryRead(path)` returning the `<IDS>` and v1.1
  fields (a second read of `MetronInfo.xml` only; ComicInfo-first behaviour untouched). `LibraryFolderScanner`
  and the CE migrator apply them per D12.
- **Settings/UI** — `AppSettings.WriteMetronInfo` + migration (up/down-up round-trip test per project convention,
  and `Down()` as a no-op orphan per the existing write-back migration precedent); `PreferencesScreenViewModel`
  property + `PersistBehaviorSetting`; one `SettingsRow` + `ToggleSwitch` in `AdvancedSection.axaml` directly
  below "Write a paperbunkr.json sidecar", `IsEnabled="{Binding WriteMetadataToFiles}"`. Title/description to state
  that it is off by default and that other tools (Metron-Tagger, Codex, Comicbox) read it.

### Avalonia check

The only UI change reuses the existing `SettingsRow`/`ToggleSwitch` pattern with its master-toggle `IsEnabled`
binding; no new control, template or style, so no Avalonia constraint is introduced. Per project rules the
`avalonia-pro-max/review-checklist` subskill still runs before the row is called done.

### Out of scope

- `.cb7`/`.cbt`/`.cbr`/PDF write support (unchanged: `SkippedUnsupportedFormat`).
- `StoreDate`, `Prices`, `Reprints`, `VolumeCount`, `SortName`, `AlternativeNames`: written only if/when `Issue` or
  `Series` gains the data; not added here.
- Finer credit roles (Artist, Script, Plot…). Our storage is the eight coarse fields; pulling Metron's role detail
  is part of the separate Metron API utilization work.
- Metron API changes (conditional requests, `modified_gt`, cover-hash matching) — separate items from the same
  research, not in this design.
- Deleting or "migrating" a stale MetronInfo.xml (D9).

## Testing

- **Mapper:** per-field unit tests (credits grouped per creator, role map, CoverDate rule D6, v1.1 elements only
  when present, `id=` only for the primary provider D5, no primary → no `id=`, never two primary IDs/URLs).
- **Round trip:** `Issue → MetronInfo → bytes → MetronInfoProvider.ToXml` equals the ComicInfo
  `IssueToComicInfoMapper` produces for the mapped fields; and parse of the schema repo's `Sample.xml` (v1.1) and
  `valid.xml` fixtures succeeds, with `dup_primary_attr.xml` rejected by the in-code check.
- **Write-back service:** real synthetic CBZ (existing `CbzFixture`) with and without a pre-existing
  `MetronInfo.xml`: off → untouched byte-for-byte (D9); on → replaced, unmodelled elements carried (D10); folder
  variant; generation failure → ComicInfo.xml still written, partial-failure outcome (D8).
- **Snapshot/queue:** toggling `WriteMetronInfo` changes `Differ`; a no-op edit still writes nothing; settings are
  re-read at flush.
- **Import:** a MetronInfo-only fixture links issue/series/GCD IDs when none exist and leaves an existing link
  alone (D12); a file with both files still prefers ComicInfo for fields.
- **Settings:** Preferences persistence test mirroring `WriteNativeSidecar`'s; migration up-down-up test.
- **Not verifiable here:** that Metron-Tagger/Codex actually accept our output. Check by hand with
  `metron-tagger --validate` (or `xmlschema` against the v1.1 XSD) on one written file before calling it done.

## Open items for review

None blocking. Two judgement calls worth a second look: D6's day-defaults-to-01, and D7's "omit `LastModified`"
trade-off with Metron-Tagger.
