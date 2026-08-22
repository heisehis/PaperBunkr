using System;
using System.IO;
using System.Linq;
using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers the crash-capture/milestone-logging infrastructure added after a live session where a
/// silent startup failure produced zero durable evidence (see DiagnosticsService's own doc
/// comment). Uses <see cref="AvaloniaTestCollection"/> for the same reason as any test mutating
/// shared static override state (<see cref="PreferencesScreenViewModelTests"/> precedent) - not
/// because this touches Avalonia/Skia itself.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DiagnosticsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DiagnosticsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "paperbunkr_diagnostics_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        DiagnosticsService.LogDirectoryOverride = _tempDir;
    }

    public void Dispose()
    {
        DiagnosticsService.LogDirectoryOverride = null;
        DiagnosticsService.MaxCrashLogsRetainedOverride = null;
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void LogMilestone_AppendsTimestampedLineToStartupLog()
    {
        DiagnosticsService.LogMilestone("First checkpoint.");
        DiagnosticsService.LogMilestone("Second checkpoint.");

        string content = File.ReadAllText(Path.Combine(_tempDir, "startup.log"));

        Assert.Contains("First checkpoint.", content);
        Assert.Contains("Second checkpoint.", content);
        Assert.True(content.IndexOf("First checkpoint.", StringComparison.Ordinal)
            < content.IndexOf("Second checkpoint.", StringComparison.Ordinal));
    }

    [Fact]
    public void LogCrash_WritesReportWithExceptionDetailsAndInnerExceptionChain()
    {
        Exception inner = new InvalidOperationException("inner failure");
        Exception outer = new Exception("outer failure", inner);

        DiagnosticsService.LogCrash("Test context", outer, isTerminating: true);

        string[] crashFiles = Directory.GetFiles(_tempDir, "crash-*.log");
        Assert.Single(crashFiles);

        string report = File.ReadAllText(crashFiles[0]);
        Assert.Contains("Test context", report);
        Assert.Contains("Terminating  : True", report);
        Assert.Contains("outer failure", report);
        Assert.Contains("inner failure", report);
        Assert.Contains(nameof(InvalidOperationException), report);
    }

    [Fact]
    public void LogCrash_NullException_StillWritesReportWithoutThrowing()
    {
        DiagnosticsService.LogCrash("No exception object available", null, isTerminating: false);

        Assert.Single(Directory.GetFiles(_tempDir, "crash-*.log"));
    }

    [Fact]
    public void LogCrash_AppendsPointerLineToStartupLog()
    {
        DiagnosticsService.LogCrash("Correlated context", new Exception("boom"), isTerminating: true);

        string startupLog = File.ReadAllText(Path.Combine(_tempDir, "startup.log"));
        Assert.Contains("CRASH [Correlated context]", startupLog);
    }

    [Fact]
    public void LogCrash_PrunesOldestReportsBeyondRetentionLimit()
    {
        DiagnosticsService.MaxCrashLogsRetainedOverride = 2;

        for (int i = 0; i < 4; i++)
        {
            DiagnosticsService.LogCrash($"Crash {i}", new Exception($"boom {i}"), isTerminating: false);
            System.Threading.Thread.Sleep(10); // ensure distinct CreationTimeUtc ordering
        }

        string[] remaining = Directory.GetFiles(_tempDir, "crash-*.log");
        Assert.Equal(2, remaining.Length);
    }
}
