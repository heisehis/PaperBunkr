using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.App.Tests.Gcd;

/// <summary>
/// A tiny Grand Comics Database extract (the <c>gcd.sqlite</c> schema <see cref="GcdExtractor"/> writes), its zip and manifest, and an
/// HTTP handler that serves them - so the installer and the Related tab can be tested with no network.
/// </summary>
internal sealed class GcdTestExtract : IDisposable
{
    public GcdTestExtract(string dumpDate = "2026-09-15", int schemaVersion = GcdExtractor.SchemaVersion)
    {
        Directory.CreateDirectory(Folder);
        DumpDate = dumpDate;
        using (var c = new SqliteConnection($"Data Source={ExtractPath};Pooling=False"))
        {
            c.Open();
            Exec(c, """
                CREATE TABLE series (id INTEGER PRIMARY KEY, name TEXT NOT NULL, name_key TEXT NOT NULL, year_began INTEGER, year_ended INTEGER,
                                     publisher TEXT, language TEXT, issue_count INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE issue (id INTEGER PRIMARY KEY, series_id INTEGER NOT NULL, number TEXT NOT NULL, key_date TEXT, on_sale_date TEXT);
                CREATE TABLE series_bond (origin_id INTEGER NOT NULL, target_id INTEGER NOT NULL, origin_issue_id INTEGER, target_issue_id INTEGER, bond_type TEXT NOT NULL);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                """);
            Exec(c, $"INSERT INTO meta VALUES ('dump_date', '{dumpDate}'), ('schema_version', '{schemaVersion}')");
        }
    }

    public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"paperbunkr_gcd_app_test_{Guid.NewGuid():N}");

    public string DumpDate { get; }

    public string ExtractPath => Path.Combine(Folder, GcdExtractor.ExtractFileName);

    public GcdTestExtract Series(int id, string name, int? yearBegan)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO series (id, name, name_key, year_began, publisher, language) VALUES ($id, $n, $k, $y, 'Marvel', 'en')";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$k", name.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$y", (object?)yearBegan ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return this;
    }

    public GcdTestExtract Bond(int originId, int targetId, string bondType)
    {
        using var c = Open();
        Exec(c, $"INSERT INTO series_bond VALUES ({originId}, {targetId}, NULL, NULL, '{bondType}')");
        return this;
    }

    /// <summary>Zips the extract and returns the zip's bytes plus a manifest describing them (<paramref name="corruptHash"/> lies about the hash).</summary>
    public (byte[] Zip, GcdManifest Manifest) Package(bool corruptHash = false)
    {
        SqliteConnection.ClearAllPools();
        string zipPath = Path.Combine(Folder, "gcd.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(ExtractPath, GcdExtractor.ExtractFileName);
        }

        byte[] bytes = File.ReadAllBytes(zipPath);
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (corruptHash)
        {
            sha = new string('0', 64);
        }

        return (bytes, new GcdManifest(DumpDate, "https://example.test/gcd.zip", sha, bytes.Length, GcdExtractor.SchemaVersion));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={ExtractPath};Pooling=False");
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Serves fixed responses by URL; anything else is a 404. Records what was asked for.</summary>
internal sealed class FakeGcdHttp : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public List<string> Requests { get; } = new();

    public FakeGcdHttp Manifest(GcdManifest manifest, string url = "https://example.test/gcd-data.json")
    {
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(manifest)) };
        return this;
    }

    public FakeGcdHttp File(string url, byte[] bytes)
    {
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.ToString();
        Requests.Add(url);
        return Task.FromResult(_routes.TryGetValue(url, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
