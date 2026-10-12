using System.Xml;
using System.Xml.Linq;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>What a ComicRack <c>.cbl</c> file holds.</summary>
public enum CblFileKind
{
    /// <summary>Not a ComicRack list: unreadable, not XML, or no <c>ReadingList</c> element.</summary>
    Invalid,

    /// <summary>A reading list: <c>&lt;Books&gt;</c> (or an empty list; the existing import path decides what to do with it).</summary>
    ReadingList,

    /// <summary>A smart list: <c>&lt;Matchers&gt;</c> with rules and no books.</summary>
    SmartList,
}

/// <summary>
/// Tells a ComicRack smart list from a reading list by content (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md): both are <c>.cbl</c>, and the
/// only difference is that a smart list has matchers instead of books. A file with both is a reading list (the books win; nothing in them is lost).
/// </summary>
public static class CblFile
{
    public static CblFileKind Detect(string path)
    {
        try
        {
            return Detect(XDocument.Load(path));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return CblFileKind.Invalid;
        }
    }

    public static CblFileKind Detect(XDocument document)
    {
        XElement? root = document.Root;
        if (root is null || root.Name.LocalName != "ReadingList")
        {
            return CblFileKind.Invalid;
        }

        bool hasBooks = root.Element("Books")?.Elements("Book").Any() == true;
        bool hasMatchers = root.Element("Matchers")?.Elements("ComicBookMatcher").Any() == true;
        return hasMatchers && !hasBooks ? CblFileKind.SmartList : CblFileKind.ReadingList;
    }
}
