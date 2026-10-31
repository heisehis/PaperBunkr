#nullable enable annotations
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

		/// <summary>
		/// The printed edition's page markers when the file carries them (an EPUB page-list or NCX pageList), in reading order; empty otherwise,
		/// which is most books (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md §6.2).
		/// </summary>
		IReadOnlyList<BookPrintPage> PrintPages => Array.Empty<BookPrintPage>();
	}

	/// <summary>One printed page marker: its label as printed ("214", "xii"), the chapter file it is in and the element id it points at (null = the file's start).</summary>
	public sealed record BookPrintPage(string Label, int ChapterIndex, string? Fragment);

	/// <summary>Title/author/series/cover extracted from the source file, independent of chapter content.</summary>
	public sealed class BookMetadata
	{
		public string Title { get; init; } = string.Empty;

		public string? Author { get; init; }

		public string? SeriesName { get; init; }

		// Descriptive metadata (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §4). Every value is
		// null / empty when the file does not declare it - never guessed.

		public string? Subtitle { get; init; }

		public string? Publisher { get; init; }

		/// <summary>Language code exactly as the file declares it (e.g. "en", "pt-BR").</summary>
		public string? LanguageISO { get; init; }

		/// <summary>Bare ISBN digits (10 or 13), hyphens/spaces/"urn:isbn:" stripped.</summary>
		public string? ISBN { get; init; }

		/// <summary>Position within <see cref="SeriesName"/>, from calibre:series_index / EPUB3 group-position / FB2 sequence number.</summary>
		public float? SeriesIndex { get; init; }

		public string? Description { get; init; }

		/// <summary>The file's own star rating on a 0-5 scale (calibre:rating is 0-10 and is halved on read).</summary>
		public float? Rating { get; init; }

		/// <summary>Subjects / genres the file declares, trimmed and de-duplicated.</summary>
		public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

		/// <summary>Keywords the file declares (PDF Keywords), distinct from <see cref="Genres"/>.</summary>
		public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

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

		/// <summary>
		/// The source's own table-of-contents entries that point into this chapter's file, in nav order. A book that packs many chapters into few files (a real EPUB can have 26
		/// chapters in 6 spine files) lists each chapter here with the element id to jump to; empty when the file has no entries of its own (the TOC then shows the file as one row).
		/// </summary>
		public IReadOnlyList<BookSection> Sections { get; set; } = Array.Empty<BookSection>();
	}

	/// <summary>One table-of-contents entry inside a <see cref="BookChapter"/>'s file: its title, the element id it points at (null = the file's start) and its part heading.</summary>
	public sealed class BookSection
	{
		public string Title { get; init; } = string.Empty;

		public string? Fragment { get; init; }

		public string? PartTitle { get; init; }
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
