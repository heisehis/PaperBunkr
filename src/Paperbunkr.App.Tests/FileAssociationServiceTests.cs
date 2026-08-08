using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="FileAssociationService"/> (docs/superpowers/specs/
/// 2026-08-07-preferences-advanced-tab-design.md §2) against an in-memory fake
/// <see cref="IShellFileAssociation"/> - never touches the real Windows registry.
/// </summary>
public class FileAssociationServiceTests
{
    [Fact]
    public void GetAvailableFormats_ListsRegisteredFormats_NoneAssociatedInitially()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        var formats = service.GetAvailableFormats();

        Assert.NotEmpty(formats);
        Assert.All(formats, f => Assert.False(f.IsAssociated));
    }

    [Fact]
    public void SetAssociated_True_RegistersEveryExtensionInThatFormat_AndRefreshesShell()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);
        var format = service.GetAvailableFormats().First();

        service.SetAssociated(format.Name, true);

        var refreshed = service.GetAvailableFormats().First(f => f.Name == format.Name);
        Assert.True(refreshed.IsAssociated);
        Assert.True(fake.RefreshCalled);
    }

    [Fact]
    public void SetAssociated_FalseAfterTrue_UnregistersEveryExtension()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);
        var format = service.GetAvailableFormats().First();

        service.SetAssociated(format.Name, true);
        service.SetAssociated(format.Name, false);

        var refreshed = service.GetAvailableFormats().First(f => f.Name == format.Name);
        Assert.False(refreshed.IsAssociated);
    }

    [Fact]
    public void SetAssociated_UnknownFormatName_DoesNothing_NoException()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetAssociated("Not A Real Format", true);

        Assert.False(fake.RefreshCalled);
    }

    private sealed class FakeShellFileAssociation : IShellFileAssociation
    {
        private readonly HashSet<(string TypeId, string Extension)> _registered = new();

        public bool RefreshCalled { get; private set; }

        public bool IsRegistered(string typeId, string extension) => _registered.Contains((typeId, extension));

        public void Register(string typeId, string extension, string displayName, string appPath) => _registered.Add((typeId, extension));

        public void Unregister(string typeId, string extension) => _registered.Remove((typeId, extension));

        public void RefreshShell() => RefreshCalled = true;
    }
}
