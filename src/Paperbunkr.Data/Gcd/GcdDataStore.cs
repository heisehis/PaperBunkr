using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Paperbunkr.Data.Gcd;

/// <summary>
/// Read-only access to the installed Grand Comics Database extract (docs/superpowers/specs/2026-09-27-gcd-data-design.md §2). Every
/// GCD feature asks <see cref="TryOpen"/> first and quietly does nothing when the data isn't installed.
/// </summary>
public sealed class GcdDataStore : IDisposable
{
    private readonly SqliteConnection _connection;

    private GcdDataStore(SqliteConnection connection, string dumpDate)
    {
        _connection = connection;
        DumpDate = dumpDate;
    }

    /// <summary>Where the app keeps the extract: <c>%AppData%/Paperbunkr/gcd/gcd.sqlite</c>.</summary>
    public static string DefaultPath => Path.Combine(AppDataPaths.Root, "gcd", GcdExtractor.ExtractFileName);

    public static bool IsInstalled(string? path = null) => File.Exists(path ?? DefaultPath);

    /// <summary>"2026-09-15": the GCD dump the extract was built from.</summary>
    public string DumpDate { get; }

    /// <summary>Opens the extract read-only; null when it's missing, unreadable or a schema this build doesn't know.</summary>
    public static GcdDataStore? TryOpen(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
        {
            return null;
        }

        var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        try
        {
            connection.Open();
            var meta = ReadMeta(connection);
            if (meta.GetValueOrDefault("schema_version") != GcdExtractor.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                connection.Dispose();
                return null;
            }

            return new GcdDataStore(connection, meta.GetValueOrDefault("dump_date") ?? "unknown");
        }
        catch (SqliteException)
        {
            connection.Dispose();
            return null;
        }
    }

    internal static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM meta";
        using var r = cmd.ExecuteReader();
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        while (r.Read())
        {
            meta[r.GetString(0)] = r.IsDBNull(1) ? string.Empty : r.GetString(1);
        }

        return meta;
    }

    /// <summary>Series whose normalized name key (<c>TitleNormalizer.StripDown</c>, lower-cased) equals <paramref name="nameKey"/>.</summary>
    public IReadOnlyList<GcdSeries> FindSeriesByKey(string nameKey)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, year_began, year_ended, publisher, language, issue_count FROM series WHERE name_key = $k LIMIT 200";
        cmd.Parameters.AddWithValue("$k", nameKey);
        return ReadSeries(cmd);
    }

    public IReadOnlyList<GcdSeries> GetSeries(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return Array.Empty<GcdSeries>();
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT id, name, year_began, year_ended, publisher, language, issue_count FROM series WHERE id IN ({string.Join(",", list)})";
        return ReadSeries(cmd);
    }

    public IReadOnlyList<GcdIssue> IssuesOf(int seriesId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, series_id, number, key_date, on_sale_date FROM issue WHERE series_id = $s";
        cmd.Parameters.AddWithValue("$s", seriesId);
        return ReadIssues(cmd);
    }

    /// <summary>On-sale (else key) date keys for GCD issues, as <c>year*100+month</c>.</summary>
    public IReadOnlyDictionary<int, int> DateKeysFor(IEnumerable<int> issueIds)
    {
        var result = new Dictionary<int, int>();
        foreach (var chunk in issueIds.Distinct().Chunk(500))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT id, key_date, on_sale_date FROM issue WHERE id IN ({string.Join(",", chunk)})";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string? key = r.IsDBNull(1) ? null : r.GetString(1);
                string? onSale = r.IsDBNull(2) ? null : r.GetString(2);
                if ((GcdLinks.DateKey(onSale) ?? GcdLinks.DateKey(key)) is int date)
                {
                    result[r.GetInt32(0)] = date;
                }
            }
        }

        return result;
    }

    /// <summary>GCD issue id → the GCD series it belongs to, for the ids the extract knows.</summary>
    public IReadOnlyDictionary<int, int> SeriesOfIssues(IEnumerable<int> issueIds)
    {
        var result = new Dictionary<int, int>();
        foreach (var chunk in issueIds.Distinct().Chunk(500))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT id, series_id FROM issue WHERE id IN ({string.Join(",", chunk)})";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result[r.GetInt32(0)] = r.GetInt32(1);
            }
        }

        return result;
    }

    /// <summary>Every bond with either end in <paramref name="seriesIds"/>.</summary>
    public IReadOnlyList<GcdBond> BondsFor(IEnumerable<int> seriesIds)
    {
        var bonds = new List<GcdBond>();
        foreach (var chunk in seriesIds.Distinct().Chunk(500))
        {
            string ids = string.Join(",", chunk);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT origin_id, target_id, origin_issue_id, target_issue_id, bond_type FROM series_bond WHERE origin_id IN ({ids}) OR target_id IN ({ids})";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                bonds.Add(new GcdBond(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.GetString(4)));
            }
        }

        return bonds.Distinct().ToList();
    }

    private static List<GcdSeries> ReadSeries(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<GcdSeries>();
        while (r.Read())
        {
            list.Add(new GcdSeries(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? 0 : r.GetInt32(6)));
        }

        return list;
    }

    private static List<GcdIssue> ReadIssues(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<GcdIssue>();
        while (r.Read())
        {
            list.Add(new GcdIssue(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return list;
    }

    public void Dispose() => _connection.Dispose();
}
