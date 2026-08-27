using System;
using System.IO;
using Avalonia.Logging;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DiagnosticsLogSink"/> (docs/superpowers/specs/2026-08-27-hardware-
/// accelerated-rendering-design.md §5/§8). Joins <see cref="AvaloniaTestCollection"/> to
/// serialize against other tests that mutate <c>DiagnosticsService.LogDirectoryOverride</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DiagnosticsLogSinkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DiagnosticsLogSink _sink = new();

    public DiagnosticsLogSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "paperbunkr_rendersink_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        DiagnosticsService.LogDirectoryOverride = _tempDir;
    }

    public void Dispose()
    {
        DiagnosticsService.LogDirectoryOverride = null;
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    private string StartupLog() =>
        File.Exists(Path.Combine(_tempDir, "startup.log"))
            ? File.ReadAllText(Path.Combine(_tempDir, "startup.log"))
            : string.Empty;

    [Theory]
    [InlineData(LogEventLevel.Warning, LogArea.Platform, true)]
    [InlineData(LogEventLevel.Error, LogArea.Win32Platform, true)]
    [InlineData(LogEventLevel.Fatal, LogArea.Visual, true)]
    [InlineData(LogEventLevel.Information, LogArea.Platform, false)]
    [InlineData(LogEventLevel.Debug, LogArea.Visual, false)]
    [InlineData(LogEventLevel.Warning, LogArea.Binding, false)]
    [InlineData(LogEventLevel.Error, LogArea.Layout, false)]
    public void IsEnabled_OnlyForRenderAreasAtWarningOrAbove(LogEventLevel level, string area, bool expected)
    {
        Assert.Equal(expected, _sink.IsEnabled(level, area));
    }

    [Fact]
    public void Log_RenderWarning_WritesRenderPrefixedMilestone()
    {
        _sink.Log(LogEventLevel.Warning, LogArea.Platform, null,
            "Unable to establish {Api} rendering, falling back to software", "OpenGL");

        string log = StartupLog();
        Assert.Contains("[render] Platform/Warning:", log);
        Assert.Contains("falling back to software", log);
        Assert.Contains("OpenGL", log);
    }

    [Fact]
    public void Log_NonRenderEvent_WritesNothing()
    {
        _sink.Log(LogEventLevel.Warning, LogArea.Binding, null, "some binding warning");

        Assert.DoesNotContain("[render]", StartupLog());
    }

    [Fact]
    public void Log_MalformedTemplate_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _sink.Log(LogEventLevel.Error, LogArea.Visual, null, "unterminated {hole", "x"));

        Assert.Null(ex);
    }
}
