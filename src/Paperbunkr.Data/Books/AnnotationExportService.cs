using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Books;

/// <summary>
/// Per-book annotation export (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-
/// annotations-design.md §"Export") - one method per format, same static-class/
/// <c>(context, id, filePath)</c> shape as <see cref="ReadingLists.CblReadingListIO"/>. Bookmarks/
/// highlights are chapter-addressed (EPUB only - the PDF reader has neither); annotation images are
/// page-addressed (PDF only). <c>Paperbunkr.Data</c> already references <c>Paperbunkr.Engine</c> (see
/// <see cref="ReadingLists.CblReadingListIO"/>'s own use of it), so real chapter titles are resolved
/// by opening the book's own <see cref="IBookTextSource"/> rather than a synthetic "Chapter N" label.
/// </summary>
public static class AnnotationExportService
{
    public static void ExportMarkdown(PaperbunkrDbContext context, int bookId, string filePath)
    {
        var book = LoadBook(context, bookId);
        var chapterTitles = ResolveChapterTitles(book);
        string imagesDir = PrepareImagesFolder(filePath, book.AnnotationImages, out var copiedFileNames);

        var sb = new StringBuilder();
        sb.AppendLine($"# {book.Title}");
        sb.AppendLine();

        var chapterIndexes = book.Bookmarks.Select(b => b.ChapterIndex)
            .Concat(book.Highlights.Select(h => h.ChapterIndex))
            .Distinct()
            .OrderBy(i => i);

        foreach (int chapterIndex in chapterIndexes)
        {
            string title = chapterTitles.TryGetValue(chapterIndex, out var t) ? t : $"Chapter {chapterIndex + 1}";
            sb.AppendLine($"## {title}");
            sb.AppendLine();

            foreach (var bookmark in book.Bookmarks.Where(b => b.ChapterIndex == chapterIndex).OrderBy(b => b.CharacterOffset))
            {
                sb.AppendLine("> 🔖 " + bookmark.Excerpt.Replace("\n", "\n> "));
                sb.AppendLine();
            }

            foreach (var highlight in book.Highlights.Where(h => h.ChapterIndex == chapterIndex).OrderBy(h => h.StartOffset))
            {
                sb.AppendLine($"> **[{highlight.Color}]** " + highlight.Excerpt.Replace("\n", "\n> "));
                if (!string.IsNullOrWhiteSpace(highlight.Note))
                {
                    sb.AppendLine($">\n> _{highlight.Note}_");
                }

                sb.AppendLine();
            }
        }

        if (book.AnnotationImages.Count > 0)
        {
            sb.AppendLine("## Captured Regions");
            sb.AppendLine();
            foreach (var image in book.AnnotationImages.OrderBy(a => a.PageIndex))
            {
                string relativePath = copiedFileNames[image.Id];
                sb.AppendLine($"**Page {image.PageIndex + 1}**");
                sb.AppendLine();
                sb.AppendLine($"![capture]({relativePath})");
                if (!string.IsNullOrWhiteSpace(image.Note))
                {
                    sb.AppendLine();
                    sb.AppendLine($"_{image.Note}_");
                }

                sb.AppendLine();
            }
        }

        File.WriteAllText(filePath, sb.ToString());
        _ = imagesDir;
    }

