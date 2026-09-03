using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books.Mobi;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// MOBI/AZW3/AZW implementation of <see cref="IBookTextSource"/> (docs/superpowers/specs/
	/// 2026-09-01-books-format-ingestion-fb2-mobi-design.md). No CE equivalent - new for Paperbunkr.
	///
	/// This is the "foundation layer" only: native PalmDB + PalmDOC/MOBI6 parsing, decompression, and
	/// EXTH metadata (<see cref="PalmDbReader"/>/<see cref="MobiHeaderReader"/>/
	/// <see cref="PalmDocDecompressor"/>). KF8 (AZW3's real content structure) skeleton reconstruction
	/// is a separate, deliberately time-boxed follow-up (design spec's Risks section) - this source
	/// reads whatever MOBI6-compatible content stream exists (real AZW3 files exported from Calibre
	/// commonly retain one as a compatibility fallback) and refuses cleanly, not silently, for a
	/// pure-KF8 file with no such stream.
	///
	/// <see cref="BookMetadata.SeriesName"/> is always null here - unlike EPUB's well-established
	/// <c>calibre:series</c> meta convention, there's no equally solid, independently-verified EXTH
	/// tag for series in the general MOBI ecosystem to build on. Documented gap, not a guessed tag
	/// number presented as verified.
	///
	/// <see cref="BookChapter.Html"/> (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-
	/// redesign-design.md) is the raw decompressed text stream per chapter chunk, passed through as-is.
	/// Real-world classic MOBI6 in-body images use <c>&lt;img recindex="00001"&gt;</c> (a PDB record
	/// index, not a <c>src</c> URL) - this source does not yet resolve those against the PDB's image
	/// records the way <see cref="MobiHeaderReader.CoverRecordIndex"/> resolves the single cover image,
	/// so in-body MOBI images won't render in the new WebView reader yet. Documented, known gap - not
	/// silently claimed as working without a real recindex-bearing fixture to verify against.
	/// </summary>
	public sealed class MobiBookSource : IBookTextSource
	{
		private static readonly Regex s_pagebreakMarker = new Regex(@"<mbp:pagebreak\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex s_headingSplit = new Regex(@"(?=<h[1-3][^>]*>)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex s_headingText = new Regex(@"<h[1-3][^>]*>(.*?)</h[1-3]>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex s_tagStrip = new Regex(@"<[^>]*>", RegexOptions.Compiled);

		public BookMetadata Metadata { get; }

		public IReadOnlyList<BookChapter> Chapters { get; }

		public MobiBookSource(string filePath)
		{
			byte[] fileBytes = File.ReadAllBytes(filePath);
			var pdb = new PalmDbReader(fileBytes);
			if (pdb.Records.Count == 0)
			{
				throw new InvalidDataException($"'{filePath}' contains no PalmDB records.");
			}

			var header = new MobiHeaderReader(pdb.Records[0]);

			if (header.EncryptionType != 0)
			{
				throw new NotSupportedException($"'{filePath}' is DRM-protected and cannot be read.");
			}

			if (header.CompressionType != 1 && header.CompressionType != 2)
			{
				throw new NotSupportedException($"'{filePath}' uses an unsupported text compression scheme (type {header.CompressionType} - Huffman/CDIC compression is not implemented).");
			}

			string html = DecodeTextRecords(pdb, header);
			if (string.IsNullOrWhiteSpace(html))
			{
				throw new InvalidDataException($"'{filePath}' produced no readable text content (possibly a pure-KF8 file with no MOBI6-compatible fallback stream - see MobiBookSource's own doc comment).");
			}

			Metadata = new BookMetadata
			{
				Title = string.IsNullOrWhiteSpace(header.Title) ? Path.GetFileNameWithoutExtension(filePath) : header.Title,
				Author = header.Author,
				SeriesName = null,
				CoverImageBytes = header.CoverRecordIndex is { } coverIndex && coverIndex >= 0 && coverIndex < pdb.Records.Count
					? pdb.Records[coverIndex]
					: null,
			};

			Chapters = SplitIntoChapters(html);
		}

		private static string DecodeTextRecords(PalmDbReader pdb, MobiHeaderReader header)
		{
			var builder = new StringBuilder();
			int lastTextRecord = Math.Min(header.TextRecordCount, pdb.Records.Count - 1);
			for (int i = 1; i <= lastTextRecord; i++)
			{
				byte[] raw = pdb.Records[i];
				byte[] decompressed = header.CompressionType == 2 ? PalmDocDecompressor.Decompress(raw) : raw;
				builder.Append(header.TextEncodingCodePage == 1252 ? Cp1252Decoder.GetString(decompressed) : Encoding.UTF8.GetString(decompressed));
			}

			return builder.ToString();
		}

		/// <summary>Splits on the format's own &lt;mbp:pagebreak/&gt; markers if present, else on heading tags - same fallback order the design spec describes.</summary>
		private static List<BookChapter> SplitIntoChapters(string html)
		{
			string[] pagebreakChunks = s_pagebreakMarker.Split(html);
			string[] chunks = pagebreakChunks.Length > 1 ? pagebreakChunks : s_headingSplit.Split(html);

			var chapters = new List<BookChapter>();
			int index = 0;
			foreach (string chunk in chunks)
			{
				var paragraphs = HtmlProseExtractor.ExtractParagraphs(chunk);
				if (paragraphs.Count == 0)
				{
					continue;
				}

				index++;
				string title = ExtractHeadingText(chunk) ?? $"Chapter {index}";
				chapters.Add(new BookChapter { Title = title, Paragraphs = paragraphs, Html = chunk });
			}

			// Neither marker was present anywhere (a short story or a file with no heading tags at
			// all) - one synthetic chapter over the whole stream, same "don't produce nothing when
			// there's real content" fallback EpubBookSource/Fb2BookSource both apply.
			if (chapters.Count == 0)
			{
				var paragraphs = HtmlProseExtractor.ExtractParagraphs(html);
				if (paragraphs.Count > 0)
				{
					chapters.Add(new BookChapter { Title = "Chapter 1", Paragraphs = paragraphs, Html = html });
				}
			}

			return chapters;
		}

		private static string? ExtractHeadingText(string chunk)
		{
			var match = s_headingText.Match(chunk);
			if (!match.Success)
			{
				return null;
			}

			string stripped = s_tagStrip.Replace(match.Groups[1].Value, string.Empty);
			string decoded = System.Net.WebUtility.HtmlDecode(stripped).Trim();
			return decoded.Length > 0 ? decoded : null;
		}

		public void Dispose()
		{
			// Everything is read into memory up front in the constructor - nothing outlives it to
			// release here, same as EpubBookSource/Fb2BookSource.
		}
	}
}
