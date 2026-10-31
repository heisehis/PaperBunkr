using System.Xml.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists.CeImport;

/// <summary>Extracts ComicRack smart rules from ComicDb.xml without deserializing its plugin-dependent list tree.</summary>
public static class CeComicDbSmartListReader
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static IReadOnlyList<CeSmartListDraft> Read(XDocument document, CeSmartListReader? reader = null)
    {
        XElement? comicLists = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "ComicLists");
        if (comicLists is null) return Array.Empty<CeSmartListDraft>();
        reader ??= new CeSmartListReader();
        var result = new List<CeSmartListDraft>();
        foreach (XElement item in Items(comicLists)) ReadItem(item, Array.Empty<string>(), reader, result);
        return result;
    }

    private static IEnumerable<XElement> Items(XElement parent) =>
        parent.Elements().Where(e => e.Name.LocalName == "Item");

    private static void ReadItem(XElement item, IReadOnlyList<string> folders, CeSmartListReader reader, List<CeSmartListDraft> result)
    {
        string type = (string?)item.Attribute(Xsi + "type") ?? string.Empty;
        string name = ((string?)item.Attribute("Name"))?.Trim() ?? string.Empty;
        if (type == "ComicListItemFolder")
        {
            var next = string.IsNullOrWhiteSpace(name) ? folders : folders.Append(name).ToArray();
            foreach (XElement child in item.Elements().Where(e => e.Name.LocalName == "Items").SelectMany(Items))
                ReadItem(child, next, reader, result);
            return;
        }

        // Ordinary reading lists and static ID lists are deliberately ignored.
        if (type != "ComicSmartListItem") return;

        string fullName = string.Join(" > ", folders.Append(string.IsNullOrWhiteSpace(name) ? "Imported Smart List" : name));
        var readingList = new XElement("ReadingList",
            new XAttribute("MatcherMode", (string?)item.Attribute("MatcherMode") ?? "And"),
            new XElement("Name", name));
        foreach (XElement child in item.Elements().Where(e => e.Name.LocalName != "Display" && e.Name.LocalName != "Items"))
            readingList.Add(new XElement(child));

        CeSmartListDraft parsed = reader.Read(new XDocument(readingList), fullName);
        parsed.List.Name = fullName;
        result.Add(new CeSmartListDraft
        {
            Name = fullName,
            List = parsed.List,
            Warnings = parsed.Warnings.Select(w => w with { ListName = fullName }).ToList(),
            IsEmpty = parsed.IsEmpty,
        });
    }
}
