using System;
using System.IO;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BootstrapSentinel"/> (docs/superpowers/specs/2026-09-10-bootstrap-crash-
/// sentinel-safe-mode-design.md §10) - pure state-file read/write/clear, no Avalonia bootstrap.
/// Redirects the state path to a temp file so nothing touches the real <c>%AppData%</c>.
/// </summary>
public class BootstrapSentinelTests : IDisposable
{
    private static readonly Version V031 = new(0, 3, 1, 0);
    private static readonly Version V032 = new(0, 3, 2, 0);

    private readonly string _statePath;

    public BootstrapSentinelTests()
    {
        _statePath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bootstrap_test_{Guid.NewGuid():N}.json");
        BootstrapSentinel.StatePathOverride = _statePath;
    }

    public void Dispose()
    {
        BootstrapSentinel.StatePathOverride = null;
        try
        {
            if (File.Exists(_statePath)) File.Delete(_statePath);
        }
        catch (IOException)
        {
        }
    }

    private void Write(string json) => File.WriteAllText(_statePath, json);

    [Fact]
    public void Evaluate_NoFile_IsNormal()
    {
        Assert.Equal(BootstrapDecision.Normal, BootstrapSentinel.Evaluate(V031));
    }

    [Fact]
    public void Evaluate_CorruptJson_IsNormal()
    {
        Write("{ this is not json");
        Assert.Equal(BootstrapDecision.Normal, BootstrapSentinel.Evaluate(V031));
    }

    [Fact]
    public void Evaluate_VersionMismatch_IsNormal_RegardlessOfAttempts()
    {
        Write("""{ "attempts": 5, "appVersion": "0.3.0.0" }""");
        Assert.Equal(BootstrapDecision.Normal, BootstrapSentinel.Evaluate(V031));
    }

    [Fact]
    public void Evaluate_OneAttempt_SameVersion_IsSafeMode()
    {
        Write("""{ "attempts": 1, "appVersion": "0.3.1.0" }""");
        Assert.Equal(BootstrapDecision.SafeMode, BootstrapSentinel.Evaluate(V031));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(9)]
    public void Evaluate_TwoOrMoreAttempts_SameVersion_IsGiveUp(int attempts)
    {
        Write($$"""{ "attempts": {{attempts}}, "appVersion": "0.3.1.0" }""");
        Assert.Equal(BootstrapDecision.GiveUp, BootstrapSentinel.Evaluate(V031));
    }

    [Fact]
    public void Record_NoFile_WritesFirstAttempt()
    {
        BootstrapSentinel.Record(V031);

        // The first Record is a Normal-path attempt: attempts=1, so the next Evaluate (if the
        // process crashed before Clear) is SafeMode, not GiveUp.
        Assert.Equal(BootstrapDecision.SafeMode, BootstrapSentinel.Evaluate(V031));
    }

    [Fact]
    public void Record_Twice_SameVersion_ReachesGiveUp()
    {
        BootstrapSentinel.Record(V031);
        BootstrapSentinel.Record(V031);

        Assert.Equal(BootstrapDecision.GiveUp, BootstrapSentinel.Evaluate(V031));
    }

    [Fact]
    public void Record_OnVersionMismatchedFile_ResetsToFirstAttempt()
    {
        Write("""{ "attempts": 4, "appVersion": "0.3.1.0" }""");

        BootstrapSentinel.Record(V032);

        Assert.Equal(BootstrapDecision.SafeMode, BootstrapSentinel.Evaluate(V032));
    }

    [Fact]
    public void Clear_DeletesFile_AndIsSafeWhenAbsent()
    {
        BootstrapSentinel.Record(V031);
        BootstrapSentinel.Clear();

        Assert.False(File.Exists(_statePath));
        Assert.Equal(BootstrapDecision.Normal, BootstrapSentinel.Evaluate(V031));

        BootstrapSentinel.Clear(); // no throw on a missing file
    }

    [Fact]
    public void ResetAfterGiveUp_DeletesFile()
    {
        BootstrapSentinel.Record(V031);
        BootstrapSentinel.Record(V031);

        BootstrapSentinel.ResetAfterGiveUp();

        Assert.Equal(BootstrapDecision.Normal, BootstrapSentinel.Evaluate(V031));
    }

    [Fact]
    public void Record_UnwritablePath_DoesNotThrow()
    {
        // A path whose parent is a file, not a directory - Directory.CreateDirectory / WriteAllText
        // both fail. Record must swallow it.
        string parentFile = Path.Combine(Path.GetTempPath(), $"paperbunkr_bootstrap_block_{Guid.NewGuid():N}");
        File.WriteAllText(parentFile, "x");
        try
        {
            BootstrapSentinel.StatePathOverride = Path.Combine(parentFile, "bootstrap.state");

            BootstrapSentinel.Record(V031);   // no throw
            BootstrapSentinel.Clear();        // no throw
            Assert.Equal(BootstrapDecision.Normal, BootstrapSentinel.Evaluate(V031));
        }
        finally
        {
            BootstrapSentinel.StatePathOverride = _statePath;
            try { File.Delete(parentFile); } catch (IOException) { }
        }
    }
}
