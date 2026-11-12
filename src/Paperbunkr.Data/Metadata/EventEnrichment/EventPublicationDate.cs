using System.Globalization;
using System.Text.Json.Serialization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.EventEnrichment;

public sealed record EventPublicationDate(int Year, int? Month = null, int? Day = null,
    bool Uncertain = false, bool Provisional = false, string Basis = "Manual", string? Coverage = null)
{
    [JsonIgnore] public DateTime Lower => new(Year, Month ?? 1, Day ?? 1);
    [JsonIgnore] public DateTime Upper => new(Year, Month ?? 12, Day ?? DateTime.DaysInMonth(Year, Month ?? 12));
    [JsonIgnore] public int DateKey => Year * 100 + (Month ?? 0);
    [JsonIgnore] public DateTime? Exact => Day.HasValue ? Lower : null;
    [JsonIgnore] public string Entry => Year.ToString("0000", CultureInfo.InvariantCulture)
        + (Month is int m ? $"-{m:00}" : "") + (Day is int d ? $"-{d:00}" : "");
    [JsonIgnore] public string Display => (Provisional ? "through " : "") + Entry + (Uncertain ? " (uncertain)" : "");

    public static EventPublicationDate? Parse(string? text, string basis = "Manual", bool uncertain = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split('-');
        if (parts.Length is < 1 or > 3 || parts[0].Length != 4
            || !int.TryParse(parts[0], out int y) || y is < 1 or > 9999) return null;
        int? month = null, day = null;
        if (parts.Length > 1)
        {
            if (parts[1].Length != 2 || !int.TryParse(parts[1], out int m) || m is < 0 or > 12) return null;
            if (m > 0) month = m;
        }
        if (parts.Length > 2)
        {
            if (parts[2].Length != 2 || !int.TryParse(parts[2], out int d) || d < 0) return null;
            if (d > 0)
            {
                if (month is null || d > DateTime.DaysInMonth(y, month.Value)) return null;
                day = d;
            }
        }
        return new(y, month, day, uncertain, Basis: basis);
    }

    public static EventPublicationDate? Effective(StoryEvent e, bool end)
    {
        var row = e.DateEndpoints.SingleOrDefault(d => d.IsEnd == end);
        if (row is not null) return new(row.Year, row.Month, row.Day, row.Uncertain, row.Provisional, row.Basis, row.Coverage);
        var legacy = end ? e.EndDate : e.StartDate;
        return legacy is DateTime date ? new(date.Year, date.Month, date.Day, Basis: "Legacy") : null;
    }

    public static EventPublicationDate? Bound(IEnumerable<EventPublicationDate?> dates, bool end, bool complete, string coverage)
    {
        var all = dates.ToArray();
        var known = all.OfType<EventPublicationDate>().ToArray();
        if (known.Length == 0) return null;
        var lo = end ? known.Max(d => d.Lower) : known.Min(d => d.Lower);
        var hi = end ? known.Max(d => d.Upper) : known.Min(d => d.Upper);
        var basis = string.Join(" / ", known.Select(d => d.Basis).Distinct().Order());
        return new(lo.Year, lo.Year == hi.Year && lo.Month == hi.Month ? lo.Month : null,
            lo == hi ? lo.Day : null, known.Any(d => d.Uncertain) || lo.Year != hi.Year,
            end || !complete || known.Length != all.Length, basis, coverage);
    }
}
