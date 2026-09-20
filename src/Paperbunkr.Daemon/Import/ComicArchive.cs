using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Paperbunkr.Data.Entities;
using SharpCompress.Archives;

namespace Paperbunkr.Daemon.Import;

/// <summary>The archive can't be imported; <see cref="Reason"/> says how to blocklist the release.</summary>
public sealed class ImportFailureException(BlocklistReason reason, string message) : Exception(message)
{
    public BlocklistReason Reason { get; } = reason;
}

/// <summary>The fields written into an imported archive's <c>ComicInfo.xml</c>. Element names are CE's <c>ComicInfo</c> property names.</summary>
public sealed record ComicInfoFields(string Series, string Number, string? Title, int? Year, int? Month, int? Day, string? Publisher);

/// <summary>
/// Reads, repacks and tags comic archives for import (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §6). Everything here works
/// on a private copy in a temp folder - the downloaded file is never touched.
/// </summary>
public static class ComicArchive
{
    public const string ComicInfoName = "ComicInfo.xml";

    /// <summary>Archive types a comic can arrive in. PDFs and everything else (nfo, images, samples) are not comics to import here.</summary>
    public static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cbz", ".zip", ".cbr", ".rar", ".cb7", ".7z", ".cbt", ".tar" };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif", ".jxl", ".tif", ".tiff",
    };

    public static bool IsArchive(string path) => ArchiveExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Produces a <c>.cbz</c> in <paramref name="tempDirectory"/> from <paramref name="sourcePath"/>: a .cbz/.zip is copied, anything else is repacked;
    /// <paramref name="info"/> is added when the archive has no usable <c>ComicInfo.xml</c> of its own. Returns the path and the page count.
    /// </summary>
    /// <exception cref="ImportFailureException">The archive is corrupt, password protected, or holds no pages.</exception>
    public static (string CbzPath, int Pages) PrepareCbz(string sourcePath, string tempDirectory, ComicInfoFields? info)
    {
        Directory.CreateDirectory(tempDirectory);
        var output = Path.Combine(tempDirectory, "issue.cbz");

        try
        {
            using var archive = ArchiveFactory.OpenArchive(sourcePath);
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();

            if (entries.Any(e => e.IsEncrypted))
            {
                throw new ImportFailureException(BlocklistReason.PasswordProtected, "The archive is password protected.");
            }

            var pages = entries.Where(e => e.Key is not null && ImageExtensions.Contains(Path.GetExtension(e.Key))).ToList();
            if (pages.Count == 0)
            {
                throw new ImportFailureException(BlocklistReason.Unreadable, "The archive contains no page images.");
            }

            var existingInfo = entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e.Key), ComicInfoName, StringComparison.OrdinalIgnoreCase));
            string? existingXml = null;
            if (existingInfo is not null)
            {
                using var stream = existingInfo.OpenEntryStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                existingXml = reader.ReadToEnd();
            }

            bool writeOurs = info is not null && !HasUsableIdentity(existingXml);

            using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                // Sequential names in natural order: pages from different folders of the source can't collide, and reading order is preserved.
                int pageNumber = 0;
                foreach (var page in pages.OrderBy(p => NaturalKey(p.Key!), StringComparer.Ordinal))
                {
                    var entry = zip.CreateEntry($"{++pageNumber:D5}{Path.GetExtension(page.Key!).ToLowerInvariant()}", CompressionLevel.Fastest);
                    using var target = entry.Open();
                    using var source = page.OpenEntryStream();
                    source.CopyTo(target);
                }

                if (writeOurs)
                {
                    WriteComicInfo(zip, info!);
                }
                else if (existingXml is not null)
                {
                    var keep = zip.CreateEntry(ComicInfoName);
                    using var writer = new StreamWriter(keep.Open(), new UTF8Encoding(false));
                    writer.Write(existingXml);
                }
            }

            return (output, pages.Count);
        }
        catch (ImportFailureException)
        {
            throw;
        }
        catch (CryptographicException)
        {
            throw new ImportFailureException(BlocklistReason.PasswordProtected, "The archive is password protected.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or NotSupportedException or EndOfStreamException
            || ex.GetType().Namespace?.StartsWith("SharpCompress", StringComparison.Ordinal) == true)
        {
            if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            {
                throw new ImportFailureException(BlocklistReason.PasswordProtected, "The archive is password protected.");
            }

            throw new ImportFailureException(BlocklistReason.Corrupt, $"The archive is corrupt or unreadable ({ex.Message}).");
        }
    }

    /// <summary>The Series and Number of an archive's own <c>ComicInfo.xml</c>, when it has one (used to match files inside a pack).</summary>
    public static (string Series, string Number)? TryReadIdentity(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(Path.GetFileName(e.Key), ComicInfoName, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.IsEncrypted)
            {
                return null;
            }

            using var reader = new StreamReader(entry.OpenEntryStream(), Encoding.UTF8);
            var doc = XDocument.Parse(reader.ReadToEnd());
            var series = doc.Root?.Element("Series")?.Value;
            var number = doc.Root?.Element("Number")?.Value;
            return string.IsNullOrWhiteSpace(series) || string.IsNullOrWhiteSpace(number) ? null : (series.Trim(), number.Trim());
        }
        catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException or InvalidOperationException or CryptographicException
            || ex.GetType().Namespace?.StartsWith("SharpCompress", StringComparison.Ordinal) == true)
        {
            return null;
        }
    }

    /// <summary>The <c>ComicInfo.xml</c> document for the given fields, in ComicRack's element names. Only known fields are written.</summary>
    public static string BuildComicInfoXml(ComicInfoFields info)
    {
        var root = new XElement("ComicInfo");
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) root.Add(new XElement(name, value.Trim()));
        }

        Add("Title", info.Title);
        Add("Series", info.Series);
        Add("Number", info.Number);
        if (info.Year is > 0) Add("Year", info.Year.ToString());
        if (info.Month is > 0 and <= 12) Add("Month", info.Month.ToString());
        if (info.Day is > 0 and <= 31) Add("Day", info.Day.ToString());
        Add("Publisher", info.Publisher);

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }

    private static void WriteComicInfo(ZipArchive zip, ComicInfoFields info)
    {
        var entry = zip.CreateEntry(ComicInfoName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(BuildComicInfoXml(info));
    }

    /// <summary>An existing ComicInfo.xml is kept only when it already identifies the issue (has both Series and Number).</summary>
    private static bool HasUsableIdentity(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return false;
        }

        try
        {
            var root = XDocument.Parse(xml).Root;
            return !string.IsNullOrWhiteSpace(root?.Element("Series")?.Value) && !string.IsNullOrWhiteSpace(root?.Element("Number")?.Value);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Sort key giving natural page order: "page2" before "page10".</summary>
    private static string NaturalKey(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(12, '0'));
}
