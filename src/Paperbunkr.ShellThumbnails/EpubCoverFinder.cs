using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// EPUB (decision 10): the cover image the package declares - EPUB 3 <c>properties="cover-image"</c> or EPUB 2
/// <c>&lt;meta name="cover" content="id"/&gt;</c> - else the first image referenced in spine (reading) order.
/// A text-only EPUB returns null and keeps Windows' normal icon.
/// </summary>
internal static partial class EpubCoverFinder
{
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    public static byte[]? FindCover(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        string? opfPath = FindPackagePath(zip);
        if (opfPath is null)
        {
            return null;
        }

        XDocument opf;
        using (var opfStream = zip.GetEntry(opfPath)?.Open())
        {
            if (opfStream is null)
            {
                return null;
            }

            opf = XDocument.Load(opfStream);
        }

        string opfDir = DirectoryOf(opfPath);
        var manifest = opf.Descendants().Where(e => e.Name.LocalName == "item").ToList();

        string? coverHref =
            manifest.FirstOrDefault(i => HasToken((string?)i.Attribute("properties"), "cover-image"))?.Attribute("href")?.Value
            ?? CoverFromMeta(opf, manifest);
        if (coverHref is not null && Read(zip, Combine(opfDir, coverHref)) is { } declared)
        {
            return declared;
        }

        var byId = manifest
            .Where(i => i.Attribute("id") is not null && i.Attribute("href") is not null)
            .GroupBy(i => (string)i.Attribute("id")!)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var itemRef in opf.Descendants().Where(e => e.Name.LocalName == "itemref"))
        {
            if ((string?)itemRef.Attribute("idref") is not { } idref || !byId.TryGetValue(idref, out var item))
            {
                continue;
            }

            string pagePath = Combine(opfDir, (string)item.Attribute("href")!);
            string media = (string?)item.Attribute("media-type") ?? string.Empty;
            if (media.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return Read(zip, pagePath);
            }

            if (ReadText(zip, pagePath) is not { } html)
            {
                continue;
            }

            var match = ImageReference().Match(html);
            if (match.Success && Read(zip, Combine(DirectoryOf(pagePath), Uri.UnescapeDataString(match.Groups["src"].Value))) is { } image)
            {
                return image;
            }
        }

        return null;
    }

    private static string? FindPackagePath(ZipArchive zip)
    {
        using var containerStream = zip.GetEntry("META-INF/container.xml")?.Open();
        if (containerStream is null)
        {
            return null;
        }

        var container = XDocument.Load(containerStream);
        return container.Descendants(Container + "rootfile").FirstOrDefault()?.Attribute("full-path")?.Value;
    }

    private static string? CoverFromMeta(XDocument opf, List<XElement> manifest)
    {
        string? coverId = opf.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "meta" && (string?)e.Attribute("name") == "cover")
            ?.Attribute("content")?.Value;
        return coverId is null ? null : manifest.FirstOrDefault(i => (string?)i.Attribute("id") == coverId)?.Attribute("href")?.Value;
    }

    private static bool HasToken(string? list, string token) =>
        list is not null && list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(token);

    private static string DirectoryOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..(slash + 1)];
    }

    /// <summary>Resolves an href relative to a folder inside the zip, collapsing <c>..</c> and dropping any #fragment.</summary>
    internal static string Combine(string folder, string href)
    {
        int hash = href.IndexOf('#');
        if (hash >= 0)
        {
            href = href[..hash];
        }

        var parts = new List<string>();
        foreach (string part in (href.StartsWith('/') ? href.TrimStart('/') : folder + href).Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part.Length > 0 && part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    private static byte[]? Read(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        using var s = entry.Open();
        return ArchiveCoverFinder.ReadAll(s);
    }

    private static string? ReadText(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>First <c>&lt;img src&gt;</c> or SVG <c>&lt;image (xlink:)href&gt;</c> in a content document.</summary>
    [GeneratedRegex("""<(?:img\b[^>]*?\bsrc|image\b[^>]*?\b(?:xlink:)?href)\s*=\s*["'](?<src>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageReference();
}
