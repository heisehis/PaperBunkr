using System;
using System.Collections.Generic;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// Shared contract for Novel format parsers (docs/superpowers/specs/
	/// 2026-08-09-novels-epub-pdf-support-design.md §4). New for Paperbunkr — no CE equivalent, see
	/// the design spec's CE-verification note. Both <c>EpubBookSource</c> and <c>PdfBookSource</c>
	/// implement this so the importer/reader don't branch on format beyond picking which one to
	/// construct.
	/// </summary>
	public interface IBookTextSource : IDisposable
	{
		BookMetadata Metadata { get; }

		IReadOnlyList<BookChapter> Chapters { get; }
	}

	/// <summary>Title/author/series/cover extracted from the source file, independent of chapter content.</summary>
	public sealed class BookMetadata
	{
		public string Title { get; init; } = string.Empty;

		public string? Author { get; init; }

		public string? SeriesName { get; init; }

		/// <summary>Raw cover image bytes (whatever format the source embeds it as), or null if none found.</summary>
		public byte[]? CoverImageBytes { get; init; }
	}

	public sealed class BookChapter
	{
		public string Title { get; set; } = string.Empty;

		public IReadOnlyList<BookParagraph> Paragraphs { get; set; } = Array.Empty<BookParagraph>();

		/// <summary>
		/// The chapter's real markup (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-
		/// redesign-design.md), populated by the reflowable sources (EPUB/FB2/MOBI) alongside
		/// <see cref="Paragraphs"/> during the reader's migration to a WebView-based renderer - null
		/// for <see cref="Paragraphs"/>-only sources (PDF, which has no real markup to normalize to).
		/// <see cref="Paragraphs"/> stays populated too during the migration window so every other
		/// consumer (chapter titles/counts for the TOC, annotation export, Home progress) keeps
		/// working unmodified; it's removed only once the reading pane itself no longer needs it.
		/// </summary>
		public string? Html { get; set; }

		/// <summary>
		/// Nearest ancestor part/section title from the source's own navigation hierarchy (docs/
		/// superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md, TOC grouping) - null for
		/// an ungrouped chapter. Only <see cref="EpubBookSource"/> populates this today (EPUB nav is
		/// the one format this project's ingestion already walks a real hierarchy for); FB2/MOBI/PDF
		/// leave it null, so their TOC stays a flat list exactly as before - a deliberate, disclosed
		/// scope limit, not an oversight.
		/// </summary>
		public string? PartTitle { get; set; }
	}

	public sealed class BookParagraph
	{
		public string Text { get; set; } = string.Empty;

		/// <summary>
		/// Minimal inline formatting only - bold/italic spans as (start, length) ranges over
		/// <see cref="Text"/>. Everything else a source format might carry (images, tables,
		/// multi-column layout, embedded fonts, footnote markers) is dropped - prose readability
		/// is the goal, not layout fidelity (design spec §4).
		/// </summary>
		public IReadOnlyList<BookTextSpan> Spans { get; set; } = Array.Empty<BookTextSpan>();
	}

	public readonly record struct BookTextSpan(int Start, int Length, bool Bold, bool Italic);
}
