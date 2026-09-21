namespace Paperbunkr.Sharing.Tests;

public class ShareSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_settings_{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_FirstUse_CreatesDefaults_WithAStableInstanceId()
    {
        var first = new ShareSettingsStore(SettingsPath).Load();
        var second = new ShareSettingsStore(SettingsPath).Load();

        Assert.False(first.Enabled);
        Assert.Equal(7614, first.Port);
        Assert.Equal(ShareMode.None, first.Scope.Mode);
        Assert.True(Guid.TryParse(first.InstanceId, out _));
        Assert.Equal(first.InstanceId, second.InstanceId);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        var store = new ShareSettingsStore(SettingsPath);
        var settings = store.Load();
        settings.Enabled = true;
        settings.Port = 9000;
        settings.DisplayName = "Living Room";
        settings.PasswordHash = "pbkdf2-sha256$1$AAAA$AAAA";
        settings.Scope = new ShareScope { Mode = ShareMode.Selected, ReadingListIds = { 1, 2 }, CollectionIds = { 3 }, SmartListIds = { 4 } };
        store.Save(settings);

        var back = new ShareSettingsStore(SettingsPath).Load();

        Assert.True(back.Enabled);
        Assert.Equal(9000, back.Port);
        Assert.Equal("Living Room", back.DisplayName);
        Assert.Equal("pbkdf2-sha256$1$AAAA$AAAA", back.PasswordHash);
        Assert.Equal(ShareMode.Selected, back.Scope.Mode);
        Assert.Equal(new[] { 1, 2 }, back.Scope.ReadingListIds);
        Assert.Equal(new[] { 3 }, back.Scope.CollectionIds);
        Assert.Equal(new[] { 4 }, back.Scope.SmartListIds);
        Assert.Equal(settings.InstanceId, back.InstanceId);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults_InsteadOfThrowing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var settings = new ShareSettingsStore(SettingsPath).Load();

        Assert.False(settings.Enabled);
        Assert.False(string.IsNullOrEmpty(settings.InstanceId));
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        var store = new ShareSettingsStore(SettingsPath);
        store.Save(store.Load());

        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void SettingsFile_NeverContainsAPlaintextPassword()
    {
        // The store only ever holds what it is given - guard the contract that callers hand it a hash.
        var store = new ShareSettingsStore(SettingsPath);
        var settings = store.Load();
        settings.PasswordHash = Server.PasswordHasher.Hash("plain-secret-value", 1_000);
        store.Save(settings);

        Assert.DoesNotContain("plain-secret-value", File.ReadAllText(SettingsPath));
    }
}
