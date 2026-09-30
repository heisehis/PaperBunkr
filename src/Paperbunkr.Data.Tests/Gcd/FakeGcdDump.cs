using Microsoft.Data.Sqlite;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.Data.Tests.Gcd;

/// <summary>
/// A tiny SQLite database shaped like the Grand Comics Database dump (only the tables and columns <see cref="GcdExtractor"/> reads),
/// plus a one-call build of the extract from it. Temp files are removed on dispose.
/// </summary>
public sealed class FakeGcdDump : IDisposable
{
    private static readonly string[] BondTypes =
    {
        "minor_name_numbering_continues", "major_name_numbering_continues", "publisher_numbering_continues", "subnumbering_continues",
        "merge_numbering_continues", "merge", "reboot",
    };

    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, int> _publishers = new(StringComparer.Ordinal);

    public FakeGcdDump()
    {
        Directory.CreateDirectory(Folder);
        DumpPath = Path.Combine(Folder, "2026-09-15.db");
        _connection = new SqliteConnection($"Data Source={DumpPath};Pooling=False");
        _connection.Open();
        Exec("""
            CREATE TABLE gcd_publisher (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE stddata_language (id INTEGER PRIMARY KEY, code TEXT);
            CREATE TABLE gcd_series (id INTEGER PRIMARY KEY, name TEXT, year_began INTEGER, year_ended INTEGER, publisher_id INTEGER,
                                     language_id INTEGER, issue_count INTEGER, deleted INTEGER, is_comics_publication INTEGER);
            CREATE TABLE gcd_issue (id INTEGER PRIMARY KEY, number TEXT, series_id INTEGER, key_date TEXT, on_sale_date TEXT,
                                    variant_of_id INTEGER, deleted INTEGER);
            CREATE TABLE gcd_series_bond_type (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE gcd_series_bond (id INTEGER PRIMARY KEY, origin_id INTEGER, target_id INTEGER, origin_issue_id INTEGER,
                                          target_issue_id INTEGER, bond_type_id INTEGER);
            INSERT INTO stddata_language VALUES (1, 'en');
            """);
        for (int i = 0; i < BondTypes.Length; i++)
        {
            Exec($"INSERT INTO gcd_series_bond_type VALUES ({i + 1}, '{BondTypes[i]}')");
        }
    }

    public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"paperbunkr_gcd_test_{Guid.NewGuid():N}");

    public string DumpPath { get; }

    public string OutDir => Path.Combine(Folder, "out");

    public string ExtractPath => Path.Combine(OutDir, GcdExtractor.ExtractFileName);

    public FakeGcdDump Series(int id, string name, int? yearBegan, string? publisher, bool deleted = false, bool isComics = true)
    {
        int? publisherId = null;
        if (publisher is not null && !_publishers.TryGetValue(publisher, out int existing))
        {
            existing = _publishers.Count + 1;
            _publishers[publisher] = existing;
            Run("INSERT INTO gcd_publisher VALUES ($id, $name)", ("$id", existing), ("$name", publisher));
        }

        if (publisher is not null)
        {
            publisherId = _publishers[publisher];
        }

        Run("INSERT INTO gcd_series VALUES ($id, $name, $yb, NULL, $pub, 1, 0, $del, $comics)",
            ("$id", id), ("$name", name), ("$yb", yearBegan), ("$pub", publisherId), ("$del", deleted ? 1 : 0), ("$comics", isComics ? 1 : 0));
        return this;
    }

    public FakeGcdDump Issue(int id, int seriesId, string number, string? keyDate = null, string? onSaleDate = null, int? variantOf = null, bool deleted = false)
    {
        Run("INSERT INTO gcd_issue VALUES ($id, $n, $s, $k, $o, $v, $d)",
            ("$id", id), ("$n", number), ("$s", seriesId), ("$k", keyDate ?? string.Empty), ("$o", onSaleDate ?? string.Empty), ("$v", variantOf), ("$d", deleted ? 1 : 0));
        return this;
    }

    /// <param name="bondType">1-7, GCD's <c>gcd_series_bond_type</c> id (1 minor_name…, 6 merge, 7 reboot).</param>
    public FakeGcdDump Bond(int originId, int targetId, int bondType)
    {
        Run("INSERT INTO gcd_series_bond (origin_id, target_id, origin_issue_id, target_issue_id, bond_type_id) VALUES ($o, $t, NULL, NULL, $b)",
            ("$o", originId), ("$t", targetId), ("$b", bondType));
        return this;
    }

    public GcdManifest BuildExtract(string? url = null)
    {
        _connection.Close();
        var manifest = GcdExtractor.Build(DumpPath, OutDir, url);
        _connection.Open();
        return manifest;
    }

    /// <summary>Builds the extract and opens it.</summary>
    public GcdDataStore Open()
    {
        BuildExtract();
        return GcdDataStore.TryOpen(ExtractPath) ?? throw new InvalidOperationException("extract didn't open");
    }

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Run(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }
}
