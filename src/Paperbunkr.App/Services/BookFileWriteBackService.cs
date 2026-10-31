using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

public enum BookWriteBackResult
{
    Success,

    /// <summary>Everything the file should hold is already in it.</summary>
    SkippedAlreadyCurrent,

    /// <summary>Only EPUB has a free, safe writer. PDF / FB2 / MOBI are a deliberate, visible skip.</summary>
    SkippedUnsupportedFormat,

    SkippedMissingFile,
    SkippedReadOnly,
    Failed,
}

public readonly record struct BookWriteBackOutcome(BookWriteBackResult Result, string? ErrorMessage)
{
    public bool IsSkip => Result is BookWriteBackResult.SkippedAlreadyCurrent or BookWriteBackResult.SkippedUnsupportedFormat
        or BookWriteBackResult.SkippedMissingFile or BookWriteBackResult.SkippedReadOnly;
}

/// <summary>
/// Writes a <see cref="Book"/>'s metadata back into its EPUB's OPF (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §7).
/// Same safety shape as the comic write-back: patch a sibling <c>.pbwrite-*.tmp</c> copy, prove it still opens, then swap it in with
/// <see cref="File.Replace(string,string,string?)"/> while the live-folder watcher is told to ignore the churn
/// (<see cref="FileWriteBackCoordinator"/>), so the book is never flagged missing. Never throws: every failure is an outcome.
/// </summary>
public class BookFileWriteBackService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public BookFileWriteBackService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    internal BookFileWriteBackService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>The "automatic" path: does nothing unless both write-back switches are on. Returns the number of files rewritten.</summary>
    public async Task<int> AutoWriteAsync(IEnumerable<int> bookIds, CancellationToken ct = default)
    {
        using (var context = _contextFactory())
        {
            var settings = context.GetOrCreateAppSettings();
            if (!settings.WriteMetadataToFiles || !settings.WriteMetadataAutomatically)
            {
                return 0;
            }
        }

        int written = 0;
        foreach (int id in bookIds.Distinct())
        {
            var outcome = await WriteAsync(id, ct).ConfigureAwait(false);
            if (outcome.Result == BookWriteBackResult.Success)
            {
                written++;
            }
        }

        return written;
    }

    public Task<BookWriteBackOutcome> WriteAsync(int bookId, CancellationToken ct = default) =>
        Task.Run(() => Write(bookId), ct);

    private BookWriteBackOutcome Write(int bookId)
    {
        try
        {
            using var context = _contextFactory();
            var book = context.Books.Include(b => b.BookSeries).Include(b => b.Tags).FirstOrDefault(b => b.Id == bookId);
            if (book is null || string.IsNullOrEmpty(book.FilePath) || !File.Exists(book.FilePath))
            {
                return new BookWriteBackOutcome(BookWriteBackResult.SkippedMissingFile, null);
            }

            if (book.Format != BookFormat.Epub)
            {
                return new BookWriteBackOutcome(BookWriteBackResult.SkippedUnsupportedFormat, null);
            }

            if (new FileInfo(book.FilePath).IsReadOnly)
            {
                return new BookWriteBackOutcome(BookWriteBackResult.SkippedReadOnly, null);
            }

            var data = new OpfBookData(
                book.Publisher,
                book.LanguageISO,
                book.ISBN,
                book.Tags.OrderBy(t => t.Field).ThenBy(t => t.Id).Select(t => t.Value).ToList(),
                book.BookSeries?.Name,
                book.SeriesIndex,
                book.Rating,
                book.Summary);

            return PatchEpub(book.FilePath, data);
        }
        catch (Exception ex)
        {
            return new BookWriteBackOutcome(BookWriteBackResult.Failed, ex.Message);
        }
    }

    /// <summary>Patches the EPUB at <paramref name="path"/> in place (via a validated sibling copy). Exposed for tests.</summary>
    internal static BookWriteBackOutcome PatchEpub(string path, OpfBookData data)
    {
        string tempPath = path + ".pbwrite-" + Guid.NewGuid().ToString("N") + ".tmp";
        FileWriteBackCoordinator.Suppress(path, TimeSpan.FromSeconds(15));

        try
        {
            File.Copy(path, tempPath, overwrite: true);

            bool changed;
            using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Update))
            {
                string? opfPath = FindOpfPath(zip);
                var opfEntry = opfPath is null ? null : zip.GetEntry(opfPath);
                if (opfEntry is null)
                {
                    return new BookWriteBackOutcome(BookWriteBackResult.Failed, "The EPUB has no readable package document.");
                }

                XDocument document;
                using (var read = opfEntry.Open())
                {
                    document = XDocument.Load(read, LoadOptions.PreserveWhitespace);
                }

                changed = OpfMetadataPatcher.Patch(document, data);
                if (!changed)
                {
                    return new BookWriteBackOutcome(BookWriteBackResult.SkippedAlreadyCurrent, null);
                }

                string name = opfEntry.FullName;
                opfEntry.Delete();
                var replacement = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var write = replacement.Open();
                using var writer = new StreamWriter(write, new UTF8Encoding(false));
                document.Save(writer, SaveOptions.DisableFormatting);
            }

            // Prove the patched copy still opens before it replaces the original.
            using (new EpubBookSource(tempPath))
            {
            }

            using (FileWriteBackCoordinator.BeginWrite(path))
            {
                FileWriteBackCoordinator.Suppress(path, TimeSpan.FromSeconds(15));
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }

            return new BookWriteBackOutcome(BookWriteBackResult.Success, null);
        }
        catch (Exception ex)
        {
            return new BookWriteBackOutcome(BookWriteBackResult.Failed, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? FindOpfPath(ZipArchive zip)
    {
        var container = zip.GetEntry("META-INF/container.xml");
        if (container is null)
        {
            return null;
        }

        using var stream = container.Open();
        var doc = XDocument.Load(stream);
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
    }
}
