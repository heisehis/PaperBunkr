namespace Paperbunkr.Data.Entities;

/// <summary>
/// A single condition within a <see cref="SmartListConditionGroup"/>. <see cref="Value"/>/<see cref="Value2"/>
/// are stored as strings and parsed per the field's <see cref="SmartListDataType"/> — same shape
/// CE itself uses (<c>MatchValue</c>/<c>MatchValue2</c>) rather than typed columns per data type.
/// </summary>
public class SmartListCondition
{
    public int Id { get; set; }

    /// <summary>
    /// The <see cref="SmartListConditionGroup"/> this condition belongs to
    /// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2) — replaces the v1
    /// implicit "belongs to SmartList" via a <c>SmartListId</c> column.
    /// </summary>
    public int GroupId { get; set; }

    public SmartListConditionGroup? Group { get; set; }

    public SmartListField Field { get; set; }

    public SmartListOperator Operator { get; set; }

    /// <summary>
    /// Negates this condition's own match result (docs/superpowers/specs/2026-08-28-smartlist-engine-
    /// v2-design.md §2) — mirrors CE's per-matcher <c>Not</c> flag
    /// (<c>_reference/ComicRackCE/ComicRack.Engine/ComicBookGroupMatcher.cs</c>). XOR'd with the
    /// operator result in <see cref="SmartLists.SmartListQueryBuilder"/>.
    /// </summary>
    public bool Not { get; set; }

    /// <summary>
    /// Case-insensitive text matching (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md
    /// §3) — matches CE's <c>ComicBookStringMatcher.IgnoreCase</c>, default true. Only consulted for
    /// Text-typed fields; <see cref="SmartLists.SmartListQueryBuilder.EvaluateText"/> passes
    /// <see cref="System.StringComparison.OrdinalIgnoreCase"/> vs <see cref="System.StringComparison.Ordinal"/>.
    /// </summary>
    public bool IgnoreCase { get; set; } = true;

    public string Value { get; set; } = string.Empty;

    /// <summary>Only populated for InRange/DateInRange operators.</summary>
    public string? Value2 { get; set; }

    /// <summary>Only populated when <see cref="Field"/> is <see cref="SmartListField.CustomValue"/> — the custom-value name to match, matching CE's 2-argument <c>ComicBookCustomValuesMatcher</c>.</summary>
    public string? CustomValueName { get; set; }

    /// <summary>Only populated when <see cref="Field"/> is <see cref="SmartListField.VirtualTag"/> — the <see cref="VirtualTagDefinition.Id"/> to evaluate and match.</summary>
    public int? VirtualTagId { get; set; }

    /// <summary>
    /// Only populated when <see cref="Field"/> is <see cref="SmartListField.AllProperties"/>
    /// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §4) — which curated field
    /// bundle from <see cref="SearchFieldBundleCatalog"/> to match against. Null = CE's "All" default,
    /// same "only populated for this one field" convention as <see cref="CustomValueName"/>/<see cref="VirtualTagId"/>.
    /// </summary>
    public SearchMode? SearchMode { get; set; }

    public int SortOrder { get; set; }
}
