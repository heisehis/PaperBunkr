#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VersOne.Epub;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// EPUB implementation of <see cref="IBookTextSource"/> (docs/superpowers/specs/
	/// 2026-08-09-novels-epub-pdf-support-design.md §4), via the <c>VersOne.Epub</c> NuGet package
	/// (MIT, actively maintained). Chapters come from the book's own spine
	/// (<see cref="EpubBook.ReadingOrder"/>) - the guaranteed reading-order sequence - with titles
	/// backfilled from <see cref="EpubBook.Navigation"/> where a matching entry exists.
	/// </summary>
	public sealed class EpubBookSource : IBookTextSource
	{
		private readonly EpubBook _book;

		public BookMetadata Metadata { get; }

		public IReadOnlyList<BookChapter> Chapters { get; }

		public EpubBookSource(string filePath)
		{
			_book = EpubReader.ReadBook(filePath);

			Metadata = BuildMetadata(_book, filePath);

			var navigationTitlesByPath = FlattenNavigationTitles(_book.Navigation);
			var navigationPartsByPath = FlattenNavigationParts(_book.Navigation);
			var imagesByPath = _book.Content.Images.Local
				.GroupBy(img => img.FilePath, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var chapters = new List<BookChapter>();
			int index = 0;
			foreach (var content in _book.ReadingOrder)
			{
				index++;
				string title = navigationTitlesByPath.TryGetValue(content.FilePath, out string? navTitle) && !string.IsNullOrWhiteSpace(navTitle)
					? navTitle
					: $"Chapter {index}";

				chapters.Add(new BookChapter
				{
					Title = title,
					Paragraphs = HtmlProseExtractor.ExtractParagraphs(content.Content),
					Html = InlineSvgImages(InlineImages(content.Content, content.FilePath, imagesByPath), content.FilePath, imagesByPath),
					PartTitle = navigationPartsByPath.TryGetValue(content.FilePath, out string? partTitle) ? partTitle : null,
				});
			}

			var spinePaths = _book.ReadingOrder.Select(c => c.FilePath).ToList();
			AttachSections(chapters, spinePaths, _book.Navigation);
			Chapters = chapters;
			PrintPages = ReadPrintPages(filePath, spinePaths);
		}

		public IReadOnlyList<BookPrintPage> PrintPages { get; }

		private static readonly Regex s_pageListNav = new Regex(
			"""<nav\b[^>]*epub:type\s*=\s*["'][^"']*\bpage-list\b[^"']*["'][^>]*>(.*?)</nav>""",
			RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

		private static readonly Regex s_navAnchor = new Regex(
			"""<a\b[^>]*\bhref\s*=\s*(["'])(.*?)\1[^>]*>(.*?)</a>""",
			RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

		private static readonly Regex s_ncxPageTarget = new Regex(
			"""<pageTarget\b.*?<text>(.*?)</text>.*?<content\b[^>]*\bsrc\s*=\s*(["'])(.*?)\2""",
			RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

		private static readonly Regex s_tags = new Regex("<[^>]+>", RegexOptions.Compiled);

		/// <summary>
		/// Reads the printed edition's page list straight from the archive: the EPUB 3 nav document's <c>page-list</c> first, else the EPUB 2
		/// NCX <c>pageList</c>. Read as markup rather than through VersOne's navigation (which only exposes the table of contents). Never throws:
		/// a file without a list, or one that cannot be read, simply has no print pages.
		/// </summary>
		private static IReadOnlyList<BookPrintPage> ReadPrintPages(string filePath, List<string> spinePaths)
		{
			try
			{
				using var zip = System.IO.Compression.ZipFile.OpenRead(filePath);
				var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < spinePaths.Count; i++)
				{
					indexByPath.TryAdd(spinePaths[i].TrimStart('/'), i);
				}

				string Read(System.IO.Compression.ZipArchiveEntry entry)
				{
					using var reader = new StreamReader(entry.Open());
					return reader.ReadToEnd();
				}

				List<BookPrintPage> Resolve(string documentPath, IEnumerable<(string Label, string Href)> targets)
				{
					var pages = new List<BookPrintPage>();
					foreach (var (label, href) in targets)
					{
						string decoded = System.Net.WebUtility.HtmlDecode(href);
						int hash = decoded.IndexOf('#');
						string path = hash >= 0 ? decoded[..hash] : decoded;
						string? fragment = hash >= 0 ? Uri.UnescapeDataString(decoded[(hash + 1)..]) : null;
						string resolved = path.Length == 0 ? documentPath : ResolveEpubPath(documentPath, Uri.UnescapeDataString(path));
						string text = System.Net.WebUtility.HtmlDecode(s_tags.Replace(label, string.Empty)).Trim();
						if (text.Length > 0 && indexByPath.TryGetValue(resolved, out int chapter))
						{
							pages.Add(new BookPrintPage(text, chapter, string.IsNullOrEmpty(fragment) ? null : fragment));
						}
					}

					return pages;
				}

				var documents = zip.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
					|| e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase));
				foreach (var entry in documents)
				{
					string html = Read(entry);
					if (html.IndexOf("page-list", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}

					var nav = s_pageListNav.Match(html);
					if (nav.Success)
					{
						var pages = Resolve(entry.FullName, s_navAnchor.Matches(nav.Groups[1].Value).Select(m => (m.Groups[3].Value, m.Groups[2].Value)));
						if (pages.Count > 0)
						{
							return pages;
						}
					}
				}

				foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase)))
				{
					var pages = Resolve(entry.FullName, s_ncxPageTarget.Matches(Read(entry)).Select(m => (m.Groups[1].Value, m.Groups[3].Value)));
					if (pages.Count > 0)
					{
						return pages;
					}
				}
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
			{
			}

			return Array.Empty<BookPrintPage>();
		}

		/// <summary>Hands each spine file the nav entries that point into it (several per file when a book packs chapters together), ordered by where their target sits in the file.</summary>
		private static void AttachSections(List<BookChapter> chapters, List<string> spinePaths, IReadOnlyList<EpubNavigationItem>? navigation)
		{
			if (navigation is null || chapters.Count == 0)
			{
				return;
			}

			var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < spinePaths.Count; i++)
			{
				indexByPath.TryAdd(spinePaths[i], i);
			}

			var found = new List<(int Chapter, int Order, BookSection Section)>();
			int order = 0;

			void Walk(IReadOnlyList<EpubNavigationItem> level, string? currentPart)
			{
				foreach (var item in level)
				{
					bool hasChildren = item.NestedItems is { Count: > 0 };
					string? path = item.Link?.ContentFilePath;
					if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(item.Title) && indexByPath.TryGetValue(path, out int chapter))
					{
						string? anchor = string.IsNullOrWhiteSpace(item.Link?.Anchor) ? null : Uri.UnescapeDataString(item.Link!.Anchor!.TrimStart('#'));
						found.Add((chapter, order++, new BookSection
						{
							Title = item.Title.Trim(),
							Fragment = anchor,
							PartTitle = hasChildren ? item.Title : currentPart,
						}));
					}

					if (hasChildren)
					{
						Walk(item.NestedItems, item.Title);
					}
				}
			}

			Walk(navigation, null);

			foreach (var group in found.GroupBy(f => f.Chapter))
			{
				string html = chapters[group.Key].Html ?? string.Empty;
				chapters[group.Key].Sections = group
					.OrderBy(f => f.Section.Fragment is null ? 0 : Math.Max(0, OffsetOf(html, f.Section.Fragment)))
					.ThenBy(f => f.Order)
					.Select(f => f.Section)
					.ToList();
			}
		}

		/// <summary>Where the element with this id (or legacy anchor name) starts in the markup; -1 when it is not there.</summary>
		public static int OffsetOf(string html, string id)
		{
			int best = -1;
			foreach (string pattern in new[] { $"id=\"{id}\"", $"id='{id}'", $"name=\"{id}\"", $"name='{id}'" })
			{
				int at = html.IndexOf(pattern, StringComparison.Ordinal);
				if (at >= 0 && (best < 0 || at < best))
				{
					best = at;
				}
			}

			return best;
		}

		private static readonly Regex s_imgSrcAttribute = new Regex(
			"""(<img\b[^>]*\bsrc\s*=\s*)(["'])(.*?)\2""",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>
		/// A real EPUB chapter's <c>&lt;img src="..."&gt;</c> references are paths *within the EPUB's
		/// own zip archive*, relative to the chapter document's own location - meaningless once handed
		/// to <c>NativeWebView.NavigateToString</c> (docs/superpowers/specs/2026-09-02-books-reflow-
		/// reader-webview-redesign-design.md), which has no base URL or archive filesystem to resolve
		/// them against. Rewrites each one to a self-contained <c>data:</c> URI instead - same approach
		/// <c>Fb2BookSource</c> already uses for its own inline images, for the same reason.
		/// </summary>
		private static string InlineImages(string html, string chapterFilePath, Dictionary<string, EpubLocalByteContentFile> imagesByPath)
		{
			return s_imgSrcAttribute.Replace(html, match =>
			{
				string src = match.Groups[3].Value;
				if (string.IsNullOrWhiteSpace(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
					|| src.Contains("://", StringComparison.Ordinal))
				{
					return match.Value;
				}

				string resolvedPath = ResolveEpubPath(chapterFilePath, System.Net.WebUtility.HtmlDecode(src));
				if (!imagesByPath.TryGetValue(resolvedPath, out var image))
				{
					return match.Value;
				}

				string dataUri = $"data:{image.ContentMimeType};base64,{Convert.ToBase64String(image.Content)}";
				return $"{match.Groups[1].Value}{match.Groups[2].Value}{dataUri}{match.Groups[2].Value}";
			});
		}

		private static readonly Regex s_svgImageHref = new Regex(
			"""(<image\b[^>]*?\b(?:xlink:href|href)\s*=\s*)(["'])(.*?)\2""",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>
		/// Calibre-style EPUB covers (real-world sample: Dune, Ender's Game, Red Queen from the
		/// user's own library, all confirmed 2026-09-02) wrap the cover image in an SVG element
		/// instead of a plain <c>&lt;img&gt;</c> - <c>&lt;svg&gt;&lt;image xlink:href="cover.jpeg"/&gt;
		/// &lt;/svg&gt;</c> - specifically to lock its aspect ratio via the SVG viewBox. This is
		/// nearly always the very first chapter a reader opens, so <see cref="InlineImages"/> alone
		/// (which only matches <c>&lt;img src&gt;</c>) left the very first thing a user sees broken
		/// even though later in-chapter <c>&lt;img&gt;</c> illustrations already inlined correctly.
		/// SVG2 also permits a bare <c>href</c> without the <c>xlink:</c> prefix, so both are matched.
		/// </summary>
		private static string InlineSvgImages(string html, string chapterFilePath, Dictionary<string, EpubLocalByteContentFile> imagesByPath)
		{
			return s_svgImageHref.Replace(html, match =>
			{
				string src = match.Groups[3].Value;
				if (string.IsNullOrWhiteSpace(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
					|| src.Contains("://", StringComparison.Ordinal))
				{
					return match.Value;
				}

				string resolvedPath = ResolveEpubPath(chapterFilePath, System.Net.WebUtility.HtmlDecode(src));
				if (!imagesByPath.TryGetValue(resolvedPath, out var image))
				{
					return match.Value;
				}

				string dataUri = $"data:{image.ContentMimeType};base64,{Convert.ToBase64String(image.Content)}";
				return $"{match.Groups[1].Value}{match.Groups[2].Value}{dataUri}{match.Groups[2].Value}";
			});
		}

		/// <summary>Resolves an EPUB-internal relative reference (forward-slash, POSIX-style, independent of the host OS path separator) against the archive-absolute path of the document that contains it.</summary>
		private static string ResolveEpubPath(string basePath, string relativePath)
		{
			if (relativePath.StartsWith('/'))
			{
				return relativePath.TrimStart('/');
			}

			var segments = new List<string>(basePath.Split('/'));
			segments.RemoveAt(segments.Count - 1); // drop the base document's own file name, keep its directory

			foreach (string part in relativePath.Split('/'))
			{
				if (part.Length == 0 || part == ".")
				{
					continue;
				}

				if (part == "..")
				{
					if (segments.Count > 0)
					{
						segments.RemoveAt(segments.Count - 1);
					}

					continue;
				}

				segments.Add(part);
			}

			return string.Join('/', segments);
		}

		/// <summary>
		/// Calibre's de facto <c>&lt;meta name="calibre:series" content="..."/&gt;</c> convention -
		/// far more commonly present in real-world EPUB files than the formal EPUB3
		/// belongs-to-collection/role=series mechanism, and much simpler to read reliably. Falls
		/// back to no series (design spec §4) rather than attempting the EPUB3 collections path.
		/// </summary>
		private static BookMetadata BuildMetadata(EpubBook book, string filePath)
		{
			var package = book.Schema?.Package?.Metadata;
			var metaItems = package?.MetaItems;

			string? FindMeta(string name) => metaItems?
				.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content))?.Content;

			// EPUB3: <meta property="belongs-to-collection" id="c1">Name</meta> refined by <meta refines="#c1" property="group-position">2</meta>.
			var collection = metaItems?.FirstOrDefault(m =>
				string.Equals(m.Property, "belongs-to-collection", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content));
			string? epub3Series = collection?.Content?.Trim();
			string? epub3Position = null;
			if (collection is not null && !string.IsNullOrEmpty(collection.Id))
			{
				string refines = "#" + collection.Id;
				epub3Position = metaItems!.FirstOrDefault(m =>
					string.Equals(m.Property, "group-position", StringComparison.OrdinalIgnoreCase)
					&& string.Equals(m.Refines, refines, StringComparison.Ordinal))?.Content;
			}

			string? seriesName = FindCalibreSeriesName(book) ?? BookMetadataParsing.NullIfBlank(epub3Series);
			float? seriesIndex = BookMetadataParsing.ParseIndex(FindMeta("calibre:series_index"))
				?? BookMetadataParsing.ParseIndex(epub3Position);

			string? isbn = null;
			if (package?.Identifiers is not null)
			{
				foreach (var identifier in package.Identifiers)
				{
					isbn = BookMetadataParsing.ExtractIsbn(identifier.Identifier);
					if (isbn is not null)
					{
						break;
					}
				}
			}

			// EPUB3 subtitle: <meta property="title-type" refines="#t1">subtitle</meta>; rare enough that only the dc:title text is used elsewhere.
			string? subtitle = null;

			return new BookMetadata
			{
				Title = string.IsNullOrWhiteSpace(book.Title) ? Path.GetFileNameWithoutExtension(filePath) : book.Title,
				Author = book.Author,
				SeriesName = seriesName,
				SeriesIndex = seriesName is null ? null : seriesIndex,
				Subtitle = subtitle,
				Publisher = BookMetadataParsing.NullIfBlank(package?.Publishers?.FirstOrDefault()?.Publisher),
				LanguageISO = BookMetadataParsing.NullIfBlank(package?.Languages?.FirstOrDefault()?.Language),
				ISBN = isbn,
				Description = BookMetadataParsing.NullIfBlank(book.Description ?? package?.Descriptions?.FirstOrDefault()?.Description),
				Rating = BookMetadataParsing.FromCalibreRating(FindMeta("calibre:rating")),
				Genres = BookMetadataParsing.CleanList(package?.Subjects?.Select(s => s.Subject)),
				CoverImageBytes = book.CoverImage,
			};
		}

		private static string? FindCalibreSeriesName(EpubBook book)
		{
			var metaItems = book.Schema?.Package?.Metadata?.MetaItems;
			if (metaItems is null)
			{
				return null;
			}

			foreach (var meta in metaItems)
			{
				if (string.Equals(meta.Name, "calibre:series", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(meta.Content))
				{
					return meta.Content;
				}
			}

			return null;
		}

		private static Dictionary<string, string> FlattenNavigationTitles(IReadOnlyList<EpubNavigationItem>? items)
		{
			var result = new Dictionary<string, string>();
			if (items is null)
			{
				return result;
			}

			void Walk(IReadOnlyList<EpubNavigationItem> level)
			{
				foreach (var item in level)
				{
					string? path = item.Link?.ContentFilePath;
					if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(item.Title) && !result.ContainsKey(path))
					{
						result[path] = item.Title;
					}

					if (item.NestedItems is { Count: > 0 })
					{
						Walk(item.NestedItems);
					}
				}
			}

			Walk(items);
			return result;
		}

		/// <summary>
		/// Maps each spine content-file path to its nearest ancestor nav item that has children -
		/// EPUB3 nav's real hierarchy (<c>EpubNavigationItem.NestedItems</c>) already distinguishes
		/// "Part One" (a heading wrapping several chapters) from a plain top-level chapter (no
		/// children) - this reads that existing structure rather than inventing new parsing.
		/// A part heading that's itself linked to a spine file (a real part-title page, not just text)
		/// maps to its own title too, so it groups under itself. Nothing here maps to a value when no
		/// ancestor has children - those chapters simply aren't in the returned dictionary, and the
		/// TOC renders them ungrouped (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-
		/// design.md).
		/// </summary>
		private static Dictionary<string, string> FlattenNavigationParts(IReadOnlyList<EpubNavigationItem>? items)
		{
			var result = new Dictionary<string, string>();
			if (items is null)
			{
				return result;
			}

			void Walk(IReadOnlyList<EpubNavigationItem> level, string? currentPart)
			{
				foreach (var item in level)
				{
					bool hasChildren = item.NestedItems is { Count: > 0 };
					string? partForThisItem = hasChildren ? item.Title : currentPart;

					string? path = item.Link?.ContentFilePath;
					if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(partForThisItem) && !result.ContainsKey(path))
					{
						result[path] = partForThisItem!;
					}

					if (hasChildren)
					{
						Walk(item.NestedItems, item.Title);
					}
				}
			}

			Walk(items, null);
			return result;
		}

		public void Dispose()
		{
			// EpubBook (as opposed to the lazy EpubBookRef) is a fully-loaded in-memory POCO with
			// no unmanaged handles - nothing to release. Implemented for IBookTextSource conformance.
		}
	}
}
