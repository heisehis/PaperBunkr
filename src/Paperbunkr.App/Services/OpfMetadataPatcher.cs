using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Services;

/// <summary>The values written into an EPUB's OPF. A null / empty value means "leave what the file has" - write-back never deletes.</summary>
public sealed record OpfBookData(
    string? Publisher,
    string? Language,
    string? Isbn,
    IReadOnlyList<string> Subjects,
    string? SeriesName,
    float? SeriesIndex,
    float? Rating,
    string? Description);

/// <summary>
/// Patches only the metadata nodes of an EPUB package document (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §7);
/// every other node, attribute and namespace is left as the publisher wrote it.
/// </summary>
public static class OpfMetadataPatcher
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    /// <returns>True when the document changed.</returns>
    public static bool Patch(XDocument document, OpfBookData data)
    {
        XElement? package = document.Root;
        XElement? metadata = package?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (package is null || metadata is null)
        {
            return false;
        }

        XNamespace opf = package.Name.Namespace;
        bool changed = false;

        changed |= SetDc(metadata, "publisher", data.Publisher);
        changed |= SetDc(metadata, "language", data.Language);
        changed |= SetDc(metadata, "description", data.Description);
        changed |= SetIsbn(metadata, data.Isbn);
        changed |= SetSubjects(metadata, data.Subjects);

        if (!string.IsNullOrWhiteSpace(data.SeriesName))
        {
            changed |= SetMeta(metadata, opf, "calibre:series", data.SeriesName.Trim());
            changed |= SetCollection(metadata, data.SeriesName.Trim(), data.SeriesIndex);
            if (data.SeriesIndex is float index)
            {
                changed |= SetMeta(metadata, opf, "calibre:series_index", index.ToString("0.0###", CultureInfo.InvariantCulture));
            }
        }

        if (data.Rating is float rating && rating > 0f)
        {
            changed |= SetMeta(metadata, opf, "calibre:rating", ((int)Math.Round(rating * 2d)).ToString(CultureInfo.InvariantCulture));
        }

        return changed;
    }

    private static bool SetDc(XElement metadata, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        XElement? existing = metadata.Elements(Dc + name).FirstOrDefault();
        if (existing is null)
        {
            metadata.Add(new XElement(Dc + name, value));
            return true;
        }

        if (string.Equals(existing.Value.Trim(), value, StringComparison.Ordinal))
        {
            return false;
        }

        existing.Value = value;
        return true;
    }

    private static bool SetIsbn(XElement metadata, string? isbn)
    {
        string? clean = BookMetadataParsing.ExtractIsbn(isbn);
        if (clean is null)
        {
            return false;
        }

        bool present = metadata.Elements(Dc + "identifier").Any(i => string.Equals(BookMetadataParsing.ExtractIsbn(i.Value), clean, StringComparison.OrdinalIgnoreCase));
        if (present)
        {
            return false;
        }

        metadata.Add(new XElement(Dc + "identifier", "urn:isbn:" + clean));
        return true;
    }

    private static bool SetSubjects(XElement metadata, IReadOnlyList<string> subjects)
    {
        var wanted = subjects.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return false;
        }

        var existing = metadata.Elements(Dc + "subject").ToList();
        var have = existing.Select(e => e.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (have.SetEquals(wanted))
        {
            return false;
        }

        foreach (var e in existing)
        {
            e.Remove();
        }

        foreach (string s in wanted)
        {
            metadata.Add(new XElement(Dc + "subject", s));
        }

        return true;
    }

    private static bool SetMeta(XElement metadata, XNamespace opf, string name, string content)
    {
        XElement? existing = metadata.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "meta" && string.Equals((string?)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            metadata.Add(new XElement(opf + "meta", new XAttribute("name", name), new XAttribute("content", content)));
            return true;
        }

        if (string.Equals((string?)existing.Attribute("content"), content, StringComparison.Ordinal))
        {
            return false;
        }

        existing.SetAttributeValue("content", content);
        return true;
    }

    /// <summary>Keeps an EPUB3 <c>belongs-to-collection</c> + <c>group-position</c> pair in step when the file already has one.</summary>
    private static bool SetCollection(XElement metadata, string seriesName, float? index)
    {
        XElement? collection = metadata.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "meta" && string.Equals((string?)e.Attribute("property"), "belongs-to-collection", StringComparison.OrdinalIgnoreCase));
        if (collection is null)
        {
            return false;
        }

        bool changed = false;
        if (!string.Equals(collection.Value.Trim(), seriesName, StringComparison.Ordinal))
        {
            collection.Value = seriesName;
            changed = true;
        }

        string? id = (string?)collection.Attribute("id");
        if (index is float i && !string.IsNullOrEmpty(id))
        {
            XElement? position = metadata.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "meta"
                && string.Equals((string?)e.Attribute("property"), "group-position", StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)e.Attribute("refines"), "#" + id, StringComparison.Ordinal));
            string text = i.ToString("0.###", CultureInfo.InvariantCulture);
            if (position is not null && !string.Equals(position.Value.Trim(), text, StringComparison.Ordinal))
            {
                position.Value = text;
                changed = true;
            }
        }

        return changed;
    }
}
