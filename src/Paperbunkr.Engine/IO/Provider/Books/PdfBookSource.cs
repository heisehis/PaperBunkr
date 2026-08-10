using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using cYo.Projects.ComicRack.Engine.IO.Provider.Native;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// PDF implementation of <see cref="IBookTextSource"/> (docs/superpowers/specs/
	/// 2026-08-09-novels-epub-pdf-support-design.md §4/§9.1), via raw <see cref="PdfiumNative"/>
	/// text-extraction entry points - <c>PDFiumSharpV2</c> (the wrapper already used for comic-page
	/// PDF rendering) exposes no text APIs. Chapters come from the PDF's outline/bookmark tree if
	/// present; a file with none becomes a single synthetic chapter spanning the whole document.
	/// Paragraph breaks are a blank-line heuristic over the raw extracted text - PDF carries no
	/// semantic reading order, so this is an accepted quality limitation (design spec §9.1), not a
	/// bug to chase perfection on.
	/// </summary>
	public sealed class PdfBookSource : IBookTextSource
	{
		private static readonly object s_initLock = new object();
		private static bool s_initialized;

		private static readonly Regex s_paragraphBreak = new Regex(@"\n{2,}", RegexOptions.Compiled);

		public BookMetadata Metadata { get; }

		public IReadOnlyList<BookChapter> Chapters { get; }

		public PdfBookSource(string filePath)
		{
			EnsureInitialized();

			IntPtr document = PdfiumNative.FPDF_LoadDocument(filePath, null!);
			if (document == IntPtr.Zero)
			{
				throw new InvalidOperationException($"PDFium could not open '{filePath}'.");
			}

			try
			{
				int pageCount = PdfiumNative.FPDF_GetPageCount(document);

				Metadata = new BookMetadata
				{
					Title = FirstNonEmpty(GetMetaText(document, "Title"), Path.GetFileNameWithoutExtension(filePath)),
					Author = GetMetaText(document, "Author"),
					SeriesName = null, // No PDF equivalent of EPUB's calibre:series convention.
					CoverImageBytes = null, // Cover comes from rendering page 0 via the existing comic-PDF pipeline, not this text-extraction path.
				};

				var bookmarks = ReadBookmarkChapters(document, pageCount);
				Chapters = bookmarks.Count > 0
					? BuildChaptersFromBookmarks(document, bookmarks, pageCount)
					: new List<BookChapter> { BuildSingleChapter(document, 0, pageCount, "No chapters found") };
			}
			finally
			{
				PdfiumNative.FPDF_CloseDocument(document);
			}
		}

		private static void EnsureInitialized()
		{
			// FPDF_InitLibrary is process-wide native state, not per-consumer - PDFiumSharpV2 (used
			// elsewhere for comic-page PDF rendering) manages its own init internally against the
			// same loaded pdfium.dll. Calling it again here is documented-safe/idempotent; never
			// calling FPDF_DestroyLibrary avoids tearing down state the comic PDF pipeline might
			// still depend on - same reasoning PdfiumNative's own doc comment gives for staying
			// unwired from that pipeline.
			lock (s_initLock)
			{
				if (!s_initialized)
				{
					PdfiumNative.FPDF_InitLibrary();
					s_initialized = true;
				}
			}
		}

		private readonly record struct BookmarkChapter(string Title, int StartPage);

		private static List<BookmarkChapter> ReadBookmarkChapters(IntPtr document, int pageCount)
		{
			var result = new List<BookmarkChapter>();
			IntPtr bookmark = PdfiumNative.FPDFBookmark_GetFirstChild(document, IntPtr.Zero);
			while (bookmark != IntPtr.Zero)
			{
				string title = GetBookmarkTitle(bookmark);
				int startPage = 0;
				IntPtr dest = PdfiumNative.FPDFBookmark_GetDest(document, bookmark);
				if (dest != IntPtr.Zero)
				{
					int page = PdfiumNative.FPDFDest_GetDestPageIndex(document, dest);
					if (page >= 0 && page < pageCount)
					{
						startPage = page;
					}
				}

				result.Add(new BookmarkChapter(string.IsNullOrWhiteSpace(title) ? $"Chapter {result.Count + 1}" : title, startPage));
				bookmark = PdfiumNative.FPDFBookmark_GetNextSibling(document, bookmark);
			}

			return result;
		}

		private static List<BookChapter> BuildChaptersFromBookmarks(IntPtr document, List<BookmarkChapter> bookmarks, int pageCount)
		{
			var chapters = new List<BookChapter>();
			for (int i = 0; i < bookmarks.Count; i++)
			{
				int startPage = bookmarks[i].StartPage;
				int endPageExclusive = i + 1 < bookmarks.Count ? Math.Max(startPage, bookmarks[i + 1].StartPage) : pageCount;
				chapters.Add(BuildSingleChapter(document, startPage, endPageExclusive, bookmarks[i].Title));
			}

			return chapters;
		}

		private static BookChapter BuildSingleChapter(IntPtr document, int startPage, int endPageExclusive, string title)
		{
			var text = new StringBuilder();
			for (int pageIndex = startPage; pageIndex < endPageExclusive; pageIndex++)
			{
				text.Append(ExtractPageText(document, pageIndex));
				text.Append("\n\n");
			}

			var paragraphs = new List<BookParagraph>();
			foreach (string block in s_paragraphBreak.Split(text.ToString()))
			{
				// Single newlines within a block are PDF line wraps, not paragraph breaks.
				string collapsed = Regex.Replace(block, @"\s*\n\s*", " ").Trim();
				if (collapsed.Length > 0)
				{
					paragraphs.Add(new BookParagraph { Text = collapsed });
				}
			}

			return new BookChapter { Title = title, Paragraphs = paragraphs };
		}

		private static string ExtractPageText(IntPtr document, int pageIndex)
		{
			IntPtr page = PdfiumNative.FPDF_LoadPage(document, pageIndex);
			if (page == IntPtr.Zero)
			{
				return string.Empty;
			}

			try
			{
				IntPtr textPage = PdfiumNative.FPDFText_LoadPage(page);
				if (textPage == IntPtr.Zero)
				{
					return string.Empty;
				}

				try
				{
					int charCount = PdfiumNative.FPDFText_CountChars(textPage);
					if (charCount <= 0)
					{
						return string.Empty;
					}

					// FPDFText_GetText writes charCount characters plus a trailing NUL.
					var buffer = new byte[(charCount + 1) * 2];
					int written = PdfiumNative.FPDFText_GetText(textPage, 0, charCount, buffer);
					int charsWritten = Math.Max(0, written - 1);
					return Encoding.Unicode.GetString(buffer, 0, charsWritten * 2);
				}
				finally
				{
					PdfiumNative.FPDFText_ClosePage(textPage);
				}
			}
			finally
			{
				PdfiumNative.FPDF_ClosePage(page);
			}
		}

		private static string GetBookmarkTitle(IntPtr bookmark)
		{
			uint byteLength = PdfiumNative.FPDFBookmark_GetTitle(bookmark, null, 0);
			if (byteLength <= 2)
			{
				return string.Empty;
			}

			var buffer = new byte[byteLength];
			PdfiumNative.FPDFBookmark_GetTitle(bookmark, buffer, byteLength);
			return DecodeUtf16WithTerminator(buffer);
		}

		private static string? GetMetaText(IntPtr document, string tag)
		{
			uint byteLength = PdfiumNative.FPDF_GetMetaText(document, tag, null, 0);
			if (byteLength <= 2)
			{
				return null;
			}

			var buffer = new byte[byteLength];
			PdfiumNative.FPDF_GetMetaText(document, tag, buffer, byteLength);
			string value = DecodeUtf16WithTerminator(buffer);
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		private static string DecodeUtf16WithTerminator(byte[] buffer)
		{
			// PDFium's string-returning size-probe APIs (FPDF_GetMetaText/FPDFBookmark_GetTitle)
			// report the buffer length in bytes, including a trailing UTF-16 NUL - trim it off.
			int usableBytes = buffer.Length >= 2 ? buffer.Length - 2 : 0;
			return usableBytes > 0 ? Encoding.Unicode.GetString(buffer, 0, usableBytes) : string.Empty;
		}

		private static string FirstNonEmpty(string? primary, string fallback) =>
			string.IsNullOrWhiteSpace(primary) ? fallback : primary!;

		public void Dispose()
		{
			// Document/page/text-page handles are all opened and closed within the constructor's
			// own scope above - nothing outlives it to release here.
		}
	}
}