    public static void ExportCsv(PaperbunkrDbContext context, int bookId, string filePath)
    {
        var book = LoadBook(context, bookId);
        var sb = new StringBuilder();
        sb.AppendLine("Type,ChapterOrPage,Excerpt,Note,Color,CreatedTime");

        foreach (var bookmark in book.Bookmarks.OrderBy(b => b.ChapterIndex).ThenBy(b => b.CharacterOffset))
        {
            sb.AppendLine(CsvRow("Bookmark", (bookmark.ChapterIndex + 1).ToString(CultureInfo.InvariantCulture),
                bookmark.Excerpt, string.Empty, string.Empty, bookmark.CreatedTime));
        }

        foreach (var highlight in book.Highlights.OrderBy(h => h.ChapterIndex).ThenBy(h => h.StartOffset))
        {
            sb.AppendLine(CsvRow("Highlight", (highlight.ChapterIndex + 1).ToString(CultureInfo.InvariantCulture),
                highlight.Excerpt, highlight.Note ?? string.Empty, highlight.Color.ToString(), highlight.CreatedTime));
        }

        foreach (var image in book.AnnotationImages.OrderBy(a => a.PageIndex))
        {
            sb.AppendLine(CsvRow("Capture", (image.PageIndex + 1).ToString(CultureInfo.InvariantCulture),
                string.Empty, image.Note ?? string.Empty, string.Empty, image.CreatedTime));
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    public static void ExportJson(PaperbunkrDbContext context, int bookId, string filePath)
    {
        var book = LoadBook(context, bookId);
        string imagesDir = PrepareImagesFolder(filePath, book.AnnotationImages, out var copiedFileNames);

        var payload = new
        {
            book.Title,
            book.Author,
            Bookmarks = book.Bookmarks.Select(b => new { b.ChapterIndex, b.CharacterOffset, b.Excerpt, b.CreatedTime }),
            Highlights = book.Highlights.Select(h => new
            {
                h.ChapterIndex, h.StartOffset, h.EndOffset, Color = h.Color.ToString(), h.Note, h.Excerpt, h.CreatedTime,
            }),
            AnnotationImages = book.AnnotationImages.Select(a => new
            {
                a.PageIndex, a.RectX, a.RectY, a.RectWidth, a.RectHeight, a.Note, a.CreatedTime,
                ImagePath = copiedFileNames[a.Id],
            }),
        };

        File.WriteAllText(filePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        _ = imagesDir;
    }

    private static Book LoadBook(PaperbunkrDbContext context, int bookId) =>
        context.Books
            .Include(b => b.Bookmarks)
            .Include(b => b.Highlights)
            .Include(b => b.AnnotationImages)
            .Single(b => b.Id == bookId);

    /// <summary>Opens the book's own <see cref="IBookTextSource"/> just long enough to read chapter titles - reflowable formats (EPUB/FB2/MOBI) only meaningfully have them; a PDF-format book has no chapters to resolve, so an empty map is returned for those (Bookmarks/Highlights never target PDF books anyway - see this file's own class doc comment).</summary>
    private static Dictionary<int, string> ResolveChapterTitles(Book book)
    {
        var titles = new Dictionary<int, string>();
        if (book.Format == BookFormat.Pdf || !File.Exists(book.FilePath))
        {
            return titles;
        }

        try
        {
            using var source = BookTextSourceFactory.Create(book.Format, book.FilePath);
            for (int i = 0; i < source.Chapters.Count; i++)
            {
                titles[i] = source.Chapters[i].Title;
            }
        }
        catch
        {
            // Source file moved/changed since the highlight was made - fall back to synthetic
            // "Chapter N" labels (the caller's own TryGetValue already handles a missing entry).
        }

        return titles;
    }

    /// <summary>Copies each annotation image alongside <paramref name="outputFilePath"/> into a "{name}_images" sibling folder, so Markdown/JSON image links resolve without embedding - per the design's explicit decision not to embed images in the export file itself.</summary>
    private static string PrepareImagesFolder(string outputFilePath, IReadOnlyCollection<BookAnnotationImage> images, out Dictionary<int, string> relativePathsById)
    {
        relativePathsById = new Dictionary<int, string>();
        string folderName = Path.GetFileNameWithoutExtension(outputFilePath) + "_images";
        string outputDir = Path.GetDirectoryName(outputFilePath) ?? ".";
        string imagesDir = Path.Combine(outputDir, folderName);

        if (images.Count == 0)
        {
            return imagesDir;
        }

        Directory.CreateDirectory(imagesDir);
        foreach (var image in images)
        {
            if (!File.Exists(image.ImagePath))
            {
                continue;
            }

            string destFileName = $"{image.Id}{Path.GetExtension(image.ImagePath)}";
            File.Copy(image.ImagePath, Path.Combine(imagesDir, destFileName), overwrite: true);
            relativePathsById[image.Id] = $"{folderName}/{destFileName}";
        }

        return imagesDir;
    }

    private static string CsvRow(params object[] fields) =>
        string.Join(",", fields.Select(f => CsvEscape(Convert.ToString(f, CultureInfo.InvariantCulture) ?? string.Empty)));

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
