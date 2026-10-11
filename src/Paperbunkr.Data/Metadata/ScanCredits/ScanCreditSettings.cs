using System.Text.Json;

namespace Paperbunkr.Data.Metadata.ScanCredits;

/// <summary>
/// The user's own additions to the scanner-credit lists (docs/superpowers/specs/2026-10-11-scan-credits-design.md), kept in <c>scan-credits.json</c> next to the database so a
/// database-path override moves it too (the same place as <c>keymap.json</c>). <see cref="Scanners"/> are extra scan-group names, <see cref="Ignored"/> extra words that mean
/// "not a scanner". A missing or unreadable file is an empty set of additions, never an error.
/// </summary>
public sealed record ScanCreditSettings(IReadOnlyList<string> Scanners, IReadOnlyList<string> Ignored)
{
    public static ScanCreditSettings Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>The default location: the database's folder (honouring a database-path override).</summary>
    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(PaperbunkrDbContext.DatabasePathOverride ?? PaperbunkrDbContext.GetDefaultDatabasePath()) ?? ".", "scan-credits.json");

    public static ScanCreditSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path))
            {
                return Empty;
            }

            var read = JsonSerializer.Deserialize<ScanCreditSettings>(File.ReadAllText(path), JsonOptions);
            return read is null ? Empty : new ScanCreditSettings(Clean(read.Scanners), Clean(read.Ignored));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Empty;
        }
    }

    /// <summary>Writes the lists through a temporary file, so a crash cannot leave a half-written one. Blank entries and repeats are dropped.</summary>
    public static void Save(ScanCreditSettings settings, string? path = null)
    {
        path ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var clean = new ScanCreditSettings(Clean(settings.Scanners), Clean(settings.Ignored));
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(clean, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? entries) =>
        (entries ?? Array.Empty<string>()).Select(e => e?.Trim() ?? string.Empty).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
