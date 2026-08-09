namespace Paperbunkr.Data.Entities;

/// <summary>
/// A single AND-ed condition within a <see cref="SmartList"/>. <see cref="Value"/>/<see cref="Value2"/>
/// are stored as strings and parsed per the field's <see cref="SmartListDataType"/> — same shape
/// CE itself uses (<c>MatchValue</c>/<c>MatchValue2</c>) rather than typed columns per data type.
/// </summary>
public class SmartListCondition
{
    public int Id { get; set; }

    public int SmartListId { get; set; }

    public SmartList? SmartList { get; set; }

    public SmartListField Field { get; set; }

    public SmartListOperator Operator { get; set; }

    public string Value { get; set; } = string.Empty;

    /// <summary>Only populated for InRange/DateInRange operators.</summary>
    public string? Value2 { get; set; }

    /// <summary>Only populated when <see cref="Field"/> is <see cref="SmartListField.CustomValue"/> — the custom-value name to match, matching CE's 2-argument <c>ComicBookCustomValuesMatcher</c>.</summary>
    public string? CustomValueName { get; set; }

    public int SortOrder { get; set; }
}
