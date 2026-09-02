# Books Format Ingestion: FB2 + MOBI/AZW3 — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-01-books-format-ingestion-fb2-mobi-design.md*

## Survey notes (grounding for the steps below)

- `IBookTextSource`/`BookMetadata`/`BookChapter`/`BookParagraph`/`BookTextSpan` already live in
  `Paperbunkr.Engine/IO/Provider/Books/IBookTextSource.cs` — no changes needed there, both new
  sources implement the existing contract.
- `EpubBookSource` (`Paperbunkr.Engine/IO/Provider/Books/EpubBookSource.cs`) and
  `HtmlProseExtractor` (same folder) are the direct precedents: `Fb2BookSource` mirrors
  `EpubBookSource`'s shape (parse in the constructor, expose `Metadata`/`Chapters`, no-op
  `Dispose`); `MobiBookSource` reuses `HtmlProseExtractor.ExtractParagraphs` on its own
  reconstructed HTML-ish stream exactly like `EpubBookSource` does on real chapter XHTML.
- `Paperbunkr.Engine` only references `Paperbunkr.Common` — it has no visibility into
  `Paperbunkr.Data.Entities.BookFormat`. `Paperbunkr.Data` *does* reference `Paperbunkr.Engine`
  already (`Paperbunkr.Data/Books/AnnotationExportService.cs` constructs `EpubBookSource`
  directly), so the new `BookTextSourceFactory` (format → source) belongs in
  `Paperbunkr.Data/Books/`, not in Engine or App — it's the one place that can see both.
- `Book.Format` is stored via `builder.Property(b => b.Format).HasConversion<string>().HasMaxLength(32)`
  (`PaperbunkrDbContext.cs:1014`) with **no CHECK constraint** enumerating allowed values — adding
  enum members is not a schema change. Confirmed by inspection; will re-confirm empirically in Step 1
  by running `dotnet ef migrations add` and inspecting the generated `Up()`/`Down()` bodies before
  deciding whether to keep a migration file. The design doc still calls for "one EF migration" and a
  matching test (mirroring `AddBooksBrowseStateMigrationTests`), so one gets created either way for
  the historical/traceability record even if its body ends up empty.
- Every existing `book.Format == BookFormat.Epub` binary check needs to become a real switch or a
  `!= BookFormat.Pdf` check once Fb2/Mobi exist, or FB2/MOBI books silently fall into PDF-only code
  paths. Full call-site audit (grep for `BookFormat\.(Epub|Pdf)` across `src/`) found:
  - **Needs a real fix** (currently binary Epub-vs-Pdf, would misroute Fb2/Mobi):
    `BookFolderScanService.cs` (extension routing + source construction),
    `BookCoverThumbnailService.cs:53` (cover source dispatch), `HomeBookCard.cs:29` (chapter
    progress gate), `BookDetailScreenViewModel.cs:264,283,341` (format badge, chapter-progress
    gate, chapter/bookmark title resolution), `BookReaderScreenViewModel.cs:318` (**the one that
    matters most** — picks the reader's actual text source; unfixed, opening an FB2/MOBI book
    would try to load it as a PDF), `AnnotationExportService.cs:151` (chapter-title resolution for
    exported annotations).
  - **Already correct, no change needed**: `MainViewModel.cs` (`NavigateToBookReaderCore` already
    checks `format == BookFormat.Pdf`, not `== Epub`; the two `BookFormat.Epub` literals at
    lines 1441/1592 are dummy "not-Pdf" placeholders for history replay, harmless as-is).
- No PalmDB/MOBI parsing library exists anywhere in the solution's dependencies — confirmed via
  `Paperbunkr.Engine.csproj`'s package list. The MOBI side really is from scratch, as the design
  says. `System.IO.Compression.ZipFile` (already used by `EpubFixture`/`EpubBookSource`'s
  dependency chain) covers `.fb2.zip` unwrapping with no new package.
- Test fixture precedent: `EpubFixture`/`CbzFixture` in `Paperbunkr.App.Tests` hand-build real
  files byte-for-byte via the real compression APIs rather than mocking — `Fb2Fixture` and
  `MobiFixture` (the PalmDB/MOBI6 foundation-layer case) follow the same pattern. `MobiFixture`
  lives in `Paperbunkr.Engine.Tests` if that project exists, otherwise alongside the new
  `MobiBookSource` tests — confirmed in Step 1 survey below.
