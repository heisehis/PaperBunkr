using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// FictionBook 2 implementation of <see cref="IBookTextSource"/> (docs/superpowers/specs/
	/// 2026-09-01-books-format-ingestion-fb2-mobi-design.md). No CE equivalent - new for Paperbunkr,
	/// see the design spec's CE-verification note.
	///
	/// Top-level traversal is <see cref="XmlReader"/>-based (reads directly off the file's own
	/// stream, so it honors whatever encoding the file's XML declaration states rather than assuming
	/// UTF-8 - real-world FB2 files commonly declare <c>windows-1251</c> or similar). Each interesting
	/// subtree (<c>&lt;description&gt;</c>, a top-level <c>&lt;body&gt;</c>, a <c>&lt;binary&gt;</c>)
	/// is then materialized into an <see cref="XElement"/> via <see cref="XNode.ReadFrom"/> so the
	/// nested-section flattening below can use normal LINQ-to-XML traversal instead of a hand-rolled
	/// state machine - still driven by the same underlying <see cref="XmlReader"/>, not a second pass
	/// or a whole-document <c>XDocument.Load</c>.
	///
	/// Element/attribute matching is by local name only, ignoring namespace - real-world FB2 files are
	/// inconsistent about declaring the formal <c>http://www.gribuser.ru/xml/fictionbook/2.0</c>
	/// namespace (some omit it, some use a different prefix), and nothing in this reader's own output
	/// depends on distinguishing same-named elements from different namespaces.
	/// </summary>
	public sealed class Fb2BookSource : IBookTextSource
	{
		public BookMetadata Metadata { get; }

		public IReadOnlyList<BookChapter> Chapters { get; }

		public Fb2BookSource(string filePath)
		{
			using Stream fileStream = OpenContentStream(filePath);
			using XmlReader reader = XmlReader.Create(fileStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

			XElement? descriptionElement = null;
			XElement? bodyElement = null;
			var binaries = new Dictionary<string, (string Base64, string ContentType)>(StringComparer.OrdinalIgnoreCase);

			reader.MoveToContent();
			if (!string.Equals(reader.LocalName, "FictionBook", StringComparison.Ordinal))
			{
				throw new InvalidDataException($"'{filePath}' is not a FictionBook (FB2) file - expected a root <FictionBook> element, found <{reader.LocalName}>.");
			}

			reader.ReadStartElement();
			while (reader.NodeType != XmlNodeType.EndElement && !reader.EOF)
			{
				if (reader.NodeType != XmlNodeType.Element)
				{
					reader.Read();
					continue;
				}

				switch (reader.LocalName)
				{
					case "description" when descriptionElement is null:
						descriptionElement = (XElement)XNode.ReadFrom(reader);
						break;

					// The main text is the first unnamed <body> - FB2 commonly carries additional
					// named bodies (footnotes, afterword) as separate <body name="notes"> siblings,
					// which this reader intentionally doesn't surface (prose readability over layout/
					// footnote fidelity, same bar IBookTextSource's own doc comment states).
					case "body" when bodyElement is null && reader.GetAttribute("name") is null:
						bodyElement = (XElement)XNode.ReadFrom(reader);
						break;

					case "binary":
						string? id = reader.GetAttribute("id");
						string? contentType = reader.GetAttribute("content-type");
						var binaryElement = (XElement)XNode.ReadFrom(reader);
						if (!string.IsNullOrEmpty(id))
						{
							binaries[id] = (binaryElement.Value, string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);
						}

						break;

					default:
						reader.Skip();
						break;
				}
			}

			// A <body name="notes">-only file (no main body) has nothing to read - fall back to the
			// first <body> encountered at all rather than leaving Chapters empty, same "don't produce
			// nothing when something's there" instinct HtmlProseExtractor's body-fallback uses.
			if (bodyElement is null && fileStream.CanSeek)
			{
				bodyElement = RescanForAnyBody(filePath);
			}

			Metadata = BuildMetadata(descriptionElement, binaries, filePath);
			Chapters = bodyElement is null
				? Array.Empty<BookChapter>()
				: BuildChapters(bodyElement, binaries);
		}

		/// <summary>Second pass, only reached when the first pass found no unnamed &lt;body&gt; at all - takes whichever &lt;body&gt; exists (even a named one) rather than leaving the book unreadable.</summary>
		private static XElement? RescanForAnyBody(string filePath)
		{
			using Stream fileStream = OpenContentStream(filePath);
			using XmlReader reader = XmlReader.Create(fileStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
			reader.MoveToContent();
			reader.ReadStartElement();
			while (reader.NodeType != XmlNodeType.EndElement && !reader.EOF)
			{
				if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "body")
				{
					return (XElement)XNode.ReadFrom(reader);
				}

				if (reader.NodeType == XmlNodeType.Element)
				{
					reader.Skip();
				}
				else
				{
					reader.Read();
				}
			}

			return null;
		}

		/// <summary>
		/// Detects a zip container via magic bytes (<c>PK</c>) and transparently extracts the single
		/// <c>.fb2</c> entry if so (the <c>.fb2.zip</c> distribution convention) - otherwise returns
		/// the raw file stream. Format-specific unwrapping, not general archive ingestion (design
		/// spec's own scope note).
		/// </summary>
		private static Stream OpenContentStream(string filePath)
		{
			var fileBytes = new byte[2];
			using (var probe = File.OpenRead(filePath))
			{
				int read = probe.Read(fileBytes, 0, 2);
				if (read < 2)
				{
					throw new InvalidDataException($"'{filePath}' is too short to be a valid FB2 file.");
				}
			}

			bool isZip = fileBytes[0] == 'P' && fileBytes[1] == 'K';
			if (!isZip)
			{
				return File.OpenRead(filePath);
			}

			using var archive = ZipFile.OpenRead(filePath);
			var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".fb2", StringComparison.OrdinalIgnoreCase))
				?? archive.Entries.FirstOrDefault(e => e.Length > 0);
			if (entry is null)
			{
				throw new InvalidDataException($"'{filePath}' is a zip file but contains no .fb2 entry.");
			}

			var buffer = new MemoryStream();
			using (var entryStream = entry.Open())
			{
				entryStream.CopyTo(buffer);
			}

			buffer.Position = 0;
			return buffer;
		}

		private static BookMetadata BuildMetadata(XElement? description, Dictionary<string, (string Base64, string ContentType)> binaries, string filePath)
		{
			XElement? titleInfo = description?.Elements().FirstOrDefault(e => e.Name.LocalName == "title-info");

			string title = titleInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "book-title")?.Value.Trim() is { Length: > 0 } t
				? t
				: Path.GetFileNameWithoutExtension(filePath);

			XElement? authorElement = titleInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "author");
			string? author = authorElement is null ? null : BuildAuthorName(authorElement);

			XElement? sequenceElement = titleInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "sequence");
			string? seriesName = sequenceElement?.Attributes().FirstOrDefault(a => a.Name.LocalName == "name")?.Value;
			seriesName = string.IsNullOrWhiteSpace(seriesName) ? null : seriesName;

			byte[]? coverBytes = null;
			XElement? coverpage = titleInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "coverpage");
			string? coverRef = coverpage?.Elements().FirstOrDefault(e => e.Name.LocalName == "image")
				?.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
			if (!string.IsNullOrEmpty(coverRef))
			{
				string coverId = coverRef.TrimStart('#');
				if (binaries.TryGetValue(coverId, out var coverBinary))
				{
					try
					{
						coverBytes = Convert.FromBase64String(coverBinary.Base64.Trim());
					}
					catch (FormatException)
					{
						// Malformed base64 in a real-world file - treat as "no cover" rather than
						// failing the whole book open over a decorative asset.
						coverBytes = null;
					}
				}
			}

			return new BookMetadata
			{
				Title = title,
				Author = author,
				SeriesName = seriesName,
				CoverImageBytes = coverBytes,
			};
		}

		private static string? BuildAuthorName(XElement authorElement)
		{
			string? first = authorElement.Elements().FirstOrDefault(e => e.Name.LocalName == "first-name")?.Value.Trim();
			string? middle = authorElement.Elements().FirstOrDefault(e => e.Name.LocalName == "middle-name")?.Value.Trim();
			string? last = authorElement.Elements().FirstOrDefault(e => e.Name.LocalName == "last-name")?.Value.Trim();

			string full = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
			if (full.Length > 0)
			{
				return full;
			}

			string? nickname = authorElement.Elements().FirstOrDefault(e => e.Name.LocalName == "nickname")?.Value.Trim();
			return string.IsNullOrWhiteSpace(nickname) ? null : nickname;
		}

		private static List<BookChapter> BuildChapters(XElement body, Dictionary<string, (string Base64, string ContentType)> binaries)
		{
			var chapters = new List<BookChapter>();
			var topLevelSections = body.Elements().Where(e => e.Name.LocalName == "section").ToList();

			// A body with no <section> wrapper at all (a handful of real-world FB2 files put <p>s
			// directly under <body>) becomes one synthetic chapter, same "don't produce nothing"
			// fallback the constructor already applies at the body level.
			if (topLevelSections.Count == 0)
			{
				var paragraphs = new List<BookParagraph>();
				CollectParagraphs(body, paragraphs, includeOwnTitle: false);
				return paragraphs.Count > 0
					? new List<BookChapter> { new BookChapter { Title = "Chapter 1", Paragraphs = paragraphs, Html = BuildHtml(body, binaries, includeOwnTitle: false) } }
					: chapters;
			}

			int index = 0;
			foreach (var section in topLevelSections)
			{
				index++;
				string title = ExtractSectionTitle(section) ?? $"Chapter {index}";

				var paragraphs = new List<BookParagraph>();
				CollectParagraphs(section, paragraphs, includeOwnTitle: false);

				chapters.Add(new BookChapter { Title = title, Paragraphs = paragraphs, Html = BuildHtml(section, binaries, includeOwnTitle: false) });
			}

			return chapters;
		}

		/// <summary>
		/// Real-markup counterpart to <see cref="CollectParagraphs"/> (docs/superpowers/specs/
		/// 2026-09-02-books-reflow-reader-webview-redesign-design.md) - same traversal shape (direct
		/// &lt;p&gt; children emitted, nested &lt;section&gt;s flattened in with their own &lt;title&gt;
		/// kept), but building an HTML string instead of a <see cref="BookParagraph"/> list, and
		/// additionally resolving standalone &lt;image&gt; elements (a sibling of &lt;p&gt; in real FB2
		/// files, not just the cover) against <paramref name="binaries"/> as inline data URIs - the one
		/// piece of real content the old paragraph-only model had no way to carry at all.
		/// </summary>
		private static string BuildHtml(XElement section, Dictionary<string, (string Base64, string ContentType)> binaries, bool includeOwnTitle)
		{
			var html = new StringBuilder();

			if (includeOwnTitle && ExtractSectionTitle(section) is { } title)
			{
				html.Append("<h2>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</h2>");
			}

			foreach (var child in section.Elements())
			{
				switch (child.Name.LocalName)
				{
					case "p":
						html.Append("<p>").Append(BuildParagraphHtml(child)).Append("</p>");
						break;

					case "image":
						AppendImageTag(html, child, binaries);
						break;

					case "section":
						html.Append(BuildHtml(child, binaries, includeOwnTitle: true));
						break;
				}
			}

			return html.ToString();
		}

		private static void AppendImageTag(StringBuilder html, XElement imageElement, Dictionary<string, (string Base64, string ContentType)> binaries)
		{
			string? href = imageElement.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
			if (string.IsNullOrEmpty(href))
			{
				return;
			}

			string id = href.TrimStart('#');
			if (!binaries.TryGetValue(id, out var binary))
			{
				return;
			}

			html.Append("<img src=\"data:").Append(binary.ContentType).Append(";base64,").Append(binary.Base64.Trim()).Append("\" />");
		}

		/// <summary>A section's &lt;title&gt; is itself a sequence of &lt;p&gt; lines - joined with a space for a single-line chapter title.</summary>
		private static string? ExtractSectionTitle(XElement section)
		{
			XElement? titleElement = section.Elements().FirstOrDefault(e => e.Name.LocalName == "title");
			if (titleElement is null)
			{
				return null;
			}

			string joined = string.Join(" ", titleElement.Elements()
				.Where(e => e.Name.LocalName == "p")
				.Select(p => p.Value.Trim())
				.Where(s => s.Length > 0));

			return joined.Length > 0 ? joined : null;
		}

		/// <summary>
		/// Walks a section's direct &lt;p&gt; paragraphs (with inline emphasis/strong spans) and
		/// recurses into nested &lt;section&gt; children, flattening them into the same paragraph list
		/// rather than producing a separate <see cref="BookChapter"/> per nested section (design
		/// spec's explicit anti-chapter-explosion rule). A nested section's own &lt;title&gt; is kept
		/// as a plain paragraph immediately before its content, rather than silently dropped, so the
		/// sub-heading isn't lost entirely.
		/// </summary>
		private static void CollectParagraphs(XElement section, List<BookParagraph> into, bool includeOwnTitle)
		{
			if (includeOwnTitle && ExtractSectionTitle(section) is { } title)
			{
				into.Add(new BookParagraph { Text = title });
			}

			foreach (var child in section.Elements())
			{
				switch (child.Name.LocalName)
				{
					case "p":
						var paragraph = ExtractParagraph(child);
						if (paragraph.Text.Length > 0)
						{
							into.Add(paragraph);
						}

						break;

					case "section":
						CollectParagraphs(child, into, includeOwnTitle: true);
						break;
				}
			}
		}

		/// <summary>Flattens a &lt;p&gt;'s text content, recording &lt;emphasis&gt;→Italic/&lt;strong&gt;→Bold spans over the flattened offsets (design spec's inline-formatting mapping).</summary>
		private static BookParagraph ExtractParagraph(XElement p)
		{
			var text = new StringBuilder();
			var spans = new List<BookTextSpan>();

			void Walk(XElement element)
			{
				bool isItalic = element.Name.LocalName == "emphasis";
				bool isBold = element.Name.LocalName == "strong";
				int start = text.Length;

				foreach (var node in element.Nodes())
				{
					if (node is XText textNode)
					{
						text.Append(CollapseWhitespace(textNode.Value));
					}
					else if (node is XElement nested)
					{
						Walk(nested);
					}
				}

				if (isItalic && text.Length > start)
				{
					spans.Add(new BookTextSpan(start, text.Length - start, false, true));
				}

				if (isBold && text.Length > start)
				{
					spans.Add(new BookTextSpan(start, text.Length - start, true, false));
				}
			}

			Walk(p);

			return new BookParagraph { Text = text.ToString().Trim(), Spans = spans };
		}

		/// <summary>HTML counterpart to <see cref="ExtractParagraph"/> - same recursive walk, emitting HTML-encoded text with real &lt;em&gt;/&lt;strong&gt; tags instead of computing <see cref="BookTextSpan"/> offsets.</summary>
		private static string BuildParagraphHtml(XElement p)
		{
			var html = new StringBuilder();

			void Walk(XElement element)
			{
				bool isItalic = element.Name.LocalName == "emphasis";
				bool isBold = element.Name.LocalName == "strong";

				if (isItalic) html.Append("<em>");
				if (isBold) html.Append("<strong>");

				foreach (var node in element.Nodes())
				{
					if (node is XText textNode)
					{
						html.Append(System.Net.WebUtility.HtmlEncode(CollapseWhitespace(textNode.Value)));
					}
					else if (node is XElement nested)
					{
						Walk(nested);
					}
				}

				if (isBold) html.Append("</strong>");
				if (isItalic) html.Append("</em>");
			}

			Walk(p);
			return html.ToString();
		}

		private static string CollapseWhitespace(string value) =>
			System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ");

		public void Dispose()
		{
			// Every stream/archive handle opened above is scoped to its own constructor-local using
			// block - nothing outlives construction to release here, same as EpubBookSource.
		}
	}
}
