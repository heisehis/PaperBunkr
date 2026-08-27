using System.Text.RegularExpressions;
using Avalonia.Logging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Forwards Avalonia's own platform/rendering log events (GPU init failures, "falling back to
/// software", D3D/ANGLE/WGL context errors) into the existing <c>startup.log</c> via
/// <see cref="DiagnosticsService.LogMilestone"/>, so the backend that actually won - and any
/// silent fallback - is visible after the fact (docs/superpowers/specs/2026-08-27-hardware-
/// accelerated-rendering-design.md §5). Warning-and-above only, and only for the three areas
/// where graphics-stack messages land.
/// </summary>
public sealed class DiagnosticsLogSink : ILogSink
{
    private static readonly string[] Areas = { LogArea.Platform, LogArea.Win32Platform, LogArea.Visual };

    private static readonly Regex TemplateHole = new(@"\{@?\w+\}", RegexOptions.Compiled);

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && System.Array.IndexOf(Areas, area) >= 0;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Emit(level, area, messageTemplate, null);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Emit(level, area, messageTemplate, propertyValues);

    private static void Emit(LogEventLevel level, string area, string messageTemplate, object?[]? propertyValues)
    {
        if (!(level >= LogEventLevel.Warning && System.Array.IndexOf(Areas, area) >= 0))
        {
            return;
        }

        try
        {
            string message = Format(messageTemplate, propertyValues);
            DiagnosticsService.LogMilestone($"[render] {area}/{level}: {message}");
        }
        catch
        {
            // Diagnostics must never throw into the code path they observe.
        }
    }

    /// <summary>
    /// Substitutes Avalonia's <c>{Named}</c> template holes with the positional property values,
    /// left to right. Not a faithful structured-logging renderer - these messages are rare and
    /// diagnostic, so approximate substitution is enough.
    /// </summary>
    private static string Format(string template, object?[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return template;
        }

        int index = 0;
        return TemplateHole.Replace(template, match =>
            index < values.Length ? values[index++]?.ToString() ?? "null" : match.Value);
    }
}
