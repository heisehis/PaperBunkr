using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Gcd;

/// <summary>
/// Turns a Grand Comics Database SQLite dump into Paperbunkr's compact extract (docs/superpowers/specs/2026-09-27-gcd-data-design.md
/// §1): comic-publication series (with a normalized <c>name_key</c> for matching), non-variant issues with their key and on-sale
/// dates, every series bond, and a <c>meta</c> table carrying the dump date and GCD's licence and credit. Run by the repo tool
/// <c>tools/Paperbunkr.GcdExtract</c> - never by the app.
/// </summary>
public static class GcdExtractor
{
    public const int SchemaVersion = 1;

    public const string ExtractFileName = "gcd.sqlite";

    private static readonly Regex DumpDateName = new(@"(?<d>\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);

    /// <summary>Builds <c>gcd.sqlite</c>, <c>gcd-{dumpDate}.zip</c> and <c>gcd-data.json</c> in <paramref name="outDir"/>.</summary>
    /// <param name="url">The release-asset URL the manifest should point at; a placeholder is written when null.</param>
    public static GcdManifest Build(string dumpPath, string outDir, string? url = null, string? dumpDate = null, Action<string>? log = null)
    {
        Directory.CreateDirectory(outDir);
        dumpDate ??= DumpDateName.Match(Path.GetFileName(dumpPath)) is { Success: true } m
            ? m.Groups["d"].Value
            : File.GetLastWriteTimeUtc(dumpPath).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        string extractPath = Path.Combine(outDir, ExtractFileName);
        if (File.Exists(extractPath))
        {
            File.Delete(extractPath);
        }

        using (var c = new SqliteConnection($"Data Source={extractPath};Pooling=False"))
        {
            c.Open();
            Exec(c, $"ATTACH DATABASE '{dumpPath.Replace("'", "''", StringComparison.Ordinal)}' AS gcd");
            Exec(c, """
                CREATE TABLE series (id INTEGER PRIMARY KEY, name TEXT NOT NULL, name_key TEXT NOT NULL, year_began INTEGER, year_ended INTEGER,
                                     publisher TEXT, language TEXT, issue_count INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE issue (id INTEGER PRIMARY KEY, series_id INTEGER NOT NULL, number TEXT NOT NULL, key_date TEXT, on_sale_date TEXT);
                CREATE TABLE series_bond (origin_id INTEGER NOT NULL, target_id INTEGER NOT NULL, origin_issue_id INTEGER, target_issue_id INTEGER, bond_type TEXT NOT NULL);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                """);

            log?.Invoke("Copying series…");
            using (var tx = c.BeginTransaction())
            {
                using var insert = c.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO series VALUES ($id, $name, $key, $yb, $ye, $pub, $lang, $count)";
                var p = new[] { "$id", "$name", "$key", "$yb", "$ye", "$pub", "$lang", "$count" }.Select(n => insert.Parameters.Add(new SqliteParameter(n, null))).ToArray();

                using var read = c.CreateCommand();
                read.Transaction = tx;
                read.CommandText = """
                    SELECT s.id, s.name, s.year_began, s.year_ended, p.name, l.code, s.issue_count
                    FROM gcd.gcd_series s
                    LEFT JOIN gcd.gcd_publisher p ON p.id = s.publisher_id
                    LEFT JOIN gcd.stddata_language l ON l.id = s.language_id
                    WHERE s.deleted = 0 AND s.is_comics_publication = 1
                    """;
                using var r = read.ExecuteReader();
                while (r.Read())
                {
                    string name = r.GetString(1);
                    p[0].Value = r.GetInt64(0);
                    p[1].Value = name;
                    p[2].Value = TitleNormalizer.StripDown(name).ToLowerInvariant();
                    p[3].Value = r.IsDBNull(2) ? DBNull.Value : r.GetValue(2);
                    p[4].Value = r.IsDBNull(3) ? DBNull.Value : r.GetValue(3);
                    p[5].Value = r.IsDBNull(4) ? DBNull.Value : r.GetValue(4);
                    p[6].Value = r.IsDBNull(5) ? DBNull.Value : r.GetValue(5);
                    p[7].Value = r.IsDBNull(6) ? 0 : r.GetValue(6);
                    insert.ExecuteNonQuery();
                }

                tx.Commit();
            }

            log?.Invoke("Copying issues…");
            Exec(c, """
                INSERT INTO issue
                SELECT i.id, i.series_id, i.number, NULLIF(i.key_date, ''), NULLIF(i.on_sale_date, '')
                FROM gcd.gcd_issue i JOIN series s ON s.id = i.series_id
                WHERE i.deleted = 0 AND i.variant_of_id IS NULL
                """);

            log?.Invoke("Copying series bonds…");
            Exec(c, """
                INSERT INTO series_bond
                SELECT b.origin_id, b.target_id, b.origin_issue_id, b.target_issue_id, t.name
                FROM gcd.gcd_series_bond b JOIN gcd.gcd_series_bond_type t ON t.id = b.bond_type_id
                """);

            Exec(c, "DETACH DATABASE gcd");
            using (var meta = c.CreateCommand())
            {
                meta.CommandText = "INSERT INTO meta VALUES ($k, $v)";
                var k = meta.Parameters.Add(new SqliteParameter("$k", null));
                var v = meta.Parameters.Add(new SqliteParameter("$v", null));
                foreach (var (key, value) in new[]
                         {
                             ("dump_date", dumpDate),
                             ("built_at", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                             ("schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                             ("licence", GcdLinks.Licence),
                             ("licence_url", GcdLinks.LicenceUrl),
                             ("attribution", GcdLinks.Attribution),
                             ("notice", "Contains data from the Grand Comics Database, licensed CC BY-SA 4.0. This extract is itself CC BY-SA 4.0."),
                         })
                {
                    k.Value = key;
                    v.Value = value;
                    meta.ExecuteNonQuery();
                }
            }

            log?.Invoke("Indexing…");
            Exec(c, """
                CREATE INDEX ix_series_key ON series(name_key);
                CREATE INDEX ix_issue_series ON issue(series_id);
                CREATE INDEX ix_bond_origin ON series_bond(origin_id);
                CREATE INDEX ix_bond_target ON series_bond(target_id);
                """);
            Exec(c, "VACUUM");
        }

        string zipPath = Path.Combine(outDir, $"gcd-{dumpDate}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(extractPath, ExtractFileName, CompressionLevel.SmallestSize);
        }

        string sha;
        using (var stream = File.OpenRead(zipPath))
        {
            sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        var manifest = new GcdManifest(dumpDate, url ?? $"https://github.com/heisehis/paperbunkr-gcd-data/releases/download/gcd-{dumpDate}/gcd-{dumpDate}.zip",
            sha, new FileInfo(zipPath).Length, SchemaVersion);
        File.WriteAllText(Path.Combine(outDir, "gcd-data.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return manifest;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        cmd.ExecuteNonQuery();
    }
}
