using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="FileWriteBackCoordinator"/>'s per-file "settled" signal the organizer waits on
/// (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md Part 2).
/// </summary>
[Collection("FileWriteBackCoordinator")]
public class FileWriteBackSettleTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pb_settle_{Guid.NewGuid():N}.cbz");

    public void Dispose() => FileWriteBackCoordinator.Reset();

    [Fact]
    public async Task A_file_nobody_is_writing_is_settled_at_once()
    {
        var wait = FileWriteBackCoordinator.WaitForSettledAsync(_path, CancellationToken.None);
        Assert.True(wait.IsCompleted);
        await wait;
    }

    [Fact]
    public async Task The_wait_ends_when_the_write_finishes()
    {
        var handle = FileWriteBackCoordinator.BeginWrite(_path);
        var wait = FileWriteBackCoordinator.WaitForSettledAsync(_path, CancellationToken.None);
        await Task.Delay(50);
        Assert.False(wait.IsCompleted);
        Assert.True(FileWriteBackCoordinator.IsWriting(_path));

        handle.Dispose();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(FileWriteBackCoordinator.IsWriting(_path));
    }

    [Fact]
    public async Task Overlapping_writes_settle_only_when_the_last_one_ends()
    {
        var first = FileWriteBackCoordinator.BeginWrite(_path);
        var second = FileWriteBackCoordinator.BeginWrite(_path);
        var wait = FileWriteBackCoordinator.WaitForSettledAsync(_path, CancellationToken.None);

        first.Dispose();
        await Task.Delay(50);
        Assert.False(wait.IsCompleted);

        second.Dispose();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_stuck_write_does_not_hold_the_organizer_forever()
    {
        var saved = FileWriteBackCoordinator.SettleTimeout;
        FileWriteBackCoordinator.SettleTimeout = TimeSpan.FromMilliseconds(100);
        try
        {
            using var _ = FileWriteBackCoordinator.BeginWrite(_path);
            await FileWriteBackCoordinator.WaitForSettledAsync(_path, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            FileWriteBackCoordinator.SettleTimeout = saved;
        }
    }
}
