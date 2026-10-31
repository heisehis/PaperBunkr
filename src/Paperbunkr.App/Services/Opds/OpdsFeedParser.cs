using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Paperbunkr.App.Services.Opds;

public sealed record OpdsLink(string Rel, string Href, string? Type, string? Title)
{
    public bool IsAcquisition => Rel.StartsWith("http://opds-spec.org/acquisition", StringComparison.OrdinalIgnoreCase);

    public bool IsImage => Rel.StartsWith("http://opds-spec.org/image", StringComparison.OrdinalIgnoreCase);

    public bool IsThumbnail => Rel.Equals("http://opds-spec.org/image/thumbnail", StringComparison.OrdinalIgnoreCase) || Rel.Equals("x-stanza-cover-image-thumbnail", StringComparison.OrdinalIgnoreCase);

    /// <summary>A link to another OPDS feed (navigation), judged by its media type.</summary>
    public bool IsFeed => Type is not null && Type.Contains("atom+xml", StringComparison.OrdinalIgnoreCase);
}

public sealed record OpdsEntry(
    string Id,
    string Title,
    IReadOnlyList<string> Authors,
    string? Summary,
    string? Series,
    float? SeriesIndex,
    IReadOnlyList<string> Categories,
    string? Language,
    DateTime? Published,
    IReadOnlyList<OpdsLink> Links)
{
    public IReadOnlyList<OpdsLink> Acquisitions => Links.Where(l => l.IsAcquisition).ToList();

    public bool IsAcquisition => Acquisitions.Count > 0;

    /// <summary>The feed a navigation entry opens: its first link that points at another feed.</summary>
    public OpdsLink? NavigationLink => IsAcquisition ? null : Links.FirstOrDefault(l => l.IsFeed && !l.Rel.Equals("self", StringComparison.OrdinalIgnoreCase)) ?? Links.FirstOrDefault(l => l.IsFeed);

    public OpdsLink? Thumbnail => Links.FirstOrDefault(l => l.IsThumbnail) ?? Links.FirstOrDefault(l => l.IsImage);

    public string? Author => Authors.Count == 0 ? null : string.Join(", ", Authors);
}

public sealed record OpdsFeed(string Title, IReadOnlyList<OpdsEntry> Entries, Uri? Next, Uri? Search, bool SearchIsTemplate, Uri? Start, Uri? Up);

/// <summary>
/// Parses OPDS 1.2 (Atom) feeds (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md §2). Relative links are resolved against the feed's own address.
/// </summary>
public static class OpdsFeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Dc = "http://purl.org/dc/terms/";
    private static readonly XNamespace DcElements = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Calibre = "http://calibre.kovidgoyal.net/2009/metadata";
    private static readonly XNamespace Schema = "http://schema.org/";
    private static readonly XNamespace OpenSearch = "http://a9.com/-/spec/opensearch/1.1/";

    public static OpdsFeed Parse(string xml, Uri feedUri)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("The catalog returned an empty document.");
        if (root.Name != Atom + "feed")
        {
            throw new FormatException("That address did not return an OPDS (Atom) feed.");
        }

        var links = ReadLinks(root, feedUri);
        Uri? Find(string rel) => links.FirstOrDefault(l => l.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase)) is { } l ? new Uri(l.Href) : null;

        var searchLink = links.FirstOrDefault(l => l.Rel.Equals("search", StringComparison.OrdinalIgnoreCase));
        Uri? search = searchLink is null ? null : new Uri(searchLink.Href);
        bool template = searchLink is not null && Uri.UnescapeDataString(searchLink.Href).Contains("{searchTerms}", StringComparison.Ordinal);

        return new OpdsFeed(
            (string?)root.Element(Atom + "title") ?? string.Empty,
            root.Elements(Atom + "entry").Select(e => ParseEntry(e, feedUri)).ToList(),
            Find("next"),
            search,
            template,
            Find("start"),
            Find("up"));
    }

    private static OpdsEntry ParseEntry(XElement entry, Uri baseUri)
    {
        var authors = entry.Elements(Atom + "author").Select(a => ((string?)a.Element(Atom + "name"))?.Trim()).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        string? summary = Text(entry.Element(Atom + "content")) ?? Text(entry.Element(Atom + "summary"));
        string? series = (string?)entry.Element(Calibre + "series") ?? (string?)entry.Element(Schema + "Series");
        float? index = float.TryParse((string?)entry.Element(Calibre + "series_index"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float i) ? i : null;
        var categories = entry.Elements(Atom + "category").Select(c => (string?)c.Attribute("label") ?? (string?)c.Attribute("term")).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).ToList();
        string? language = (string?)entry.Element(Dc + "language") ?? (string?)entry.Element(DcElements + "language");
        DateTime? published = DateTime.TryParse((string?)entry.Element(Atom + "published") ?? (string?)entry.Element(Dc + "issued"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

        return new OpdsEntry(
            ((string?)entry.Element(Atom + "id") ?? string.Empty).Trim(),
            ((string?)entry.Element(Atom + "title") ?? string.Empty).Trim(),
            authors,
            summary,
            string.IsNullOrWhiteSpace(series) ? null : series.Trim(),
            index,
            categories,
            string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
            published,
            ReadLinks(entry, baseUri));
    }

    private static List<OpdsLink> ReadLinks(XElement parent, Uri baseUri)
    {
        var list = new List<OpdsLink>();
        foreach (var link in parent.Elements(Atom + "link"))
        {
            string? href = (string?)link.Attribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            // A search template ("/search?q={searchTerms}") must stay a template, so build it against the base without escaping the braces.
            Uri resolved = Uri.TryCreate(baseUri, href, out var uri) ? uri : new Uri(baseUri, "/");
            list.Add(new OpdsLink((string?)link.Attribute("rel") ?? "alternate", resolved.OriginalString, (string?)link.Attribute("type"), (string?)link.Attribute("title")));
        }

        return list;
    }

    /// <summary>Atom text may be plain or XHTML (<c>type="xhtml"</c>); either way the summary is plain text.</summary>
    private static string? Text(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        string text = element.Attribute("type")?.Value is "xhtml" ? string.Concat(element.DescendantNodes().OfType<XText>().Select(t => t.Value + " ")) : element.Value;
        text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " "));
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Finds the Atom search template in an OpenSearch description document, or null.</summary>
    public static string? ParseOpenSearchTemplate(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        var urls = root?.Elements(OpenSearch + "Url").ToList() ?? new List<XElement>();
        var atom = urls.FirstOrDefault(u => ((string?)u.Attribute("type"))?.Contains("atom", StringComparison.OrdinalIgnoreCase) == true) ?? urls.FirstOrDefault();
        return (string?)atom?.Attribute("template");
    }
}
