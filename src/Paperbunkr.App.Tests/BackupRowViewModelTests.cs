using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BackupRowViewModel"/> (docs/superpowers/specs/2026-09-08-advanced-backup-
/// file-association-redesign-design.md). First test coverage for this class - it had none before
/// this phase.
/// </summary>
public class BackupRowViewModelTests : IDisposable
{
    private readonly string _filePath;

    public BackupRowViewModelTests()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"paperbunkr_backup_row_test_{Guid.NewGuid():N}.db");
        File.WriteAllBytes(_filePath, new byte[1024]);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        catch (IOException)
        {
        }
    }

    private BackupRowViewModel CreateRow(Action<BackupRowViewModel>? onRestore = null) =>
        new(_filePath, onRestore ?? (_ => { }));

    [Fact]
    public void DisplayDate_ReflectsFileLastWriteTime()
    {
        var expected = new DateTime(2026, 9, 7, 20, 39, 0);
        File.SetLastWriteTime(_filePath, expected);

        var row = CreateRow();

        Assert.Equal(expected.ToString("MMM d, yyyy — h:mm tt"), row.DisplayDate);
    }

    [Fact]
    public void DisplaySize_ReflectsFileLength()
    {
        // 1024 bytes (from the fixture) rounds to "0 MB" - the >0-bytes rounding case, not the
        // <=0 zero-guard branch.
        var row = CreateRow();

        Assert.Equal("0 MB", row.DisplaySize);
    }

    [Fact]
    public void DisplaySize_LargerFile_FormatsAsMegabytes()
    {
        File.WriteAllBytes(_filePath, new byte[5 * 1024 * 1024]);

        var row = CreateRow();

        Assert.Equal("5 MB", row.DisplaySize);
    }

    [Fact]
    public void IsArmed_FalseInitially_TrueAfterFirstRestoreClick_FalseAfterConfirm()
    {
        BackupRowViewModel? confirmedRow = null;
        var row = CreateRow(r => confirmedRow = r);

        Assert.False(row.IsArmed);
        Assert.Equal("Restore", row.RestoreLabel);

        row.RestoreCommand.Execute(null);

        Assert.True(row.IsArmed);
        Assert.Equal("Confirm restore?", row.RestoreLabel);
        Assert.Null(confirmedRow);

        row.RestoreCommand.Execute(null);

        Assert.False(row.IsArmed);
        Assert.Equal("Restore", row.RestoreLabel);
        Assert.Same(row, confirmedRow);
    }
}
