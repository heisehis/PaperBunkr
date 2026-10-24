using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>The pure parts of the Connections status model (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md): the resolver, the age wording and the check cache.</summary>
public class ConnectionStatusTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pb_connstatus_{Guid.NewGuid():N}", "connection-status.json");

    public void Dispose()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Resolve_NotConnected_IsNotSetUp_EvenWithAFailedCheck()
    {
        var failed = ConnectionCheck.Failed(ConnectionFailure.Auth, Now, "x");
        Assert.Equal(ConnectionStatus.NotSetUp, ConnectionStatusResolver.Resolve(false, failed, true));
    }

    [Fact]
    public void Resolve_FailedCheck_SplitsAuthFromUnreachable()
    {
        Assert.Equal(ConnectionStatus.Expired, ConnectionStatusResolver.Resolve(true, ConnectionCheck.Failed(ConnectionFailure.Auth, Now, null), false));
        Assert.Equal(ConnectionStatus.Unreachable, ConnectionStatusResolver.Resolve(true, ConnectionCheck.Failed(ConnectionFailure.Unreachable, Now, null), false));
    }

    [Fact]
    public void Resolve_FailedWithNoReason_IsTreatedAsUnreachable()
        => Assert.Equal(ConnectionFailure.Unreachable, ConnectionCheck.Failed(ConnectionFailure.None, Now, null).Failure);

    [Fact]
    public void Resolve_ConnectedAndSyncing_IsSyncing_OtherwiseConnected()
    {
        Assert.Equal(ConnectionStatus.Syncing, ConnectionStatusResolver.Resolve(true, null, true));
        Assert.Equal(ConnectionStatus.Connected, ConnectionStatusResolver.Resolve(true, null, false));
        Assert.Equal(ConnectionStatus.Connected, ConnectionStatusResolver.Resolve(true, ConnectionCheck.Success(Now), false));
    }

    [Fact]
    public void Resolve_AFailedCheckBeatsSyncing()
        => Assert.Equal(ConnectionStatus.Unreachable, ConnectionStatusResolver.Resolve(true, ConnectionCheck.Failed(ConnectionFailure.Unreachable, Now, null), true));

    [Theory]
    [InlineData(null, "never")]
    [InlineData(0, "just now")]
    [InlineData(12, "12 min ago")]
    [InlineData(60 * 3, "3 h ago")]
    [InlineData(60 * 24 * 2, "2 d ago")]
    public void AgeText_ReadsNaturally(int? minutesAgo, string expected)
    {
        DateTimeOffset? at = minutesAgo is { } m ? Now.AddMinutes(-m) : null;
        Assert.Equal(expected, ConnectionStatusResolver.AgeText(at, Now));
    }

    [Fact]
    public void Store_RoundTripsAndKeepsOtherProviders()
    {
        var store = new ConnectionStatusStore(_path);
        store.Record("Prowlarr", ConnectionCheck.Failed(ConnectionFailure.Unreachable, Now, "Connection refused"));
        store.Record("qBittorrent", ConnectionCheck.Success(Now, "OK"));

        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.False(loaded["Prowlarr"].Ok);
        Assert.Equal(ConnectionFailure.Unreachable, loaded["Prowlarr"].Failure);
        Assert.Equal("Connection refused", loaded["Prowlarr"].Detail);
        Assert.True(loaded["qBittorrent"].Ok);
        Assert.Equal(Now, loaded["qBittorrent"].CheckedUtc);
    }

    [Fact]
    public void Store_Remove_ForgetsOneProviderOnly()
    {
        var store = new ConnectionStatusStore(_path);
        store.Record("Prowlarr", ConnectionCheck.Success(Now));
        store.Record("qBittorrent", ConnectionCheck.Success(Now));

        store.Remove("Prowlarr");

        Assert.Equal(new[] { "qBittorrent" }, store.Load().Keys);
    }

    [Fact]
    public void Store_MissingFile_IsEmpty()
        => Assert.Empty(new ConnectionStatusStore(_path).Load());

    [Fact]
    public void Store_UnreadableFile_IsTreatedAsEmpty_NotACrash()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not json");

        var store = new ConnectionStatusStore(_path);

        Assert.Empty(store.Load());
        store.Record("Prowlarr", ConnectionCheck.Success(Now));      // and it recovers on the next write
        Assert.Single(store.Load());
    }

    [Fact]
    public void Store_NewerVersionFile_IsIgnored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{\"version\":99,\"checks\":{\"Prowlarr\":{\"ok\":true,\"failure\":\"None\",\"checkedUtc\":\"2026-10-12T12:00:00+00:00\"}}}");

        Assert.Empty(new ConnectionStatusStore(_path).Load());
    }

    [Fact]
    public void Store_WriteLeavesNoTempFileBehind()
    {
        var store = new ConnectionStatusStore(_path);
        store.Record("Prowlarr", ConnectionCheck.Success(Now));

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }
}
