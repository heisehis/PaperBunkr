using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Paperbunkr.Data.Naming;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Scraper.Import;

/// <summary>A profile read from a <c>.lop</c> file, with what could not be carried over (<see cref="Notes"/>, one sentence each, without the profile's name).</summary>
public sealed record ImportedProfile(OrganizerProfile Profile, IReadOnlyList<string> Notes);

/// <summary>A profile that was not imported, and why.</summary>
public sealed record RejectedProfile(string Name, string Reason);

/// <summary>What <see cref="LopProfileReader.Read"/> found: the profiles it can use, the ones it refused, or a problem with the whole file.</summary>
public sealed record LopReadResult(IReadOnlyList<ImportedProfile> Profiles, IReadOnlyList<RejectedProfile> Rejected, string? FileError);

/// <summary>
/// Reads the profile files the Library Organizer plugin (2.x) saves (<c>losettings.py:274-380</c>) into <see cref="OrganizerProfile"/>s
/// (docs/superpowers/specs/2026-10-04-lop-profile-import-design.md). Pure: no database, no file system beyond the stream it is given.
/// </summary>
/// <remarks>
/// Every setting the profile model has is copied. A setting it does not have is reported in <see cref="ImportedProfile.Notes"/> when the file actually uses it, and
/// ignored silently when it is a plugin-UI nicety. The exclude rules are the one thing that is never approximated: see <see cref="ExcludeRuleConverter"/>.
/// An imported profile never runs by itself (<see cref="OrganizerProfile.UseForScheduledRun"/> is false), and its base folder is kept as written, with a note on a
/// <c>Move</c> profile so it is looked at before the first run.
/// </remarks>
public static class LopProfileReader
{
    // The plugin's own illegal-character table (losettings.py:68); Paperbunkr's Sanitizer uses exactly these.
    private static readonly Dictionary<string, string> DefaultIllegalCharacters = new()
    {
        ["?"] = string.Empty, ["/"] = string.Empty, ["\\"] = string.Empty, ["*"] = string.Empty,
        [":"] = " - ", ["<"] = "[", [">"] = "]", ["|"] = "!", ["\""] = "'",
    };

