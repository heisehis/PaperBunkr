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
