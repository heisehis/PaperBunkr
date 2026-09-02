# Books Format Ingestion: FB2 + MOBI/AZW3 — Design

**Second of three deferred follow-ups** to the Thorium/eBoox research-doc application to Paperbunkr's
Books section. First spec (reader ergonomics + annotations/export,
`2026-09-01-books-reader-ergonomics-and-annotations-design.md`) is written and committed, on hold
pending its own implementation plan. Third (accessibility/TTS) and fourth (catalog federation + cloud
sync) are separate later specs.

## Background

The research doc's ingestion wishlist (EPUB2/3, PDF, MOBI, AZW3, FB2, DOCX, RTF, ODT, HTML, plus
in-memory zip/rar archive extraction) was scoped down: the user only wants actual **e-book formats**
(MOBI, AZW3, FB2) — office documents (DOCX/RTF/ODT/HTML) and general archive-wrapped ingestion are
out of scope for this round.

Ingestion already goes through a clean seam. `IBookTextSource` (`src/Paperbunkr.Engine/IO/Provider/
Books/IBookTextSource.cs`, namespace `cYo.Projects.ComicRack.Engine.IO.Provider.Books` — this Engine
code descends from the ComicRack CE engine) exposes `BookMetadata` (Title/Author/SeriesName/
CoverImageBytes) and `IReadOnlyList<BookChapter>`, where each `BookChapter` is `Title` +
`IReadOnlyList<BookParagraph>`, and each `BookParagraph` is `Text` + `IReadOnlyList<BookTextSpan>`
(bold/italic ranges only — "prose readability is the goal, not layout fidelity", per the interface's
own doc comment). `EpubBookSource` and `PdfBookSource` both implement it; `BookFolderScanService`
dispatches on file extension. `BookFormat` (`Paperbunkr.Data.Entities`) tags each `Book` row.

PDF is a fundamentally different reading experience — rasterized page images via `PdfPageReaderScreenViewModel`,
not reflowable text — so it doesn't participate in `IBookTextSource`'s chapter/paragraph model the
same way. FB2 and MOBI/AZW3 are both reflowable prose formats, so both target `IBookTextSource` and
plug straight into the existing reflow reader (and the `ParagraphView` control introduced by the first
spec) with zero reader-side changes.

**CE note:** no CE equivalent, same as the original Novels spec — nothing to verify against.

## Decisions

