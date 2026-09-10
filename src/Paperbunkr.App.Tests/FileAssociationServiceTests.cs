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

    // --- Comic-format scoping (docs/superpowers/specs/2026-09-09-installer-redesign-design.md
    //     decision 5, + the 2026-09-09 .cbt-parity correction). Drives the installer's per-format
    //     [Tasks] and Program.cs's --register-file-associations CLI path. ---

    [Fact]
    public void ComicAssociationExtensions_IsExactlyTheSevenComicSpecificExtensions()
    {
        Assert.Equal(
            new[] { ".pdf", ".cbz", ".cbr", ".cb7", ".cbt", ".cbw", ".djvu" }.OrderBy(x => x),
            FileAssociationService.ComicAssociationExtensions.OrderBy(x => x));
    }

    [Fact]
    public void SetComicAssociationsFor_SingleComicExtension_RegistersOnlyThatExtension()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".cbz" }, true);

        Assert.Contains(".cbz", fake.RegisteredExtensions);
        Assert.DoesNotContain(".zip", fake.RegisteredExtensions);
        Assert.DoesNotContain(".rar", fake.RegisteredExtensions);
        Assert.DoesNotContain(".7z", fake.RegisteredExtensions);
        Assert.DoesNotContain(".cbr", fake.RegisteredExtensions);
        Assert.DoesNotContain(".pdf", fake.RegisteredExtensions);
        Assert.True(fake.RefreshCalled);
    }

    [Fact]
    public void SetComicAssociationsFor_Cbr_CoversBothTheRarAndRar5ProviderRegistrations_NotBareRar()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".cbr" }, true);

        // Both "eComic (RAR)" and "eComic (RAR5)" claim .cbr - one task, both registered.
        Assert.Contains(".cbr", fake.RegisteredExtensions);
        var cbrTypeIds = fake.RegistrationsFor(".cbr").ToList();
        Assert.Equal(2, cbrTypeIds.Count);
        Assert.DoesNotContain(".rar", fake.RegisteredExtensions);
    }

    [Fact]
    public void SetComicAssociationsFor_FullAllowList_RegistersTheArchiveComicFormats_NeverGenericArchives()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(FileAssociationService.ComicAssociationExtensions, true);

        // The archive-backed comic providers (Cbz/Cbr/Rar5/Cb7/Cbt/WebComic) are always registered.
        // .pdf and .djvu ride on native deps (pdfium / djvm.exe) whose providers are
        // IValidateProvider and may be absent in a headless test bin dir - not asserted here.
        foreach (var ext in new[] { ".cbz", ".cbr", ".cb7", ".cbt", ".cbw" })
        {
            Assert.Contains(ext, fake.RegisteredExtensions);
        }

        // The whole point of the scoping: bare archive extensions are never touched.
        Assert.DoesNotContain(".zip", fake.RegisteredExtensions);
        Assert.DoesNotContain(".rar", fake.RegisteredExtensions);
        Assert.DoesNotContain(".7z", fake.RegisteredExtensions);
        Assert.DoesNotContain(".tar", fake.RegisteredExtensions);
        Assert.All(fake.RegisteredExtensions,
            ext => Assert.Contains(ext, FileAssociationService.ComicAssociationExtensions));
    }

    [Fact]
    public void SetComicAssociationsFor_GenericArchiveExtension_RegistersNothing()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".zip", ".rar", ".7z" }, true);

        Assert.Empty(fake.RegisteredExtensions);
        Assert.False(fake.RefreshCalled);
    }

    [Fact]
    public void SetComicAssociationsFor_ToleratesMissingLeadingDotAndCasing()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { "CBZ" }, true);

        Assert.Contains(".cbz", fake.RegisteredExtensions);
    }

    [Fact]
    public void SetComicAssociationsFor_FalseAfterTrue_UnregistersThatExtension()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".cb7" }, true);
        service.SetComicAssociationsFor(new[] { ".cb7" }, false);

        Assert.DoesNotContain(".cb7", fake.RegisteredExtensions);
    }

    private sealed class FakeShellFileAssociation : IShellFileAssociation
    {
        private readonly HashSet<(string TypeId, string Extension)> _registered = new();

        public bool RefreshCalled { get; private set; }

        public IEnumerable<string> RegisteredExtensions => _registered.Select(r => r.Extension).Distinct();

        public IEnumerable<string> RegistrationsFor(string extension) =>
            _registered.Where(r => r.Extension == extension).Select(r => r.TypeId);

        public bool IsRegistered(string typeId, string extension) => _registered.Contains((typeId, extension));

        public void Register(string typeId, string extension, string displayName, string appPath) => _registered.Add((typeId, extension));

        public void Unregister(string typeId, string extension) => _registered.Remove((typeId, extension));

        public void RefreshShell() => RefreshCalled = true;
    }
}
