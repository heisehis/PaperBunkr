using System;
using System.Collections.Generic;
using Avalonia.Logging;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CompositeLogSink"/> (docs/superpowers/specs/2026-08-27-hardware-
/// accelerated-rendering-design.md §5/§8).
/// </summary>
public class CompositeLogSinkTests
{
    private sealed class RecordingSink : ILogSink
    {
        public List<string> Messages { get; } = new();
        public bool Enabled { get; set; } = true;
        public bool ThrowOnLog { get; set; }

        public bool IsEnabled(LogEventLevel level, string area) => Enabled;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (ThrowOnLog) throw new InvalidOperationException("boom");
            Messages.Add(messageTemplate);
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
            => Log(level, area, source, messageTemplate);
    }

    [Fact]
    public void Log_FansOutToEveryInnerSink()
    {
        var a = new RecordingSink();
        var b = new RecordingSink();
        var composite = new CompositeLogSink(a, b);

        composite.Log(LogEventLevel.Warning, LogArea.Visual, null, "hello");

        Assert.Equal(new[] { "hello" }, a.Messages);
        Assert.Equal(new[] { "hello" }, b.Messages);
    }

    [Fact]
    public void IsEnabled_IsLogicalOrAcrossInners()
    {
        var a = new RecordingSink { Enabled = false };
        var b = new RecordingSink { Enabled = true };
        var composite = new CompositeLogSink(a, b);

        Assert.True(composite.IsEnabled(LogEventLevel.Debug, LogArea.Binding));

        b.Enabled = false;
        Assert.False(composite.IsEnabled(LogEventLevel.Debug, LogArea.Binding));
    }

    [Fact]
    public void Log_OneInnerThrowing_DoesNotStopOthersOrEscape()
    {
        var bad = new RecordingSink { ThrowOnLog = true };
        var good = new RecordingSink();
        var composite = new CompositeLogSink(bad, good);

        composite.Log(LogEventLevel.Error, LogArea.Platform, null, "still logged");

        Assert.Equal(new[] { "still logged" }, good.Messages);
    }

    [Fact]
    public void Constructor_FiltersNullSinks()
    {
        var real = new RecordingSink();
        var composite = new CompositeLogSink(null, real, null);

        Assert.Single(composite.Sinks);
        composite.Log(LogEventLevel.Warning, LogArea.Visual, null, "ok");
        Assert.Equal(new[] { "ok" }, real.Messages);
    }
}
