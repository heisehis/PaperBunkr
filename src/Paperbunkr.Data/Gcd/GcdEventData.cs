using Microsoft.Data.Sqlite;
using Paperbunkr.Data.Metadata.EventEnrichment;

namespace Paperbunkr.Data.Gcd;

public sealed record GcdArcInfo(int Id, string Name, string? Description, string? Language, int? FirstYear, bool YearUncertain);
public sealed record GcdEventIssue(int Id, int SeriesId, string Series, string Number, string? Format,
    string? OnSaleDate, string? KeyDate, bool OnSaleUncertain, bool Reprint)
{
    public EventPublicationDate? Date => EventPublicationDate.Parse(OnSaleDate, "GCD on-sale", OnSaleUncertain)
        ?? EventPublicationDate.Parse(KeyDate, "GCD key date");
    public bool Collected => Format?.Contains("collected", StringComparison.OrdinalIgnoreCase) == true
        || Format?.Contains("reprint", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed partial class GcdDataStore
{
    public int SchemaVersion => int.TryParse(ReadMeta(_connection).GetValueOrDefault("schema_version"), out int version) ? version : 0;
    public bool HasEventData => SchemaVersion >= 2;

    public IReadOnlyList<EventPublicationDate?> LegacyEventDates(IEnumerable<int> issueIds)
    {
        var dates = new List<EventPublicationDate?>();
        foreach (var batch in issueIds.Distinct().Chunk(400))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT on_sale_date,key_date FROM issue WHERE id IN ({string.Join(',', batch)})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) dates.Add(EventPublicationDate.Parse(Text(r, 0), "GCD schema-1 on-sale (uncertainty not retained)", true)
                ?? EventPublicationDate.Parse(Text(r, 1), "GCD key date", true));
        }
        return dates;
    }

    public IReadOnlyList<GcdArcInfo> EventArcs(IEnumerable<int> verifiedIssueIds, int? arcId = null)
    {
        if (!HasEventData) return [];
        var ids = verifiedIssueIds.Distinct().Take(500).ToArray();
        if (arcId is null && ids.Length < 2) return [];
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT a.id,a.name,a.description,a.language,a.first_year,a.year_uncertain FROM arc a WHERE "
            + (arcId is int ? "a.id=$arc" : $"a.id IN (SELECT arc_id FROM arc_issue WHERE issue_id IN ({string.Join(',', ids)}) GROUP BY arc_id HAVING COUNT(DISTINCT issue_id) >= 2)");
        if (arcId is int id) cmd.Parameters.AddWithValue("$arc", id);
        var result = new List<GcdArcInfo>(); using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new(r.GetInt32(0), r.GetString(1), Text(r, 2), Text(r, 3), r.IsDBNull(4) ? null : r.GetInt32(4), r.GetInt32(5) != 0));
        return result;
    }

    public IReadOnlyList<GcdEventIssue> EventIssues(IEnumerable<int> ids, int? arcId = null)
    {
        if (!HasEventData) return [];
        var result = new List<GcdEventIssue>();
        var batches = arcId.HasValue ? new[] { Array.Empty<int>() } : ids.Distinct().Chunk(400).ToArray();
        foreach (var batch in batches)
        {
            if (!arcId.HasValue && batch.Length == 0) continue;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT i.id,i.series_id,s.name,i.number,d.format,i.on_sale_date,i.key_date,d.uncertain,"
                + (arcId.HasValue ? "a.reprint" : "d.reprint")
                + " FROM issue i JOIN series s ON s.id=i.series_id JOIN issue_event d ON d.issue_id=i.id "
                + (arcId.HasValue ? "JOIN arc_issue a ON a.issue_id=i.id WHERE a.arc_id=$arc" : $"WHERE i.id IN ({string.Join(',', batch)})");
            if (arcId is int aid) cmd.Parameters.AddWithValue("$arc", aid);
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), Text(r, 4), Text(r, 5), Text(r, 6), r.GetInt32(7) != 0, r.GetInt32(8) != 0));
        }
        return result;
    }

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public static partial class GcdExtractor
{
    private static bool DumpTable(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM gcd.sqlite_master WHERE type='table' AND name=$name";
        cmd.Parameters.AddWithValue("$name", table); return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
    private static bool DumpColumn(SqliteConnection c, string table, string column)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = $"PRAGMA gcd.table_info('{table}')";
        using var r = cmd.ExecuteReader(); while (r.Read()) if (r.GetString(1) == column) return true; return false;
    }
    private static void CopyEventData(SqliteConnection c)
    {
        Exec(c, """
            CREATE TABLE arc (id INTEGER PRIMARY KEY,name TEXT NOT NULL,description TEXT,language TEXT,first_year INTEGER,year_uncertain INTEGER NOT NULL);
            CREATE TABLE arc_issue (arc_id INTEGER NOT NULL,issue_id INTEGER NOT NULL,reprint INTEGER NOT NULL,PRIMARY KEY(arc_id,issue_id));
            CREATE INDEX ix_arc_issue_issue ON arc_issue(issue_id);
            CREATE TABLE issue_event (issue_id INTEGER PRIMARY KEY,format TEXT,uncertain INTEGER NOT NULL,reprint INTEGER NOT NULL);
            """);
        string format = DumpColumn(c, "gcd_series", "publishing_format") ? "s.publishing_format" : "NULL";
        string uncertainty = DumpColumn(c, "gcd_issue", "on_sale_date_uncertain") ? "COALESCE(i.on_sale_date_uncertain,0)" : "0";
        string issueReprint = DumpTable(c, "gcd_reprint") && DumpColumn(c, "gcd_reprint", "target_issue_id")
            ? "EXISTS(SELECT 1 FROM gcd.gcd_reprint r WHERE r.target_issue_id=i.id)" : "0";
        Exec(c, $"INSERT INTO issue_event SELECT i.id,{format},{uncertainty},{issueReprint} FROM gcd.gcd_issue i JOIN issue x ON x.id=i.id JOIN gcd.gcd_series s ON s.id=i.series_id");
        if (!DumpTable(c, "gcd_story_arc") || !DumpTable(c, "gcd_story_story_arc")) return;
        Exec(c, """
            INSERT INTO arc SELECT a.id,a.name,NULLIF(a.description,''),l.code,a.year_first_published,COALESCE(a.year_first_published_uncertain,0)
            FROM gcd.gcd_story_arc a LEFT JOIN gcd.stddata_language l ON l.id=a.language_id WHERE a.deleted=0;
            """);
        // Reprint flag is supplied below only when its actual relation exists; no guessed table joins.
        string reprint = DumpTable(c, "gcd_reprint") && DumpColumn(c, "gcd_reprint", "target_id")
            ? "EXISTS(SELECT 1 FROM gcd.gcd_reprint r WHERE r.target_id=t.id)" : "0";
        Exec(c, $"""
            INSERT INTO arc_issue
            SELECT a.id,t.issue_id,MIN({reprint})
            FROM gcd.gcd_story_story_arc link JOIN arc a ON a.id=link.storyarc_id
            JOIN gcd.gcd_story t ON t.id=link.story_id JOIN issue i ON i.id=t.issue_id
            WHERE t.deleted=0 GROUP BY a.id,t.issue_id;
            """);
    }
}
