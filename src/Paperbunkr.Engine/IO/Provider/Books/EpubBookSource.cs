using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
				});
			}

			Chapters = chapters;
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

		public void Dispose()
		{
			// EpubBook (as opposed to the lazy EpubBookRef) is a fully-loaded in-memory POCO with
			// no unmanaged handles - nothing to release. Implemented for IBookTextSource conformance.
		}
	}
}
