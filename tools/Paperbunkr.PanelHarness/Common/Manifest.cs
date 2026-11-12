using System.Text;

namespace Paperbunkr.PanelHarness.Common;

/// <summary>One sampled page, as <c>tools/panel-training/sample_pages.py</c> writes it to <c>manifest.csv</c>.</summary>
internal sealed record ManifestRow(string Id, string Split, string ContentType, string Direction, bool Tall)
{
    public bool RightToLeft => Direction.Equals("rtl", StringComparison.OrdinalIgnoreCase);

    /// <summary>The scoring group: Western (Comic and Unknown), Manga, or Webtoon (Manhwa and Manhua).</summary>
    public string Group => ContentType switch
    {
        "Manga" => "Manga",
        "Manhwa" or "Manhua" => "Webtoon",
        _ => "Western",
    };
}

/// <summary>Reads and rewrites <c>manifest.csv</c> (RFC 4180 quoting, as Python's csv module writes it). Unknown columns are kept as they are.</summary>
internal sealed class Manifest
{
    private readonly List<string> _header;
    private readonly List<List<string>> _rows;

    private Manifest(List<string> header, List<List<string>> rows)
    {
        _header = header;
        _rows = rows;
    }

    /// <summary>Reads a text file even while another program (Excel, Notepad) has it open.</summary>
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static Manifest Load(string path)
    {
        var lines = ParseCsv(ReadShared(path));
        if (lines.Count == 0)
        {
            throw new InvalidDataException($"{path} is empty");
        }

        var header = lines[0];
        foreach (var needed in new[] { "id", "split", "content_type", "direction", "tall" })
        {
            if (!header.Contains(needed))
            {
                throw new InvalidDataException($"{path} has no '{needed}' column");
            }
        }

        return new Manifest(header, lines.Skip(1).Where(r => r.Count == header.Count).ToList());
    }

    public Dictionary<string, ManifestRow> Rows()
    {
        int id = _header.IndexOf("id"), split = _header.IndexOf("split"), type = _header.IndexOf("content_type"),
            dir = _header.IndexOf("direction"), tall = _header.IndexOf("tall");
        return _rows.ToDictionary(
            r => r[id],
            r => new ManifestRow(r[id], r[split], r[type], r[dir], r[tall].Equals("true", StringComparison.OrdinalIgnoreCase) || r[tall] == "1"),
            StringComparer.OrdinalIgnoreCase);
    }

    public void SetDirection(string id, string direction)
    {
        int idCol = _header.IndexOf("id"), dirCol = _header.IndexOf("direction");
        foreach (var r in _rows.Where(r => r[idCol].Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            r[dirCol] = direction;
        }
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        foreach (var r in _rows.Prepend(_header))
        {
            sb.Append(string.Join(',', r.Select(Quote))).Append("\r\n");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string Quote(string s) => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || row[0].Length > 0)
                {
                    rows.Add(row);
                }

                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
