using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paperbunkr.App.Services;

/// <summary>
/// The Insights screen's own few per-user switches (today: hide the milestones strip on History), kept in <c>insights.json</c> next to the
/// database like <c>keymap.json</c> rather than as <c>AppSettings</c> columns, so a view toggle costs no migration. A missing, unreadable or
/// malformed file reads as defaults and is never fatal; saving is best-effort. Construct with no path for an in-memory instance (tests).
/// </summary>
public sealed class InsightsPreferences
{
    private readonly string? _path;
    private bool _hideMilestones;

    public InsightsPreferences(string? path = null)
    {
        _path = path;
        Load();
    }

    /// <summary>The default location: the database's folder, so a database-path override moves it too.</summary>
    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath()) ?? ".", "insights.json");

    public bool HideMilestones
    {
        get => _hideMilestones;
        set
        {
            if (_hideMilestones == value)
            {
                return;
            }

            _hideMilestones = value;
            Save();
        }
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is JsonObject root && root["hideMilestones"] is JsonValue v && v.TryGetValue<bool>(out bool hide))
            {
                _hideMilestones = hide;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Defaults. The file is a convenience; a bad one must not stop Insights opening.
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, new JsonObject { ["hideMilestones"] = _hideMilestones }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the choice still holds for this session.
        }
    }
}
