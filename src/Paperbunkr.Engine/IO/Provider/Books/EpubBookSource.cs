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

			Metadata = new BookMetadata
			{
				Title = string.IsNullOrWhiteSpace(_book.Title) ? Path.GetFileNameWithoutExtension(filePath) : _book.Title,
				Author = _book.Author,
				SeriesName = FindCalibreSeriesName(_book),
				CoverImageBytes = _book.CoverImage,
			};

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

			Chapters = chapters;
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