- Migration test precedent: `AddBooksBrowseStateMigrationTests.cs` (`Paperbunkr.Data.Tests`) —
  migrate to HEAD, seed via ORM, assert; migrate back to the prior migration, assert the added
  surface is gone and existing rows survive.

## Step 1: Confirm test project layout, then add `BookFormat.Fb2`/`Mobi` + migration ✅ done

**Files:** `src/Paperbunkr.Data/Entities/BookFormat.cs` (edit), new migration under
`src/Paperbunkr.Data/Migrations/`, `src/Paperbunkr.Data.Tests/AddFb2MobiFormatMigrationTests.cs` (new)
**What:**
1. Check whether a `Paperbunkr.Engine.Tests` project exists (`Glob src/Paperbunkr.Engine.Tests`) —
   if yes, new engine-level parser tests (Fb2/Mobi) go there; if no, they go in
   `Paperbunkr.App.Tests` alongside `EpubFixture`/existing book-source tests (current precedent,
   since no Engine-level test project exists as of this survey).
2. Add `Fb2` and `Mobi` to `BookFormat` enum.
3. Run `dotnet ef migrations add AddFb2MobiBookFormat --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App`,
   inspect the generated migration body. If empty (expected, per survey notes), keep the migration
   file anyway (matches the design's explicit ask + every prior schema-touching spec's convention)
   with a doc comment explaining why `Up()`/`Down()` are empty (no CHECK constraint on the
   `HasConversion<string>` column — new enum members are just new strings an existing column
   already accepts).
4. Migration test mirrors `AddBooksBrowseStateMigrationTests`: migrate to HEAD, insert a `Book` with
   `Format = BookFormat.Fb2` and one with `Format = BookFormat.Mobi`, assert both round-trip; migrate
   back to the prior migration, assert those rows are unaffected (no column to disappear, so this is
   mostly a smoke test that the migration doesn't corrupt anything).
**Depends on:** none
**Verify:** `dotnet test src/Paperbunkr.Data.Tests` (new test), `dotnet build` whole solution.

## Step 2: `Fb2BookSource` ✅ done

**Files:** `src/Paperbunkr.Engine/IO/Provider/Books/Fb2BookSource.cs` (new),
`<test project from Step 1>/Fb2Fixture.cs` (new), `<test project>/Fb2BookSourceTests.cs` (new)
**What:**
- `Fb2Fixture.Create(path, ..., zipWrapped: false)`: hand-authored minimal valid FB2 XML — a
  `<description><title-info>` block (title/author/sequence-for-series), a `<body>` with 2+
  top-level `<section>`s (one containing a nested sub-`<section>` to exercise flattening), inline
  `<emphasis>`/`<strong>` in at least one paragraph, and a `<coverpage>` referencing a `<binary>`
  element with base64 image bytes. `zipWrapped: true` wraps that same XML as the single entry of a
  `.fb2.zip` via `System.IO.Compression.ZipFile`.
- `Fb2BookSource(string filePath)`: detect zip via magic bytes (`PK\x03\x04`) at the start of the
  file, transparently read the wrapped entry's stream if so, otherwise read the file directly.
  `XmlReader`-based parse (not `XDocument.Load` — design specifies `XmlReader` for FB2's own
  encoding-declaration handling): title/author/series from `<description><title-info>` (author is
  FB2's `<first-name>`/`<last-name>` pair, joined; series from `<sequence name="...">`), cover from
  `<coverpage><image l:href="#id"/>` resolved against the matching `<binary id="id">` (base64
  decode), chapters from top-level `<body><section>` elements with nested sub-`<section>`s
  flattened into the parent's paragraph list (per the design's explicit anti-explosion rule),
  `<emphasis>`→Italic / `<strong>`→Bold spans over paragraph text extracted from `<p>` elements.
- `Fb2BookSourceTests`: metadata (title/author/series/cover) extraction from both the bare and
  zip-wrapped fixture, nested-section flattening (assert the sub-section's paragraphs land in the
  parent chapter, not a separate `BookChapter`), inline span extraction, and a malformed-XML
  negative case (clean exception, not a silent empty result — check what exception type
  `EpubBookSource`/`PdfBookSource` let propagate for their own malformed-file case first, for
  consistency).
**Depends on:** Step 1 (test project location decided there)
**Verify:** new unit tests green; no other file references `Fb2BookSource` yet (wired in Step 5).

## Step 3: MOBI/AZW3 foundation layer (PalmDB + MOBI6/PalmDOC) ✅ done

**Files:** `src/Paperbunkr.Engine/IO/Provider/Books/MobiBookSource.cs` (new),
`src/Paperbunkr.Engine/IO/Provider/Books/Mobi/PalmDbReader.cs` (new),
`src/Paperbunkr.Engine/IO/Provider/Books/Mobi/MobiHeaderReader.cs` (new),
`src/Paperbunkr.Engine/IO/Provider/Books/Mobi/PalmDocDecompressor.cs` (new),
`<test project>/MobiFixture.cs` (new), `<test project>/MobiBookSourceTests.cs` (new)
**What:**
- `PalmDbReader`: parses the 78-byte PDB header (name, attributes, record count) and the record
  offset table into a list of `(int Offset, byte[] UniqueId)` — exposes `GetRecord(int index)`
  slicing the file's bytes between that record's offset and the next record's offset (or EOF for
  the last record).
