using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Paperbunkr.Data.Calibre;

/// <summary>One format file of a Calibre book: <c>data.format</c> (EPUB, PDF...) and <c>data.name</c> (the file name without extension).</summary>
public sealed record CalibreFormat(string Format, string FileName);

/// <summary>A Calibre book as stored in <c>metadata.db</c> (docs/superpowers/specs/2026-10-27-books-slice4a-calibre-import-design.md §2).</summary>
public sealed record CalibreBook(
    int Id,
    string Title,
    IReadOnlyList<string> Authors,
    string? Series,
    float? SeriesIndex,
    IReadOnlyList<string> Tags,
    string? Publisher,
    DateTime? PubDate,
    string? Language,
    string? Comments,
    string? Isbn,
    int Rating10,
    string RelativePath,
    bool HasCover,
    IReadOnlyList<CalibreFormat> Formats,
    bool? IsRead);

public sealed record CalibreCustomColumn(int Id, string Label, string Name, string DataType);

/// <summary>
/// Reads a Calibre library's <c>metadata.db</c> without ever writing to it: opened read-only, falling back to a temporary copy when Calibre has
/// the database locked. Everything is read in a handful of bulk queries (one per table) and joined in memory.
/// </summary>
public sealed class CalibreLibraryReader : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string? _tempCopy;

    public string LibraryRoot { get; }

    /// <summary><c>library_id.uuid</c>, or the folder path when the table is missing.</summary>
    public string LibraryId { get; }

    private CalibreLibraryReader(string root, SqliteConnection connection, string? tempCopy)
    {
        LibraryRoot = root;
        _connection = connection;
        _tempCopy = tempCopy;
        LibraryId = Scalar("select uuid from library_id limit 1") ?? root;
    }

    public static bool IsCalibreLibrary(string folder) => File.Exists(Path.Combine(folder, "metadata.db"));

    public static CalibreLibraryReader Open(string libraryRoot)
    {
        string db = Path.Combine(libraryRoot, "metadata.db");
        if (!File.Exists(db))
        {
            throw new FileNotFoundException("Not a Calibre library: metadata.db was not found.", db);
        }

        try
        {
            return OpenPath(libraryRoot, db, tempCopy: null);
        }
        catch (SqliteException)
        {
            // Locked or otherwise unreadable in place: read a private copy instead.
            string copy = Path.Combine(Path.GetTempPath(), $"paperbunkr_calibre_{Guid.NewGuid():N}.db");
            using (var source = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dest = new FileStream(copy, FileMode.Create, FileAccess.Write))
            {
                source.CopyTo(dest);
            }

            return OpenPath(libraryRoot, copy, tempCopy: copy);
        }
    }

    private static CalibreLibraryReader OpenPath(string root, string dbPath, string? tempCopy)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            using (var probe = connection.CreateCommand())
            {
                // Fails here (not later) when Calibre has the database locked, so Open can fall back to a copy.
                probe.CommandText = "select count(*) from books";
                probe.ExecuteScalar();
            }

            return new CalibreLibraryReader(root, connection, tempCopy);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public IReadOnlyList<CalibreCustomColumn> CustomColumns()
    {
        var list = new List<CalibreCustomColumn>();
        if (!TableExists("custom_columns"))
        {
            return list;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "select id, label, name, datatype from custom_columns where mark_for_delete = 0 order by id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new CalibreCustomColumn(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return list;
    }

    /// <param name="readStatusColumnLabel">The label of a bool or text custom column to read as "has been read", or null.</param>
    public IReadOnlyList<CalibreBook> ReadBooks(string? readStatusColumnLabel = null)
    {
        var authors = Multi("select l.book, a.name from books_authors_link l join authors a on a.id = l.author order by l.id");
        var tags = Multi("select l.book, t.name from books_tags_link l join tags t on t.id = l.tag order by l.id");
        var series = Single("select l.book, s.name from books_series_link l join series s on s.id = l.series");
        var publishers = Single("select l.book, p.name from books_publishers_link l join publishers p on p.id = l.publisher");
        var languages = Multi("select l.book, g.lang_code from books_languages_link l join languages g on g.id = l.lang_code order by l.item_order");
        var comments = Single("select book, text from comments");
        var isbns = Single("select book, val from identifiers where lower(type) = 'isbn'");
        var ratings = Single("select l.book, cast(r.rating as text) from books_ratings_link l join ratings r on r.id = l.rating");
        var formats = new Dictionary<int, List<CalibreFormat>>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "select book, format, name from data order by id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int book = reader.GetInt32(0);
                if (!formats.TryGetValue(book, out var list))
                {
                    formats[book] = list = new List<CalibreFormat>();
                }

                list.Add(new CalibreFormat(reader.GetString(1), reader.GetString(2)));
            }
        }

        var read = ReadStatus(readStatusColumnLabel);

        var books = new List<CalibreBook>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "select id, title, series_index, pubdate, path, has_cover from books order by id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                string? firstSeries = series.GetValueOrDefault(id);
                books.Add(new CalibreBook(
                    id,
                    reader.GetString(1),
                    authors.GetValueOrDefault(id) ?? new List<string>(),
                    firstSeries,
                    firstSeries is null || reader.IsDBNull(2) ? null : (float)reader.GetDouble(2),
                    tags.GetValueOrDefault(id) ?? new List<string>(),
                    publishers.GetValueOrDefault(id),
                    ParsePubDate(reader.IsDBNull(3) ? null : reader.GetValue(3).ToString()),
                    languages.GetValueOrDefault(id)?.FirstOrDefault(),
                    comments.GetValueOrDefault(id),
                    isbns.GetValueOrDefault(id),
                    int.TryParse(ratings.GetValueOrDefault(id), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : 0,
                    reader.GetString(4),
                    !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                    formats.GetValueOrDefault(id) ?? new List<CalibreFormat>(),
                    read?.GetValueOrDefault(id)));
            }
        }

        return books;
    }

    /// <summary>Calibre stores "no date" as year 101 or 0101; anything before 1000 is treated as unknown.</summary>
    internal static DateTime? ParsePubDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 10)
        {
            return null;
        }

        return DateTime.TryParseExact(raw[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year >= 1000
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    private Dictionary<int, bool>? ReadStatus(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var column = CustomColumns().FirstOrDefault(c => string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase));
        if (column is null || column.DataType is not ("bool" or "text" or "enumeration"))
        {
            return null;
        }

        var result = new Dictionary<int, bool>();
        using var command = _connection.CreateCommand();
        command.CommandText = $"select book, value from custom_column_{column.Id.ToString(CultureInfo.InvariantCulture)}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string value = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty;
            result[reader.GetInt32(0)] = value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("read", StringComparison.OrdinalIgnoreCase)
                || value.Equals("finished", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private bool TableExists(string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "select count(*) from sqlite_master where type = 'table' and name = $name";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private string? Scalar(string sql)
    {
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private Dictionary<int, List<string>> Multi(string sql)
    {
        var map = new Dictionary<int, List<string>>();
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1))
            {
                continue;
            }

            int book = reader.GetInt32(0);
            if (!map.TryGetValue(book, out var list))
            {
                map[book] = list = new List<string>();
            }

            list.Add(reader.GetString(1));
        }

        return map;
    }

    private Dictionary<int, string> Single(string sql)
    {
        var map = new Dictionary<int, string>();
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(1) && !map.ContainsKey(reader.GetInt32(0)))
            {
                map[reader.GetInt32(0)] = reader.GetString(1);
            }
        }

        return map;
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (_tempCopy is not null)
        {
            try
            {
                File.Delete(_tempCopy);
            }
            catch (IOException)
            {
            }
        }
    }
}