    public static LopReadResult Read(Stream stream)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(stream);
        }
        catch (XmlException ex)
        {
            return FileError($"Not a Library Organizer profile file: {ex.Message}");
        }

        XElement? root = document.Root;
        IEnumerable<XElement> nodes;
        switch (root?.Name.LocalName)
        {
            case "Profile":
                nodes = [root];
                break;
            case "Profiles":
                nodes = root.Elements("Profile");
                break;
            case "Settings" or "Setting":
                return FileError("This is a pre-2.0 Library Organizer profile file, which isn't supported. Open it in Library Organizer 2.x and export it again.");
            default:
                return FileError("Not a Library Organizer profile file.");
        }

        var profiles = new List<ImportedProfile>();
        var rejected = new List<RejectedProfile>();
        foreach (XElement node in nodes)
        {
            string name = ((string?)node.Attribute("Name"))?.Trim() ?? string.Empty;
            try
            {
                (ImportedProfile? profile, string? reason) = ReadProfile(node, name);
                if (profile is not null)
                {
                    profiles.Add(profile);
                }
                else
                {
                    rejected.Add(new RejectedProfile(name.Length == 0 ? "(unnamed)" : name, reason!));
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
            {
                rejected.Add(new RejectedProfile(name.Length == 0 ? "(unnamed)" : name, $"it could not be read: {ex.Message}"));
            }
        }

        return new LopReadResult(profiles, rejected, null);
    }

    private static LopReadResult FileError(string message) => new(Array.Empty<ImportedProfile>(), Array.Empty<RejectedProfile>(), message);

    private static (ImportedProfile? Profile, string? RejectedBecause) ReadProfile(XElement node, string name)
    {
        if (name.Length == 0)
        {
            return (null, "it has no name");
        }

        var notes = new List<string>();
        string modeText = Text(node, "Mode") ?? "Move";
        if (!Enum.TryParse(modeText, ignoreCase: true, out OrganizerMode mode))
        {
            return (null, $"its mode '{modeText}' is not Move, Copy or Simulate");
        }

        ExcludeConversion exclude = ExcludeRuleConverter.Convert(ReadExcludeRules(node));
        if (exclude.Rejected)
        {
            return (null, $"{exclude.RejectedBecause}. Importing it without that rule would organize different books than it does in Library Organizer");
        }

        var profile = new OrganizerProfile
        {
            Name = name,
            FolderTemplate = Text(node, "FolderTemplate") ?? string.Empty,
            FileTemplate = Text(node, "FileTemplate") ?? string.Empty,
            BaseFolder = Text(node, "BaseFolder") ?? string.Empty,
            Mode = mode,
            UseFolder = Bool(node, "UseFolder", true),
            UseFileName = Bool(node, "UseFileName", true),
            RemoveEmptyFolders = Bool(node, "RemoveEmptyFolder", true),
            EmptyFolder = Text(node, "EmptyFolder") ?? string.Empty,
            FailEmptyValues = Bool(node, "FailEmptyValues", false),
            UseForScheduledRun = false,
            ExcludeRuleJson = exclude.Group is null ? null : JsonSerializer.Serialize(exclude.Group),
        };

        var emptyData = Items(node, "EmptyData");
        if (emptyData.Count > 0)
        {
            profile.EmptyData = emptyData.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value);
        }

        var failedFields = List(node, "FailedFields");
        if (failedFields.Count > 0)
        {
            profile.FailedFields = failedFields;
        }

        var excludeFolders = List(node, "ExcludeFolders");
        if (excludeFolders.Count > 0)
        {
            profile.ExcludeFolders = excludeFolders;
        }

        Dictionary<int, string> months = Items(node, "Months")
            .Where(p => int.TryParse(p.Key, out _))
            .ToDictionary(p => int.Parse(p.Key), p => p.Value);
        if (months.Any(p => !FieldResolversDefaultMonth(p.Key, p.Value)))
        {
            profile.MonthNames = months;
        }

        CollectNotes(node, profile, notes);
        return (new ImportedProfile(profile, notes), null);
    }

    private static bool FieldResolversDefaultMonth(int month, string name) =>
        Paperbunkr.Data.Naming.FieldResolvers.DefaultMonthNames.TryGetValue(month, out string? standard) && standard == name;

    private static void CollectNotes(XElement node, OrganizerProfile profile, List<string> notes)
    {
        var illegal = new Dictionary<string, string>(DefaultIllegalCharacters);
        foreach ((string key, string value) in Items(node, "IllegalCharacters"))
        {
            illegal[key] = value;
        }

        if (illegal.Count != DefaultIllegalCharacters.Count || illegal.Any(p => !DefaultIllegalCharacters.TryGetValue(p.Key, out string? standard) || standard != p.Value))
        {
            notes.Add("It replaces illegal file-name characters differently from the standard table, and this app always uses the standard one");
        }

        foreach (string element in new[] { "Prefix", "Postfix", "Seperator", "TextBox" })
        {
            if (Items(node, element).Count > 0)
            {
                notes.Add($"Its per-token '{element.Replace("Seperator", "Separator")}' text isn't supported and was left out");
            }
        }

        if (List(node, "ExcludedEmptyFolder").Count > 0)
        {
            notes.Add("Its list of folders to keep when they are empty isn't supported (only folders inside the base folder are cleaned up)");
        }

        if (Bool(node, "MoveFileless", false))
        {
            notes.Add("It moves covers for books that have no file, which isn't supported");
        }

        if (Bool(node, "MoveFailed", false))
        {
            notes.Add("It moves books it could not name into a separate folder, which isn't supported; they stay where they are and are reported");
        }

        if (!Bool(node, "AutoSpaceFields", true) || !Bool(node, "ReplaceMultipleSpaces", true))
        {
            notes.Add("It turns off automatic spacing between fields or the collapsing of repeated spaces, which can't be turned off here");
        }

        if (profile.Mode == OrganizerMode.Copy && Bool(node, "CopyMode", true))
        {
            notes.Add("It adds each copy to the library; copies made here are not added to it");
        }

        if (profile.Mode == OrganizerMode.Move)
        {
            string where = string.IsNullOrWhiteSpace(profile.BaseFolder) ? "(no base folder set)" : profile.BaseFolder;
            notes.Add($"It moves files. Check the base folder before running it: {where}");
        }

        if (profile.UseFolder && TemplateValidator.Validate(profile.FolderTemplate, allowEmpty: true) is { } folderProblem)
        {
            notes.Add($"Folder template: {folderProblem}");
        }

        if (profile.UseFileName && TemplateValidator.Validate(profile.FileTemplate, allowEmpty: false) is { } fileProblem)
        {
            notes.Add($"File template: {fileProblem}");
        }
    }

    private static LopExcludeRules ReadExcludeRules(XElement node)
    {
        XElement? rules = node.Element("ExcludeRules");

        // The plugin keeps the operator and mode both as plain elements and, when it has rules, as attributes of <ExcludeRules>; the attributes win.
        string op = (string?)rules?.Attribute("Operator") ?? Text(node, "ExcludeOperator") ?? "Any";
        string mode = (string?)rules?.Attribute("ExcludeMode") ?? Text(node, "ExcludeMode") ?? "Do not";
        return new LopExcludeRules(op, mode, rules is null ? Array.Empty<LopRuleNode>() : ReadNodes(rules));
    }

    private static List<LopRuleNode> ReadNodes(XElement parent)
    {
        var nodes = new List<LopRuleNode>();
        foreach (XElement child in parent.Elements())
        {
            if (child.Name.LocalName == "ExcludeRule")
            {
                // Older files call the text attribute "Text" (locommon.py:257-260).
                string value = (string?)child.Attribute("Value") ?? (string?)child.Attribute("Text") ?? string.Empty;
                nodes.Add(new LopRule((string?)child.Attribute("Field") ?? string.Empty, (string?)child.Attribute("Operator") ?? string.Empty, value));
            }
            else if (child.Name.LocalName == "ExcludeGroup")
            {
                nodes.Add(new LopRuleGroup((string?)child.Attribute("Operator") ?? string.Empty, ReadNodes(child)));
            }
        }

        return nodes;
    }

    private static string? Text(XElement node, string name) => node.Element(name)?.Value;

    private static bool Bool(XElement node, string name, bool fallback) =>
        node.Element(name) is { } e ? Convert.ToBoolean(e.Value.Trim()) : fallback;

    private static List<string> List(XElement node, string name) =>
        node.Element(name)?.Elements("Item").Select(i => i.Value).ToList() ?? new List<string>();

    /// <summary>The <c>&lt;Item Name="k" Value="v"/&gt;</c> pairs of a dictionary setting; the plugin ignores items that don't have exactly those two attributes.</summary>
    private static List<KeyValuePair<string, string>> Items(XElement node, string name) =>
        node.Element(name)?.Elements("Item")
            .Where(i => i.Attributes().Count() == 2 && i.Attribute("Name") is not null && i.Attribute("Value") is not null)
            .Select(i => new KeyValuePair<string, string>((string)i.Attribute("Name")!, (string)i.Attribute("Value")!))
            .ToList() ?? new List<KeyValuePair<string, string>>();
}