- `MobiHeaderReader`: reads record 0 as the PalmDOC header (compression type: 1=none, 2=PalmDOC,
  17480=Huffman/CDIC; text length; record count; encryption type: 0=none, else refuse) immediately
  followed by the MOBI header (identifier `MOBI`, header length, MOBI version, first non-book
  index) and, if present past the MOBI header's declared length, the EXTH metadata block (a
  tag-length-value sequence — pull author=100, title=503/full-title-offset+length in the PalmDOC
  header, series name=536/series index=537 if present, cover offset=201 combined with the first
  image record).
- `PalmDocDecompressor`: the LZ77-style scheme — a byte 0x00 is literal, 0x01-0x08 means "copy the
  next N bytes literally", 0x09-0x7F is a literal ASCII byte, 0x80-0xBF is a 2-byte
  back-reference (distance+length packed across both bytes), 0xC0-0xFF is a literal space followed
  by the byte XORed with 0x80. Concatenate all decompressed text records into one string.
- `MobiBookSource(string filePath)`: `PalmDbReader` → `MobiHeaderReader` → check encryption
  (throw a clear, typed exception if non-zero, e.g. `NotSupportedException("This MOBI/AZW3 file is
  DRM-protected and cannot be read.")` — do not attempt any further parsing past this check) →
  check compression type (throw the same kind of clear exception for Huffman/CDIC — "not attempted"
  per the design, not a silent garbage result) → `PalmDocDecompressor` on the text records →
  `HtmlProseExtractor.ExtractParagraphs` on the result, splitting into `BookChapter`s on heading
  tags (`h1`-`h3`) or `<mbp:pagebreak/>` markers (whichever the fixture/real files actually use —
  confirm against the hand-built fixture and note if real-world AZW3 samples in Step 4 use a
  different convention). Metadata from the EXTH block, falling back to filename for title if EXTH
  has none (same fallback `EpubBookSource` uses).
- `MobiFixture.Create(path, ...)`: hand-constructed minimal valid PDB + PalmDOC-compressed MOBI6
  file, byte-for-byte, same precedent as `EpubFixture`. Needs at least: a valid PDB header with 2+
  records (record 0 = PalmDOC+MOBI+EXTH header, record 1+ = PalmDOC-compressed text), EXTH title/
  author, and enough text across records to prove multi-record reassembly works. Also produce two
  negative fixtures: one with the encryption-type field set non-zero, one with compression type set
  to the Huffman value (17480) — content in both can be garbage, since the point is refusal before
  any decompression is attempted.
- `MobiBookSourceTests`: metadata extraction, multi-record decompression reassembly (assert the
  reconstructed text matches what was encoded, across a record boundary specifically), chapter
  splitting, DRM fixture → refusal with the expected exception type/message (not a crash, not
  garbage text), Huffman fixture → same.
**Depends on:** Step 1 (test project location)
**Verify:** new unit tests green, including both negative-path fixtures.

## Step 4: KF8 skeleton reconstruction — time-boxed spike (outcome: deferred)

