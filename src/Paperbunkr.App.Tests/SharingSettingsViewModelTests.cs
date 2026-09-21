using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views.Preferences;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Sharing;
using Paperbunkr.Sharing.Discovery;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Preferences → Sharing (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §6/§9):
/// the view model behind the section - host controls, the two-step add flow (the password is only sent
/// after the user confirms the fingerprint), inline remove/re-trust/relink - driven against a real host
/// with its own database, plus a headless load of the real XAML.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class SharingSettingsViewModelTests : IAsyncLifetime
{
    private const string Password = "hunter2-hunter2";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_sharevm_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _hostDb;
    private readonly DbContextOptions<PaperbunkrDbContext> _clientDb;
    private readonly ActivityService _activity = new(a => a(), _ => { });
    private readonly int _port;
    private ShareHostService _hostSvc = null!;      // the machine being connected TO
    private ShareHostService _localHost = null!;    // this machine's own sharing settings (the VM under test controls this one)
    private RemoteLibraryService _remote = null!;
    private SharingSettingsViewModel _vm = null!;

    // The view models marshal to the UI thread through an injectable "post". These tests run their awaits on pool
    // threads, so posted work is queued and flushed explicitly - which also lets a test prove a deferral really is one.
    private readonly Queue<Action> _posted = new();
    private void Post(Action a) { lock (_posted) _posted.Enqueue(a); }
    private void Flush()
    {
        while (true)
        {
            Action? next;
            lock (_posted) next = _posted.Count > 0 ? _posted.Dequeue() : null;
            if (next is null) return;
            next();
        }
    }

    public SharingSettingsViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _hostDb = Options("host.db");
        _clientDb = Options("client.db");

        using (var context = new PaperbunkrDbContext(_hostDb))
        {
            context.Database.EnsureCreated();
            var series = new Series { Name = "Saga" };
            context.Series.Add(series);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", Title = "One", FilePath = CbzFixture.Create(Path.Combine(_root, "saga1.cbz"), 2) });
            context.SaveChanges();
        }

        using (var context = new PaperbunkrDbContext(_clientDb))
        {
            context.Database.EnsureCreated();
            context.ReadingLists.Add(new ReadingList { Name = "Favorites" });
            context.Collections.Add(new Collection { Name = "Crossovers" });
            var issueRoot = new SmartListConditionGroup { Mode = SmartListGroupMode.And };
            context.SmartLists.Add(new SmartList { Name = "Image books", RootGroup = issueRoot, TargetKind = SmartListTargetKind.Issue });
            context.SmartLists.Add(new SmartList { Name = "Long series", RootGroup = new SmartListConditionGroup(), TargetKind = SmartListTargetKind.Series });
            context.SaveChanges();
        }

        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public async Task InitializeAsync()
    {
        var hostStore = new ShareSettingsStore(Path.Combine(_root, "otherhost", "settings.json"));
        var hs = hostStore.Load();
        hs.Port = _port; hs.DisplayName = "Den PC"; hs.Enabled = true; hs.Scope = new ShareScope { Mode = ShareMode.All };
        hostStore.Save(hs);
        _hostSvc = new ShareHostService(hostStore, () => new PaperbunkrDbContext(_hostDb), new ActivityService(a => a(), _ => { }), Path.Combine(_root, "otherhost"));
        _hostSvc.SetPassword(Password);
        await _hostSvc.StartAsync();

        var localStore = new ShareSettingsStore(Path.Combine(_root, "local", "settings.json"));
        _localHost = new ShareHostService(localStore, () => new PaperbunkrDbContext(_clientDb), _activity, Path.Combine(_root, "local"),
            o => o.Port = 0);
        _remote = new RemoteLibraryService(inc => new PaperbunkrDbContext(_clientDb) { IncludeRemote = inc }, _activity);
        _vm = new SharingSettingsViewModel(_localHost, _remote, () => new PaperbunkrDbContext(_clientDb), Post);
    }

    public async Task DisposeAsync()
    {
        _vm.Dispose();
        _remote.Dispose();
        await _localHost.DisposeAsync();
        await _hostSvc.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DbContextOptions<PaperbunkrDbContext> Options(string file) =>
        new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, file)}").Options;

    private async Task AddLibraryAsync()
    {
        _vm.AddHost = "127.0.0.1";
        _vm.AddPortText = _port.ToString();
        _vm.AddPassword = Password;
        await _vm.ConnectCommand.ExecuteAsync(null);
        await _vm.TrustAndAddCommand.ExecuteAsync(null);
        Flush();
    }

    // ---------------------------------------------------------------- host side

    [Fact]
    public void Load_ReflectsSettings_AndOffersOnlyShareableLists()
    {
        Assert.False(_vm.ShareEnabled);
        Assert.True(_vm.ScopeNothing);
        Assert.Equal("7614", _vm.PortText);
        Assert.Equal(new[] { "Favorites", "Crossovers", "Image books" }, _vm.ListChoices.Select(c => c.Name).ToArray());
        Assert.DoesNotContain(_vm.ListChoices, c => c.Name == "Long series");   // series-target smart lists aren't shareable in v1
        Assert.Equal(new[] { "Reading list", "Collection", "Smart list" }, _vm.ListChoices.Select(c => c.KindLabel).ToArray());
    }

    [Fact]
    public void ScopeRadios_AreMutuallyExclusive_AndDriveTheListPicker()
    {
        _vm.ScopeSelected = true;
        Assert.True(_vm.ScopeSelected);
        Assert.False(_vm.ScopeAll);
        Assert.False(_vm.ScopeNothing);

        _vm.ScopeAll = true;
        Assert.True(_vm.ScopeAll);
        Assert.False(_vm.ScopeSelected);
        Assert.Equal(ShareMode.All, _vm.ScopeMode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("80")]
    [InlineData("70000")]
    [InlineData("")]
    public async Task Apply_RejectsAnInvalidPort_AndChangesNothing(string port)
    {
        _vm.PortText = port;

        await _vm.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("port", _vm.HostMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(_localHost.IsRunning);
        Assert.Equal(7614, _localHost.Settings.Port);
    }

    [Fact]
    public async Task Apply_SavesTheScopeWithTheSelectedListIds()
    {
        _vm.ScopeSelected = true;
        _vm.ListChoices.Single(c => c.Name == "Favorites").IsSelected = true;
        _vm.ListChoices.Single(c => c.Name == "Image books").IsSelected = true;
        _vm.DisplayName = "  Living Room  ";
        _vm.PortText = "9123";

        await _vm.ApplyCommand.ExecuteAsync(null);

        var saved = new ShareSettingsStore(Path.Combine(_root, "local", "settings.json")).Load();
        Assert.Equal(ShareMode.Selected, saved.Scope.Mode);
        Assert.Single(saved.Scope.ReadingListIds);
        Assert.Empty(saved.Scope.CollectionIds);
        Assert.Single(saved.Scope.SmartListIds);
        Assert.Equal("Living Room", saved.DisplayName);
        Assert.Equal(9123, saved.Port);
    }

    [Fact]
    public void SetPassword_RequiresEightCharacters_AndNeverKeepsThePlaintext()
    {
        _vm.NewPassword = "short";
        _vm.SetPasswordCommand.Execute(null);
        Assert.Contains("8", _vm.HostMessage);
        Assert.False(_vm.HasPassword);

        _vm.NewPassword = "long-enough-pass";
        _vm.SetPasswordCommand.Execute(null);

        Assert.True(_vm.HasPassword);
        Assert.Equal("", _vm.NewPassword);                        // cleared from the UI
        Assert.DoesNotContain("long-enough-pass", File.ReadAllText(Path.Combine(_root, "local", "settings.json")));
    }

    [Fact]
    public async Task TurningSharingOnWithoutAPassword_ExplainsInsteadOfStarting()
    {
        _vm.ShareEnabled = true;
        await WaitUntilAsync(() => _localHost.LastError is not null);

        Assert.False(_localHost.IsRunning);
        Assert.Contains("password", _vm.HostStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TurningSharingOnWithAPassword_Starts_AndReportsIt()
    {
        _vm.NewPassword = "long-enough-pass";
        _vm.SetPasswordCommand.Execute(null);

        _vm.ShareEnabled = true;
        await WaitUntilAsync(() => _localHost.IsRunning);
        Flush();

        Assert.True(_vm.IsHostRunning);
        Assert.StartsWith("Sharing on port", _vm.HostStatus);
    }

    [Fact]
    public async Task RegeneratingTheCertificate_NeedsAConfirmation_ThenChangesTheFingerprint()
    {
        string before = _vm.CertificateFingerprint;
        _vm.AskRegenerateCommand.Execute(null);
        Assert.True(_vm.IsConfirmingRegenerate);
        Assert.Equal(before, _vm.CertificateFingerprint);          // asking changes nothing

        _vm.CancelRegenerateCommand.Execute(null);
        Assert.False(_vm.IsConfirmingRegenerate);

        _vm.AskRegenerateCommand.Execute(null);
        await _vm.ConfirmRegenerateCommand.ExecuteAsync(null);

        Assert.NotEqual(before, _vm.CertificateFingerprint);
        Assert.False(_vm.IsConfirmingRegenerate);
    }

    // ---------------------------------------------------------------- add flow

    [Fact]
    public async Task Connect_ValidatesTheFieldsBeforeTouchingTheNetwork()
    {
        await _vm.ConnectCommand.ExecuteAsync(null);
        Assert.Contains("address", _vm.AddError!, StringComparison.OrdinalIgnoreCase);

        _vm.AddHost = "127.0.0.1"; _vm.AddPortText = "nope";
        await _vm.ConnectCommand.ExecuteAsync(null);
        Assert.Contains("port", _vm.AddError!, StringComparison.OrdinalIgnoreCase);

        _vm.AddPortText = _port.ToString(); _vm.AddPassword = "";
        await _vm.ConnectCommand.ExecuteAsync(null);
        Assert.Contains("password", _vm.AddError!, StringComparison.OrdinalIgnoreCase);
        Assert.False(_vm.HasPendingProbe);
    }

    [Fact]
    public async Task Connect_ShowsTheHostAndItsFingerprint_WithoutSendingThePassword()
    {
        _vm.AddHost = "127.0.0.1"; _vm.AddPortText = _port.ToString(); _vm.AddPassword = "definitely-wrong-password";

        await _vm.ConnectCommand.ExecuteAsync(null);

        // Even a wrong password gets this far: step 1 is only "who are you", and it never sends the password.
        Assert.True(_vm.HasPendingProbe);
        Assert.Equal("Den PC", _vm.PendingName);
        Assert.Equal(RemoteSourceRowViewModel.FormatFingerprint(_hostSvc.CertificateFingerprint), _vm.PendingFingerprint);
        Assert.Empty(_remote.List());                              // nothing saved yet
        Assert.Equal(0, _hostSvc.ConnectedClients);                // and no session was ever opened
    }

    [Fact]
    public async Task TrustAndAdd_WithAWrongPassword_ReportsIt_AndSavesNothing()
    {
        _vm.AddHost = "127.0.0.1"; _vm.AddPortText = _port.ToString(); _vm.AddPassword = "definitely-wrong-password";
        await _vm.ConnectCommand.ExecuteAsync(null);

        await _vm.TrustAndAddCommand.ExecuteAsync(null);

        Assert.Contains("rejected", _vm.AddError!, StringComparison.OrdinalIgnoreCase);
        Assert.False(_vm.HasPendingProbe);
        Assert.Empty(_remote.List());
        Assert.Empty(_vm.Sources);
    }

    [Fact]
    public async Task TrustAndAdd_Succeeds_ClosesThePanel_AndListsTheLibrary()
    {
        _vm.OpenAddPanelCommand.Execute(null);

        await AddLibraryAsync();

        Assert.False(_vm.IsAddPanelOpen);
        Assert.False(_vm.HasPendingProbe);
        Assert.Equal("", _vm.AddPassword);                          // never lingers in the UI
        var row = Assert.Single(_vm.Sources);
        Assert.Equal("Den PC", row.Name);
        Assert.Equal($"127.0.0.1:{_port}", row.Address);
        Assert.Equal("Online", row.StatusText);
        Assert.True(row.IsStatusOk);
        Assert.True(_vm.HasSources);
        Assert.False(_vm.HasNoSources);
    }

    [Fact]
    public async Task CancelAdd_DropsEverythingPending()
    {
        _vm.AddHost = "127.0.0.1"; _vm.AddPortText = _port.ToString(); _vm.AddPassword = Password;
        await _vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(_vm.HasPendingProbe);

        _vm.CancelAddCommand.Execute(null);

        Assert.False(_vm.HasPendingProbe);
        Assert.False(_vm.IsAddPanelOpen);
        Assert.Equal("", _vm.AddPassword);
        await _vm.TrustAndAddCommand.ExecuteAsync(null);            // a stale click can't add anything
        Assert.Empty(_remote.List());
    }

    // ---------------------------------------------------------------- rows

    [Fact]
    public async Task Remove_NeedsAConfirmation_AndThenDeletesTheMirror_NotTheLocalLibrary()
    {
        await AddLibraryAsync();
        var row = _vm.Sources.Single();

        row.AskRemoveCommand.Execute(null);
        Assert.True(row.IsConfirmingRemove);
        Assert.False(row.ShowNormalActions);
        Assert.Single(_remote.List());                              // asking removed nothing

        row.CancelRemoveCommand.Execute(null);
        Assert.Single(_remote.List());

        row.AskRemoveCommand.Execute(null);
        row.ConfirmRemoveCommand.Execute(null);
        Assert.Single(_remote.List());                              // deferred one dispatcher tick, never inline
        Flush();

        Assert.Empty(_remote.List());
        Assert.Empty(_vm.Sources);
        Assert.True(_vm.HasNoSources);
        using var context = new PaperbunkrDbContext(_clientDb) { IncludeRemote = true };
        Assert.Empty(context.Issues.Where(i => i.RemoteSourceId != null));
    }

    [Fact]
    public async Task AChangedCertificate_ShowsOldAndNew_AndOnlyAnExplicitTrustReconnects()
    {
        await AddLibraryAsync();
        var row = _vm.Sources.Single();
        string oldFingerprint = _hostSvc.CertificateFingerprint;
        await _hostSvc.RegenerateCertificateAsync();

        await row.SyncNowCommand.ExecuteAsync(null);
        Flush();

        Assert.True(row.IsCertificateChanged);
        Assert.True(row.IsStatusError);
        Assert.Equal("Certificate changed", row.StatusText);        // text says it, not just colour
        Assert.Equal(RemoteSourceRowViewModel.FormatFingerprint(oldFingerprint), row.TrustedFingerprint);
        Assert.Equal(RemoteSourceRowViewModel.FormatFingerprint(_hostSvc.CertificateFingerprint), row.NewFingerprint);
        Assert.NotEqual(row.TrustedFingerprint, row.NewFingerprint);

        await row.TrustNewCertificateCommand.ExecuteAsync(null);
        Flush();

        Assert.False(row.IsCertificateChanged);
        Assert.Equal("Online", row.StatusText);
    }

    [Fact]
    public async Task ARotatedPassword_AsksForTheNewOne_InlineOnTheRow()
    {
        await AddLibraryAsync();
        var row = _vm.Sources.Single();
        _hostSvc.SetPassword("a-brand-new-password");
        await _hostSvc.SaveAndApplyAsync();

        await row.SyncNowCommand.ExecuteAsync(null);
        Flush();
        Assert.True(row.NeedsPassword);
        Assert.Equal("Password needed", row.StatusText);

        row.RowPassword = "a-brand-new-password";
        await row.SubmitPasswordCommand.ExecuteAsync(null);
        Flush();

        Assert.False(row.NeedsPassword);
        Assert.Equal("Online", row.StatusText);
        Assert.Equal("", row.RowPassword);
    }

    [Fact]
    public async Task HostChanged_OffersRelink_WhichShowsTheNewFingerprint_AndKeepsProgress()
    {
        await AddLibraryAsync();
        var row = _vm.Sources.Single();
        using (var context = new PaperbunkrDbContext(_clientDb) { IncludeRemote = true })
        {
            var issue = context.Issues.Single(i => i.RemoteSourceId != null);
            issue.LastPageRead = 11;
            context.SaveChanges();
        }
        _hostSvc.Settings.InstanceId = Guid.NewGuid().ToString();
        await _hostSvc.SaveAndApplyAsync();
        await row.SyncNowCommand.ExecuteAsync(null);
        Flush();
        Assert.True(row.IsHostChanged);

        await row.StartRelinkCommand.ExecuteAsync(null);
        Assert.True(row.IsRelinking);
        Assert.Equal("Den PC", row.RelinkHostName);
        Assert.Equal(RemoteSourceRowViewModel.FormatFingerprint(_hostSvc.CertificateFingerprint), row.RelinkFingerprint);

        row.RowPassword = "";
        await row.ConfirmRelinkCommand.ExecuteAsync(null);
        Assert.Contains("password", row.RelinkError!, StringComparison.OrdinalIgnoreCase);   // password required, explained inline

        row.RowPassword = Password;
        await row.ConfirmRelinkCommand.ExecuteAsync(null);
        Flush();

        Assert.False(row.IsRelinking);
        Assert.Null(row.RelinkError);
        using var check = new PaperbunkrDbContext(_clientDb) { IncludeRemote = true };
        Assert.Equal(11, check.Issues.Single(i => i.RemoteSourceId != null).LastPageRead);
    }

    [Fact]
    public async Task AddAsNew_FromAHostChangedRow_PrefillsTheAddress()
    {
        await AddLibraryAsync();
        var row = _vm.Sources.Single();
        _hostSvc.Settings.InstanceId = Guid.NewGuid().ToString();
        await _hostSvc.SaveAndApplyAsync();
        await row.SyncNowCommand.ExecuteAsync(null);
        Flush();

        row.AddAsNewCommand.Execute(null);

        Assert.True(_vm.IsAddPanelOpen);
        Assert.Equal("127.0.0.1", _vm.AddHost);
        Assert.Equal(_port.ToString(), _vm.AddPortText);
        Assert.Equal("", _vm.AddPassword);
    }

    [Fact]
    public void ParsePublicNetworks_PicksOnlyPublicOnes_IgnoringCaseAndDuplicates()
    {
        string output = string.Join(Environment.NewLine, new[] { "Home WiFi|Private", "Cafe Guest|Public", "Corp|DomainAuthenticated", "cafe guest|public", "", "Bad line without a bar", "|Public" });

        Assert.Equal(new[] { "Cafe Guest" }, NetworkProfileProbe.ParsePublicNetworks(output));
        Assert.Empty(NetworkProfileProbe.ParsePublicNetworks(null));
        Assert.Empty(NetworkProfileProbe.ParsePublicNetworks("Home|Private"));
    }

    [Fact]
    public void TheWarning_NamesTheNetwork_AndIsAbsentWhenThereIsNone()
    {
        Assert.Null(NetworkProfileProbe.WarningFor(Array.Empty<string>()));
        Assert.Contains("Cafe Guest", NetworkProfileProbe.WarningFor(new[] { "Cafe Guest" }));
        Assert.Contains("these networks", NetworkProfileProbe.WarningFor(new[] { "A", "B" }));
    }

    [Fact]
    public async Task AWarning_ShowsForAPublicNetwork_ButNeverStopsSharingStarting()
    {
        var vm = new SharingSettingsViewModel(_localHost, _remote, () => new PaperbunkrDbContext(_clientDb), Post,
            publicNetworks: () => Task.FromResult<IReadOnlyList<string>>(new[] { "Airport WiFi" }));
        vm.NewPassword = "long-enough-pass";
        vm.SetPasswordCommand.Execute(null);

        vm.ShareEnabled = true;
        await WaitUntilAsync(() => _localHost.IsRunning && vm.HasNetworkWarning);

        Assert.True(_localHost.IsRunning);                       // a warning, not a block
        Assert.Contains("Airport WiFi", vm.NetworkWarning);
        vm.Dispose();
    }

    private SharingSettingsViewModel VmWithBrowse(params DiscoveredHost[] found) =>
        new(_localHost, _remote, () => new PaperbunkrDbContext(_clientDb), Post, _ => Task.FromResult<IReadOnlyList<DiscoveredHost>>(found));

    [Fact]
    public async Task FindOnNetwork_ListsOtherHosts_NeverThisComputer_AndNeverOnesAlreadyAdded()
    {
        await AddLibraryAsync();                                                     // "Den PC" is now a saved library
        var vm = VmWithBrowse(
            new DiscoveredHost(_hostSvc.Settings.InstanceId, "Den PC", 1, "127.0.0.1", _port),              // already added
            new DiscoveredHost(_localHost.Settings.InstanceId, "This machine", 1, "127.0.0.1", 7614),       // ourselves
            new DiscoveredHost("someone-else", "Living Room", 1, "192.168.1.30", 7614));

        await vm.FindOnNetworkCommand.ExecuteAsync(null);

        var only = Assert.Single(vm.DiscoveredHosts);
        Assert.Equal("Living Room", only.Name);
        Assert.Equal("192.168.1.30:7614", only.Address);
        Assert.True(vm.HasDiscoveredHosts);
        Assert.False(vm.IsSearching);
    }

    [Fact]
    public async Task FindOnNetwork_WithNothingFound_SaysSo_AndManualEntryStillWorks()
    {
        var vm = VmWithBrowse();

        await vm.FindOnNetworkCommand.ExecuteAsync(null);

        Assert.Empty(vm.DiscoveredHosts);
        Assert.Contains("type an address", vm.DiscoveryMessage);
        Assert.True(vm.HasDiscoveryMessage);
    }

    [Fact]
    public async Task UsingADiscoveredHost_OnlyPrefillsTheAddress_NothingIsTrustedOrSent()
    {
        var vm = VmWithBrowse(new DiscoveredHost("h1", "Living Room", 1, "192.168.1.30", 9000));
        await vm.FindOnNetworkCommand.ExecuteAsync(null);

        vm.DiscoveredHosts.Single().UseCommand.Execute(null);

        Assert.Equal("192.168.1.30", vm.AddHost);
        Assert.Equal("9000", vm.AddPortText);
        Assert.False(vm.HasPendingProbe);                    // discovery grants nothing: the fingerprint prompt still comes first
        Assert.Empty(_remote.List());
    }

    [Fact]
    public async Task TheHost_AdvertisesWhileSharing_AndStopsWhenSharingStops()
    {
        Assert.True(_hostSvc.IsRunning);
        // Whether multicast works here is environmental; what must hold is that advertising never breaks sharing.
        await _hostSvc.StopAsync();
        Assert.False(_hostSvc.IsAdvertising);
        await _hostSvc.StartAsync();
        Assert.True(_hostSvc.IsRunning);
    }

    [Theory]
    [InlineData("ABCDEF0123456789", "ABCD EF01 2345 6789")]
    [InlineData("abcdef01", "ABCD EF01")]
    [InlineData("ABCDE", "ABCD E")]
    [InlineData("", "")]
    public void FormatFingerprint_GroupsByFourInUppercase(string raw, string expected)
    {
        Assert.Equal(expected, RemoteSourceRowViewModel.FormatFingerprint(raw));
    }

    // ---------------------------------------------------------------- the real XAML

    [Fact]
    public void TheSection_LoadsAndBinds_AndTheScopeRadioRevealsTheListPicker()
    {
        // Synchronous on purpose: the view needs the UI thread, which awaiting the network would leave.
        using (var context = new PaperbunkrDbContext(_clientDb))
        {
            context.RemoteSources.Add(new RemoteSource { InstanceId = "seeded", DisplayName = "Den PC", Host = "192.168.1.20", Port = 7614, CertFingerprint = new string('A', 64) });
            context.SaveChanges();
        }
        _vm.Load();
        var view = new SharingSection { DataContext = _vm };
        var window = new Window { Width = 900, Height = 1400, Content = view };
        // The headless test app loads no theme; without one the templated controls (ToggleSwitch, ...) can't render.
        window.Styles.Add(new FluentAvalonia.Styling.FluentAvaloniaTheme());

        window.Show();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);

        // Loaded and bound without throwing, and the controls the flows depend on exist.
        Assert.Single(_vm.Sources);
        Assert.NotNull(view.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault(t => Avalonia.Automation.AutomationProperties.GetAutomationId(t) == "SharingEnabledToggle"));
        Assert.NotNull(view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "SharingApplyButton"));
        Assert.NotNull(view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "SharingAddRemoteButton"));
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("SHARE THIS LIBRARY", texts);
        Assert.Contains("REMOTE LIBRARIES", texts);
        Assert.Contains("Share this library", texts);

        // Bindings are live: the "no lists yet" hint sits in the panel the scope radio reveals.
        TextBlock hint = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Nothing to choose from yet") == true);
        Assert.False(hint.IsEffectivelyVisible);
        _vm.ScopeSelected = true;
        window.UpdateLayout();
        Assert.True(((Avalonia.Controls.Control)hint.Parent!).IsVisible);

        // Radios are one group; the three scope choices are all present.
        var radios = view.GetVisualDescendants().OfType<RadioButton>().Where(r => r.GroupName == "ShareScope").ToList();
        Assert.Equal(3, radios.Count);
        Assert.Single(radios, r => r.IsChecked == true);

        window.Close();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }
}
