using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists.CeImport;

/// <summary>
/// Reads a ComicRack smart list (a <c>.cbl</c> whose <c>&lt;Matchers&gt;</c> hold the rules) into a detached <see cref="SmartList"/> plus the warnings for everything it
/// could not keep (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md). Pure: no database, no UI. It reads the XML itself rather than going through
/// CE's <c>XmlSerializer</c>, which cannot deserialize the plugin matcher type at all.
/// </summary>
/// <remarks>
/// <para>A <c>Not</c> on a group has no spelling in our model (a group has no negation), so it is pushed down by De Morgan: the group's mode flips and every condition
/// below it flips its own <c>Not</c>. Warnings are worded by the <em>effective</em> mode of the group a dropped condition sat in, so "list may match more" is true after the flip too.</para>
/// <para>A condition that cannot be kept is dropped, never approximated: the warning says which way that moves the list.</para>
/// </remarks>
public sealed class CeSmartListReader
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private readonly PluginMatcherRegistry _plugins;

    public CeSmartListReader(PluginMatcherRegistry? plugins = null)
    {
        _plugins = plugins ?? PluginMatcherRegistry.Default;
    }

    /// <summary>Reads the file's XML. Throws <see cref="InvalidDataException"/> when it is not a ComicRack list at all.</summary>
    public CeSmartListDraft Read(Stream stream, string fallbackName)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(stream);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Not a ComicRack list: {ex.Message}", ex);
        }

        return Read(document, fallbackName);
    }

    public CeSmartListDraft Read(XDocument document, string fallbackName)
    {
        XElement? root = document.Root;
        if (root is null || root.Name.LocalName != "ReadingList")
        {
            throw new InvalidDataException("Not a ComicRack list (no ReadingList element).");
        }

        string name = root.Element("Name")?.Value.Trim() is { Length: > 0 } named ? named : fallbackName;
        var warnings = new List<CeImportWarning>();

        // CE's smart list is itself a group matcher: a MatcherMode on <ReadingList> is how the top-level matchers combine (10 of the 154 supplied files say Or).
        var rootMode = string.Equals((string?)root.Attribute("MatcherMode"), "Or", StringComparison.OrdinalIgnoreCase) ? SmartListGroupMode.Or : SmartListGroupMode.And;
        var rootGroup = new SmartListConditionGroup { Mode = rootMode };

        XElement? matchers = root.Element("Matchers");
        int sourceCount = matchers?.Elements("ComicBookMatcher").Count() ?? 0;
        if (matchers is not null)
        {
            ReadChildren(matchers, rootGroup, negated: false, name, warnings, wholeLibrary: true);
        }

        // Options of ComicSmartListItem that narrow a list beyond its matchers; none of the supplied files set them, and we have no equivalent.
        if (string.Equals(root.Element("Limit")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new CeImportWarning(name, "limit", "capped the list to a number of books, which isn't supported", CeWarningEffect.Widened));
        }

        if (Guid.TryParse(root.Element("BaseListId")?.Value, out Guid baseList) && baseList != Guid.Empty)
        {
            warnings.Add(new CeImportWarning(name, "base list", "restricted the list to (or excluded) the books of another list, which isn't supported", CeWarningEffect.Widened));
        }

        return new CeSmartListDraft
        {
            Name = name,
            List = new SmartList { Name = name, IsSystem = false, TargetKind = SmartListTargetKind.Issue, RootGroup = rootGroup },
            Warnings = warnings,
            IsEmpty = sourceCount > 0 && rootGroup.Conditions.Count == 0 && rootGroup.ChildGroups.Count == 0,
        };
    }

    private void ReadChildren(XElement parent, SmartListConditionGroup target, bool negated, string listName, List<CeImportWarning> warnings, bool wholeLibrary)
    {
        int conditionOrder = 0;
        int groupOrder = 0;
        CeWarningEffect effect = target.Mode == SmartListGroupMode.And ? CeWarningEffect.Widened : CeWarningEffect.Narrowed;

        int position = -1;
        foreach (XElement matcher in parent.Elements("ComicBookMatcher"))
        {
            position++;
            string type = (string?)matcher.Attribute(Xsi + "type") ?? string.Empty;
            bool not = string.Equals((string?)matcher.Attribute("Not"), "true", StringComparison.OrdinalIgnoreCase);

            if (type == CeMatcherMap.GroupType)
            {
                bool childNegated = negated ^ not;
                var mode = string.Equals((string?)matcher.Attribute("MatcherMode"), "Or", StringComparison.OrdinalIgnoreCase) ? SmartListGroupMode.Or : SmartListGroupMode.And;
                var group = new SmartListConditionGroup { Mode = childNegated ? Flip(mode) : mode, SortOrder = groupOrder };

                XElement? inner = matcher.Element("Matchers");
                bool hadChildren = inner?.Elements("ComicBookMatcher").Any() == true;
                if (inner is not null)
                {
                    ReadChildren(inner, group, childNegated, listName, warnings, wholeLibrary && position == 0);
                }

                if (hadChildren && group.Conditions.Count == 0 && group.ChildGroups.Count == 0)
                {
                    warnings.Add(new CeImportWarning(listName, "group", "has no condition that could be imported, so the whole group was dropped", effect));
                    continue;
                }

                target.ChildGroups.Add(group);
                groupOrder++;
                continue;
            }

            SmartListCondition? condition = BuildLeaf(matcher, type, out string detail);
            if (condition is null)
            {
                warnings.Add(new CeImportWarning(listName, Describe(matcher, type), detail, effect));
                continue;
            }

            // CE ran a script over the books the rules before it had kept; a condition that looks across books (same X, different Y) is evaluated over the whole library here, which is
            // the same thing only while nothing before it narrowed the list (true of 8 of the 9 supplied uses, where it is the first rule).
            if (condition.Field == SmartListField.SameXDifferentY && !(wholeLibrary && position == 0))
            {
                warnings.Add(new CeImportWarning(listName, Describe(matcher, type), "ran after other rules in ComicRack and only looked at the books they kept; here it looks at the whole library", CeWarningEffect.Approximated));
            }

            condition.Not ^= not ^ negated;
            condition.SortOrder = conditionOrder++;
            target.Conditions.Add(condition);
        }
    }

    private SmartListCondition? BuildLeaf(XElement matcher, string type, out string detail)
    {
        detail = string.Empty;
        string value = matcher.Element("MatchValue")?.Value ?? string.Empty;
        string value2 = matcher.Element("MatchValue2")?.Value ?? string.Empty;

        if (type == CeMatcherMap.PluginType)
        {
            string? key = (string?)matcher.Attribute("PluginKey");
            SmartListCondition? native = _plugins.TryCreate(key, value, value2);
            if (native is null)
            {
                detail = string.IsNullOrEmpty(key)
                    ? "is a script matcher with no key"
                    : "is a ComicRack script with no equivalent here yet";
            }

            return native;
        }

        if (!CeMatcherMap.TryGet(type, out CeMatcherMap.Entry? entry))
        {
            detail = CeMatcherMap.IsKnownUnsupportedSeriesDateMatcher(type)
                ? "is a series date matcher, which isn't supported yet"
                : "isn't a matcher type this app knows";
            return null;
        }

        int index = int.TryParse((string?)matcher.Attribute("MatchOperator"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
        SmartListOperator? op = CeMatcherMap.OperatorFor(entry.Kind, index);
        if (op is null)
        {
            detail = $"uses operator {index}, which has no equivalent here";
            return null;
        }

        var condition = new SmartListCondition
        {
            Field = entry.Field,
            Operator = op.Value,
            IgnoreCase = !string.Equals((string?)matcher.Attribute("IgnoreCase"), "false", StringComparison.OrdinalIgnoreCase),
        };

        switch (entry.Kind)
        {
            case CeMatcherKind.String:
                condition.Value = value;
                break;
            case CeMatcherKind.CustomValue:
                // Column 1: the value's name is MatchValue and the text compared is MatchValue2.
                condition.CustomValueName = value;
                condition.Value = value2;
                break;
            case CeMatcherKind.Number:
                condition.Value = Number(value);
                condition.Value2 = op == SmartListOperator.InRange ? Number(value2) : null;
                break;
            case CeMatcherKind.Date:
                condition.Value = value;
                condition.Value2 = op == SmartListOperator.DateInRange ? value2 : null;

                // CE reads an empty date as DateTime.MinValue, and a comic with no year has Published == MinValue, so "published is <empty>" means "has no
                // published date" there (and "is after <empty>" means "has one"). Our date conditions never match an unset date, so those two spell it
                // through the year instead (Published is unset exactly when the year is not positive).
                if (entry.Field == SmartListField.Published && string.IsNullOrWhiteSpace(value) && op is SmartListOperator.Is or SmartListOperator.IsAfter)
                {
                    bool isUnset = op == SmartListOperator.Is;
                    condition.Field = SmartListField.Year;
                    condition.Operator = isUnset ? SmartListOperator.LessThan : SmartListOperator.GreaterThan;
                    condition.Value = isUnset ? "1" : "0";
                    condition.Value2 = null;
                }

                break;
            case CeMatcherKind.Duplicate:
                condition.Value = string.Empty;
                break;
        }

        return condition;
    }

    /// <summary>CE's numeric matcher turns a value it cannot parse into -1 (<c>ComicBookNumericMatcher.GetInvalidValue</c>); our evaluator would read it as 0.</summary>
    private static string Number(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f.ToString(CultureInfo.InvariantCulture) : "-1";

    private static SmartListGroupMode Flip(SmartListGroupMode mode) => mode == SmartListGroupMode.And ? SmartListGroupMode.Or : SmartListGroupMode.And;

    private static string Describe(XElement matcher, string type)
    {
        if (type == CeMatcherMap.PluginType)
        {
            string? key = (string?)matcher.Attribute("PluginKey");
            return string.IsNullOrEmpty(key) ? "script matcher" : $"script '{key}'";
        }

        string friendly = type;
        foreach (string prefix in new[] { "ComicBook", "SmartList" })
        {
            if (friendly.StartsWith(prefix, StringComparison.Ordinal))
            {
                friendly = friendly[prefix.Length..];
                break;
            }
        }

        return friendly.EndsWith("Matcher", StringComparison.Ordinal) ? friendly[..^"Matcher".Length] : friendly;
    }
}