| Area | Decision |
|---|---|
| **New `BookFormat` values** | `Fb2`, `Mobi` (AZW3 shares `Mobi` — same container family, distinguished at parse time not by format tag). One EF migration. |
| **Routing** | `BookFolderScanService`: `.fb2`/`.fb2.zip` → `Fb2BookSource`; `.mobi`/`.azw3`/`.azw` → `MobiBookSource`. Both implement `IBookTextSource`. |
| **FB2 parsing** | Native XML parsing (`XmlReader`, handles the format's own encoding declarations). `<description><title-info>` → metadata; `<coverpage>`-referenced `<binary>` → cover bytes; top-level `<body><section>` → `BookChapter`s, nested sub-sections flattened into the parent chapter's paragraphs (avoids chapter-list explosion for books that use sections as sub-headings); `<emphasis>`/`<strong>` → `BookTextSpan` Bold/Italic. `.fb2.zip` (a very common FB2 distribution convention) is unwrapped by `Fb2BookSource` itself — format-specific, not general archive ingestion. |
| **MOBI/AZW3 parsing — foundation layer** | Native PDB (PalmDB) header + record table parsing; record 0's PalmDOC header (compression type, encryption flag) + MOBI header + optional EXTH metadata block (author/title/series/etc.); PalmDOC (LZ77-style) or uncompressed text-record decompression, reusing the existing `HtmlProseExtractor` on the resulting simplified-HTML stream (same flattening `EpubBookSource` uses), chapters split on heading tags / the format's own `<mbp:pagebreak/>` markers. |
| **MOBI/AZW3 parsing — KF8 layer** | KF8 (AZW3's real content structure) content is reconstructed via its **skeleton index** (splits the decompressed text stream into per-"page" chunks, concatenated in order) — algorithm ported (reimplemented independently in C#, not transliterated) from **KindleUnpack**, the e-book community's reference implementation for this reconstruction. **Fragment-index resolution (precise footnote/cross-reference reinsertion) is out of scope** — consistent with the "prose readability, not layout fidelity" bar `BookParagraph` already states. Treated as a bounded implementation spike (see Risks) with an explicit fallback to foundation-layer-only if it doesn't produce usable prose against real fixtures. |
| **Explicitly unsupported, clean failure not silent corruption** | DRM-encrypted files (PalmDOC header's encryption-type field checked up front, non-zero → refuse with a clear error); Huffman/CDIC-compressed text (rarer, separate complexity axis from KF8, not attempted); pure-KF8 files with no usable skeleton reconstruction (falls through to the same clear-error path). |
| **UI** | None needed — no existing screen references `BookFormat` directly; FB2/MOBI books flow through the existing scan → import → reflow-reader pipeline exactly like EPUB today. |
| **Licensing** | KindleUnpack is GPL-licensed; Paperbunkr has no stated project license currently. The skeleton-index *algorithm* is implemented independently in C# from understanding the format, not copied/transliterated from KindleUnpack's Python source, to avoid license entanglement. |

## Components

### 1. Data model

- `BookFormat` enum: add `Fb2`, `Mobi`. One EF migration (enum-as-string + sentinel pattern, same as
  every other enum column in `AppSettings`/`Book`).

### 2. `Fb2BookSource` (`Paperbunkr.Engine/IO/Provider/Books/Fb2BookSource.cs`)

- Implements `IBookTextSource`. Detects a zip container at open time (magic bytes) and transparently
  extracts the single `.fb2` entry if so; otherwise reads the file directly as XML.
- `XmlReader`-based parse of `<description><title-info>` for metadata, `<body>` for chapters — see
  Decisions for the section-flattening rule and inline-formatting mapping.

### 3. `MobiBookSource` (`Paperbunkr.Engine/IO/Provider/Books/MobiBookSource.cs`)

- Implements `IBookTextSource`. Internal structure: a `PalmDbReader` (PDB header + record offset
  table), a `MobiHeaderReader` (PalmDOC header, MOBI header, EXTH metadata block), a
  `PalmDocDecompressor` (the LZ77-style scheme), and a `Kf8SkeletonReconstructor` (the ported
  algorithm from Decisions).
- Parse flow: read PDB + MOBI/EXTH headers → check encryption flag (refuse if set) → check
  compression type (refuse if Huffman/CDIC) → decompress text records → if an EXTH "KF8 Boundary"
  tag is present, attempt `Kf8SkeletonReconstructor` on the KF8 part; on any reconstruction failure,
  fall back to treating the MOBI6 stream (if present) as the content, or refuse with a clear error if
  neither path produces usable text.
- Resulting HTML-ish stream flattened via the existing `HtmlProseExtractor`, same as EPUB/the MOBI6
  foundation layer.

## Risks / Open Questions

- **KF8 skeleton reconstruction is the one genuinely open-ended piece of this spec.** The plan should
  treat it as a time-boxed spike against a handful of real AZW3 fixtures (e.g. Calibre-converted
  files, which commonly retain a MOBI6 fallback stream, and pure-KF8 files, which may not) with an
  explicit decision point: if it isn't producing usable prose within that spike, ship the foundation
  layer only (MOBI6-compatible content, whether the file is `.mobi` or `.azw3`) rather than continuing
  indefinitely — this still delivers real value on its own.
- **No solid pure-.NET reference implementation exists for either format** — the FB2 side is
  low-risk because the format itself is simple XML, but the MOBI/KF8 side is inherently a from-scratch
  binary-format implementation with real chance of edge cases (encoding variants, malformed real-world
  files) not caught until tested against a broader sample than the fixtures used during development.
- **DRM refusal is a hard requirement, not a nice-to-have** — no code path should attempt to read
  content past an encryption check; this needs to be verified with an actual DRM-flagged test fixture,
  not just trusted by inspection.

## Testing

- `Fb2FixtureTests`: hand-authored minimal valid FB2 (bare and `.fb2.zip`-wrapped), metadata/chapter/
  cover extraction, nested-section flattening, inline bold/italic spans.
- `MobiFixtureTests`: a hand-constructed minimal valid PalmDB+MOBI6 fixture (byte-for-byte, same
  "generate via the real code path" precedent as `EpubFixture`/`CbzFixture`) for the foundation layer;
  a small set of real-world sample AZW3 files checked into the test fixtures folder for the KF8 path,
  since hand-constructing valid skeleton/fragment index bytes isn't practical the way the simpler
  formats are.
- Negative-path tests: DRM-flagged fixture → clean refusal with the expected error (not a crash or
  garbage text); Huffman-compressed fixture → same; pure-KF8-with-unreconstructable-content fixture →
  same.
- `BookFormat` migration test, mirroring `AddBooksBrowseStateMigrationTests`.
