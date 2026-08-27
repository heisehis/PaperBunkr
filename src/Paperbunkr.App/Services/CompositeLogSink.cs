using System.Collections.Generic;
using System.Linq;
using Avalonia.Logging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Fans every Avalonia log event out to several <see cref="ILogSink"/>s. <see cref="Logger.Sink"/>
/// is single-valued and <c>.LogToTrace()</c> claims it, so this is how trace logging and
/// <see cref="DiagnosticsLogSink"/> capture coexist (docs/superpowers/specs/2026-08-27-hardware-
/// accelerated-rendering-design.md §5). Each inner is called inside its own try/catch so one
/// faulting sink can't suppress the others.
/// </summary>
public sealed class CompositeLogSink : ILogSink
{
    private readonly ILogSink[] _sinks;

    public CompositeLogSink(params ILogSink?[] sinks) =>
        _sinks = sinks.Where(s => s is not null).Cast<ILogSink>().ToArray();

    /// <summary>
    /// Installs a <see cref="CompositeLogSink"/> of the current <see cref="Logger.Sink"/> plus a
    /// fresh <see cref="DiagnosticsLogSink"/>, unless one is already installed. Idempotent, so it
    /// is safe to call from more than one startup hook.
    /// </summary>
    public static void EnsureRenderCaptureInstalled()
    {
        if (Logger.Sink is not CompositeLogSink)
        {
            Logger.Sink = new CompositeLogSink(Logger.Sink, new DiagnosticsLogSink());
        }
    }

    public bool IsEnabled(LogEventLevel level, string area)
    {
        foreach (var sink in _sinks)
        {
            if (SafeIsEnabled(sink, level, area))
            {
                return true;
            }
        }

        return false;
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Log(level, area, source, messageTemplate);
            }
            catch
            {
                // A faulting sink must not stop the others, and logging must never throw into
                // the code path it observes.
            }
        }
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Log(level, area, source, messageTemplate, propertyValues);
            }
            catch
            {
                // See above.
            }
        }
    }

    private static bool SafeIsEnabled(ILogSink sink, LogEventLevel level, string area)
    {
        try
        {
            return sink.IsEnabled(level, area);
        }
        catch
        {
            return false;
        }
    }

    internal IReadOnlyList<ILogSink> Sinks => _sinks;
}