**Not attempted this session.** This environment has no real `.azw3` sample files available (no
internet file-download capability, no existing fixtures checked into the repo) and no way to obtain
any - the design's own spike plan explicitly depends on testing against real fixtures to know
whether a reconstruction attempt is producing usable prose. Writing the binary-format skeleton-index
parsing blind, with nothing real to validate it against, would be unverifiable guesswork rather than
a real implementation - worse than not attempting it. Per the design's own explicit fallback
contract ("if it isn't producing usable prose within that spike, ship the foundation layer only...
this still delivers real value on its own"), Steps 1-3/5/6 ship as the complete, real deliverable for
this round: MOBI6-compatible content reads correctly (including files that are technically `.azw3`
but retain a MOBI6 fallback stream, which `MobiBookSource` already reads since it doesn't
differentiate by file extension, only by what's actually in the PalmDOC/MOBI6 stream). A pure-KF8
file with no MOBI6 fallback stream hits `MobiBookSource`'s existing clean-refusal path (empty
decoded text → `InvalidDataException` naming the pure-KF8 possibility explicitly), which is the
documented, legitimate shipping state per the design, not a bug.

**To resume this later:** obtain 2-3 real `.azw3` files (a Calibre-converted one with a MOBI6
fallback, ideally one pure-KF8 file) and start from `MobiHeaderReader.Kf8BoundaryRecordIndex`
(already implemented - reads EXTH tag 121, the record index where the KF8 part begins), which is
the one piece of this step already built and verified against the fixture builder.

<details>
<summary>Original step description</summary>

**Files:** `src/Paperbunkr.Engine/IO/Provider/Books/Mobi/Kf8SkeletonReconstructor.cs` (new, only if
the spike succeeds), `MobiBookSource.cs` (edit — try KF8 path first when an EXTH "KF8 Boundary" tag
is present, fall back to the MOBI6 stream from Step 3 on any reconstruction failure)
**What:** This is explicitly bounded per the design's Risks section, not open-ended:
1. Source 2-3 real `.azw3` sample files (Calibre-converted ones commonly keep a MOBI6 fallback
   stream; at least one "pure KF8" sample if available) — check into a test fixtures folder
   alongside `MobiFixture` if licensing/size allows, otherwise document what was tested against
   without checking the files in.
2. Implement the skeleton-index algorithm **independently from understanding the KF8 format**
   (never transliterate KindleUnpack's Python — licensing reason stated in the design): read the
   KF8-specific EXTH/FDST/skeleton index records, use them to split the decompressed text stream
   into per-fragment chunks and concatenate in order.
3. Decision point: if real fixtures produce usable prose (readable paragraphs, correct chapter
   boundaries) within a reasonably bounded effort, ship it wired into `MobiBookSource`. If not,
   **stop and ship the Step 3 foundation layer only** — `MobiBookSource` already refuses cleanly
   for anything it can't handle, so an unreconstructable pure-KF8 file falls through to the same
   clear-error path as the DRM/Huffman cases, which is a legitimate, documented shipping state per
   the design (not a bug to keep chasing).
**Depends on:** Step 3
**Verify:** if shipped, new tests against the real fixture files (paragraph count/spot-check
content, not exact-byte assertions given real-world file variance); if not shipped, a note in this
plan's own tracking (or the design doc's Risks section) recording what was tried and why it didn't
land, so a future session doesn't re-discover the same dead end from scratch.

</details>

## Step 5: `BookTextSourceFactory` + call-site fixes ✅ done

**Files:** `src/Paperbunkr.Data/Books/BookTextSourceFactory.cs` (new),
`src/Paperbunkr.App/Services/BookFolderScanService.cs` (edit),
`src/Paperbunkr.App/Services/BookCoverThumbnailService.cs` (edit),
`src/Paperbunkr.App/Models/HomeBookCard.cs` (edit),
`src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.Data/Books/AnnotationExportService.cs` (edit)
**What:**
- `BookTextSourceFactory.Create(BookFormat format, string filePath) : IBookTextSource` — a `switch`
  over all four formats (`Epub`→`EpubBookSource`, `Fb2`→`Fb2BookSource`, `Mobi`→`MobiBookSource`,
  `Pdf`→`PdfBookSource`), throwing on an unhandled value rather than falling through, so a future
  fifth format fails loudly instead of silently misrouting.
- `BookReaderScreenViewModel.cs:318-320` — replace the `== Epub ? Epub : Pdf` ternary with
  `BookTextSourceFactory.Create(_book.Format, _book.FilePath)`. This is the highest-priority fix
  (opening an FB2/MOBI book in the reader is the core user-facing outcome of this whole spec).
- `BookDetailScreenViewModel.cs`: line 264 `FormatBadge` becomes a real switch ("EPUB"/"PDF"/
  "FB2"/"MOBI" — AZW3 shares the "MOBI" badge per the design's format-tag decision); line 283
  `HasChapterProgress` becomes `book.Format != BookFormat.Pdf && book.ChapterCount > 0`; line 341's
  `if (book.Format == BookFormat.Epub)` in `LoadChaptersAndBookmarks` becomes
  `if (book.Format != BookFormat.Pdf)` with `new EpubBookSource(book.FilePath)` replaced by
  `BookTextSourceFactory.Create(book.Format, book.FilePath)`.
- `BookFolderScanService.cs`: extension routing gains `.fb2`/`.fb2.zip`→`Fb2`,
  `.mobi`/`.azw3`/`.azw`→`Mobi` branches; the `format == Epub ? new EpubBookSource(...) : new
  PdfBookSource(...)` ternary becomes `BookTextSourceFactory.Create(format, filePath)`.
- `BookCoverThumbnailService.cs:53-56`: `format == Epub ? TryGenerateFromEpubCover : TryGenerateFromPdfFirstPage`
  becomes `format == Pdf ? TryGenerateFromPdfFirstPage(...) : TryGenerateFromReflowCover(format, filePath, destPath)`,
  where `TryGenerateFromReflowCover` is `TryGenerateFromEpubCover` generalized to call
  `BookTextSourceFactory.Create(format, filePath)` instead of `new EpubBookSource(filePath)`
  directly (the rest of the method — reading `Metadata.CoverImageBytes` — is format-agnostic
  already).
- `HomeBookCard.cs:29`: `book.Format == BookFormat.Epub && book.ChapterCount > 1` becomes
  `book.Format != BookFormat.Pdf && book.ChapterCount > 1`.
- `AnnotationExportService.cs:151`: `book.Format != BookFormat.Epub` becomes
  `book.Format == BookFormat.Pdf` (inverted sense, same outcome — skip only for Pdf), and
  `new EpubBookSource(book.FilePath)` becomes `BookTextSourceFactory.Create(book.Format, book.FilePath)`.
**Depends on:** Steps 2 and 3 (both source types must exist for the factory to construct them)
**Verify:** full `dotnet build`; existing `BookFolderScanServiceTests`, `BookCoverThumbnailServiceTests`,
`BookDetailScreenViewModelTests`, `BookReaderScreenViewModelTests`, `HomeScreenViewModelTests` all
still green (these exercise the exact call sites being changed — regressions here would be real,
not test-infrastructure noise).

## Step 6: New routing + scan tests ✅ done

**Files:** `src/Paperbunkr.App.Tests/BookFolderScanServiceTests.cs` (edit)
**What:** Add cases mirroring the existing EPUB/PDF ones: an `.fb2` file (via `Fb2Fixture`) scans
in as `BookFormat.Fb2` with correct title/author/series; a `.fb2.zip` file does the same; a MOBI
file (via `MobiFixture`) scans in as `BookFormat.Mobi`. Reuses the existing test's folder-seeding
helper pattern.
**Depends on:** Steps 2, 3, 5
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter BookFolderScanServiceTests`

## Step 7: Full-suite verification + manual check — automated part done: whole-solution `dotnet build`
clean; `Paperbunkr.Data.Tests` 690/690 green; `Paperbunkr.App.Tests` 1475/1477 green. The 2 failures
are both pre-existing and unrelated to this spec - `BulkIssuePropertiesScreenViewModelTests...` (comic
bulk-editing domain, untouched by this work) and `DatabaseIntegrityServiceTests.CheckIntegrity_
ReturnsFalse_ForACorruptedDatabaseFile` (the known-flaky test a separate concurrently-running session
was already spawned to fix). Manual on-screen check still needs the user.

**Files:** none (verification only)
**What:**
1. `dotnet build` the whole solution.
2. `dotnet test` `Paperbunkr.Data.Tests`, `Paperbunkr.App.Tests` in full (not just the touched
   files) — this spec's call-site fixes touch enough shared code
   (`BookDetailScreenViewModel`/`BookReaderScreenViewModel`/`HomeBookCard`) that an unrelated
   regression is plausible.
3. Manual, on-screen: scan a real folder containing a real `.fb2` and (if the KF8 spike didn't pan
   out, at least) a real `.mobi` file, confirm they appear in Books with correct title/cover, open
   each in the reflow reader, confirm chapters/pagination work like an EPUB does. Ask the user to do
   this step directly (matches this project's standing note that on-screen GUI verification is
   routinely deferred to the user).
**Depends on:** Steps 1-6
**Verify:** as above.
